using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>The Abyss's own dead (2026-10-20).</summary>
    public static partial class CharacterArt
    {
        private static readonly Color AbyssViolet = new Color(0.62f, 0.35f, 1f);

        /// <summary>
        /// The Abyssal Eye: a great bloodshot eye the size of a barrel, hanging in the air with heavy lids,
        /// a violet iris that brightens when it locks on, and a fringe of dangling tendrils. The eye is the
        /// head (a headshot), and the iris is a weak point.
        /// </summary>
        private static HumanoidRig BuildAbyssEye(Transform root)
        {
            var visual = MeshKit.Pivot(root, "EyeVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = AbyssViolet;
            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 1.75f, 0f));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            // Legs and arms exist for the animator: the tendrils hang from them and sway as it drifts.
            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.15f, -0.35f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.15f, -0.35f, 0f));
            rig.ArmL = MeshKit.Pivot(rig.Torso, "ArmL", new Vector3(-0.42f, -0.1f, 0f));
            rig.ArmR = MeshKit.Pivot(rig.Torso, "ArmR", new Vector3(0.42f, -0.1f, 0f));
            var flesh = new Color(0.36f, 0.24f, 0.3f);
            foreach (var (pivot, n) in new[] { (rig.LegL, 3), (rig.LegR, 3), (rig.ArmL, 2), (rig.ArmR, 2) })
                for (int i = 0; i < n; i++)
                {
                    float off = (i - (n - 1) / 2f) * 0.12f;
                    var t = MeshKit.Part(pivot, "Tendril", MeshKit.Cylinder(0.05f, 0.015f, 0.75f + i * 0.18f, 5), Mat.Leather,
                        new Vector3(off, -0.75f - i * 0.18f, off * 0.6f), new Vector3(180f + off * 40f, 0f, off * 30f));
                    ArtLibrary.Tint(t.GetComponent<Renderer>(), flesh);
                }

            rig.Head = MeshKit.Pivot(rig.Torso, "Eye", Vector3.zero);
            var ball = MeshKit.Part(rig.Head, "Ball", MeshKit.Ball(0.45f, 14, 10), Mat.Bone, Vector3.zero);
            ArtLibrary.Tint(ball.GetComponent<Renderer>(), new Color(0.86f, 0.8f, 0.78f));
            // Bloodshot veins across the white.
            for (int v = 0; v < 6; v++)
            {
                float a = v * 60f + 20f;
                var vein = MeshKit.Part(rig.Head, "Vein", MeshKit.Box(new Vector3(0.012f, 0.3f, 0.012f)), Mat.Leather,
                    Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.43f) + Vector3.up * ((v % 3) - 1) * 0.12f, new Vector3((v % 2 == 0 ? 25f : -25f), a, 60f));
                ArtLibrary.Tint(vein.GetComponent<Renderer>(), new Color(0.6f, 0.08f, 0.1f));
            }
            var iris = MeshKit.Part(rig.Head, "Iris", MeshKit.Cylinder(0.2f, 0.2f, 0.03f, 18), Mat.Glow, new Vector3(0f, 0f, 0.42f), new Vector3(90f, 0f, 0f));
            ArtLibrary.SetGlow(iris.GetComponent<Renderer>(), AbyssViolet * 1.1f);
            var pupil = MeshKit.Part(rig.Head, "Pupil", MeshKit.Cylinder(0.08f, 0.08f, 0.035f, 12), Mat.Dark, new Vector3(0f, 0f, 0.44f), new Vector3(90f, 0f, 0f), new Vector3(0.55f, 1f, 1f));
            // Heavy lids, half closed over the top and bottom.
            foreach (float s in new[] { 1f, -1f })
            {
                // A dome over each half, tipped back so its front lip opens over the iris.
                var lid = MeshKit.Part(rig.Head, s > 0 ? "LidTop" : "LidBottom", MeshKit.Lathe("eyeLid", new[] { new Vector2(0.47f, 0f), new Vector2(0.49f, 0.12f), new Vector2(0.4f, 0.3f), new Vector2(0.2f, 0.44f), new Vector2(0f, 0.48f) }, 16),
                    Mat.Leather, Vector3.zero, new Vector3(s > 0 ? -32f : 148f, 0f, 0f));
                ArtLibrary.Tint(lid.GetComponent<Renderer>(), flesh * 0.85f);
            }
            WeakPoint.Add(rig.Head, new Vector3(0f, 0f, 0.43f), 0.12f, AbyssViolet);
            rig.GlowRenderers = new[] { iris.GetComponent<Renderer>() };
            rig.StrideLength = 0.9f;
            return rig;
        }
    }
}
