using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A SPIKE BED (rebuilt 2026-11-12, the user: "make spike traps larger, and please change their visual. the spike
    /// trap should be activated by a pressure plate in front and behind it, and should be hidden enough if players run
    /// past it they get spiked, but if they see it they can get past"). A stretch of floor <see cref="Length"/> m long and
    /// as wide as the corridor (or <see cref="RoomWidth"/> in a room), laid in flagstone with rows of small dark holes,
    /// and a PRESSURE PLATE at each end (a slab a hair proud of the floor, its seams a shade dark). Tread on either plate
    /// and it clicks; <see cref="Delay"/> s later rusted spikes a metre tall punch up through the whole bed and stay up
    /// a moment, impaling whoever is on it (skeletons too) - and someone running through is on it by then. Seen in time,
    /// it is passed by jumping each plate: the bed itself only fires from the plates.
    /// The transform's forward is the way through. From the second act some are HIDDEN: no seams, no holes, until
    /// something treads on a plate (<see cref="HiddenChance"/>).
    /// </summary>
    public class SpikeTrap : NetworkBehaviour
    {
        public const float Length = 3f, PlateLength = 0.9f, RoomWidth = 3.4f;
        public const float Delay = 0.35f, Damage = 75f, Rearm = 1.4f, ImpaleSeconds = 0.8f, UpSeconds = 0.6f;
        /// <summary>Kept for the pets' warning radius and old callers: half the bed's length.</summary>
        public const float Radius = Length / 2f;

        /// <summary>No seams, no holes: only a tread shows it.</summary>
        public readonly NetworkVariable<bool> Hidden = new NetworkVariable<bool>();
        /// <summary>Across the way (a corridor's width, or <see cref="RoomWidth"/>). Set before Spawn.</summary>
        public readonly NetworkVariable<float> Width = new NetworkVariable<float>(RoomWidth);
        /// <summary>
        /// JAMMED (2026-11-13, the user: "Rogues (or anyone with a tool) can kneel on a plate for two seconds to jam it"):
        /// stand on a plate and hold F for <see cref="JamSeconds"/>; a Rogue needs nothing, anyone else spends one of their
        /// Tinker's Picks. A jammed bed never fires again; wedges show in its plates.
        /// </summary>
        public readonly NetworkVariable<bool> Jammed = new NetworkVariable<bool>();
        public const float JamSeconds = 2f, GlintRange = 7f;
        private float jamHeld;
        private float nextGlint;

        /// <summary>Server: how often a bed on this floor is hidden (none in the first act, half by the fifth).</summary>
        public static float HiddenChance(int depth) => Mathf.Clamp01(0.14f * (DungeonDirector.Tier(depth) - 0.5f));

        /// <summary>Every bed on this peer (pets look for hidden ones).</summary>
        public static readonly System.Collections.Generic.List<SpikeTrap> All = new System.Collections.Generic.List<SpikeTrap>();

        private Transform spikes;
        private Transform[] plates = new Transform[2];
        private float armedAt = -1f, readyAt, upUntil = -1f;
        private float spikeOut, pressed;
        private readonly System.Collections.Generic.HashSet<Health> struck = new System.Collections.Generic.HashSet<Health>();

        /// <summary>Where plate 0 (behind) or 1 (in front) lies, world space.</summary>
        public Vector3 PlateCenter(int i) => transform.position + transform.forward * ((i == 0 ? -1f : 1f) * (Length / 2f + PlateLength / 2f));

        /// <summary>Is <paramref name="p"/> on the bed (<paramref name="along"/> 0) or a plate (the plate's centre offset)?</summary>
        private bool Inside(Vector3 p, float along, float halfLen)
        {
            var d = transform.InverseTransformPoint(p);
            return Mathf.Abs(d.y) < 1f && Mathf.Abs(d.x) <= Width.Value / 2f && Mathf.Abs(d.z - along) <= halfLen;
        }

        public bool OnBed(Vector3 p) => Inside(p, 0f, Length / 2f);
        public bool OnPlate(Vector3 p) => Inside(p, -(Length / 2f + PlateLength / 2f), PlateLength / 2f) || Inside(p, Length / 2f + PlateLength / 2f, PlateLength / 2f);

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);
            BuildVisual();
            // Hidden can change after spawn (a test shot, a late value): the look follows.
            Hidden.OnValueChanged += (_, _) => Rebuild();
            Jammed.OnValueChanged += (_, _) => Rebuild();
        }

        private void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            BuildVisual();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        private void Update()
        {
            spikeOut = Mathf.MoveTowards(spikeOut, 0f, Time.deltaTime * 0.9f);
            pressed = Mathf.MoveTowards(pressed, 0f, Time.deltaTime * 1.5f);
            if (spikes != null) spikes.localPosition = new Vector3(0f, Mathf.Lerp(-1.05f, 0.02f, Mathf.Clamp01(spikeOut * 3f)), 0f);
            foreach (var pl in plates) if (pl != null) pl.localPosition = new Vector3(0f, 0.012f - 0.02f * Mathf.Clamp01(pressed * 2f), pl.localPosition.z);

            LocalJam();
            if (!IsServer) return;
            if (Jammed.Value) { armedAt = -1f; return; }

            if (armedAt > 0f && Time.time >= armedAt) Fire();
            if (upUntil > 0f)
            {
                if (Time.time < upUntil) Strike();
                else upUntil = -1f;
                return;
            }
            if (armedAt > 0f || Time.time < readyAt) return;

            foreach (var h in Health.All)
            {
                if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                if (!OnPlate(h.transform.position)) continue;
                armedAt = Time.time + Delay;
                ArmRpc();
                return;
            }
        }

        private void Fire()
        {
            armedAt = -1f;
            upUntil = Time.time + UpSeconds;
            readyAt = upUntil + Rearm;
            struck.Clear();
            Strike();
            FireRpc();
        }

        /// <summary>Server, while the spikes are up: each one on the bed is struck once.</summary>
        private void Strike()
        {
            int depth = Mathf.Max(0, DungeonDirector.Instance != null ? DungeonDirector.Instance.Floor.Value.y : 0);
            foreach (var h in Health.All.ToArray())
            {
                if (h == null || !h.IsAlive || h.Team == Team.Props || struck.Contains(h)) continue;
                if (!OnBed(h.transform.position)) continue;
                struck.Add(h);
                bool player = h.Team == Team.Players;
                h.ServerDamage(player ? DungeonDirector.TrapBite(h) : Damage * (1f + 0.25f * DungeonDirector.Tier(depth)) * 2f, h.transform.position + Vector3.down, NetworkObjectId);
                if (player && h.IsAlive && h.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerRoot(ImpaleSeconds);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ArmRpc()
        {
            pressed = 1f;
            Sfx.Play(Sound.Creak, transform.position, 0.8f, 1.6f);
            Sfx.Play(Sound.Bash, transform.position, 0.35f, 2.2f);
        }

        /// <summary>Local: kneeling on a plate with F held jams it; a Rogue sees hidden plates glint.</summary>
        private void LocalJam()
        {
            var me = NetworkPlayer.Local;
            if (me == null || Jammed.Value) { jamHeld = 0f; return; }
            var prog = me.GetComponent<PlayerProgress>();
            if (prog == null) return;
            // Light fingers: a Rogue near a hidden plate catches its glint.
            if (Hidden.Value && prog.ClassId.Value == Classes.ClassId.Rogue && Time.time >= nextGlint && (me.transform.position - transform.position).sqrMagnitude < GlintRange * GlintRange)
            {
                nextGlint = Time.time + 1.6f;
                for (int i = 0; i < 2; i++) Vfx.Embers(PlateCenter(i) + Vector3.up * 0.05f, 3, new Color(1f, 0.9f, 0.6f), 0.4f, 0.04f, 0.6f);
            }
            bool onPlate = OnPlate(me.transform.position) && prog.CanDisarm && (!Hidden.Value || prog.ClassId.Value == Classes.ClassId.Rogue || jamHeld > 0f || Seen);
            if (!onPlate || !UI.Binds.Held(UI.Bind.Use) || UI.GameHud.MenuOpen) { jamHeld = 0f; return; }
            jamHeld += Time.deltaTime;
            if (jamHeld >= JamSeconds)
            {
                jamHeld = 0f;
                JamRpc(me.NetworkObject);
            }
        }

        /// <summary>Seen: it has fired or been armed while you looked (a hidden plate you have not found cannot be jammed, except by a Rogue).</summary>
        private bool Seen => spikeOut > 0f || pressed > 0f || everFired;
        private bool everFired;

        [Rpc(SendTo.Server)]
        private void JamRpc(NetworkObjectReference who)
        {
            if (!who.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var p)) return;
            ServerJam(p);
        }

        /// <summary>Server: <paramref name="by"/> jams the plates (tests call this). False if they cannot.</summary>
        public bool ServerJam(PlayerProgress by)
        {
            if (Jammed.Value || by == null || !by.CanDisarm) return false;
            if (!OnPlate(by.transform.position) && (by.transform.position - transform.position).sqrMagnitude > 9f) return false;
            if (by.ClassId.Value != Classes.ClassId.Rogue) by.Picks.Value--;
            Jammed.Value = true;
            armedAt = -1f;
            JammedRpc(by.name, by.ClassId.Value == Classes.ClassId.Rogue);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JammedRpc(string who, bool rogue)
        {
            Sfx.Play(Sound.Creak, transform.position, 0.8f, 0.8f);
            Sfx.Play(Sound.Block, transform.position, 0.5f, 1.6f);
            var me = NetworkPlayer.Local;
            if (me != null && (me.transform.position - transform.position).sqrMagnitude < 20f * 20f)
                UI.GameHud.Feed(who + (rogue ? " wedges the plate with a knife blade. The spikes stay down." : " jams the plate with a pick. The spikes stay down."), new Color(0.85f, 0.85f, 0.7f));
        }

        private void OnGUI()
        {
            if (jamHeld <= 0f) return;
            useGUILayout = false;
            UI.UiSkin.Apply();
            float W = UI.UiScale.W, H = UI.UiScale.H;
            var r = new Rect(W / 2f - 110f, H * 0.62f, 220f, 14f);
            UI.UiSkin.Rect(r, new Color(0f, 0f, 0f, 0.6f));
            UI.UiSkin.Rect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(jamHeld / JamSeconds), r.height), new Color(0.9f, 0.8f, 0.5f));
            GUI.Label(new Rect(r.x, r.y - 22f, r.width, 20f), "Jamming the plate...", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>The prompt text for the HUD when standing on a plate (null when there is nothing to say).</summary>
        public static string PromptFor(PlayerProgress p)
        {
            if (p == null) return null;
            foreach (var t in All)
            {
                if (t == null || t.Jammed.Value || !t.OnPlate(p.transform.position)) continue;
                if (t.Hidden.Value && p.ClassId.Value != Classes.ClassId.Rogue && !t.Seen) continue;
                return p.CanDisarm ? "Hold F to jam the spike plate" + (p.ClassId.Value == Classes.ClassId.Rogue ? "" : "  (" + p.Picks.Value + " picks)") : "A spike plate  -  Tinker's Picks (Hobb) or a Rogue could jam it";
            }
            return null;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FireRpc()
        {
            spikeOut = 1f;
            everFired = true;
            Sfx.Play(Sound.Shatter, transform.position + Vector3.up * 0.3f, 0.9f, 0.6f);
            Sfx.Play(Sound.Slam, transform.position, 0.6f, 1.3f);
            Vfx.Embers(transform.position + Vector3.up * 0.2f, 10, new Color(0.45f, 0.4f, 0.35f), 2.5f, 0.06f, 0.8f);
        }

        private void BuildVisual()
        {
            float w = Width.Value;
            bool hidden = Hidden.Value;
            var stone = new Color(0.36f, 0.34f, 0.32f);
            var seam = new Color(0.07f, 0.065f, 0.06f);
            // The bed: flagstone a hair proud of the floor, holes in rows where the spikes wait.
            var bed = MeshKit.Part(transform, "Bed", MeshKit.Box(new Vector3(w, 0.03f, Length)), Mat.Flagstone, new Vector3(0f, 0.015f, 0f));
            int cols = Mathf.Max(3, Mathf.RoundToInt(w / 0.42f)), rows = Mathf.RoundToInt(Length / 0.42f);
            spikes = MeshKit.Pivot(transform, "Spikes", new Vector3(0f, -1.05f, 0f));
            for (int x = 0; x < cols; x++)
                for (int z = 0; z < rows; z++)
                {
                    var p = new Vector3((x + 0.5f) / cols * w - w / 2f + ((z % 2) * 0.1f), 0f, (z + 0.5f) / rows * Length - Length / 2f);
                    if (!hidden)
                    {
                        var hole = MeshKit.Part(transform, "Hole", MeshKit.Box(new Vector3(0.07f, 0.032f, 0.07f)), Mat.Dark, p + Vector3.up * 0.016f);
                        ArtLibrary.Tint(hole.GetComponent<Renderer>(), seam);
                    }
                    float tall = 0.95f + ((x * 7 + z * 13) % 5) * 0.04f;
                    var spike = MeshKit.Part(spikes, "Spike", MeshKit.Lathe("bedSpike", new[] { new Vector2(0.045f, 0f), new Vector2(0.035f, 0.6f), new Vector2(0f, 1f) }, 5), Mat.Steel,
                        p, new Vector3(((x + z) % 3 - 1) * 4f, 0f, ((x * z) % 3 - 1) * 4f), new Vector3(1f, tall, 1f));
                    ArtLibrary.Tint(spike.GetComponent<Renderer>(), new Color(0.45f, 0.32f, 0.24f)); // rust
                }
            // The two plates, one before the bed and one after.
            for (int i = 0; i < 2; i++)
            {
                float along = (i == 0 ? -1f : 1f) * (Length / 2f + PlateLength / 2f);
                plates[i] = MeshKit.Pivot(transform, "Plate" + i, new Vector3(0f, 0.012f, along));
                var slab = MeshKit.Part(plates[i], "Slab", MeshKit.Box(new Vector3(w - 0.06f, 0.024f, PlateLength - 0.06f)), Mat.Flagstone, Vector3.zero);
                ArtLibrary.Tint(slab.GetComponent<Renderer>(), stone);
                if (hidden) continue;
                // A dark seam round it: you see it if you look at the floor.
                foreach (float s in new[] { -1f, 1f })
                {
                    var a = MeshKit.Part(plates[i], "Seam", MeshKit.Box(new Vector3(w, 0.026f, 0.03f)), Mat.Dark, new Vector3(0f, 0f, s * PlateLength / 2f));
                    var b = MeshKit.Part(plates[i], "Seam", MeshKit.Box(new Vector3(0.03f, 0.026f, PlateLength)), Mat.Dark, new Vector3(s * (w / 2f - 0.015f), 0f, 0f));
                    ArtLibrary.Tint(a.GetComponent<Renderer>(), seam);
                    ArtLibrary.Tint(b.GetComponent<Renderer>(), seam);
                }
            }
            if (Jammed.Value)
                for (int i = 0; i < 2; i++)
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var wedge = MeshKit.Part(plates[i], "Wedge", MeshKit.Box(new Vector3(0.09f, 0.08f, 0.36f)), Mat.Steel, new Vector3(k * (w / 2f - 0.08f), 0.04f, 0f), new Vector3(0f, k * 24f, 0f));
                        ArtLibrary.Tint(wedge.GetComponent<Renderer>(), new Color(0.85f, 0.78f, 0.6f));
                    }
            MeshKit.MergeParts(transform); // the spikes rise as one; holes and seams are a mesh each
        }
    }
}
