using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A PUZZLE ROOM (2026-11-08, the user's list: "puzzle rooms (statues, plate sequences)"). Two special side rooms share
    /// this object, which stands at the sealed coffer by the back wall:
    /// <list type="bullet">
    /// <item>THE WATCHERS' ROOM (<see cref="Kind.Watchers"/>): four stone watchers (<see cref="PuzzleStatue"/>) stand round a
    /// sigil in the floor, each looking the wrong way. F turns one a quarter round; when all four look at the sigil the
    /// coffer opens. The grinding sometimes raises one of the dead.</item>
    /// <item>THE CHIME ROOM (<see cref="Kind.Chimes"/>): four coloured plates round the middle, each with its own bell. The
    /// first time someone walks in, the bells ring a tune (4-6 notes, longer deeper down); step on the plates in the same
    /// order. A wrong plate buzzes, wakes a skeleton and the tune plays again from the start. F at the coffer replays it.</item>
    /// <item>THE SCALES (<see cref="Kind.Scales"/>, 2026-11-08): a great balance in the middle, a stone counterweight
    /// (<see cref="Target"/>, studs on it) on one pan. Five urns (<see cref="ScaleWeight"/>) lie about; set them down by the
    /// empty pan and they go on it (<see cref="Load"/>). The beam tilts by the difference; held exactly level for
    /// <see cref="BalanceHold"/> s, the coffer opens.</item>
    /// </list>
    /// Solved: a chest, a Sigil and gold, like the Oath Room (<see cref="DungeonDirector.ServerPuzzleSolved"/>).
    /// </summary>
    public class PuzzleRoom : Interactable
    {
        public enum Kind : byte { Watchers = 0, Chimes = 1, Mirrors = 2, Scales = 3 }

        public static readonly List<PuzzleRoom> Rooms = new List<PuzzleRoom>();
        public const float StatueRing = 2.7f, PlateRing = 3.3f, PlateRadius = 0.85f, NoteGap = 0.72f, Lead = 0.9f, WrongPause = 1.8f;
        public static readonly Color[] NoteColour = { new Color(1f, 0.35f, 0.25f), new Color(1f, 0.8f, 0.3f), new Color(0.4f, 1f, 0.5f), new Color(0.4f, 0.65f, 1f) };
        public static readonly float[] NotePitch = { 0.74f, 0.88f, 1.04f, 1.24f };
        public static readonly Color Gold = new Color(1f, 0.78f, 0.35f);

        public readonly NetworkVariable<Kind> Type = new NetworkVariable<Kind>();
        public readonly NetworkVariable<Vector3> Centre = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<int> RoomId = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<bool> Solved = new NetworkVariable<bool>();
        /// <summary>Chimes: the tune, two bits a note (plate 0-3), first note lowest.</summary>
        public readonly NetworkVariable<uint> Sequence = new NetworkVariable<uint>();
        public readonly NetworkVariable<byte> Length = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Step = new NetworkVariable<byte>();
        /// <summary>Chimes: server time the tune last began playing (-1: never).</summary>
        public readonly NetworkVariable<double> ShowAt = new NetworkVariable<double>(-1);
        /// <summary>Mirrors: the lens's row on the -A edge and the crystal's row on the +A edge.</summary>
        public readonly NetworkVariable<sbyte> SourceRow = new NetworkVariable<sbyte>();
        public readonly NetworkVariable<sbyte> CrystalRow = new NetworkVariable<sbyte>();
        /// <summary>Scales: the counterweight, and what stands on the other pan now (server-written).</summary>
        public readonly NetworkVariable<byte> Target = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Load = new NetworkVariable<byte>();
        public const int ScaleWeights = 5;
        public const float PanOffset = 1.35f, PivotY = 2.15f, PanY = 0.62f, PanCatch = 1.3f, TiltPerUnit = 3.5f, MaxTilt = 14f, BalanceHold = 1.2f, PanRadius = 0.55f;
        public const float MirrorCell = 1.8f;
        public const int GridEdge = 3;

        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        // Server.
        private byte lastWeighed;
        private float balancedFor;
        public int Wrongs { get; private set; }

        // Local.
        private bool built;
        private Transform coffer;
        private Renderer cofferLock;
        private readonly Renderer[] plateGlow = new Renderer[4];
        private readonly Transform[] plateTop = new Transform[4];
        private readonly float[] flash = new float[4];
        private readonly List<Renderer> sigil = new List<Renderer>();
        private int lastNote = -1;
        private double lastShow = -2;

        public override float Range => 2.6f;

        public static Vector3 Axis(int i) => Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
        public Vector3 PlateAt(int i) => Centre.Value + Axis(i) * PlateRing;
        public int NoteAt(int i) => (int)((Sequence.Value >> (i * 2)) & 3u);
        /// <summary>When the tune finishes playing (server time); before then the plates do not listen.</summary>
        public double ListenFrom => ShowAt.Value + Lead + Length.Value * NoteGap;
        public bool Listening => Type.Value == Kind.Chimes && ShowAt.Value >= 0 && Now >= ListenFrom;

        public override string Prompt(PlayerProgress who)
        {
            if (Solved.Value) return null;
            if (Type.Value == Kind.Watchers) return "The coffer is sealed  -  turn every watcher to look at the sigil";
            if (Type.Value == Kind.Mirrors) return "The coffer is sealed  -  turn the mirrors until the light reaches the crystal";
            if (Type.Value == Kind.Scales) return "The coffer is sealed  -  load the empty pan with exactly " + Target.Value + "  (it holds " + Load.Value + ")";
            if (ShowAt.Value >= 0 && Now < ListenFrom) return null;
            return "Ring the coffer's bell  -  hear the chimes again (" + Length.Value + " notes)";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Type.Value == Kind.Chimes && !Solved.Value) ServerReplay(0.2);
        }

        /// <summary>Server: set it up (after spawn).</summary>
        public void ServerSetup(Kind kind, Vector3 centre, int roomId, int depth, int seed)
        {
            Type.Value = kind;
            Centre.Value = Vfx.Floor(centre);
            RoomId.Value = roomId;
            if (kind != Kind.Chimes) return;
            var rng = new System.Random(seed);
            int len = 4 + (DungeonDirector.Act(depth) >= 2 ? 1 : 0) + (DungeonDirector.Act(depth) >= 3 ? 1 : 0);
            uint seq = 0;
            int prev = -1;
            for (int i = 0; i < len; i++)
            {
                int n = rng.Next(4);
                if (n == prev) n = (n + 1 + rng.Next(3)) % 4; // never the same plate twice running
                seq |= (uint)n << (i * 2);
                prev = n;
            }
            Sequence.Value = seq;
            Length.Value = (byte)len;
        }

        public void ServerReplay(double delay)
        {
            Step.Value = 0;
            ShowAt.Value = Now + delay;
        }

        private void Update()
        {
            if (IsServer && IsSpawned && !Solved.Value && Type.Value == Kind.Chimes && Centre.Value != Vector3.zero) ServerTick();
            if (IsServer && IsSpawned && Type.Value == Kind.Scales && Centre.Value != Vector3.zero) ServerWeigh();
            Draw();
        }

        private bool Inside(Vector3 p)
        {
            if (RoomId.Value >= 0 && DungeonDirector.Layout != null && RoomId.Value < DungeonDirector.Layout.Rooms.Count)
                return DungeonDirector.Layout.RoomAt(p, 0f) == DungeonDirector.Layout.Rooms[RoomId.Value];
            return Vector3.Distance(p, Centre.Value) < 8f;
        }

        public bool OnPlate(int i, Vector3 at)
        {
            var d = at - PlateAt(i);
            if (Mathf.Abs(d.y) > 1.4f) return false;
            d.y = 0f;
            return d.magnitude <= PlateRadius;
        }

        private void ServerTick()
        {
            if (ShowAt.Value < 0)
            {
                foreach (var p in PlayerLife.All)
                    if (p != null && !p.Downed.Value && !p.Soul.Value && Inside(p.transform.position)) { ServerReplay(0.6); break; }
                return;
            }
            byte weighed = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Soul.Value) continue;
                for (int i = 0; i < 4; i++) if (OnPlate(i, p.transform.position)) weighed |= (byte)(1 << i);
            }
            byte rising = (byte)(weighed & ~lastWeighed);
            lastWeighed = weighed;
            if (rising == 0 || !Listening) return;
            for (int i = 0; i < 4; i++) if ((rising & (1 << i)) != 0) { ServerStep(i); if (Solved.Value || Step.Value == 0) break; }
        }

        /// <summary>Server: a plate goes down. Public for tests (plays as if the tune had finished).</summary>
        public bool ServerStep(int plate)
        {
            if (Solved.Value || Type.Value != Kind.Chimes) return false;
            if (plate == NoteAt(Step.Value))
            {
                Step.Value++;
                NoteRpc(plate, true);
                if (Step.Value >= Length.Value) ServerSolve();
                return true;
            }
            Wrongs++;
            NoteRpc(plate, false);
            if (!DungeonDirector.TestMode && DungeonDirector.Instance != null)
                DungeonDirector.Instance.ServerDebugSpawn("skeleton", Centre.Value + Axis((plate + 2) % 4) * 1.4f, false);
            ServerReplay(WrongPause);
            return false;
        }

        // ---------------------------------------------------------------- the scales

        /// <summary>The middle of the load pan (+A) and the counterweight's pan (-A), on the floor.</summary>
        public Vector3 LoadPan => Centre.Value + AxisA * PanOffset;
        public Vector3 WeightPan => Centre.Value - AxisA * PanOffset;
        /// <summary>The beam's tilt in degrees for a load: positive = the load pan hangs low.</summary>
        public float TiltFor(int load) => Mathf.Clamp((load - Target.Value) * TiltPerUnit, -MaxTilt, MaxTilt);
        /// <summary>Height of a pan's top over the floor at a tilt (side +1 = the load pan).</summary>
        public static float PanTop(float side, float tilt) => PanY - side * PanOffset * Mathf.Sin(tilt * Mathf.Deg2Rad);
        private static readonly Vector2[] Slots = { Vector2.zero, new Vector2(0.27f, 0.1f), new Vector2(-0.27f, 0.1f), new Vector2(0.1f, -0.27f), new Vector2(-0.12f, 0.29f), new Vector2(0f, 0f) };

        /// <summary>Server: what is on the load pan; settle each weight onto it; level long enough = solved.</summary>
        private void ServerWeigh()
        {
            float tilt = TiltFor(Load.Value);
            int load = 0, slot = 0;
            var pan = LoadPan;
            var top = pan + Vector3.up * PanTop(1f, tilt);
            foreach (var c in Carryable.All)
            {
                if (!(c is ScaleWeight sw) || sw.Group.Value != RoomId.Value || sw.Carried) continue;
                var d = sw.Rest.Value - pan;
                if (Mathf.Abs(d.y) > 1.6f) continue;
                d.y = 0f;
                if (d.magnitude > PanCatch) continue;
                load += sw.Weight.Value;
                var o = Slots[Mathf.Min(slot++, Slots.Length - 1)];
                var want = top + AxisA * o.x + AxisB * o.y;
                if ((sw.Rest.Value - want).sqrMagnitude > 0.0004f) sw.Rest.Value = want;
            }
            if (Load.Value != load) { Load.Value = (byte)Mathf.Min(255, load); WeighRpc(load == Target.Value); }
            if (Solved.Value) return;
            balancedFor = load == Target.Value && load > 0 ? balancedFor + Time.deltaTime : 0f;
            if (balancedFor >= BalanceHold) ServerSolve();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WeighRpc(bool level)
        {
            Sfx.Play(Sound.Creak, Centre.Value + Vector3.up * PivotY, 0.8f, level ? 1.15f : 0.85f);
            if (level) Sfx.Play(Sound.Chain, Centre.Value + Vector3.up * PivotY, 0.5f, 1.2f);
        }

        // ---------------------------------------------------------------- the light-beam room

        /// <summary>The grid's axes: B points from the middle to the coffer (snapped to the world axes), A across it.</summary>
        public Vector3 AxisB
        {
            get
            {
                var d = transform.position - Centre.Value; d.y = 0f;
                return Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, d.z >= 0f ? 1f : -1f);
            }
        }
        public Vector3 AxisA => Vector3.Cross(Vector3.up, AxisB);
        public Vector3 GridPos(float x, float r) => Centre.Value + AxisA * (x * MirrorCell) + AxisB * (r * MirrorCell);

        /// <summary>
        /// The beam on the grid: from the lens at (-GridEdge, sourceRow) heading +A, one cell at a time; a mirror '/' (state 0)
        /// turns (dx, dr) into (dr, dx), '\' (1) into (-dr, -dx). True when it reaches the crystal at (GridEdge, crystalRow).
        /// <paramref name="path"/> gets the corners (grid units), ending at the crystal or where the beam leaves the grid.
        /// </summary>
        public static bool TraceGrid(int sourceRow, int crystalRow, Dictionary<int, byte> mirrors, List<Vector2> path)
        {
            int x = -GridEdge, r = sourceRow, dx = 1, dr = 0;
            path?.Add(new Vector2(x, r));
            for (int step = 0; step < 60; step++)
            {
                x += dx; r += dr;
                if (x == GridEdge && r == crystalRow) { path?.Add(new Vector2(x, r)); return true; }
                if (Mathf.Abs(x) > GridEdge || Mathf.Abs(r) > GridEdge) { path?.Add(new Vector2(x - dx * 0.5f, r - dr * 0.5f)); return false; }
                if (mirrors.TryGetValue(Key(x, r), out var st))
                {
                    path?.Add(new Vector2(x, r));
                    int ndx, ndr;
                    if (st == 0) { ndx = dr; ndr = dx; } else { ndx = -dr; ndr = -dx; }
                    dx = ndx; dr = ndr;
                }
            }
            return false;
        }
        public static int Key(int x, int r) => (x + 8) * 32 + (r + 8);

        private readonly Dictionary<int, byte> mirrorStates = new Dictionary<int, byte>();
        private readonly List<Vector2> beamPath = new List<Vector2>();

        /// <summary>The room's mirrors as the trace wants them; true if the beam reaches the crystal now.</summary>
        public bool BeamHits(List<Vector2> path = null)
        {
            mirrorStates.Clear();
            foreach (var m in PuzzleMirror.All) if (m != null && m.Group.Value == RoomId.Value) mirrorStates[Key(m.GridX.Value, m.GridR.Value)] = m.State.Value;
            return TraceGrid(SourceRow.Value, CrystalRow.Value, mirrorStates, path);
        }

        /// <summary>Server: after a mirror turns.</summary>
        public void ServerCheckBeam()
        {
            if (Type.Value == Kind.Mirrors && !Solved.Value && BeamHits()) ServerSolve();
        }

        /// <summary>Server: the puzzle is done (both kinds).</summary>
        public void ServerSolve()
        {
            if (Solved.Value) return;
            Solved.Value = true;
            foreach (var m in PuzzleMirror.All) if (m != null && m.Group.Value == RoomId.Value) m.Solved.Value = true;
            SolvedRpc();
            DungeonDirector.Instance?.ServerPuzzleSolved(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NoteRpc(int plate, bool right)
        {
            if (plate < 0 || plate > 3) return;
            var at = PlateAt(plate);
            if (right)
            {
                flash[plate] = 1f;
                Sfx.Play(Sound.Toll, at + Vector3.up, 0.55f, NotePitch[plate] * 1.5f);
                Vfx.Ring(at + Vector3.up * 0.06f, PlateRadius + 0.3f, NoteColour[plate], 0.4f);
            }
            else
            {
                Sfx.Play(Sound.UiError, at + Vector3.up, 1f, 0.7f);
                Sfx.Play(Sound.Rattle, Centre.Value, 0.8f, 0.8f);
                Vfx.Smoke(at + Vector3.up * 0.2f, 3, 0.6f, 1.2f);
                UI.GameHud.ShowCombo(at + Vector3.up * 1.4f, "WRONG CHIME", new Color(0.8f, 0.8f, 0.85f));
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SolvedRpc()
        {
            var at = transform.position;
            Sfx.Play(Sound.Relic, at, 1f, 0.9f);
            Sfx.Play(Sound.Chest, at, 1f, 1f);
            Vfx.Ring(at + Vector3.up * 0.05f, 3f, Gold, 0.8f);
            Vfx.Embers(at + Vector3.up * 0.8f, 20, Gold, 4f, 0.05f, 1f);
            UI.GameHud.Feed(Type.Value == Kind.Watchers ? "The watchers see the sigil: the coffer opens." : Type.Value == Kind.Mirrors ? "The light finds the crystal: the coffer opens."
                : Type.Value == Kind.Scales ? "The beam hangs level: the coffer opens." : "The last chime rings true: the coffer opens.", Gold);
            if (Type.Value == Kind.Chimes)
                for (int i = 0; i < 4; i++) { flash[i] = 1.4f; Sfx.Play(Sound.Toll, PlateAt(i) + Vector3.up, 0.4f, NotePitch[i] * 1.5f); }
        }

        // ------------------------------------------------------------------ visuals

        private void Draw()
        {
            if (Centre.Value == Vector3.zero) return;
            if (!built) Build();
            if (coffer != null && coffer.gameObject.activeSelf == Solved.Value) coffer.gameObject.SetActive(!Solved.Value);
            float pulse = 0.85f + Mathf.Sin(Time.time * 2.5f) * 0.15f;
            if (Type.Value == Kind.Mirrors) { DrawBeam(pulse); return; }
            if (Type.Value == Kind.Scales) { DrawScale(pulse); return; }
            if (Type.Value == Kind.Watchers)
            {
                int seeing = 0;
                foreach (var s in PuzzleStatue.All) if (s != null && s.Group.Value == RoomId.Value && (s.Watching || s.Solved.Value)) seeing++;
                if (Solved.Value) seeing = 4;
                foreach (var r in sigil) if (r != null) ArtLibrary.SetGlow(r, Gold * (0.35f + seeing * 0.3f) * pulse);
                if (cofferLock != null) ArtLibrary.SetGlow(cofferLock, Gold * (0.8f + seeing * 0.35f) * pulse);
                return;
            }
            // The tune: each note lights its plate for a beat and rings its bell (every peer plays it from the clock).
            double show = ShowAt.Value;
            if (show != lastShow) { lastShow = show; lastNote = -1; }
            if (show >= 0 && !Solved.Value)
            {
                double t = Now - show - Lead;
                int note = t < 0 ? -1 : (int)(t / NoteGap);
                if (note != lastNote && note >= 0 && note < Length.Value)
                {
                    lastNote = note;
                    int p = NoteAt(note);
                    flash[p] = 1.2f;
                    Sfx.Play(Sound.Toll, PlateAt(p) + Vector3.up, 0.6f, NotePitch[p] * 1.5f);
                }
                else if (note >= Length.Value) lastNote = note;
            }
            for (int i = 0; i < 4; i++)
            {
                flash[i] = Mathf.Max(0f, flash[i] - Time.deltaTime * 1.6f);
                if (plateGlow[i] != null) ArtLibrary.SetGlow(plateGlow[i], NoteColour[i] * (Solved.Value ? 0.45f : 0.2f + Mathf.Min(1f, flash[i]) * 0.9f)); // glow over ~1.2 blooms the plate to white
                if (plateTop[i] != null)
                {
                    bool down = false;
                    foreach (var p in PlayerLife.All) if (p != null && !p.Soul.Value && OnPlate(i, p.transform.position)) down = true;
                    plateTop[i].position = PlateAt(i) + Vector3.up * (down ? 0.02f : 0.06f);
                }
            }
            if (cofferLock != null) ArtLibrary.SetGlow(cofferLock, Gold * (0.8f + 1.6f * Step.Value / Mathf.Max(1f, Length.Value)) * pulse);
        }

        private void Build()
        {
            built = true;
            var vis = new GameObject("PuzzleVis").transform;
            vis.SetParent(transform, false);
            // Every part below is laid out in WORLD coordinates (the sigil, plates, lens, scale...): the root sits at the
            // world origin. It sat at the coffer, which put the sigil stone and chime rings a whole room-offset away.
            vis.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            BuildCoffer(vis);
            if (Type.Value == Kind.Mirrors) BuildLens(vis);
            else if (Type.Value == Kind.Scales) BuildScale(vis);
            else if (Type.Value == Kind.Watchers) BuildSigil(vis);
            else for (int i = 0; i < 4; i++) BuildPlate(vis, i);
        }

        // ---- the scales, drawn
        private Transform scaleBeam, loadPan, weightPan;
        private float shownTilt;
        private byte builtTarget;
        private static readonly Color Bronze = new Color(0.55f, 0.38f, 0.2f);

        private void BuildScale(Transform vis)
        {
            var c = Centre.Value;
            var rot = Quaternion.LookRotation(AxisB); // local x = A (the beam), local z = B (toward the coffer)
            var stand = MeshKit.Pivot(vis, "Scale", Vector3.zero);
            stand.SetPositionAndRotation(c, rot);
            MeshKit.Part(stand, "Base", MeshKit.Box(new Vector3(0.95f, 0.22f, 0.95f)), Mat.StoneWall, Vector3.up * 0.11f);
            MeshKit.Part(stand, "Step", MeshKit.Box(new Vector3(0.7f, 0.12f, 0.7f)), Mat.StoneWall, Vector3.up * 0.28f);
            var post = MeshKit.Part(stand, "Post", MeshKit.Cylinder(0.08f, 0.065f, PivotY - 0.34f, 10), Mat.Steel, Vector3.up * 0.34f);
            ArtLibrary.Tint(post.GetComponent<Renderer>(), Bronze * 0.8f);
            var crown = MeshKit.Part(stand, "Crown", MeshKit.Ball(0.11f, 10, 8), Mat.Gold, Vector3.up * (PivotY + 0.05f));
            ArtLibrary.Tint(crown.GetComponent<Renderer>(), Bronze * 1.2f);
            // The pointer: a needle up from the beam's middle that stands straight when the scale is level, against a
            // little dial on the post.
            var dial = MeshKit.Part(stand, "Dial", MeshKit.Box(new Vector3(0.5f, 0.05f, 0.03f)), Mat.Gold, new Vector3(0f, PivotY + 0.42f, 0.05f));
            ArtLibrary.Tint(dial.GetComponent<Renderer>(), Bronze);
            var mark = MeshKit.Part(stand, "LevelMark", MeshKit.Box(new Vector3(0.03f, 0.08f, 0.035f)), Mat.Glow, new Vector3(0f, PivotY + 0.48f, 0.05f));
            ArtLibrary.SetGlow(mark.GetComponent<Renderer>(), Gold * 0.6f);
            scaleBeam = MeshKit.Pivot(stand, "Beam", Vector3.up * PivotY);
            var bar = MeshKit.Part(scaleBeam, "Bar", MeshKit.Box(new Vector3(PanOffset * 2f + 0.2f, 0.09f, 0.1f)), Mat.Steel, Vector3.zero);
            ArtLibrary.Tint(bar.GetComponent<Renderer>(), Bronze);
            var needle = MeshKit.Part(scaleBeam, "Needle", MeshKit.Box(new Vector3(0.035f, 0.42f, 0.035f)), Mat.Gold, new Vector3(0f, 0.21f, 0.08f));
            ArtLibrary.Tint(needle.GetComponent<Renderer>(), Bronze * 1.3f);
            foreach (float s in new[] { -1f, 1f })
                MeshKit.Part(scaleBeam, "Hook", MeshKit.Ball(0.06f, 8, 6), Mat.Steel, new Vector3(s * PanOffset, -0.04f, 0f));
            loadPan = BuildPan(vis, "LoadPan");
            weightPan = BuildPan(vis, "WeightPan");
            // The counterweight: a dark stone block with the target in gold studs on both faces.
            builtTarget = Target.Value;
            var block = MeshKit.Pivot(weightPan, "Counterweight", Vector3.zero);
            block.localRotation = Quaternion.identity;
            var stone = MeshKit.Part(block, "Block", MeshKit.Box(new Vector3(0.62f, 0.4f, 0.42f)), Mat.Marble, Vector3.up * 0.2f);
            ArtLibrary.Tint(stone.GetComponent<Renderer>(), new Color(0.3f, 0.3f, 0.32f));
            int n = Mathf.Clamp(builtTarget, 1, 16), perRow = n > 8 ? (n + 1) / 2 : n;
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow, inRow = row == 0 ? perRow : n - perRow;
                float x = (col - (inRow - 1) * 0.5f) * 0.068f, y = n > 8 ? (row == 0 ? 0.27f : 0.13f) : 0.2f;
                foreach (float face in new[] { -1f, 1f })
                {
                    var stud = MeshKit.Part(block, "Stud", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(x, y, face * 0.215f));
                    stud.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(stud.GetComponent<Renderer>(), Gold * 0.7f);
                }
            }
            shownTilt = TiltFor(Load.Value);
            PlaceScale(shownTilt);
        }

        /// <summary>A bronze pan (its origin on the pan's top face) and three chains up to the beam's hook.</summary>
        private Transform BuildPan(Transform vis, string name)
        {
            var pan = MeshKit.Pivot(vis, name, Vector3.zero);
            pan.rotation = Quaternion.LookRotation(AxisB);
            var dish = MeshKit.Part(pan, "Dish", MeshKit.Cylinder(PanRadius * 0.85f, PanRadius, 0.07f, 16), Mat.Gold, Vector3.down * 0.07f);
            ArtLibrary.Tint(dish.GetComponent<Renderer>(), Bronze);
            float chain = PivotY - PanY - 0.06f;
            var hook = Vector3.up * chain;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                var rim = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (PanRadius * 0.9f);
                var mid = (rim + hook) * 0.5f;
                var link = MeshKit.Part(pan, "Chain", MeshKit.Box(new Vector3(0.03f, (hook - rim).magnitude, 0.03f)), Mat.Steel, mid);
                link.transform.localRotation = Quaternion.FromToRotation(Vector3.up, hook - rim);
                ArtLibrary.Tint(link.GetComponent<Renderer>(), new Color(0.3f, 0.3f, 0.32f));
            }
            return pan;
        }

        private void PlaceScale(float tilt)
        {
            if (scaleBeam == null) return;
            scaleBeam.localRotation = Quaternion.Euler(0f, 0f, -tilt);
            float rad = tilt * Mathf.Deg2Rad;
            var c = Centre.Value;
            loadPan.position = c + AxisA * (PanOffset * Mathf.Cos(rad)) + Vector3.up * PanTop(1f, tilt);
            weightPan.position = c - AxisA * (PanOffset * Mathf.Cos(rad)) + Vector3.up * PanTop(-1f, tilt);
        }

        private void DrawScale(float pulse)
        {
            float want = Solved.Value ? 0f : TiltFor(Load.Value);
            shownTilt = Mathf.Lerp(shownTilt, want, 1f - Mathf.Exp(-3.5f * Time.deltaTime));
            PlaceScale(shownTilt);
            float near = Target.Value > 0 ? 1f - Mathf.Clamp01(Mathf.Abs(Load.Value - Target.Value) / (float)Target.Value) : 0f;
            if (cofferLock != null) ArtLibrary.SetGlow(cofferLock, Gold * (0.8f + 1.6f * near) * pulse);
        }

        private static GUIStyle scaleStyle;

        /// <summary>Close to the scale: the load against the counterweight over the beam (gold level, red over).</summary>
        private void OnGUI()
        {
            useGUILayout = false;
            if (Type.Value != Kind.Scales || Solved.Value || Centre.Value == Vector3.zero) return;
            var cam = ViewCamera.Current;
            if (cam == null || UI.PauseMenu.Open || UI.GameHud.PanelShowing) return;
            var top = Centre.Value + Vector3.up * (PivotY + 0.85f);
            if ((cam.transform.position - top).sqrMagnitude > 9f * 9f) return;
            UI.UiSkin.Apply();
            var sp = UI.UiScale.World(cam, top);
            if (sp.z <= 0f) return;
            scaleStyle ??= new GUIStyle(UI.UiSkin.Small) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            int load = Load.Value, target = Target.Value;
            scaleStyle.normal.textColor = load == target ? Gold : load > target ? new Color(1f, 0.45f, 0.35f) : new Color(0.85f, 0.82f, 0.75f);
            var r = new Rect(sp.x - 70f, UI.UiScale.H - sp.y - 15f, 140f, 30f);
            UI.UiSkin.Rect(r, new Color(0.04f, 0.035f, 0.03f, 0.7f));
            GUI.Label(r, load + "  /  " + target, scaleStyle);
        }

        private readonly List<Transform> beam = new List<Transform>();
        private Renderer crystalGlow;
        private Light crystalLight;
        private Transform beamRoot;
        private const float BeamY = 1.12f;

        /// <summary>The lens on its pedestal (the light), the crystal on its plinth (where the light must go).</summary>
        private void BuildLens(Transform vis)
        {
            var floorY = Centre.Value.y;
            var src = GridPos(-GridEdge, SourceRow.Value);
            // Pivot takes a LOCAL position and vis stands at the coffer: place these in world space (they sat a whole
            // room-offset away, out past the walls, until 2026-11-08).
            var lens = MeshKit.Pivot(vis, "Lens", Vector3.zero);
            lens.position = new Vector3(src.x, floorY, src.z);
            lens.rotation = Quaternion.LookRotation(AxisA);
            MeshKit.Part(lens, "Pedestal", MeshKit.Cylinder(0.25f, 0.3f, 0.95f, 10), Mat.StoneWall, Vector3.zero);
            MeshKit.Part(lens, "Housing", MeshKit.Cylinder(0.2f, 0.2f, 0.36f, 12), Mat.Gold, new Vector3(0f, BeamY, -0.18f), new Vector3(90f, 0f, 0f));
            var glass = MeshKit.Part(lens, "LensGlass", MeshKit.Cylinder(0.16f, 0.16f, 0.03f, 12), Mat.Glow, new Vector3(0f, BeamY, 0.19f), new Vector3(90f, 0f, 0f));
            glass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), new Color(1f, 0.9f, 0.6f) * 1.6f);
            var cr = GridPos(GridEdge, CrystalRow.Value);
            var plinth = MeshKit.Pivot(vis, "Crystal", Vector3.zero);
            plinth.position = new Vector3(cr.x, floorY, cr.z);
            MeshKit.Part(plinth, "Plinth", MeshKit.Box(new Vector3(0.6f, 0.85f, 0.6f)), Mat.StoneWall, Vector3.up * 0.425f);
            var shard = MeshKit.Part(plinth, "Shard", MeshKit.Lathe("puzzleCrystal", new[] { new Vector2(0f, 0f), new Vector2(0.16f, 0.12f), new Vector2(0.14f, 0.42f), new Vector2(0f, 0.6f) }, 6), Mat.Glow, new Vector3(0f, 0.85f, 0f));
            shard.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            crystalGlow = shard.GetComponent<Renderer>();
            crystalLight = new GameObject("CrystalLight").AddComponent<Light>();
            crystalLight.transform.SetParent(plinth, false);
            crystalLight.transform.localPosition = Vector3.up * 1.2f;
            crystalLight.type = LightType.Point;
            crystalLight.color = new Color(0.6f, 0.8f, 1f);
            crystalLight.range = 5f;
            crystalLight.intensity = 0.3f;
            crystalLight.shadows = LightShadows.None;
            beamRoot = MeshKit.Pivot(vis, "Beam", Vector3.zero);
        }

        /// <summary>The beam: one glowing bar per straight run, from the lens to each mirror and on.</summary>
        private void DrawBeam(float pulse)
        {
            if (beamRoot == null) return;
            beamPath.Clear();
            bool hit = BeamHits(beamPath) || Solved.Value;
            float y = Centre.Value.y + BeamY + 0.12f;
            for (int i = 0; i + 1 < beamPath.Count; i++)
            {
                if (beam.Count <= i)
                {
                    var bar = MeshKit.Part(beamRoot, "BeamBar", MeshKit.Box(new Vector3(0.05f, 0.05f, 1f)), Mat.Glow, Vector3.zero);
                    bar.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(bar.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.5f) * 1.3f);
                    beam.Add(bar.transform);
                }
                var a = GridPos(beamPath[i].x, beamPath[i].y); a.y = y;
                var b = GridPos(beamPath[i + 1].x, beamPath[i + 1].y); b.y = y;
                var t = beam[i];
                t.gameObject.SetActive(true);
                t.position = (a + b) / 2f;
                var d = b - a;
                if (d.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(d);
                t.localScale = new Vector3(1f, 1f, d.magnitude);
            }
            for (int i = Mathf.Max(0, beamPath.Count - 1); i < beam.Count; i++) beam[i].gameObject.SetActive(false);
            if (crystalGlow != null) ArtLibrary.SetGlow(crystalGlow, new Color(0.55f, 0.8f, 1f) * (hit ? 1.4f * pulse : 0.35f));
            if (crystalLight != null) crystalLight.intensity = hit ? 1.6f : 0.3f;
            if (cofferLock != null) ArtLibrary.SetGlow(cofferLock, Gold * (hit ? 2f : 0.8f) * pulse);
        }

        /// <summary>The sigil the watchers must look at: a gold ring cut into the floor with four rays.</summary>
        private void BuildSigil(Transform vis)
        {
            var c = Centre.Value;
            MeshKit.Part(vis, "SigilStone", MeshKit.Cylinder(1.25f, 1.2f, 0.06f, 20), Mat.Marble, c);
            foreach (float k in new[] { 1.05f, 0.55f })
            {
                var ring = MeshKit.Part(vis, "SigilRing", MeshKit.Lathe("puzzleRing", new[] { new Vector2(0.94f, 0f), new Vector2(1f, 0.01f), new Vector2(0.94f, 0.02f) }, 32, false),
                    Mat.Glow, c + Vector3.up * 0.06f, Vector3.zero, new Vector3(k, 1f, k));
                ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sigil.Add(ring.GetComponent<Renderer>());
            }
            for (int i = 0; i < 4; i++)
            {
                var ray = MeshKit.Part(vis, "SigilRay", MeshKit.Box(new Vector3(0.08f, 0.02f, 0.5f)), Mat.Glow, c + Vector3.up * 0.07f + Axis(i) * 0.8f, new Vector3(0f, i * 90f, 0f));
                ray.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sigil.Add(ray.GetComponent<Renderer>());
            }
            var eye = MeshKit.Part(vis, "SigilEye", MeshKit.Cylinder(0.16f, 0.16f, 0.02f, 12), Mat.Glow, c + Vector3.up * 0.07f);
            eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sigil.Add(eye.GetComponent<Renderer>());
        }

        /// <summary>A chime plate: a stone ring, a coloured glass face, and a small bell on a post at its outer edge.</summary>
        private void BuildPlate(Transform vis, int i)
        {
            var at = PlateAt(i);
            MeshKit.Part(vis, "ChimeRing", MeshKit.Cylinder(PlateRadius + 0.12f, PlateRadius + 0.08f, 0.06f, 18), Mat.StoneWall, at);
            plateTop[i] = MeshKit.Pivot(vis, "ChimeTop", at + Vector3.up * 0.06f);
            var face = MeshKit.Part(plateTop[i], "ChimeFace", MeshKit.Cylinder(PlateRadius - 0.05f, PlateRadius - 0.05f, 0.03f, 18), Mat.Glow, Vector3.zero);
            face.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plateGlow[i] = face.GetComponent<Renderer>();
            ArtLibrary.SetGlow(plateGlow[i], NoteColour[i] * 0.28f);
            // The bell post, outside the plate (away from the middle) so nobody walks into it.
            var post = at + Axis(i) * (PlateRadius + 0.55f);
            MeshKit.Part(vis, "BellPost", MeshKit.Box(new Vector3(0.09f, 1.5f, 0.09f)), Mat.Wood, post + Vector3.up * 0.75f);
            MeshKit.Part(vis, "BellArm", MeshKit.Box(new Vector3(0.06f, 0.06f, 0.4f)), Mat.Wood, post + Vector3.up * 1.45f - Axis(i) * 0.18f, new Vector3(0f, i * 90f, 0f));
            var bell = MeshKit.Part(vis, "Bell", MeshKit.Lathe("chimeBell", new[] { new Vector2(0.16f, 0f), new Vector2(0.13f, 0.06f), new Vector2(0.09f, 0.2f), new Vector2(0.04f, 0.26f), new Vector2(0f, 0.27f) }, 12),
                Mat.Gold, post + Vector3.up * 1.12f - Axis(i) * 0.34f);
            ArtLibrary.Tint(bell.GetComponent<Renderer>(), Color.Lerp(new Color(0.85f, 0.7f, 0.4f), NoteColour[i], 0.35f));
        }

        /// <summary>The sealed coffer at the back (the real chest takes its place when it opens). Same hand as the Oath's.</summary>
        private void BuildCoffer(Transform vis)
        {
            coffer = new GameObject("PuzzleCoffer").transform;
            coffer.SetParent(vis, false);
            coffer.position = Vfx.Floor(transform.position);
            var face = Centre.Value - coffer.position;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f) coffer.rotation = Quaternion.LookRotation(face);
            MeshKit.Part(coffer, "Plinth", MeshKit.Box(new Vector3(1.6f, 0.25f, 1.1f)), Mat.StoneWall, new Vector3(0f, 0.125f, 0f));
            MeshKit.Part(coffer, "Body", MeshKit.Box(new Vector3(1.2f, 0.62f, 0.75f)), Mat.Marble, new Vector3(0f, 0.56f, 0f));
            MeshKit.Part(coffer, "Lid", MeshKit.Box(new Vector3(1.28f, 0.14f, 0.82f)), Mat.StoneWall, new Vector3(0f, 0.94f, 0f));
            foreach (float side in new[] { -1f, 1f })
                MeshKit.Part(coffer, "Chain", MeshKit.Box(new Vector3(0.05f, 0.05f, 0.95f)), Mat.Steel, new Vector3(side * 0.32f, 1.02f, 0f), new Vector3(0f, side * 22f, 0f));
            MeshKit.Part(coffer, "Hasp", MeshKit.Box(new Vector3(0.24f, 0.28f, 0.06f)), Mat.Steel, new Vector3(0f, 0.72f, 0.4f));
            var lockGlow = MeshKit.Part(coffer, "Lock", MeshKit.Box(new Vector3(0.12f, 0.14f, 0.02f)), Mat.Glow, new Vector3(0f, 0.72f, 0.44f));
            lockGlow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cofferLock = lockGlow.GetComponent<Renderer>();
            ArtLibrary.SetGlow(cofferLock, Gold);
            if (Type.Value == Kind.Chimes)
            {
                // The coffer's own bell, the one F rings.
                var bell = MeshKit.Part(coffer, "CofferBell", MeshKit.Lathe("chimeBell", new[] { new Vector2(0.16f, 0f), new Vector2(0.13f, 0.06f), new Vector2(0.09f, 0.2f), new Vector2(0.04f, 0.26f), new Vector2(0f, 0.27f) }, 12),
                    Mat.Gold, new Vector3(0.48f, 1.01f, 0.2f));
                ArtLibrary.Tint(bell.GetComponent<Renderer>(), new Color(0.85f, 0.7f, 0.4f));
            }
        }

        public override void OnNetworkSpawn() { base.OnNetworkSpawn(); Rooms.Add(this); }
        public override void OnNetworkDespawn() { Rooms.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { Rooms.Remove(this); base.OnDestroy(); }
    }
}
