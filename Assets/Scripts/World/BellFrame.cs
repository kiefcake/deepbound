using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.World
{
    /// <summary>
    /// THE BELL FRAME by the shrine (2026-11-08, the user's list: "bell ringing, a co-op rhythm game"). Four bells hang in
    /// an oak frame, each on its rope. F takes a rope (a panel opens): SPACE asks Brother Aldric to call a PEAL - sixteen
    /// strikes on a steady beat, each naming a bell, scrolling down four lanes to the strike line. Press 1-4 (the bells,
    /// treble to tenor, left to right) as each strike reaches the line. Anyone at the frame can ring any bell, so a party
    /// shares the work. <see cref="CleanPeal"/> or better is a CLEAN PEAL: everyone who rang a true strike is blessed for the
    /// next floor (gold if already blessed), once a camp visit. Every peer hears the bells and sees them swing.
    /// </summary>
    public class BellFrame : Interactable
    {
        public const int Bells = 4, Strikes = 16, CleanPeal = 13, PealGold = 25;
        public const float Beat = 0.9f, Lead = 2.7f, Window = 0.3f, Spacing = 1.0f, BellHeight = 2.9f;
        public static readonly float[] Pitch = { 1.3f, 1.1f, 0.94f, 0.78f };
        public static readonly string[] Names = { "TREBLE", "SECOND", "THIRD", "TENOR" };
        private static readonly Color Brass = new Color(0.8f, 0.62f, 0.3f);

        /// <summary>Server time the peal's first strike is due (-1: no peal).</summary>
        public readonly NetworkVariable<double> PealAt = new NetworkVariable<double>(-1);
        /// <summary>The peal: two bits a strike.</summary>
        public readonly NetworkVariable<uint> Pattern = new NetworkVariable<uint>();
        /// <summary>Strikes rung true so far (a bit each).</summary>
        public readonly NetworkVariable<ushort> Struck = new NetworkVariable<ushort>();
        /// <summary>The camp visit the party last rang a clean peal in.</summary>
        public readonly NetworkVariable<int> PealedVisit = new NetworkVariable<int>(-1);

        public static bool LocalOpen { get; private set; }
        private static BellFrame localFrame;

        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value * 1000 + (int)(DungeonDirector.Instance.RunStartedAt.Value % 997) : 0;

        // Server.
        private readonly HashSet<ulong> ringers = new HashSet<ulong>();
        public int LastHits { get; private set; } = -1;

        // Local.
        private readonly Transform[] bells = new Transform[Bells];
        private readonly float[] swingAt = { -10f, -10f, -10f, -10f };
        private float openedAt;
        private string result;
        private float resultAt = -10f;

        public override float Range => 3.4f;
        public bool Ringing => PealAt.Value >= 0;
        public int NoteAt(int i) => (int)((Pattern.Value >> (i * 2)) & 3u);
        public int Hits { get { int n = 0; for (int i = 0; i < Strikes; i++) if ((Struck.Value & (1 << i)) != 0) n++; return n; } }
        /// <summary>Bell <paramref name="b"/> in the world (left to right as you face the frame).</summary>
        public Vector3 BellPos(int b) => transform.TransformPoint(new Vector3((1.5f - b) * Spacing, BellHeight, 0f));

        public override string Prompt(PlayerProgress who) => LocalOpen ? null
            : "Take a bell rope  -  ring a peal with Brother Aldric" + (PealedVisit.Value == Visit ? "  (pealed this visit)" : "  (a clean peal blesses the next floor)");

        protected override void ServerUse(PlayerProgress who) => OpenRpc(RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void OpenRpc(RpcParams p = default)
        {
            LocalOpen = true;
            localFrame = this;
            openedAt = Time.time;
            Sfx.Play2D(Sound.UiOpen, 0.6f);
        }

        public static void CloseLocal()
        {
            if (!LocalOpen) return;
            LocalOpen = false;
            localFrame = null;
            Sfx.Play2D(Sound.UiClose, 0.6f);
        }

        // ---------------------------------------------------------------- server

        [Rpc(SendTo.Server)]
        private void BeginRpc(NetworkObjectReference player)
        {
            if (!player.TryGet(out var obj) || Vector3.Distance(obj.transform.position, transform.position) > Range + 1.5f) return;
            ServerBegin();
        }

        /// <summary>Server: Aldric calls a new peal (not while one is ringing).</summary>
        public void ServerBegin()
        {
            if (Ringing) return;
            // Mostly stepping to a neighbour, now and then a leap, like call changes.
            uint pat = 0;
            int b = Random.Range(0, Bells);
            for (int i = 0; i < Strikes; i++)
            {
                pat |= (uint)b << (i * 2);
                int step = Random.value < 0.7f ? (Random.value < 0.5f ? -1 : 1) : Random.Range(-3, 4);
                b = Mathf.Clamp(b + (step == 0 ? 1 : step), 0, Bells - 1);
            }
            Pattern.Value = pat;
            Struck.Value = 0;
            ringers.Clear();
            PealAt.Value = Now + Lead;
            CalledRpc();
        }

        [Rpc(SendTo.Server)]
        private void RingRpc(NetworkObjectReference player, int bell, double at)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1.5f || bell < 0 || bell >= Bells) return;
            // The owner's own clock reading of the press (it heard the beat locally); never later than now.
            ServerRing(who, bell, System.Math.Min(at, Now));
        }

        /// <summary>Server: a bell is rung at server time <paramref name="at"/>. True if it was a true strike. Public for tests.</summary>
        public bool ServerRing(PlayerProgress who, int bell, double at)
        {
            SwingRpc(bell);
            if (!Ringing) return false;
            double t = at - PealAt.Value;
            int i = (int)System.Math.Round(t / Beat);
            if (i < 0 || i >= Strikes || System.Math.Abs(t - i * Beat) > Window || NoteAt(i) != bell || (Struck.Value & (1 << i)) != 0) return false;
            Struck.Value |= (ushort)(1 << i);
            ringers.Add(who.OwnerClientId);
            return true;
        }

        private void Update()
        {
            if (IsServer && IsSpawned && Ringing && Now > PealAt.Value + (Strikes - 1) * Beat + Window + 0.2) ServerFinish();
            if (LocalOpen && localFrame == this) LocalInput();
            AnimateBells();
        }

        /// <summary>Server: the peal is over. Clean: the ringers are blessed (once a visit).</summary>
        public void ServerFinish()
        {
            int hits = Hits;
            LastHits = hits;
            bool clean = hits >= CleanPeal;
            bool paid = clean && PealedVisit.Value != Visit;
            PealAt.Value = -1;
            if (paid)
            {
                PealedVisit.Value = Visit;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !ringers.Contains(p.OwnerClientId)) continue;
                    var prog = p.GetComponent<PlayerProgress>();
                    if (prog.ActiveBlessing.Value == 0) prog.ServerBless(new[] { Blessing.Wrath, Blessing.Bulwark, Blessing.Swiftness, Blessing.Vigor }[Random.Range(0, 4)]);
                    else prog.ServerAddGold(PealGold);
                }
            }
            FinishedRpc(hits, clean, paid);
        }

        // ---------------------------------------------------------------- everyone

        [Rpc(SendTo.ClientsAndHost)]
        private void CalledRpc()
        {
            UI.CampChatter.Say(FolkId.Aldric, "*He lifts a hand.* Rounds first, then follow my call. Sixteen strikes. Do not shame me before the saints.", 4f);
            Sfx.Play(Sound.Toll, BellPos(3), 0.5f, Pitch[3]);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingRpc(int bell)
        {
            if (bell < 0 || bell >= Bells) return;
            swingAt[bell] = Time.time;
            Sfx.Play(Sound.Toll, BellPos(bell), 0.75f, Pitch[bell]);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FinishedRpc(int hits, bool clean, bool paid)
        {
            result = hits + " of " + Strikes + " true  -  " + (clean ? paid ? "A CLEAN PEAL: blessed for the next floor" : "a clean peal (the blessing is given once a visit)" : "a ragged peal: " + CleanPeal + " true strikes make it clean");
            resultAt = Time.time;
            var me = NetworkPlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, transform.position) < 16f)
            {
                UI.GameHud.Feed("The bells: " + result, clean ? new Color(1f, 0.85f, 0.45f) : new Color(0.85f, 0.85f, 0.8f));
                UI.CampChatter.Say(FolkId.Aldric, clean ? "*He crosses himself, surprised.* Clean. The saints heard that one. Go, and go blessed." : hits >= 8 ? "Close. The tenor dragged. Again, when you have the breath." : "*He winces.* The dead below heard that. So did I. Again.", 4f);
            }
            if (clean) Vfx.Embers(transform.position + Vector3.up * BellHeight, 24, Brass, 4f, 0.05f, 1.2f);
        }

        private void AnimateBells()
        {
            for (int b = 0; b < Bells; b++)
            {
                if (bells[b] == null) continue;
                float k = Time.time - swingAt[b];
                // A full swing up and back over a second, damped.
                float a = k < 1.4f ? Mathf.Sin(k * Mathf.PI * 1.5f) * 70f * Mathf.Exp(-k * 1.6f) : 0f;
                bells[b].localRotation = Quaternion.Euler(a, 0f, 0f);
            }
        }

        // ---------------------------------------------------------------- the owner's panel

        private void LocalInput()
        {
            var me = NetworkPlayer.Local;
            if (me == null || !DungeonDirector.InLobby || Vector3.Distance(me.transform.position, transform.position) > Range + 1.5f) { CloseLocal(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || (UI.Binds.Down(UI.Bind.Use) && Time.time - openedAt > 0.3f)) { CloseLocal(); return; }
            if (kb.spaceKey.wasPressedThisFrame && !Ringing) BeginRpc(me.NetworkObject);
            for (int b = 0; b < Bells; b++)
                if (kb[(Key)((int)Key.Digit1 + b)].wasPressedThisFrame) RingRpc(me.NetworkObject, b, Now);
        }

        private void OnGUI()
        {
            if (!LocalOpen || localFrame != this) return;
            useGUILayout = false;
            UI.UiSkin.Apply();
            float W = UI.UiScale.W, H = UI.UiScale.H;
            float pw = 420f, ph = 470f;
            // Right of the crosshair, so the bells stay in view.
            var r = new Rect(W * 0.5f + 140f, H * 0.5f - ph * 0.5f, pw, ph);
            UI.UiSkin.Panel(r, 0.95f);
            GUI.Label(new Rect(r.x + 20, r.y + 10, pw - 40, 28), "THE BELLS   ·   ring a peal", new GUIStyle(UI.UiSkin.Heading) { fontSize = 20 });
            GUI.Label(new Rect(r.x + 20, r.y + 40, pw - 40, 34), "Press 1-4 as each strike reaches the line. " + CleanPeal + " of " + Strikes + " true is a clean peal.", new GUIStyle(UI.UiSkin.Small) { wordWrap = true });
            // Four lanes, notes falling to the strike line.
            float lx = r.x + 30f, ly = r.y + 82f, lw = (pw - 60f) / Bells, lh = 300f;
            float line = ly + lh - 30f;
            const float PxPerSecond = 120f;
            for (int b = 0; b < Bells; b++)
            {
                var lane = new Rect(lx + b * lw + 3f, ly, lw - 6f, lh);
                UI.UiSkin.Rect(lane, new Color(0f, 0f, 0f, 0.35f));
                float flash = Mathf.Clamp01(1f - (Time.time - swingAt[b]) * 3f);
                UI.UiSkin.Rect(new Rect(lane.x, line - 3f, lane.width, 6f), Color.Lerp(new Color(0.6f, 0.5f, 0.3f), Color.white, flash));
                GUI.Label(new Rect(lane.x, ly + lh - 22f, lane.width, 20f), (b + 1) + "  " + Names[b], new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
            }
            if (Ringing)
            {
                double now = Now;
                for (int i = 0; i < Strikes; i++)
                {
                    float dt = (float)(PealAt.Value + i * Beat - now);
                    float y = line - dt * PxPerSecond;
                    if (y < ly - 10f || y > ly + lh) continue;
                    int b = NoteAt(i);
                    bool hit = (Struck.Value & (1 << i)) != 0;
                    var col = hit ? new Color(1f, 0.85f, 0.4f) : dt < -Window ? new Color(0.45f, 0.2f, 0.2f) : Brass;
                    UI.UiSkin.Rect(new Rect(lx + b * lw + 8f, y - 7f, lw - 16f, 14f), col);
                }
                GUI.Label(new Rect(r.x + 20, ly + lh + 8f, pw - 40, 24), "True strikes: " + Hits + " / " + Strikes, new GUIStyle(UI.UiSkin.Body) { fontSize = 16, fontStyle = FontStyle.Bold });
            }
            else
            {
                GUI.Label(new Rect(r.x + 20, ly + lh + 8f, pw - 40, 24), "SPACE  -  ask Aldric to call a peal", new GUIStyle(UI.UiSkin.Body) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = UI.UiSkin.Gold } });
                if (result != null && Time.time - resultAt < 20f)
                    GUI.Label(new Rect(r.x + 20, ly + lh + 34f, pw - 40, 40), result, new GUIStyle(UI.UiSkin.Small) { wordWrap = true });
            }
            GUI.Label(new Rect(r.xMax - 200, r.yMax - 24, 190, 20), "Esc / F to let go of the rope", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleRight });
        }

        private void OnDisable() { if (localFrame == this) { LocalOpen = false; localFrame = null; } }

        // ---------------------------------------------------------------- looks

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // An oak frame: two posts, a headstock beam, four bells on their stocks, a rope and a striped sally under each.
            float half = Spacing * 2f + 0.3f;
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(transform, "Post", MeshKit.Box(new Vector3(0.24f, BellHeight + 0.5f, 0.24f)), Mat.Wood, new Vector3(s * half, (BellHeight + 0.5f) / 2f, 0f));
                MeshKit.Part(transform, "Brace", MeshKit.Box(new Vector3(0.12f, 1.4f, 0.12f)), Mat.Wood, new Vector3(s * half, 0.6f, 0.45f), new Vector3(-35f, 0f, 0f));
                MeshKit.Part(transform, "Brace", MeshKit.Box(new Vector3(0.12f, 1.4f, 0.12f)), Mat.Wood, new Vector3(s * half, 0.6f, -0.45f), new Vector3(35f, 0f, 0f));
            }
            MeshKit.Part(transform, "Beam", MeshKit.Box(new Vector3(half * 2f + 0.3f, 0.26f, 0.3f)), Mat.Wood, new Vector3(0f, BellHeight + 0.38f, 0f));
            MeshKit.Part(transform, "Cap", MeshKit.Box(new Vector3(half * 2f + 0.7f, 0.08f, 0.6f)), Mat.Wood, new Vector3(0f, BellHeight + 0.55f, 0f));
            var bellMesh = MeshKit.Lathe("ringBell", new[] { new Vector2(0.3f, -0.45f), new Vector2(0.26f, -0.38f), new Vector2(0.18f, -0.15f), new Vector2(0.15f, 0.05f), new Vector2(0.06f, 0.1f), new Vector2(0f, 0.11f) }, 14);
            for (int b = 0; b < Bells; b++)
            {
                float x = (1.5f - b) * Spacing;
                float size = 0.8f + b * 0.12f; // treble small, tenor big
                bells[b] = MeshKit.Pivot(transform, "Bell" + b, new Vector3(x, BellHeight + 0.2f, 0f));
                MeshKit.Part(bells[b], "Stock", MeshKit.Box(new Vector3(0.16f, 0.14f, 0.5f)), Mat.Wood, Vector3.zero);
                var bell = MeshKit.Part(bells[b], "Bell", bellMesh, Mat.Gold, new Vector3(0f, -0.1f, 0f), Vector3.zero, Vector3.one * size);
                ArtLibrary.Tint(bell.GetComponent<Renderer>(), Color.Lerp(Brass, new Color(0.55f, 0.45f, 0.3f), b * 0.15f));
                MeshKit.Part(bells[b], "Clapper", MeshKit.Ball(0.05f, 6, 4), Mat.Steel, new Vector3(0f, -0.48f * size, 0f));
                // The rope down to a red-and-white woolly grip at hand height.
                var rope = MeshKit.Part(transform, "Rope", MeshKit.Box(new Vector3(0.03f, BellHeight - 0.4f, 0.03f)), Mat.Cloth, new Vector3(x, (BellHeight + 0.4f) / 2f, 0.24f));
                ArtLibrary.Tint(rope.GetComponent<Renderer>(), new Color(0.72f, 0.64f, 0.48f));
                for (int k = 0; k < 3; k++)
                {
                    var sally = MeshKit.Part(transform, "Sally", MeshKit.Cylinder(0.055f, 0.055f, 0.12f, 8), Mat.Cloth, new Vector3(x, 1.15f + k * 0.12f, 0.24f));
                    ArtLibrary.Tint(sally.GetComponent<Renderer>(), k % 2 == 0 ? new Color(0.65f, 0.12f, 0.1f) : new Color(0.9f, 0.88f, 0.8f));
                }
            }
        }
    }
}
