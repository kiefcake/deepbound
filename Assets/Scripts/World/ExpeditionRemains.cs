using Deepbound.Art;
using Deepbound.Loot;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// One of the FIRST EXPEDITION (<see cref="Expedition"/>), sat against a wall where they stopped: bones in a rotted coat,
    /// a pack beside them and their keepsake in their lap, glowing faintly so it can be found in the dark. Search it (F):
    /// what they carried spills out (a relic, gold, embers), their last note goes into everyone's Journal, and the host's
    /// camp remembers them for good (the roll in the gaming tent, the dealer's mood). Spawned by
    /// <see cref="DungeonDirector"/> at most one a floor, only for an act whose member is not yet home.
    /// </summary>
    public class ExpeditionRemains : Interactable
    {
        public readonly NetworkVariable<byte> Member = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> Searched = new NetworkVariable<bool>();
        private Light glow;
        private Transform keepsake;
        private int builtFor = -1;

        public override float Range => 2.4f;

        public override string Prompt(PlayerProgress who) => Searched.Value ? null
            : "Search the remains  (" + Expedition.Roll[Mathf.Clamp(Member.Value, 0, Expedition.Members - 1)].Name + ", of the first expedition)";

        protected override void ServerUse(PlayerProgress who)
        {
            if (Searched.Value || who == null) return;
            Searched.Value = true;
            int m = Mathf.Clamp(Member.Value, 0, Expedition.Members - 1);
            var dir = DungeonDirector.Instance;
            if (dir != null)
            {
                int tier = Mathf.RoundToInt(DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0));
                var at = transform.position + transform.forward * 1.1f + Vector3.up * 0.4f;
                dir.ServerDropLoot(at, LootKind.Relic, (int)Relics.Random());
                for (int g = 0; g < 3; g++) dir.ServerDropLoot(at + Quaternion.Euler(0f, g * 120f, 0f) * Vector3.forward * 0.4f, LootKind.Gold, 10 + 4 * tier);
                dir.ServerDropEmbers(at, 8 + 3 * tier);
            }
            int home = Expedition.Remember(m);
            FoundRpc((byte)m, (byte)home, who.NetworkObjectId);
        }

        /// <summary>Server, for tests.</summary>
        public void ServerDebugUse(PlayerProgress who) => ServerUse(who);

        [Rpc(SendTo.ClientsAndHost)]
        private void FoundRpc(byte member, byte home, ulong searcher)
        {
            var who = Expedition.Roll[Mathf.Clamp(member, 0, Expedition.Members - 1)];
            Sfx.Play(Sound.Relic, transform.position + Vector3.up * 0.6f, 0.7f, 0.8f);
            Vfx.Embers(transform.position + Vector3.up * 0.6f, 10, who.Light, 1.5f, 0.03f, 0.6f);
            UI.GameHud.ShowTitleCard("THE FIRST EXPEDITION  (" + Mathf.Max(1, (int)home) + " / " + Expedition.Members + ")",
                who.Name + ". You take " + who.Keepsake + " home for them.", new Color(0.85f, 0.78f, 0.6f));
            UI.GameHud.Feed("Found " + who.Name + ", of the first expedition. The camp will remember.", new Color(0.85f, 0.78f, 0.6f));
            int page = UI.Journal.ExpeditionPage(member);
            bool isNew = UI.Journal.Add(page);
            var local = Player.NetworkPlayer.Local;
            if (local != null && local.NetworkObjectId == searcher) UI.GameHud.ShowPage(page, isNew);
            else if (isNew) UI.GameHud.Feed("A new page in your Journal: " + UI.Journal.Entries[page].Title, new Color(0.9f, 0.8f, 0.55f));
            if (home >= Expedition.Members) UI.GameHud.Feed("All four are home. The dealer at Deadman's Hand has something to tell you.", new Color(0.95f, 0.85f, 0.55f));
            UI.Tips.OfferNow("expedition", "THE FIRST EXPEDITION: five went down for the anvil. Four lie somewhere below, one in each of the first four acts. Bring them all home and someone at camp will finally say who the fifth was.");
            if (glow != null) glow.intensity = 0.25f;
            if (keepsake != null) keepsake.gameObject.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Member.OnValueChanged += (_, m) => Build(m);
            Build(Member.Value);
            if (Searched.Value) { if (glow != null) glow.intensity = 0.25f; if (keepsake != null) keepsake.gameObject.SetActive(false); }
        }

        private void Update()
        {
            if (glow == null || Searched.Value) return;
            glow.intensity = 0.9f + 0.25f * Mathf.Sin(Time.time * 1.7f);
        }

        /// <summary>A body sat against the wall behind it (its back to -forward), legs out, head fallen to one side.</summary>
        private void Build(int member)
        {
            if (builtFor == member) return;
            builtFor = member;
            var old = transform.Find("Remains");
            if (old != null) Destroy(old.gameObject);
            var root = MeshKit.Pivot(transform, "Remains", Vector3.zero);
            // A little over life size seated: at 1.0 it read as a child's bones in the first render (2026-11-14).
            root.localScale = Vector3.one * 1.2f;
            var who = Expedition.Roll[Mathf.Clamp(member, 0, Expedition.Members - 1)];
            var bone = new Color(0.78f, 0.74f, 0.64f);
            var coat = member == 2 ? new Color(0.16f, 0.15f, 0.18f) : member == 1 ? new Color(0.35f, 0.3f, 0.22f) : new Color(0.3f, 0.22f, 0.16f);
            GameObject P(Mesh mesh, string mat, Vector3 pos, Vector3 euler = default, Vector3? scale = null, Color? tint = null)
            {
                var g = MeshKit.Part(root, "Part", mesh, mat, pos, euler, scale);
                if (tint.HasValue) ArtLibrary.Tint(g.GetComponent<Renderer>(), tint.Value);
                return g;
            }
            // Pelvis on the floor, spine leaning back against the wall, skull dropped to the side.
            P(MeshKit.Ball(0.15f, 8, 6), Mat.Bone, new Vector3(0f, 0.12f, -0.35f), default, new Vector3(1.3f, 0.6f, 0.9f), bone);
            P(MeshKit.Box(new Vector3(0.38f, 0.5f, 0.2f)), Mat.Cloth, new Vector3(0f, 0.45f, -0.48f), new Vector3(-14f, 0f, 0f), null, coat);
            for (int r = 0; r < 4; r++)
                P(MeshKit.Box(new Vector3(0.34f, 0.025f, 0.05f)), Mat.Bone, new Vector3(0f, 0.36f + r * 0.09f, -0.36f - r * 0.022f), new Vector3(-14f, 0f, 0f), null, bone);
            P(MeshKit.Ball(0.11f, 10, 8), Mat.Bone, new Vector3(0.1f, 0.78f, -0.5f), new Vector3(16f, 12f, 20f), new Vector3(0.95f, 1.05f, 1.1f), bone);
            P(MeshKit.Box(new Vector3(0.12f, 0.04f, 0.08f)), Mat.Bone, new Vector3(0.11f, 0.69f, -0.43f), new Vector3(30f, 12f, 20f), null, bone);
            foreach (float s in new[] { -1f, 1f })
            {
                // The sockets stand proud of the skull's face (inside it they were invisible).
                P(MeshKit.Ball(0.03f, 6, 5), Mat.Dark, new Vector3(0.1f + s * 0.04f, 0.79f, -0.385f), default, null, new Color(0.03f, 0.02f, 0.02f));
                // Legs out along the floor, one knee up.
                bool knee = s > 0f;
                P(MeshKit.Cylinder(0.035f, 0.03f, 0.42f, 6), Mat.Bone, new Vector3(s * 0.12f, knee ? 0.2f : 0.06f, -0.12f), new Vector3(knee ? 55f : 90f, 0f, 0f), null, bone);
                P(MeshKit.Cylinder(0.03f, 0.025f, 0.4f, 6), Mat.Bone, new Vector3(s * 0.12f, knee ? 0.2f : 0.05f, knee ? 0.2f : 0.28f), new Vector3(knee ? 140f : 90f, 0f, 0f), null, bone);
                P(MeshKit.Box(new Vector3(0.1f, 0.08f, 0.2f)), Mat.Cloth, new Vector3(s * 0.12f, 0.04f, knee ? 0.32f : 0.5f), default, null, new Color(0.18f, 0.12f, 0.08f));
                // Arms hanging, hands in the lap.
                P(MeshKit.Cylinder(0.03f, 0.025f, 0.32f, 6), Mat.Bone, new Vector3(s * 0.21f, 0.42f, -0.36f), new Vector3(-30f, 0f, s * 12f), null, bone);
                P(MeshKit.Cylinder(0.025f, 0.02f, 0.28f, 6), Mat.Bone, new Vector3(s * 0.16f, 0.22f, -0.18f), new Vector3(70f, 0f, s * -25f), null, bone);
            }
            // The pack beside them, a rope coil, and a stub of candle long since out.
            P(MeshKit.Box(new Vector3(0.32f, 0.38f, 0.22f)), Mat.Cloth, new Vector3(-0.55f, 0.19f, -0.4f), new Vector3(0f, 18f, -8f), null, new Color(0.32f, 0.25f, 0.16f));
            P(MeshKit.Box(new Vector3(0.34f, 0.04f, 0.24f)), Mat.Cloth, new Vector3(-0.55f, 0.39f, -0.4f), new Vector3(0f, 18f, -8f), null, new Color(0.26f, 0.19f, 0.12f));
            P(MeshKit.Cylinder(0.16f, 0.16f, 0.06f, 12), Mat.Cloth, new Vector3(0.55f, 0.03f, -0.2f), default, null, new Color(0.5f, 0.42f, 0.28f));
            P(MeshKit.Cylinder(0.03f, 0.03f, 0.06f, 6), Mat.Bone, new Vector3(0.42f, 0.03f, 0.15f), default, null, new Color(0.85f, 0.8f, 0.68f));
            // The keepsake in the lap.
            keepsake = MeshKit.Pivot(root, "Keepsake", new Vector3(0f, 0.2f, -0.12f));
            var metal = new Color(0.75f, 0.72f, 0.66f);
            switch (member)
            {
                case 0: // a tin whistle
                    ArtLibrary.Tint(MeshKit.Part(keepsake, "Whistle", MeshKit.Cylinder(0.015f, 0.015f, 0.22f, 8), Mat.Steel, Vector3.zero, new Vector3(0f, 0f, 90f)).GetComponent<Renderer>(), metal);
                    break;
                case 1: // a cracked lantern
                    ArtLibrary.Tint(MeshKit.Part(keepsake, "Lantern", MeshKit.Box(new Vector3(0.12f, 0.16f, 0.12f)), Mat.Steel, Vector3.up * 0.06f).GetComponent<Renderer>(), new Color(0.3f, 0.28f, 0.25f));
                    ArtLibrary.SetGlow(MeshKit.Part(keepsake, "Glass", MeshKit.Box(new Vector3(0.09f, 0.11f, 0.13f)), Mat.Glow, Vector3.up * 0.06f).GetComponent<Renderer>(), who.Light * 0.8f);
                    break;
                case 2: // prayer beads
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad;
                        ArtLibrary.Tint(MeshKit.Part(keepsake, "Bead", MeshKit.Ball(0.014f, 5, 4), Mat.Wood, new Vector3(Mathf.Cos(a) * 0.08f, 0f, Mathf.Sin(a) * 0.06f)).GetComponent<Renderer>(), new Color(0.3f, 0.18f, 0.1f));
                    }
                    ArtLibrary.Tint(MeshKit.Part(keepsake, "Cross", MeshKit.Box(new Vector3(0.02f, 0.008f, 0.06f)), Mat.Steel, new Vector3(0f, 0f, -0.1f)).GetComponent<Renderer>(), metal);
                    break;
                default: // an iron ring
                    ArtLibrary.Tint(MeshKit.Part(keepsake, "Ring", MeshKit.Cylinder(0.035f, 0.035f, 0.012f, 12), Mat.Steel, Vector3.zero).GetComponent<Renderer>(), new Color(0.35f, 0.33f, 0.32f));
                    break;
            }
            // A pale light over the lap: the find reads in the dark from across the room.
            glow = new GameObject("RemainsGlow").AddComponent<Light>();
            glow.transform.SetParent(root, false);
            glow.transform.localPosition = new Vector3(0f, 0.6f, 0.3f);
            glow.type = LightType.Point;
            glow.color = who.Light;
            glow.range = 3.2f;
            glow.intensity = 1f;
            glow.shadows = LightShadows.None;
        }
    }
}
