using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Shared gothic detailing for the player classes: cloaks, collars, plate pieces,
    /// circlets. Each class builder in <see cref="CharacterArt"/> layers these on, so the
    /// whole party reads as one dark-medieval cast rather than five toys.
    /// </summary>
    public static class GothicKit
    {
        /// <summary>A cosmetic dye in effect while a player's body is being built (null = the class's own).</summary>
        public static Color? CloakOverride, TrimOverride;

        /// <summary>
        /// A cloak hung from the shoulders: a curved sheet behind the body that flares toward
        /// the hem, with gold clasps and (optionally) a trim band or a ragged, tattered hem.
        /// </summary>
        public static void Cloak(Transform torso, Color color, float length, float flare, Color? trim = null, bool tattered = false)
        {
            if (CloakOverride.HasValue)
            {
                color = CloakOverride.Value;
                trim = TrimOverride ?? trim;
            }
            float top = 0.56f, bottom = top - length;
            // Everything below hangs from a pivot at the shoulders so the cloak can move (CloakSway).
            var hang = MeshKit.Pivot(torso, "CloakHang", new Vector3(0f, top, -0.02f));
            var drop = new Vector3(0f, -top, 0.02f);
            var sheet = MeshKit.Part(hang, "Cloak", MeshKit.Lathe("cloak" + length.ToString("F2") + flare.ToString("F2"), new[]
            {
                new Vector2(0.27f, top), new Vector2(0.3f, top - 0.15f), new Vector2(0.31f + flare * 0.4f, top - length * 0.5f), new Vector2(0.3f + flare, bottom),
            }, 10, true, 170f), Mat.Cloth, drop, new Vector3(0f, 95f, 0f), new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(sheet.GetComponent<Renderer>(), color);
            // The lathe is a surface of revolution; flip normals isn't possible, so add a
            // second, slightly smaller sheet facing in — the cloak has an inside.
            var lining = MeshKit.Part(hang, "CloakLining", MeshKit.Lathe("cloakIn" + length.ToString("F2") + flare.ToString("F2"), new[]
            {
                new Vector2(0.3f + flare - 0.015f, bottom), new Vector2(0.31f + flare * 0.4f - 0.015f, top - length * 0.5f), new Vector2(0.285f, top - 0.15f), new Vector2(0.255f, top),
            }, 10, true, 170f), Mat.Cloth, drop, new Vector3(0f, 95f, 0f), new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(lining.GetComponent<Renderer>(), color * 0.55f);

            if (trim.HasValue)
            {
                var band = MeshKit.Part(hang, "CloakTrim", MeshKit.Lathe("cloakTrim" + length.ToString("F2") + flare.ToString("F2"), new[]
                {
                    new Vector2(0.3f + flare + 0.005f, bottom), new Vector2(0.3f + flare - 0.02f, bottom + 0.07f),
                }, 10, true, 170f), Mat.Cloth, drop, new Vector3(0f, 95f, 0f), new Vector3(1f, 1f, 0.82f));
                ArtLibrary.Tint(band.GetComponent<Renderer>(), trim.Value);
            }
            if (tattered)
            {
                // Ragged tongues of cloth below the hem.
                for (int i = 0; i < 7; i++)
                {
                    float a = (115f + i * 22f) * Mathf.Deg2Rad;
                    float r = 0.3f + flare;
                    var p = new Vector3(Mathf.Cos(a) * r, bottom - 0.06f - (i % 2) * 0.05f, -Mathf.Abs(Mathf.Sin(a)) * r * 0.82f);
                    var tongue = MeshKit.Part(hang, "Rag" + i, MeshKit.Box(new Vector3(0.09f, 0.14f + (i % 3) * 0.04f, 0.015f)), Mat.Cloth, p + drop,
                        new Vector3(8f, Mathf.Atan2(p.x, -p.z) * Mathf.Rad2Deg, (i % 2 == 0 ? 6f : -6f)));
                    ArtLibrary.Tint(tongue.GetComponent<Renderer>(), color * 0.9f);
                }
            }
            hang.gameObject.AddComponent<CloakSway>();
            foreach (float side in new[] { -1f, 1f })
                MeshKit.Part(torso, "Clasp", MeshKit.Ball(0.035f, 6, 4), Mat.Gold, new Vector3(0.17f * side, 0.55f, 0.13f));
            MeshKit.Part(torso, "ClaspChain", MeshKit.Box(new Vector3(0.32f, 0.012f, 0.012f)), Mat.Gold, new Vector3(0f, 0.53f, 0.15f));
        }

        /// <summary>A tall standing collar rising behind the head (the sorcerer's silhouette).</summary>
        public static void HighCollar(Transform torso, Color color, float height = 0.22f)
        {
            var collar = MeshKit.Part(torso, "HighCollar", MeshKit.Lathe("highCollar" + height.ToString("F2"), new[]
            {
                new Vector2(0.13f, 0.58f), new Vector2(0.19f, 0.58f + height * 0.6f), new Vector2(0.24f, 0.58f + height),
            }, 10, true, 200f), Mat.Cloth, Vector3.zero, new Vector3(0f, 80f, 0f));
            ArtLibrary.Tint(collar.GetComponent<Renderer>(), color);
        }

        /// <summary>Plate gorget at the neck and tiered faulds below the belt.</summary>
        public static void PlateDetails(Transform torso, Transform hips, Color steelTint)
        {
            var gorget = MeshKit.Part(torso, "Gorget", MeshKit.Cylinder(0.16f, 0.12f, 0.1f, 10), Mat.Steel, new Vector3(0f, 0.58f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.85f));
            ArtLibrary.Tint(gorget.GetComponent<Renderer>(), steelTint);
            for (int i = 0; i < 3; i++)
            {
                var fauld = MeshKit.Part(hips, "Fauld" + i, MeshKit.Cylinder(0.24f + i * 0.02f, 0.25f + i * 0.025f, 0.08f, 10), Mat.Steel,
                    new Vector3(0f, -0.04f - i * 0.075f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
                ArtLibrary.Tint(fauld.GetComponent<Renderer>(), steelTint * (1f - i * 0.06f));
            }
            // Fluting: three raised ridges fanning up the breastplate, the gothic armourer's mark.
            for (int i = -1; i <= 1; i++)
                MeshKit.Part(torso, "Flute" + i, MeshKit.Box(new Vector3(0.018f, 0.36f, 0.02f)), Mat.Gold, new Vector3(i * 0.07f, 0.3f, 0.215f), new Vector3(-8f, 0f, i * -9f));
        }

        /// <summary>A circlet with a glowing brow gem. Returns the gem so it can pulse with the rig.</summary>
        public static Renderer Circlet(Transform head, float radius, float height, Color gem)
        {
            MeshKit.Part(head, "Circlet", MeshKit.Cylinder(radius, radius, 0.03f, 12), Mat.Steel, new Vector3(0f, height, 0f), Vector3.zero, new Vector3(1f, 1f, 0.95f));
            for (int i = -2; i <= 2; i++)
                MeshKit.Part(head, "CircletSpike" + i, MeshKit.Lathe("circletSpike", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.06f) }, 4), Mat.Steel,
                    Quaternion.Euler(0f, i * 24f, 0f) * new Vector3(0f, height + 0.015f, radius), Vector3.zero);
            var g = MeshKit.Part(head, "BrowGem", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(0f, height, radius + 0.01f));
            ArtLibrary.SetGlow(g.GetComponent<Renderer>(), gem * 1.6f);
            return g.GetComponent<Renderer>();
        }

        /// <summary>Tint every steel part under a root: blackened plate rather than chrome.</summary>
        public static void BlackenSteel(Transform root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith(Mat.Steel)) ArtLibrary.Tint(r, tint);
        }
    }
}
