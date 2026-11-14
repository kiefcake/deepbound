using Deepbound.Art;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE CAMP REMEMBERS (2026-11-08, docs/ACTIVITIES_PLAN.md section 4: "trophies ... finished quests change the camp").
    /// The host's history, synced as one value (<see cref="DungeonDirector.CampKeeps"/>), dresses the camp on every peer:
    /// <list type="bullet">
    /// <item>TROPHIES, one per guardian ever felled on the host's machine (from <see cref="Deeds"/>): the Gravewarden's shield
    /// over the gate, the Tide Lich's drowned crown by the shrine, the Hollow King's crown on a pike by the gong, the Pale
    /// Cardinal's mitre over Marta's bar, the Unsung's three-eyed skull on a plinth by the well, a Warden's jawbone by the yard.</item>
    /// <item>KEEPSAKES, one per camp person whose cargo or finds came home (host PlayerPrefs <c>db.keep</c>, never in tests):
    /// Hobb's father's hammer on the chimney, the dead merchant's strongbox behind Silas, the Deepcellar cask on the bar, deep
    /// silk on Vey's rack, Corwin's coach lamp burning by the gate, the saint's bones glowing on the shrine dais, Brann's
    /// squad standard flying over the yard, Ysolde's shards circling her stones.</item>
    /// </list>
    /// The folk speak of them too (<see cref="UI.DialogueLines.Remembered"/>). Added to the camp root by <see cref="Lobby.Build"/>.
    /// </summary>
    public class CampMemory : MonoBehaviour
    {
        public const string PrefKey = "db.keep";
        public const int CargoBit = 8, GatherBit = 18, WardenBit = 7;
        public static bool TestMode;

        /// <summary>Host: what the camp should show (deeds + kept quests).</summary>
        public static uint Load()
        {
            uint bits = 0;
            if (Deeds.Has(Deeds.Deed.Gravewarden)) bits |= 1u << 0;
            if (Deeds.Has(Deeds.Deed.TideLich)) bits |= 1u << 1;
            if (Deeds.Has(Deeds.Deed.HollowKing)) bits |= 1u << 2;
            if (Deeds.Has(Deeds.Deed.Cardinal)) bits |= 1u << 3;
            if (Deeds.Has(Deeds.Deed.Unsung)) bits |= 1u << 4;
            if (Deeds.Has(Deeds.Deed.Warden)) bits |= 1u << WardenBit;
            if (!TestMode && !Cosmetics.TestMode) bits |= (uint)PlayerPrefs.GetInt(PrefKey, 0) & 0xFFFFFF00u;
            return bits;
        }

        /// <summary>Host: a quest came home - remember it for good (and return the bit).</summary>
        public static uint Remember(FolkId who, bool cargo)
        {
            uint bit = 1u << ((cargo ? CargoBit : GatherBit) + (int)who);
            if (!TestMode && !Cosmetics.TestMode)
            {
                PlayerPrefs.SetInt(PrefKey, (int)((uint)PlayerPrefs.GetInt(PrefKey, 0) | bit));
                PlayerPrefs.Save();
            }
            return bit;
        }

        public static bool Has(uint bits, FolkId who, bool cargo) => (bits & (1u << ((cargo ? CargoBit : GatherBit) + (int)who))) != 0;
        public static uint Now => DungeonDirector.Instance != null ? DungeonDirector.Instance.CampKeeps.Value : 0u;

        private uint built = uint.MaxValue;
        private Transform root;
        private Transform shards;
        private Transform banner;

        private void Update()
        {
            uint want = Now;
            if (want != built) { built = want; Rebuild(want); }
            if (shards != null) shards.Rotate(0f, 25f * Time.deltaTime, 0f);
            if (banner != null) banner.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 1.3f) * 6f, 0f);
        }

        private void Rebuild(uint bits)
        {
            if (root != null) Destroy(root.gameObject);
            root = new GameObject("CampMemory").transform;
            root.SetParent(transform, false);
            shards = null; banner = null;
            var o = Lobby.Origin;
            // ---- Trophies ----
            if ((bits & 1u) != 0)
            {
                // The Gravewarden's tower shield, nailed over the inside of the gate.
                var g = Group("GravewardenShield", o + new Vector3(0f, 8.4f, Lobby.Radius - 1.05f), Vector3.back);
                Part(g, MeshKit.Box(new Vector3(1.6f, 2.2f, 0.12f)), Mat.Steel, Vector3.zero, new Color(0.35f, 0.33f, 0.32f));
                Part(g, MeshKit.Box(new Vector3(1.7f, 0.12f, 0.16f)), Mat.Bone, new Vector3(0f, 1.1f, 0f));
                Part(g, MeshKit.Box(new Vector3(1.7f, 0.12f, 0.16f)), Mat.Bone, new Vector3(0f, -1.1f, 0f));
                Part(g, MeshKit.Ball(0.28f, 8, 6), Mat.Bone, new Vector3(0f, 0.2f, 0.12f));
                Part(g, MeshKit.Box(new Vector3(0.12f, 2.2f, 0.06f)), Mat.Dark, new Vector3(0.45f, 0f, 0.07f), new Color(0.1f, 0.08f, 0.08f));
            }
            if ((bits & 2u) != 0)
            {
                // The Tide Lich's drowned crown on a wet plinth beside the shrine.
                var g = Group("LichCrown", Lobby.ShrineSpot + new Vector3(0.4f, 0f, -2.9f), Vector3.left);
                Part(g, MeshKit.Cylinder(0.32f, 0.36f, 1f, 8), Mat.StoneWall, Vector3.zero);
                Part(g, MeshKit.Cylinder(0.2f, 0.22f, 0.12f, 10), Mat.Gold, Vector3.up * 1.02f, new Color(0.45f, 0.6f, 0.55f));
                for (int i = 0; i < 6; i++)
                    Part(g, MeshKit.Lathe("lichPoint", new[] { new Vector2(0.035f, 0f), new Vector2(0f, 0.16f) }, 4), Mat.Gold, Quaternion.Euler(0f, i * 60f, 0f) * new Vector3(0f, 1.12f, 0.19f), new Color(0.45f, 0.6f, 0.55f));
                Glow(g, Vector3.up * 1.15f, new Color(0.3f, 0.85f, 0.8f), 0.6f, 2.5f);
            }
            if ((bits & 4u) != 0)
            {
                // The Hollow King's crown on a pike beside the gong.
                var g = Group("KingsCrown", Lobby.GongSpot + new Vector3(2.4f, 0f, -1.8f), Vector3.left);
                Part(g, MeshKit.Cylinder(0.04f, 0.03f, 2.6f, 6), Mat.Wood, Vector3.zero);
                Part(g, MeshKit.Cylinder(0.24f, 0.22f, 0.16f, 12), Mat.Gold, Vector3.up * 2.5f);
                for (int i = 0; i < 5; i++)
                    Part(g, MeshKit.Lathe("kingPoint", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.2f) }, 4), Mat.Gold, Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0f, 2.64f, 0.21f));
                Part(g, MeshKit.Ball(0.06f, 6, 4), Mat.Glow, Vector3.up * 2.58f + Vector3.forward * 0.23f, null, new Color(0.9f, 0.15f, 0.15f));
            }
            if ((bits & 8u) != 0)
            {
                // The Pale Cardinal's two-peaked mitre over Marta's bar.
                var g = Group("CardinalMitre", Lobby.TavernCenter + new Vector3(-Lobby.TavernHalfX + 0.3f, 2.45f, 1.2f), Vector3.right);
                Part(g, MeshKit.Box(new Vector3(0.5f, 0.06f, 0.3f)), Mat.Wood, Vector3.down * 0.05f);
                foreach (float s in new[] { -1f, 1f })
                    Part(g, MeshKit.Box(new Vector3(0.36f, 0.5f, 0.06f)), Mat.Cloth, new Vector3(0f, 0.25f, s * 0.05f), new Color(0.9f, 0.88f, 0.82f), null, new Vector3(s * 8f, 0f, 0f));
                Part(g, MeshKit.Box(new Vector3(0.06f, 0.48f, 0.13f)), Mat.Gold, new Vector3(0f, 0.25f, 0f));
            }
            if ((bits & 16u) != 0)
            {
                // The Unsung's three-eyed skull on a plinth by the well, cold blue light in its eyes.
                var g = Group("UnsungSkull", Lobby.WellSpot + new Vector3(2.6f, 0f, 1.8f), Vector3.back);
                Part(g, MeshKit.Box(new Vector3(0.7f, 1.1f, 0.7f)), Mat.StoneWall, Vector3.up * 0.55f);
                Part(g, MeshKit.Ball(0.32f, 10, 8), Mat.Bone, Vector3.up * 1.42f, null, null, Vector3.zero, new Vector3(1f, 1.1f, 1.15f));
                foreach (var e in new[] { new Vector3(-0.11f, 1.48f, 0.3f), new Vector3(0.11f, 1.48f, 0.3f), new Vector3(0f, 1.62f, 0.3f) })
                    Part(g, MeshKit.Ball(0.05f, 6, 4), Mat.Glow, e, null, new Color(0.4f, 0.65f, 1f) * 1.4f);
                Glow(g, new Vector3(0f, 1.5f, 0.6f), new Color(0.45f, 0.6f, 1f), 0.9f, 3.5f);
            }
            if ((bits & (1u << WardenBit)) != 0)
            {
                // A Warden's great jawbone arched over two posts at the yard's edge.
                var g = Group("WardenJaw", Lobby.YardSpot + new Vector3(-1.2f, 0f, 2.2f), Vector3.right);
                foreach (float s in new[] { -1f, 1f }) Part(g, MeshKit.Box(new Vector3(0.14f, 2.2f, 0.14f)), Mat.Wood, new Vector3(0f, 1.1f, s * 0.9f));
                Part(g, MeshKit.Lathe("wardenJaw", new[] { new Vector2(0.12f, -0.1f), new Vector2(0.16f, 0f), new Vector2(0.12f, 0.1f) }, 8, false, 180f), Mat.Bone, new Vector3(0f, 2.2f, 0f), null, null, new Vector3(90f, 0f, 0f), new Vector3(6f, 6f, 1f));
                for (int i = 0; i < 6; i++) Part(g, MeshKit.Lathe("wardenTooth", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.18f) }, 4), Mat.Bone, new Vector3(0f, 2.28f, -0.75f + i * 0.3f), null, null, new Vector3(180f, 0f, 0f));
            }

            // ---- The roll of the first expedition (2026-11-14) ----
            // Always up, in the gaming tent at the dealer's shoulder: a board under a small anvil plaque, five names. Those
            // brought home are gilded, their keepsake on the shelf below with a candle; the rest dim; the fifth is scratched
            // out until all four are home, and then it reads JORY.
            {
                var dir = Quaternion.Euler(0f, -320f, 0f) * Vector3.right; // 320 degrees round the tent, inside its wall
                var at = Lobby.TentCenter + dir * 3.9f;
                var face = -dir;
                var g = Group("ExpeditionRoll", at, face);
                int home = Progression.Expedition.Count(bits);
                // Above the blackjack table's rim (it hid the lower names and the shelf at first: render 2026-11-14).
                Part(g, MeshKit.Box(new Vector3(1.25f, 1.62f, 0.05f)), Mat.Wood, new Vector3(0f, 1.86f, -0.03f), new Color(0.2f, 0.13f, 0.09f));
                foreach (float s in new[] { -0.66f, 0.66f }) Part(g, MeshKit.Box(new Vector3(0.07f, 2.75f, 0.07f)), Mat.Wood, new Vector3(s, 1.37f, -0.03f), new Color(0.24f, 0.16f, 0.1f));
                var head = SignArt.Plaque(g, g.TransformPoint(new Vector3(0f, 2.5f, 0f)), face, "EXPEDITION", SignArt.Icon.Anvil, 1.05f);
                head.material.SetColor("_BaseColor", new Color(0.9f, 0.82f, 0.62f));
                for (int i = 0; i <= Progression.Expedition.Members; i++)
                {
                    bool fifth = i == Progression.Expedition.Members;
                    bool found = fifth ? home >= Progression.Expedition.Members : (bits & Progression.Expedition.Bit(i)) != 0;
                    string name = fifth ? (found ? Progression.Expedition.Fifth : "") : Progression.Expedition.Roll[i].Short;
                    var plaque = SignArt.Plaque(g, g.TransformPoint(new Vector3(0f, 2.24f - i * 0.24f, 0f)), face, name, SignArt.Icon.Candle, 0.82f);
                    plaque.material.SetColor("_BaseColor", found ? new Color(1f, 0.88f, 0.6f) : new Color(0.38f, 0.34f, 0.3f));
                    if (fifth && !found)
                        for (int k = 0; k < 4; k++)
                            Part(g, MeshKit.Box(new Vector3(0.6f, 0.012f, 0.01f)), Mat.Dark, new Vector3(0.05f, 1.25f + k * 0.03f, 0.03f), new Color(0.08f, 0.06f, 0.05f), null, new Vector3(0f, 0f, -6f + k * 5f));
                }
                // The shelf, and on it what came home.
                Part(g, MeshKit.Box(new Vector3(1.2f, 0.04f, 0.22f)), Mat.Wood, new Vector3(0f, 1.08f, 0.1f), new Color(0.24f, 0.16f, 0.1f));
                var tin = new Color(0.75f, 0.72f, 0.66f);
                if ((bits & Progression.Expedition.Bit(0)) != 0) Part(g, MeshKit.Cylinder(0.015f, 0.015f, 0.22f, 8), Mat.Steel, new Vector3(-0.3f, 1.115f, 0.1f), tin, null, new Vector3(0f, 0f, 90f));
                if ((bits & Progression.Expedition.Bit(1)) != 0)
                {
                    Part(g, MeshKit.Box(new Vector3(0.12f, 0.16f, 0.12f)), Mat.Steel, new Vector3(-0.1f, 1.18f, 0.1f), new Color(0.3f, 0.28f, 0.25f));
                    Part(g, MeshKit.Box(new Vector3(0.09f, 0.11f, 0.13f)), Mat.Glow, new Vector3(-0.1f, 1.18f, 0.1f), null, new Color(1f, 0.7f, 0.35f) * 0.8f);
                }
                if ((bits & Progression.Expedition.Bit(2)) != 0)
                    for (int k = 0; k < 10; k++)
                        Part(g, MeshKit.Ball(0.014f, 5, 4), Mat.Wood, new Vector3(0.12f + Mathf.Cos(k * 0.63f) * 0.07f, 1.115f, 0.1f + Mathf.Sin(k * 0.63f) * 0.05f), new Color(0.3f, 0.18f, 0.1f));
                if ((bits & Progression.Expedition.Bit(3)) != 0) Part(g, MeshKit.Cylinder(0.035f, 0.035f, 0.012f, 12), Mat.Steel, new Vector3(0.32f, 1.11f, 0.1f), new Color(0.35f, 0.33f, 0.32f));
                if (home > 0)
                {
                    // A stub of candle at each end of the shelf, lit for those brought home.
                    foreach (float s in new[] { -0.52f, 0.52f })
                    {
                        Part(g, MeshKit.Cylinder(0.03f, 0.03f, 0.13f, 8), Mat.Bone, new Vector3(s, 1.165f, 0.1f), new Color(0.92f, 0.86f, 0.72f));
                        Part(g, MeshKit.Ball(0.022f, 6, 5), Mat.Glow, new Vector3(s, 1.255f, 0.1f), null, new Color(1f, 0.65f, 0.25f) * 1.6f, default, new Vector3(1f, 1.6f, 1f));
                    }
                    Glow(g, new Vector3(0f, 1.6f, 0.6f), new Color(1f, 0.75f, 0.45f), 0.5f + 0.15f * home, 3f);
                }
            }

            // ---- Keepsakes ----
            if (Has(bits, FolkId.Hobb, true))
            {
                // Hobb's father's hammer back on its hook, on the chimney's face.
                var forge = Lobby.AnvilSpot + Lobby.S(-2.6f, 0f, -1.6f);
                var g = Group("FathersHammer", forge + new Vector3(0.43f, 1.75f, -0.3f), Vector3.right);
                Part(g, MeshKit.Box(new Vector3(0.05f, 0.05f, 0.12f)), Mat.Steel, new Vector3(0f, 0.55f, 0.04f));
                Part(g, MeshKit.Cylinder(0.035f, 0.03f, 0.9f, 7), Mat.Wood, new Vector3(0f, -0.35f, 0.08f));
                Part(g, MeshKit.Box(new Vector3(0.18f, 0.2f, 0.4f)), Mat.Steel, new Vector3(0f, -0.4f, 0.08f));
                Part(g, MeshKit.Box(new Vector3(0.1f, 0.1f, 0.01f)), Mat.Gold, new Vector3(0.095f, -0.4f, 0.08f), null, null, new Vector3(0f, 90f, 0f));
            }
            if (Has(bits, FolkId.Silas, true))
            {
                var g = Group("Strongbox", Lobby.MerchantSpot + new Vector3(-0.7f, 0f, 1.1f), Vector3.right);
                Part(g, MeshKit.Box(new Vector3(0.55f, 0.36f, 0.38f)), Mat.Wood, Vector3.up * 0.18f);
                foreach (float x in new[] { -0.2f, 0.2f }) Part(g, MeshKit.Box(new Vector3(0.05f, 0.38f, 0.4f)), Mat.Steel, new Vector3(x, 0.18f, 0f));
                Part(g, MeshKit.Box(new Vector3(0.1f, 0.12f, 0.04f)), Mat.Gold, new Vector3(0f, 0.22f, 0.2f));
            }
            if (Has(bits, FolkId.Marta, true))
            {
                var bar = Lobby.TavernKeepSpot + new Vector3(-0.4f, 1.15f, 1.2f);
                var g = Group("DeepcellarCask", bar, Vector3.right);
                Part(g, MeshKit.Lathe("keptCask", new[] { new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0.16f, 0.4f), new Vector2(0f, 0.4f) }, 12), Mat.Wood, Vector3.zero);
                Part(g, MeshKit.Box(new Vector3(0.1f, 0.1f, 0.02f)), Mat.Cloth, new Vector3(0.19f, 0.2f, 0f), new Color(0.6f, 0.08f, 0.08f), null, new Vector3(0f, 90f, 0f));
            }
            if (Has(bits, FolkId.Vey, true))
            {
                var g = Group("DeepSilk", Lobby.TailorSpot + new Vector3(0.9f, 0f, 1.7f), Vector3.right);
                Part(g, MeshKit.Cylinder(0.13f, 0.13f, 1.3f, 12), Mat.Cloth, Vector3.zero, new Color(0.12f, 0.05f, 0.22f), null, new Vector3(-6f, 0f, 0f));
                Part(g, MeshKit.Cylinder(0.133f, 0.133f, 0.12f, 12), Mat.Glow, Vector3.up * 0.7f, null, new Color(0.5f, 0.3f, 0.9f) * 0.45f);
            }
            if (Has(bits, FolkId.Corwin, true))
            {
                // Corwin's coach lamp, lit again, on a bracket by the gate.
                var g = Group("CoachLamp", o + new Vector3(-3.1f, 2.6f, Lobby.Radius - 1.1f), Vector3.back);
                Part(g, MeshKit.Box(new Vector3(0.05f, 0.05f, 0.4f)), Mat.Steel, new Vector3(0f, 0.3f, 0.15f));
                Part(g, MeshKit.Box(new Vector3(0.18f, 0.26f, 0.18f)), Mat.Glow, new Vector3(0f, 0f, 0.35f), null, new Color(1f, 0.75f, 0.4f) * 1.3f);
                Part(g, MeshKit.Lathe("keptLampRoof", new[] { new Vector2(0.16f, 0f), new Vector2(0f, 0.12f) }, 4), Mat.Steel, new Vector3(0f, 0.13f, 0.35f));
                Glow(g, new Vector3(0f, 0f, 0.4f), new Color(1f, 0.74f, 0.42f), 1.6f, 7f);
            }
            if (Has(bits, FolkId.Aldric, false))
            {
                // The saint's bones in a little gilt reliquary on the shrine dais, glowing.
                var g = Group("SaintsBones", Lobby.ShrineSpot + new Vector3(1.3f, 0.12f, 1.1f), Vector3.left);
                Part(g, MeshKit.Box(new Vector3(0.4f, 0.28f, 0.28f)), Mat.Gold, Vector3.up * 0.14f);
                Part(g, MeshKit.Box(new Vector3(0.3f, 0.12f, 0.2f)), Mat.Glow, Vector3.up * 0.33f, null, new Color(1f, 0.85f, 0.5f) * 0.9f);
                Glow(g, Vector3.up * 0.6f, new Color(1f, 0.85f, 0.5f), 0.7f, 2.5f);
            }
            if (Has(bits, FolkId.Brann, false))
            {
                // The squad's standard, sewn back together, flying over the yard.
                var g = Group("SquadStandard", Lobby.YardSpot + new Vector3(-1.4f, 0f, -1.8f), Vector3.right);
                Part(g, MeshKit.Cylinder(0.05f, 0.04f, 3.6f, 6), Mat.Wood, Vector3.zero);
                banner = new GameObject("Banner").transform;
                banner.SetParent(g, false);
                banner.localPosition = Vector3.up * 3.3f;
                Part(banner, MeshKit.Box(new Vector3(0.04f, 1f, 0.9f)), Mat.Cloth, new Vector3(0f, -0.5f, 0.45f), new Color(0.5f, 0.1f, 0.1f));
                Part(banner, MeshKit.Box(new Vector3(0.05f, 0.3f, 0.3f)), Mat.Gold, new Vector3(0f, -0.45f, 0.45f));
            }
            if (Has(bits, FolkId.Ysolde, false))
            {
                // The shards she wanted back, turning slowly over her stones.
                var g = Group("RuneShards", Lobby.RunewrightSpot + new Vector3(0f, 2.8f, 0f), Vector3.forward);
                shards = g;
                for (int i = 0; i < 5; i++)
                    Part(g, MeshKit.Lathe("keptShard", new[] { new Vector2(0f, -0.15f), new Vector2(0.06f, 0f), new Vector2(0f, 0.18f) }, 4), Mat.Glow,
                        Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0f, Mathf.Sin(i) * 0.15f, 0.7f), null, new Color(0.45f, 0.7f, 1f) * 1.2f);
                Glow(g, Vector3.zero, new Color(0.5f, 0.75f, 1f), 0.8f, 4f);
            }
        }

        private Transform Group(string name, Vector3 at, Vector3 face)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, true);
            t.position = at;
            t.rotation = Quaternion.LookRotation(face);
            return t;
        }

        private static GameObject Part(Transform parent, Mesh mesh, string mat, Vector3 pos, Color? tint = null, Color? glow = null, Vector3 euler = default, Vector3? scale = null)
        {
            var p = MeshKit.Part(parent, "Part", mesh, mat, pos, euler, scale);
            if (tint.HasValue) ArtLibrary.Tint(p.GetComponent<Renderer>(), tint.Value);
            if (glow.HasValue) ArtLibrary.SetGlow(p.GetComponent<Renderer>(), glow.Value);
            return p;
        }

        private static void Glow(Transform parent, Vector3 pos, Color color, float intensity, float range)
        {
            var l = new GameObject("KeepLight").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.localPosition = pos;
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }
    }
}
