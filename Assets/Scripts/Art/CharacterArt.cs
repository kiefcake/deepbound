using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public static partial class CharacterArt
    {
        public static HumanoidRig BuildKnight(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "KnightVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            BuildLegs(rig, 0.13f, 0.95f, Mat.Steel, Mat.Leather, 0.19f);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);

            MeshKit.Part(rig.Torso, "Breastplate", MeshKit.Lathe("knightChest", new[]
            {
                new Vector2(0.20f, 0.00f), new Vector2(0.24f, 0.15f), new Vector2(0.27f, 0.38f),
                new Vector2(0.24f, 0.55f), new Vector2(0.10f, 0.62f),
            }, 8), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.78f));

            var tabardF = MeshKit.Part(rig.Torso, "Tabard", MeshKit.Box(new Vector3(0.3f, 0.75f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.02f, 0.2f));
            var tabardB = MeshKit.Part(rig.Torso, "TabardBack", MeshKit.Box(new Vector3(0.3f, 0.7f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.04f, -0.2f));
            ArtLibrary.Tint(tabardF.GetComponent<Renderer>(), teamColor);
            ArtLibrary.Tint(tabardB.GetComponent<Renderer>(), teamColor);
            MeshKit.Part(rig.Torso, "Belt", MeshKit.Cylinder(0.225f, 0.225f, 0.07f, 8), Mat.Leather, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
            MeshKit.Part(rig.Torso, "Buckle", MeshKit.Box(new Vector3(0.07f, 0.06f, 0.03f)), Mat.Gold, new Vector3(0f, 0.055f, 0.19f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.62f, 0f));

            MeshKit.Part(rig.Head, "Bascinet", MeshKit.Lathe("bascinet", new[]
            {
                new Vector2(0.15f, 0.02f), new Vector2(0.165f, 0.12f), new Vector2(0.15f, 0.25f), new Vector2(0.08f, 0.35f), new Vector2(0.0f, 0.4f),
            }, 12), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 1.08f));
            MeshKit.Part(rig.Head, "Aventail", MeshKit.Cylinder(0.2f, 0.16f, 0.16f, 12), Mat.Steel, new Vector3(0f, -0.06f, -0.01f), Vector3.zero, new Vector3(1f, 1f, 0.95f));
            MeshKit.Part(rig.Head, "Visor", MeshKit.Lathe("hounskull", new[]
            {
                new Vector2(0.13f, 0f), new Vector2(0.1f, 0.08f), new Vector2(0.045f, 0.16f), new Vector2(0f, 0.2f),
            }, 10), Mat.Steel, new Vector3(0f, 0.13f, 0.1f), new Vector3(90f, 0f, 0f), new Vector3(1.05f, 1f, 0.85f));
            MeshKit.Part(rig.Head, "VisorSlitL", MeshKit.Box(new Vector3(0.07f, 0.018f, 0.06f)), Mat.Dark, new Vector3(-0.05f, 0.18f, 0.2f), new Vector3(0f, -20f, 0f));
            MeshKit.Part(rig.Head, "VisorSlitR", MeshKit.Box(new Vector3(0.07f, 0.018f, 0.06f)), Mat.Dark, new Vector3(0.05f, 0.18f, 0.2f), new Vector3(0f, 20f, 0f));
            for (int i = 0; i < 3; i++)
                MeshKit.Part(rig.Head, "Breath" + i, MeshKit.Box(new Vector3(0.012f, 0.012f, 0.05f)), Mat.Dark, new Vector3(0.035f, 0.1f - i * 0.025f, 0.24f - i * 0.01f));
            MeshKit.Part(rig.Head, "Crest", MeshKit.Box(new Vector3(0.02f, 0.04f, 0.18f)), Mat.Gold, new Vector3(0f, 0.37f, -0.02f), new Vector3(10f, 0f, 0f));
            var plume = MeshKit.Part(rig.Head, "Plume", MeshKit.Box(new Vector3(0.04f, 0.1f, 0.34f)), Mat.Cloth, new Vector3(0f, 0.42f, -0.12f), new Vector3(-22f, 0f, 0f));
            ArtLibrary.Tint(plume.GetComponent<Renderer>(), teamColor);

            rig.ArmR = BuildKnightArm(rig.Torso, "ArmR", 1f);
            rig.ArmL = BuildKnightArm(rig.Torso, "ArmL", -1f);

            MeshKit.Part(rig.Torso, "SurcoatCrossV", MeshKit.Box(new Vector3(0.05f, 0.4f, 0.01f)), Mat.Gold, new Vector3(0f, 0.12f, 0.218f));
            MeshKit.Part(rig.Torso, "SurcoatCrossH", MeshKit.Box(new Vector3(0.22f, 0.05f, 0.01f)), Mat.Gold, new Vector3(0f, 0.22f, 0.218f));
            var steel = new Color(0.56f, 0.56f, 0.62f);
            GothicKit.PlateDetails(rig.Torso, rig.Hips, steel);
            var cloakColor = teamColor * 0.45f;
            cloakColor.a = 1f;
            GothicKit.Cloak(rig.Torso, cloakColor, 1.05f, 0.2f, new Color(0.7f, 0.55f, 0.25f));

            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.62f, 0.02f), new Vector3(80f, 0f, 0f));
            BuildSword(grip, 1f);

            var shieldMount = MeshKit.Pivot(rig.ArmL, "ShieldMount", new Vector3(-0.08f, -0.42f, 0.04f), new Vector3(0f, -90f, 0f));
            BuildShield(shieldMount, teamColor, 1f);
            rig.SetGuardShield(shieldMount, new Vector3(-0.12f, 0.28f, 0.6f), new Vector3(-4f, 12f, 0f));

            GothicKit.BlackenSteel(visual, new Color(0.58f, 0.58f, 0.64f));
            rig.StrideLength = 1.5f;
            return rig;
        }

        private static Transform BuildKnightArm(Transform torso, string name, float side)
        {
            var shoulder = MeshKit.Pivot(torso, name, new Vector3(0.29f * side, 0.5f, 0f));

            MeshKit.Part(shoulder, "Pauldron", MeshKit.Lathe("pauldron", new[]
            {
                new Vector2(0.17f, -0.10f), new Vector2(0.16f, 0.0f), new Vector2(0.12f, 0.08f), new Vector2(0.0f, 0.11f),
            }, 8), Mat.Steel, new Vector3(0.03f * side, 0.02f, 0f), new Vector3(0f, 0f, -18f * side));
            MeshKit.Part(shoulder, "PauldronTrim", MeshKit.Cylinder(0.175f, 0.175f, 0.025f, 8), Mat.Gold, new Vector3(0.03f * side, -0.1f, 0f), new Vector3(0f, 0f, -18f * side));

            MeshKit.Part(shoulder, "UpperArm", MeshKit.Box(new Vector3(0.13f, 0.32f, 0.13f)), Mat.Leather, new Vector3(0f, -0.2f, 0f));
            MeshKit.Part(shoulder, "Bracer", MeshKit.Cylinder(0.075f, 0.085f, 0.25f, 6), Mat.Steel, new Vector3(0f, -0.58f, 0f));
            MeshKit.Part(shoulder, "Gauntlet", MeshKit.Ball(0.075f, 6, 4), Mat.Steel, new Vector3(0f, -0.62f, 0.01f));
            return shoulder;
        }

        public static void BuildSword(Transform grip, float scale)
        {
            MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.019f * scale, 0.022f * scale, 0.16f * scale, 8), Mat.Leather, new Vector3(0f, -0.06f * scale, 0f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(grip, "Wrap" + i, MeshKit.Cylinder(0.024f * scale, 0.024f * scale, 0.012f * scale, 8), Mat.Leather, new Vector3(0f, (-0.045f + i * 0.04f) * scale, 0f), new Vector3(0f, 0f, i % 2 == 0 ? 8f : -8f));
            MeshKit.Part(grip, "Pommel", MeshKit.Cylinder(0.042f * scale, 0.042f * scale, 0.026f * scale, 12), Mat.Steel, new Vector3(0f, -0.085f * scale, 0f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(grip, "PommelCap", MeshKit.Ball(0.018f * scale, 6, 4), Mat.Gold, new Vector3(0f, -0.085f * scale, 0f));

            MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.2f, 0.03f, 0.04f) * scale), Mat.Gold, new Vector3(0f, 0.11f * scale, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(grip, "GuardArm", MeshKit.Box(new Vector3(0.06f, 0.026f, 0.036f) * scale), Mat.Gold, new Vector3(s * 0.12f * scale, 0.118f * scale, 0f), new Vector3(0f, 0f, s * 14f));
                MeshKit.Part(grip, "Quillon", MeshKit.Ball(0.022f * scale, 6, 4), Mat.Gold, new Vector3(s * 0.155f * scale, 0.128f * scale, 0f));
            }
            MeshKit.Part(grip, "Ricasso", MeshKit.Box(new Vector3(0.05f, 0.05f, 0.022f) * scale), Mat.Steel, new Vector3(0f, 0.15f * scale, 0f));

            MeshKit.Part(grip, "Blade", MeshKit.Lathe("swordBlade", new[]
            {
                new Vector2(0.034f, 0f), new Vector2(0.032f, 0.5f), new Vector2(0.028f, 0.68f), new Vector2(0f, 0.8f),
            }, 4), Mat.Steel, new Vector3(0f, 0.17f * scale, 0f), new Vector3(0f, 45f, 0f), new Vector3(scale, scale, scale * 0.28f));
            var fuller = MeshKit.Part(grip, "Fuller", MeshKit.Box(new Vector3(0.012f, 0.46f, 0.016f) * scale), Mat.Dark, new Vector3(0f, 0.43f * scale, 0f));
            fuller.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public static void BuildShield(Transform mount, Color teamColor, float scale)
        {
            MeshKit.Part(mount, "ShieldBoard", MeshKit.Box(new Vector3(0.52f, 0.62f, 0.05f) * scale), Mat.Wood, Vector3.zero);
            var face = MeshKit.Part(mount, "ShieldFace", MeshKit.Box(new Vector3(0.44f, 0.54f, 0.02f) * scale), Mat.Cloth, new Vector3(0f, 0f, 0.03f * scale));
            ArtLibrary.Tint(face.GetComponent<Renderer>(), teamColor);

            MeshKit.Part(mount, "ShieldPoint", MeshKit.Box(new Vector3(0.37f, 0.37f, 0.05f) * scale), Mat.Wood,
                new Vector3(0f, -0.31f * scale, 0f), new Vector3(0f, 0f, 45f));
            MeshKit.Part(mount, "RimTop", MeshKit.Box(new Vector3(0.56f, 0.04f, 0.07f) * scale), Mat.Steel, new Vector3(0f, 0.31f * scale, 0f));
            MeshKit.Part(mount, "Boss", MeshKit.Ball(0.07f * scale, 8, 4), Mat.Gold, new Vector3(0f, 0.02f * scale, 0.05f * scale), Vector3.zero, new Vector3(1f, 1f, 0.6f));
            MeshKit.Part(mount, "CrossV", MeshKit.Box(new Vector3(0.06f, 0.5f, 0.025f) * scale), Mat.Gold, new Vector3(0f, -0.02f * scale, 0.045f * scale));
            MeshKit.Part(mount, "CrossH", MeshKit.Box(new Vector3(0.38f, 0.06f, 0.025f) * scale), Mat.Gold, new Vector3(0f, 0.1f * scale, 0.045f * scale));
        }

        public class ViewModel
        {
            public Transform Root;

            public Transform SwordPivot;

            public Transform ShieldPivot;

            public Renderer SpellGlow;

            public Light SpellLight;

            public Renderer Slash;

            public Transform TipR, EdgeR, TipL, EdgeL;
        }

        public static ViewModel BuildKnightViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);

            var swordPivot = MeshKit.Pivot(root, "SwordHand", new Vector3(0.4f, -0.47f, 0.62f), new Vector3(-24f, -10f, 16f));

            MeshKit.Part(swordPivot, "Gauntlet", MeshKit.Ball(0.068f, 8, 6), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 0.85f, 1.25f));
            for (int k = 0; k < 4; k++)
                MeshKit.Part(swordPivot, "Knuckle" + k, MeshKit.Box(new Vector3(0.022f, 0.02f, 0.03f)), Mat.Steel, new Vector3(-0.03f + k * 0.02f, 0.03f, 0.055f), new Vector3(20f, 0f, 0f));
            MeshKit.Part(swordPivot, "Cuff", MeshKit.Cylinder(0.08f, 0.07f, 0.08f, 8), Mat.Steel, new Vector3(0f, -0.03f, -0.07f), new Vector3(-100f, 0f, 0f));
            MeshKit.Part(swordPivot, "Bracer", MeshKit.Cylinder(0.072f, 0.082f, 0.32f, 8), Mat.Steel, new Vector3(0f, -0.06f, -0.14f), new Vector3(-100f, 0f, 0f));
            var sleeve = MeshKit.Part(swordPivot, "Sleeve", MeshKit.Cylinder(0.09f, 0.1f, 0.3f, 8), Mat.Cloth, new Vector3(0f, -0.11f, -0.38f), new Vector3(-100f, 0f, 0f));
            ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), teamColor * 0.8f);
            var grip = MeshKit.Pivot(swordPivot, "Grip", new Vector3(0f, 0.02f, 0.02f), new Vector3(-10f, 0f, 0f));
            BuildSword(grip, 0.62f);
            var tipR = MeshKit.Pivot(grip, "TipMark", new Vector3(0f, 0.95f * 0.62f, 0f));
            var edgeR = MeshKit.Pivot(grip, "EdgeMark", new Vector3(0f, 0.36f * 0.62f, 0f));

            var shieldPivot = MeshKit.Pivot(root, "ShieldHand", new Vector3(-0.55f, -0.5f, 0.7f), new Vector3(8f, 28f, 6f));
            var mount = MeshKit.Pivot(shieldPivot, "Mount", Vector3.zero, new Vector3(0f, 180f, 0f));
            BuildShield(mount, teamColor, 0.62f);

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return new ViewModel { Root = root, SwordPivot = swordPivot, ShieldPivot = shieldPivot, TipR = tipR, EdgeR = edgeR };
        }

        public static HumanoidRig BuildMage(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "MageVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var robe = Color.Lerp(teamColor, new Color(0.13f, 0.1f, 0.18f), 0.78f);
            robe.a = 1f;

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            BuildLegs(rig, 0.11f, 0.95f, Mat.Leather, Mat.Cloth, 0.16f);
            var hem = MeshKit.Part(rig.Hips, "SkirtTrim", MeshKit.Cylinder(0.365f, 0.36f, 0.05f, 9), Mat.Cloth, new Vector3(0f, -0.86f, 0f));
            ArtLibrary.Tint(hem.GetComponent<Renderer>(), teamColor * 0.85f);
            var skirt = MeshKit.Part(rig.Hips, "Skirt", MeshKit.Lathe("mageSkirt", new[]
            {
                new Vector2(0.36f, -0.85f), new Vector2(0.3f, -0.45f), new Vector2(0.22f, 0f), new Vector2(0.2f, 0.06f),
            }, 9), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(skirt.GetComponent<Renderer>(), robe);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            var body = MeshKit.Part(rig.Torso, "Robe", MeshKit.Lathe("mageRobe", new[]
            {
                new Vector2(0.21f, 0f), new Vector2(0.23f, 0.2f), new Vector2(0.24f, 0.42f), new Vector2(0.2f, 0.56f), new Vector2(0.09f, 0.62f),
            }, 9), Mat.Cloth, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(body.GetComponent<Renderer>(), robe);
            MeshKit.Part(rig.Torso, "Sash", MeshKit.Cylinder(0.215f, 0.215f, 0.08f, 9), Mat.Leather, new Vector3(0f, 0.08f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.82f));
            MeshKit.Part(rig.Torso, "Clasp", MeshKit.Ball(0.04f, 6, 4), Mat.Gold, new Vector3(0f, 0.52f, 0.17f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.62f, 0f));
            MeshKit.Part(rig.Head, "Face", MeshKit.Ball(0.13f, 8, 6), Mat.Dark, new Vector3(0f, 0.14f, 0.01f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.016f, 6, 4), Mat.Glow, new Vector3(-0.045f, 0.15f, 0.135f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.016f, 6, 4), Mat.Glow, new Vector3(0.045f, 0.15f, 0.135f));
            var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("mageHood", new[]
            {
                new Vector2(0.17f, -0.02f), new Vector2(0.18f, 0.12f), new Vector2(0.14f, 0.24f), new Vector2(0.05f, 0.28f), new Vector2(0f, 0.29f),
            }, 9, true, 280f), Mat.Cloth, new Vector3(0f, 0.02f, -0.04f), new Vector3(0f, 40f, 0f), new Vector3(1.18f, 1.12f, 1.18f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), robe * 0.8f);

            var brow = GothicKit.Circlet(rig.Head, 0.137f, 0.19f, new Color(0.55f, 0.75f, 1f));
            GothicKit.HighCollar(rig.Torso, robe * 0.5f, 0.24f);
            var mageCloak = robe * 0.5f;
            mageCloak.a = 1f;
            GothicKit.Cloak(rig.Torso, mageCloak, 1.35f, 0.28f, teamColor * 0.8f);
            var tome = MeshKit.Part(rig.Hips, "Tome", MeshKit.Box(new Vector3(0.2f, 0.24f, 0.08f)), Mat.Leather, new Vector3(0.26f, -0.08f, 0.04f), new Vector3(0f, 0f, -10f));
            ArtLibrary.Tint(tome.GetComponent<Renderer>(), new Color(0.3f, 0.12f, 0.14f));
            MeshKit.Part(rig.Hips, "TomeClasp", MeshKit.Box(new Vector3(0.21f, 0.04f, 0.09f)), Mat.Gold, new Vector3(0.26f, -0.08f, 0.04f), new Vector3(0f, 0f, -10f));

            rig.ArmR = BuildSleeveArm(rig.Torso, "ArmR", 1f, robe);
            rig.ArmL = BuildSleeveArm(rig.Torso, "ArmL", -1f, robe);

            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.6f, 0.02f), new Vector3(80f, 0f, 0f));
            MeshKit.Part(grip, "Staff", MeshKit.Lathe("vmStaff", new[] { new Vector2(0.024f, -0.9f), new Vector2(0.02f, -0.3f), new Vector2(0.023f, 0.1f), new Vector2(0.019f, 0.38f), new Vector2(0.03f, 0.44f) }, 7),
                Mat.Wood, new Vector3(0f, 0.4f, 0f), Vector3.zero, new Vector3(1.25f, 1.25f, 1.25f));
            MeshKit.Part(grip, "Collar", MeshKit.Cylinder(0.04f, 0.036f, 0.035f, 8), Mat.Gold, new Vector3(0f, 0.95f, 0f));
            var orb = MeshKit.Part(grip, "Orb", MeshKit.Lathe("vmCrystal", new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.04f), new Vector2(0.026f, 0.1f), new Vector2(0f, 0.15f) }, 6),
                Mat.Glow, new Vector3(0f, 0.97f, 0f), Vector3.zero, Vector3.one * 1.4f);
            for (int i = 0; i < 3; i++)
            {
                var claw = MeshKit.Pivot(grip, "Prong" + i, new Vector3(0f, 0.96f, 0f), new Vector3(0f, i * 120f + 30f, 0f));
                MeshKit.Part(claw, "Lower", MeshKit.Box(new Vector3(0.016f, 0.1f, 0.018f)), Mat.Gold, new Vector3(0f, 0.045f, 0.05f), new Vector3(22f, 0f, 0f));
                MeshKit.Part(claw, "Upper", MeshKit.Box(new Vector3(0.013f, 0.09f, 0.015f)), Mat.Gold, new Vector3(0f, 0.13f, 0.056f), new Vector3(-28f, 0f, 0f));
            }

            rig.GlowColor = new Color(0.55f, 0.75f, 1f);
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), orb.GetComponent<Renderer>(), brow };
            rig.StrideLength = 1.4f;
            return rig;
        }

        public static HumanoidRig BuildCleric(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "ClericVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var white = new Color(0.88f, 0.85f, 0.78f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            BuildLegs(rig, 0.11f, 0.95f, Mat.Leather, Mat.Cloth, 0.16f);
            var skirt = MeshKit.Part(rig.Hips, "Vestment", MeshKit.Lathe("clericSkirt", new[]
            {
                new Vector2(0.34f, -0.85f), new Vector2(0.29f, -0.45f), new Vector2(0.22f, 0f), new Vector2(0.2f, 0.06f),
            }, 10), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(skirt.GetComponent<Renderer>(), white);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            var body = MeshKit.Part(rig.Torso, "Robe", MeshKit.Lathe("mageRobe", new[]
            {
                new Vector2(0.21f, 0f), new Vector2(0.23f, 0.2f), new Vector2(0.24f, 0.42f), new Vector2(0.2f, 0.56f), new Vector2(0.09f, 0.62f),
            }, 9), Mat.Cloth, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(body.GetComponent<Renderer>(), white);
            var stole = MeshKit.Part(rig.Torso, "Stole", MeshKit.Box(new Vector3(0.12f, 0.9f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.12f, 0.19f));
            ArtLibrary.Tint(stole.GetComponent<Renderer>(), teamColor);
            MeshKit.Part(rig.Torso, "Mantle", MeshKit.Cylinder(0.26f, 0.2f, 0.12f, 10), Mat.Gold, new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.85f));
            MeshKit.Part(rig.Torso, "Sun", MeshKit.Cylinder(0.07f, 0.07f, 0.02f, 12), Mat.Gold, new Vector3(0f, 0.36f, 0.2f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(rig.Hips, "Book", MeshKit.Box(new Vector3(0.18f, 0.22f, 0.07f)), Mat.Leather, new Vector3(-0.24f, -0.05f, 0.05f), new Vector3(0f, 0f, 8f));
            GothicKit.Cloak(rig.Torso, white * 0.92f, 1.25f, 0.26f, new Color(0.85f, 0.65f, 0.25f));

            MeshKit.Part(rig.Hips, "CenserChain", MeshKit.Box(new Vector3(0.012f, 0.3f, 0.012f)), Mat.Gold, new Vector3(0.25f, -0.18f, 0.06f));
            MeshKit.Part(rig.Hips, "Censer", MeshKit.Lathe("censer", new[]
            {
                new Vector2(0.02f, 0f), new Vector2(0.07f, 0.03f), new Vector2(0.075f, 0.08f), new Vector2(0.03f, 0.13f), new Vector2(0f, 0.15f),
            }, 10), Mat.Gold, new Vector3(0.25f, -0.43f, 0.06f));
            var ember = MeshKit.Part(rig.Hips, "CenserEmber", MeshKit.Ball(0.03f, 6, 4), Mat.Glow, new Vector3(0.25f, -0.36f, 0.06f));
            ArtLibrary.SetGlow(ember.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 1.4f);
            for (int i = 0; i < 9; i++)
            {
                float a = (200f + i * 16f) * Mathf.Deg2Rad;
                MeshKit.Part(rig.Torso, "Bead" + i, MeshKit.Ball(0.018f, 6, 4), Mat.Wood, new Vector3(Mathf.Cos(a) * 0.16f, 0.45f + Mathf.Sin(a) * 0.22f, 0.2f));
            }

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.62f, 0f));
            MeshKit.Part(rig.Head, "Face", MeshKit.Ball(0.13f, 8, 6), Mat.Dark, new Vector3(0f, 0.14f, 0.01f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.016f, 6, 4), Mat.Glow, new Vector3(-0.045f, 0.15f, 0.12f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.016f, 6, 4), Mat.Glow, new Vector3(0.045f, 0.15f, 0.12f));
            var cowl = MeshKit.Part(rig.Head, "Cowl", MeshKit.Lathe("mageHood", new[]
            {
                new Vector2(0.17f, -0.02f), new Vector2(0.18f, 0.12f), new Vector2(0.14f, 0.24f), new Vector2(0.05f, 0.28f), new Vector2(0f, 0.29f),
            }, 9, true, 280f), Mat.Cloth, new Vector3(0f, 0.03f, -0.02f), new Vector3(0f, 40f, 0f));
            ArtLibrary.Tint(cowl.GetComponent<Renderer>(), white * 0.9f);

            var mitre = MeshKit.Part(rig.Head, "Mitre", MeshKit.Lathe("mitre", new[]
            {
                new Vector2(0.15f, 0f), new Vector2(0.16f, 0.15f), new Vector2(0.12f, 0.32f), new Vector2(0f, 0.42f),
            }, 10), Mat.Cloth, new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.6f));
            ArtLibrary.Tint(mitre.GetComponent<Renderer>(), white);
            MeshKit.Part(rig.Head, "MitreBand", MeshKit.Cylinder(0.155f, 0.155f, 0.05f, 10), Mat.Gold, new Vector3(0f, 0.26f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.62f));
            MeshKit.Part(rig.Head, "MitreCross", MeshKit.Box(new Vector3(0.03f, 0.16f, 0.02f)), Mat.Gold, new Vector3(0f, 0.42f, 0.08f));

            var robeSleeve = white * 0.95f;
            robeSleeve.a = 1f;
            rig.ArmR = BuildSleeveArm(rig.Torso, "ArmR", 1f, robeSleeve);
            rig.ArmL = BuildSleeveArm(rig.Torso, "ArmL", -1f, robeSleeve);

            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.6f, 0.02f), new Vector3(80f, 0f, 0f));
            MeshKit.Part(grip, "Sceptre", MeshKit.Cylinder(0.022f, 0.026f, 0.9f, 6), Mat.Gold, new Vector3(0f, -0.25f, 0f));
            var sun = MeshKit.Part(grip, "SunDisc", MeshKit.Cylinder(0.11f, 0.11f, 0.03f, 14), Mat.Glow, new Vector3(0f, 0.7f, 0f), new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 8; i++)
            {
                MeshKit.Part(grip, "Ray" + i, MeshKit.Box(new Vector3(0.02f, 0.08f, 0.02f)), Mat.Gold,
                    new Vector3(0f, 0.7f, 0f) + Quaternion.Euler(0f, 0f, i * 45f) * new Vector3(0f, 0.15f, 0f), new Vector3(0f, 0f, i * 45f));
            }

            rig.GlowColor = new Color(1f, 0.85f, 0.45f);
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), sun.GetComponent<Renderer>() };
            rig.StrideLength = 1.4f;
            return rig;
        }

        public static ViewModel BuildClericViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var white = new Color(0.88f, 0.85f, 0.78f);

            var hand = MeshKit.Pivot(root, "SceptreHand", new Vector3(0.36f, -0.4f, 0.62f), new Vector3(16f, -8f, -12f));
            CasterArm(hand, white, teamColor, true);

            MeshKit.Part(hand, "Sceptre", MeshKit.Lathe("vmSceptre", new[] { new Vector2(0.02f, -0.6f), new Vector2(0.016f, 0.1f), new Vector2(0.03f, 0.14f), new Vector2(0.016f, 0.18f), new Vector2(0.016f, 0.36f), new Vector2(0.028f, 0.4f) }, 8), Mat.Gold, Vector3.zero);
            var sunAt = MeshKit.Pivot(hand, "Sun", new Vector3(0f, 0.47f, 0f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(sunAt, "Ring", MeshKit.Cylinder(0.05f, 0.05f, 0.016f, 16), Mat.Gold, Vector3.zero);
            var sun = MeshKit.Part(sunAt, "SunDisc", MeshKit.Cylinder(0.036f, 0.036f, 0.022f, 14), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(sun.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.45f) * 1.1f);
            for (int i = 0; i < 8; i++)
                MeshKit.Part(sunAt, "Ray" + i, MeshKit.Box(new Vector3(0.012f, 0.012f, i % 2 == 0 ? 0.07f : 0.045f)), Mat.Gold,
                    Quaternion.Euler(0f, i * 45f, 0f) * new Vector3(0f, 0f, i % 2 == 0 ? 0.08f : 0.068f), new Vector3(0f, i * 45f, 0f));

            var castHand = MeshKit.Pivot(root, "CastHand", new Vector3(-0.38f, -0.37f, 0.58f), new Vector3(-30f, 24f, 12f));
            CasterArm(castHand, white, teamColor, false);
            var glow = PalmFlame(castHand, new Color(1f, 0.85f, 0.45f), out var palmLight);

            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = hand, ShieldPivot = castHand, SpellGlow = glow, SpellLight = palmLight };
        }

        public static HumanoidRig BuildRogue(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "RogueVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var leather = new Color(0.32f, 0.27f, 0.24f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.93f, 0f));
            BuildLegs(rig, 0.11f, 0.93f, Mat.Leather, Mat.Leather, 0.15f);
            TintLegs(rig, new Color(0.24f, 0.22f, 0.22f), new Color(0.16f, 0.12f, 0.1f));

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            var jerkin = MeshKit.Part(rig.Torso, "Jerkin", MeshKit.Lathe("rogueChest", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.2f, 0.18f), new Vector2(0.22f, 0.4f), new Vector2(0.19f, 0.55f), new Vector2(0.08f, 0.6f),
            }, 8), Mat.Leather, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.78f));
            ArtLibrary.Tint(jerkin.GetComponent<Renderer>(), leather);
            for (int i = 0; i < 3; i++)
                MeshKit.Part(rig.Torso, "Strap" + i, MeshKit.Box(new Vector3(0.36f, 0.03f, 0.3f)), Mat.Leather, new Vector3(0f, 0.15f + i * 0.14f, 0f), new Vector3(0f, 0f, i == 1 ? 25f : 0f));
            var rogueCloak = teamColor * 0.35f;
            rogueCloak.a = 1f;
            GothicKit.Cloak(rig.Torso, rogueCloak, 0.85f, 0.12f, null, tattered: true);

            MeshKit.Part(rig.Torso, "Bandolier", MeshKit.Box(new Vector3(0.06f, 0.62f, 0.4f)), Mat.Leather, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0f, -34f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(rig.Torso, "Knife" + i, MeshKit.Box(new Vector3(0.025f, 0.11f, 0.008f)), Mat.Steel,
                    new Vector3(-0.1f + i * 0.065f, 0.18f + i * 0.09f, 0.21f), new Vector3(0f, 0f, -34f));
            MeshKit.Part(rig.Hips, "Belt", MeshKit.Cylinder(0.19f, 0.19f, 0.06f, 8), Mat.Leather, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
            MeshKit.Part(rig.Hips, "Pouch", MeshKit.Box(new Vector3(0.1f, 0.1f, 0.06f)), Mat.Leather, new Vector3(0.17f, -0.03f, 0.1f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.6f, 0f));
            MeshKit.Part(rig.Head, "Face", MeshKit.Ball(0.12f, 8, 6), Mat.Dark, new Vector3(0f, 0.13f, 0.01f));
            var mask = MeshKit.Part(rig.Head, "Mask", MeshKit.Box(new Vector3(0.2f, 0.08f, 0.04f)), Mat.Cloth, new Vector3(0f, 0.08f, 0.1f));
            ArtLibrary.Tint(mask.GetComponent<Renderer>(), new Color(0.36f, 0.33f, 0.3f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.014f, 6, 4), Mat.Glow, new Vector3(-0.04f, 0.15f, 0.11f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.014f, 6, 4), Mat.Glow, new Vector3(0.04f, 0.15f, 0.11f));
            var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("rogueHood", new[]
            {
                new Vector2(0.16f, -0.04f), new Vector2(0.18f, 0.1f), new Vector2(0.15f, 0.24f), new Vector2(0.06f, 0.33f), new Vector2(0f, 0.36f),
            }, 9, true, 260f), Mat.Cloth, new Vector3(0f, 0.04f, -0.03f), new Vector3(10f, 50f, 0f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), teamColor * 0.55f);

            rig.ArmR = BuildSleeveArm(rig.Torso, "ArmR", 1f, leather);
            rig.ArmL = BuildSleeveArm(rig.Torso, "ArmL", -1f, leather);
            foreach (var arm in new[] { rig.ArmR, rig.ArmL })
            {
                var grip = MeshKit.Pivot(arm, "Grip", new Vector3(0f, -0.6f, 0.02f), new Vector3(80f, 0f, 0f));
                BuildDagger(grip, 1f);
            }

            rig.GlowColor = new Color(0.75f, 0.55f, 1f);
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 1.3f;
            return rig;
        }

        private static void BuildDagger(Transform grip, float scale)
        {
            MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.016f * scale, 0.016f * scale, 0.1f * scale, 6), Mat.Leather, new Vector3(0f, -0.04f * scale, 0f));
            MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.08f, 0.015f, 0.025f) * scale), Mat.Steel, new Vector3(0f, 0.065f * scale, 0f));
            MeshKit.Part(grip, "Blade", MeshKit.Box(new Vector3(0.035f, 0.26f, 0.008f) * scale), Mat.Steel, new Vector3(0f, 0.2f * scale, 0f));
            MeshKit.Part(grip, "Tip", MeshKit.Lathe("daggerTip", new[] { new Vector2(0.025f, 0f), new Vector2(0f, 0.06f) }, 4), Mat.Steel,
                new Vector3(0f, 0.33f * scale, 0f), new Vector3(0f, 45f, 0f), new Vector3(scale, scale, scale * 0.3f));
        }

        public static HumanoidRig BuildRanger(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "RangerVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var green = new Color(0.25f, 0.38f, 0.22f);
            var leather = new Color(0.42f, 0.32f, 0.22f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.94f, 0f));
            BuildLegs(rig, 0.11f, 0.94f, Mat.Leather, Mat.Leather, 0.15f);
            TintLegs(rig, new Color(0.32f, 0.36f, 0.22f), new Color(0.3f, 0.2f, 0.12f));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            var jerkin = MeshKit.Part(rig.Torso, "Jerkin", MeshKit.Lathe("rogueChest", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.2f, 0.18f), new Vector2(0.22f, 0.4f), new Vector2(0.19f, 0.55f), new Vector2(0.08f, 0.6f),
            }, 8), Mat.Leather, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.78f));
            ArtLibrary.Tint(jerkin.GetComponent<Renderer>(), leather);
            GothicKit.Cloak(rig.Torso, green * 0.85f, 1.15f, 0.2f, null, tattered: true);
            var sash = MeshKit.Part(rig.Torso, "Sash", MeshKit.Box(new Vector3(0.08f, 0.6f, 0.36f)), Mat.Cloth, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0f, 32f));
            ArtLibrary.Tint(sash.GetComponent<Renderer>(), teamColor);
            MeshKit.Part(rig.Torso, "Quiver", MeshKit.Cylinder(0.07f, 0.08f, 0.55f, 6), Mat.Leather, new Vector3(0.12f, 0.25f, -0.18f), new Vector3(-15f, 0f, 20f));
            for (int i = 0; i < 4; i++)
            {
                var f = MeshKit.Part(rig.Torso, "Fletch" + i, MeshKit.Box(new Vector3(0.015f, 0.12f, 0.04f)), Mat.Cloth,
                    new Vector3(0.14f + i * 0.02f, 0.8f, -0.27f + i * 0.015f), new Vector3(-15f, 0f, 20f));
                ArtLibrary.Tint(f.GetComponent<Renderer>(), teamColor);
            }

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.6f, 0f));
            MeshKit.Part(rig.Head, "Face", MeshKit.Ball(0.12f, 8, 6), Mat.Dark, new Vector3(0f, 0.13f, 0.01f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.014f, 6, 4), Mat.Glow, new Vector3(-0.04f, 0.15f, 0.11f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.014f, 6, 4), Mat.Glow, new Vector3(0.04f, 0.15f, 0.11f));
            var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("rangerHood", new[]
            {
                new Vector2(0.16f, -0.04f), new Vector2(0.18f, 0.1f), new Vector2(0.15f, 0.24f), new Vector2(0.06f, 0.36f), new Vector2(0f, 0.42f),
            }, 9, true, 270f), Mat.Cloth, new Vector3(0f, 0.04f, -0.03f), new Vector3(14f, 45f, 0f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), green);
            var feather = MeshKit.Part(rig.Head, "Feather", MeshKit.Box(new Vector3(0.012f, 0.24f, 0.05f)), Mat.Cloth, new Vector3(0.14f, 0.3f, -0.08f), new Vector3(-30f, 0f, -25f));
            ArtLibrary.Tint(feather.GetComponent<Renderer>(), teamColor);

            rig.ArmR = BuildSleeveArm(rig.Torso, "ArmR", 1f, leather);
            rig.ArmL = BuildSleeveArm(rig.Torso, "ArmL", -1f, leather);
            foreach (var arm in new[] { rig.ArmR, rig.ArmL })
            {
                var bracer = MeshKit.Part(arm, "Bracer", MeshKit.Cylinder(0.085f, 0.095f, 0.16f, 8), Mat.Leather, new Vector3(0f, -0.46f, 0f));
                ArtLibrary.Tint(bracer.GetComponent<Renderer>(), leather * 0.7f);
            }

            var bowGrip = MeshKit.Pivot(rig.ArmL, "BowGrip", new Vector3(0f, -0.6f, 0.02f));
            for (int i = -3; i <= 3; i++)
            {
                float a = i * 13f * Mathf.Deg2Rad;
                var p = new Vector3(0f, Mathf.Sin(a) * 0.55f, -(1f - Mathf.Cos(a)) * 0.55f);
                MeshKit.Part(bowGrip, "Limb" + i, MeshKit.Box(new Vector3(0.03f, 0.14f, 0.035f)), Mat.Wood, p, new Vector3(-i * 13f, 0f, 0f));
            }
            MeshKit.Part(bowGrip, "Grip", MeshKit.Box(new Vector3(0.04f, 0.1f, 0.045f)), Mat.Leather, Vector3.zero);
            MeshKit.Part(bowGrip, "String", MeshKit.Box(new Vector3(0.006f, 0.7f, 0.006f)), Mat.Bone, new Vector3(0f, 0f, -0.122f));
            rig.OffhandItem = bowGrip;

            rig.GlowColor = new Color(0.5f, 1f, 0.5f);
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 1.35f;
            return rig;
        }

        public static ViewModel BuildRangerViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var leather = new Color(0.42f, 0.32f, 0.22f);

            var bowHand = MeshKit.Pivot(root, "BowHand", new Vector3(-0.27f, -0.23f, 0.7f), new Vector3(0f, 30f, -20f));
            CasterArm(bowHand, leather, teamColor, true);

            const float bowR = 0.5f, step = 12f;
            for (int i = -4; i <= 4; i++)
            {
                float a = i * step * Mathf.Deg2Rad;
                float taper = 1f - Mathf.Abs(i) * 0.12f;
                var p = new Vector3(0f, Mathf.Sin(a) * bowR, (1f - Mathf.Cos(a)) * bowR);
                var limb = MeshKit.Part(bowHand, "Limb" + i, MeshKit.Box(new Vector3(0.026f * taper, 0.11f, 0.034f * taper)), Mat.Wood, p, new Vector3(i * step, 0f, 0f));
                ArtLibrary.Tint(limb.GetComponent<Renderer>(), new Color(0.55f, 0.38f, 0.24f));
            }
            MeshKit.Part(bowHand, "GripWrap", MeshKit.Box(new Vector3(0.034f, 0.12f, 0.04f)), Mat.Leather, Vector3.zero);
            float tipA = 4.5f * step * Mathf.Deg2Rad;
            var tipTop = MeshKit.Pivot(bowHand, "TipTop", new Vector3(0f, Mathf.Sin(tipA) * bowR, (1f - Mathf.Cos(tipA)) * bowR));
            var tipBottom = MeshKit.Pivot(bowHand, "TipBottom", new Vector3(0f, -Mathf.Sin(tipA) * bowR, (1f - Mathf.Cos(tipA)) * bowR));
            MeshKit.Part(tipTop, "Nock", MeshKit.Box(new Vector3(0.022f, 0.035f, 0.026f)), Mat.Bone, Vector3.zero);
            MeshKit.Part(tipBottom, "Nock", MeshKit.Box(new Vector3(0.022f, 0.035f, 0.026f)), Mat.Bone, Vector3.zero);

            var drawEuler = new Vector3(-28f, -38f, 0f);
            var drawHand = MeshKit.Pivot(root, "DrawHand", new Vector3(-0.235f, -0.235f, 0.57f), drawEuler);
            CasterArm(drawHand, leather, teamColor, true);

            var arrow = MeshKit.Pivot(drawHand, "NockedArrow", new Vector3(0.012f, 0.02f, 0.02f));
            arrow.localRotation = Quaternion.Inverse(Quaternion.Euler(drawEuler));
            MeshKit.Part(arrow, "Shaft", MeshKit.Box(new Vector3(0.011f, 0.011f, 0.52f)), Mat.Wood, new Vector3(0f, 0f, 0.26f));
            MeshKit.Part(arrow, "Head", MeshKit.Lathe("arrowHead", new[] { new Vector2(0.02f, 0f), new Vector2(0f, 0.06f) }, 4), Mat.Steel, new Vector3(0f, 0f, 0.52f), new Vector3(90f, 0f, 0f));
            for (int f = 0; f < 3; f++)
            {
                var fl = MeshKit.Part(arrow, "Fletch" + f, MeshKit.Box(new Vector3(0.002f, 0.022f, 0.07f)), Mat.Cloth, new Vector3(0f, 0f, 0.05f), new Vector3(0f, 0f, f * 120f));
                ArtLibrary.Tint(fl.GetComponent<Renderer>(), f == 0 ? teamColor : new Color(0.85f, 0.82f, 0.75f));
            }
            var nock = MeshKit.Pivot(drawHand, "StringNock", new Vector3(0.012f, 0.02f, 0.02f));

            var seg = MeshKit.Box(new Vector3(0.004f, 0.004f, 1f));
            var upper = MeshKit.Part(root, "StringUpper", seg, Mat.Bone, Vector3.zero).transform;
            var lower = MeshKit.Part(root, "StringLower", seg, Mat.Bone, Vector3.zero).transform;
            var bs = root.gameObject.AddComponent<BowString>();
            bs.TipTop = tipTop; bs.TipBottom = tipBottom; bs.Nock = nock; bs.Upper = upper; bs.Lower = lower;

            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = drawHand, ShieldPivot = bowHand };
        }

        public static ViewModel BuildRogueViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var leather = new Color(0.32f, 0.27f, 0.24f);
            Transform Hand(string name, Vector3 pos, Vector3 euler)
            {
                var hand = MeshKit.Pivot(root, name, pos, euler);
                CasterArm(hand, leather, teamColor, true);
                var grip = MeshKit.Pivot(hand, "Grip", new Vector3(0f, 0.01f, 0.02f), new Vector3(-10f, 0f, 0f));
                BuildDagger(grip, 0.85f);
                MeshKit.Pivot(grip, "TipMark", new Vector3(0f, 0.38f * 0.85f, 0f));
                MeshKit.Pivot(grip, "EdgeMark", new Vector3(0f, 0.12f * 0.85f, 0f));
                return hand;
            }
            var right = Hand("RightHand", new Vector3(0.3f, -0.36f, 0.5f), new Vector3(-25f, -10f, -15f));
            var left = Hand("LeftHand", new Vector3(-0.3f, -0.36f, 0.5f), new Vector3(-25f, 10f, 15f));
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = right, ShieldPivot = left,
                TipR = right.Find("Grip/TipMark"), EdgeR = right.Find("Grip/EdgeMark"), TipL = left.Find("Grip/TipMark"), EdgeL = left.Find("Grip/EdgeMark") };
        }

        private static Transform BuildSleeveArm(Transform torso, string name, float side, Color robe)
        {
            var shoulder = MeshKit.Pivot(torso, name, new Vector3(0.25f * side, 0.5f, 0f));
            var upper = MeshKit.Part(shoulder, "Sleeve", MeshKit.Cylinder(0.075f, 0.1f, 0.5f, 7), Mat.Cloth, new Vector3(0f, -0.5f, 0f));
            ArtLibrary.Tint(upper.GetComponent<Renderer>(), robe);
            var cuff = MeshKit.Part(shoulder, "Cuff", MeshKit.Cylinder(0.105f, 0.11f, 0.06f, 8), Mat.Cloth, new Vector3(0f, -0.5f, 0f));
            ArtLibrary.Tint(cuff.GetComponent<Renderer>(), robe * 0.6f);
            MeshKit.Part(shoulder, "Hand", MeshKit.Ball(0.06f, 6, 4), Mat.Leather, new Vector3(0f, -0.6f, 0.01f));
            return shoulder;
        }

        public static ViewModel BuildMageViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var robe = Color.Lerp(teamColor, new Color(0.13f, 0.1f, 0.18f), 0.78f);
            robe.a = 1f;

            var staffHand = MeshKit.Pivot(root, "StaffHand", new Vector3(0.36f, -0.4f, 0.62f), new Vector3(16f, -8f, -12f));
            CasterArm(staffHand, robe, teamColor, true);

            MeshKit.Part(staffHand, "Staff", MeshKit.Lathe("vmStaff", new[] { new Vector2(0.024f, -0.9f), new Vector2(0.02f, -0.3f), new Vector2(0.023f, 0.1f), new Vector2(0.019f, 0.38f), new Vector2(0.03f, 0.44f) }, 7), Mat.Wood, Vector3.zero);
            foreach (float y in new[] { 0.0f, 0.06f, 0.36f })
                MeshKit.Part(staffHand, "Binding", MeshKit.Cylinder(0.027f, 0.027f, 0.022f, 8), Mat.Leather, new Vector3(0f, y, 0f));
            MeshKit.Part(staffHand, "Collar", MeshKit.Cylinder(0.034f, 0.03f, 0.03f, 8), Mat.Gold, new Vector3(0f, 0.44f, 0f));
            var crystal = MeshKit.Part(staffHand, "Crystal", MeshKit.Lathe("vmCrystal", new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0.04f), new Vector2(0.026f, 0.1f), new Vector2(0f, 0.15f) }, 6), Mat.Glow, new Vector3(0f, 0.47f, 0f));
            ArtLibrary.SetGlow(crystal.GetComponent<Renderer>(), new Color(0.45f, 0.65f, 1f) * 1.1f);
            for (int i = 0; i < 3; i++)
            {
                var claw = MeshKit.Pivot(staffHand, "Claw" + i, new Vector3(0f, 0.45f, 0f), new Vector3(0f, i * 120f, 0f));
                MeshKit.Part(claw, "Lower", MeshKit.Box(new Vector3(0.012f, 0.08f, 0.014f)), Mat.Gold, new Vector3(0f, 0.035f, 0.04f), new Vector3(22f, 0f, 0f));
                MeshKit.Part(claw, "Upper", MeshKit.Box(new Vector3(0.01f, 0.07f, 0.012f)), Mat.Gold, new Vector3(0f, 0.1f, 0.045f), new Vector3(-28f, 0f, 0f));
            }

            var castHand = MeshKit.Pivot(root, "CastHand", new Vector3(-0.38f, -0.37f, 0.58f), new Vector3(-30f, 24f, 12f));
            CasterArm(castHand, robe, teamColor, false);
            var spell = PalmFlame(castHand, new Color(1f, 0.5f, 0.15f), out var light);

            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = staffHand, ShieldPivot = castHand, SpellGlow = spell, SpellLight = light };
        }

        private static void CasterArm(Transform hand, Color robe, Color trim, bool grip)
        {
            var sleeve = MeshKit.Part(hand, "Sleeve", MeshKit.Cylinder(0.075f, 0.11f, 0.42f, 9), Mat.Cloth, new Vector3(0f, -0.06f, -0.24f), new Vector3(-100f, 0f, 0f));
            ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), robe);

            var cuff = MeshKit.Part(hand, "Cuff", MeshKit.Cylinder(0.1f, 0.085f, 0.07f, 9), Mat.Cloth, new Vector3(0f, -0.025f, -0.06f), new Vector3(-100f, 0f, 0f));
            ArtLibrary.Tint(cuff.GetComponent<Renderer>(), robe * 0.7f);
            var band = MeshKit.Part(hand, "CuffTrim", MeshKit.Cylinder(0.102f, 0.102f, 0.016f, 9), Mat.Cloth, new Vector3(0f, -0.02f, -0.035f), new Vector3(-100f, 0f, 0f));
            ArtLibrary.Tint(band.GetComponent<Renderer>(), Color.Lerp(trim, new Color(0.9f, 0.7f, 0.3f), 0.5f));
            MeshKit.Part(hand, "Palm", MeshKit.Ball(0.05f, 8, 6), Mat.Leather, Vector3.zero, Vector3.zero, new Vector3(1.05f, 0.55f, 1.2f));
            for (int f = 0; f < 4; f++)
            {
                float x = -0.033f + f * 0.022f;
                if (grip)
                    MeshKit.Part(hand, "Finger" + f, MeshKit.Box(new Vector3(0.017f, 0.02f, 0.045f)), Mat.Leather, new Vector3(x, 0.012f, 0.045f), new Vector3(60f, 0f, 0f));
                else
                    MeshKit.Part(hand, "Finger" + f, MeshKit.Box(new Vector3(0.015f, 0.014f, 0.06f - Mathf.Abs(f - 1.5f) * 0.008f)), Mat.Leather, new Vector3(x, 0.006f, 0.07f), new Vector3(-8f, (f - 1.5f) * 7f, 0f));
            }
            MeshKit.Part(hand, "Thumb", MeshKit.Box(new Vector3(0.016f, 0.016f, 0.045f)), Mat.Leather, new Vector3(grip ? -0.05f : 0.055f, 0.012f, 0.03f), new Vector3(0f, grip ? 40f : -40f, 0f));
        }

        private static Renderer PalmFlame(Transform hand, Color color, out Light light)
        {
            var at = MeshKit.Pivot(hand, "Flame", new Vector3(0.005f, 0.05f, 0.05f));
            at.localScale = Vector3.one * 0.65f;
            var core = MeshKit.Part(at, "Core", MeshKit.Lathe("vmFlame", new[] { new Vector2(0f, -0.02f), new Vector2(0.026f, 0.0f), new Vector2(0.022f, 0.035f), new Vector2(0.008f, 0.075f), new Vector2(0f, 0.09f) }, 8), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), color * 1.2f);

            foreach (float side in new[] { -1f, 1f })
            {
                var lick = MeshKit.Part(at, "Lick", MeshKit.Lathe("vmFlame", new[] { new Vector2(0f, -0.02f), new Vector2(0.026f, 0.0f), new Vector2(0.022f, 0.035f), new Vector2(0.008f, 0.075f), new Vector2(0f, 0.09f) }, 8), Mat.Glow,
                    new Vector3(side * 0.018f, -0.005f, 0.004f), new Vector3(0f, 0f, -side * 24f), Vector3.one * 0.55f);
                ArtLibrary.SetGlow(lick.GetComponent<Renderer>(), color * 0.9f);
            }

            light = null;
            return core.GetComponent<Renderer>();
        }

        public enum SkeletonKind { Warrior, King, Archer, Gravewarden, Lich, Ghoul, Brute, Necromancer, Wraith, Cardinal, Shieldbearer, Mimic, Hoarder, Gargoyle, Plaguebearer, FrostWight, Hexer, Mourner, Unsung, Entling, Orbweaver, ChoirAcolyte, AbyssEye, BonePiper, Cantor, RimeMagus, InkLamprey, WitheringHag, Bellringer }

        public static int SkeletonVariant;

        public static HumanoidRig BuildSkeleton(Transform root, SkeletonKind kind)
        {
            if (kind == SkeletonKind.Wraith) return BuildWraith(root);
            if (kind == SkeletonKind.Mimic) return BuildMimic(root);
            if (kind == SkeletonKind.AbyssEye) return BuildAbyssEye(root);
            if (kind == SkeletonKind.Gargoyle) return BuildGargoyle(root);
            if (kind == SkeletonKind.Mourner) return BuildMourner(root);
            if (kind == SkeletonKind.Unsung) return BuildUnsung(root);
            if (kind == SkeletonKind.Entling) return BuildEntling(root);
            bool king = kind == SkeletonKind.King;
            float s = king ? 1.75f : kind == SkeletonKind.Gravewarden ? 2.0f : kind == SkeletonKind.Lich ? 1.45f : kind == SkeletonKind.Cardinal ? 1.7f
                : kind == SkeletonKind.Brute ? 1.45f : kind == SkeletonKind.Plaguebearer ? 1.15f : kind == SkeletonKind.Ghoul ? 0.92f : kind == SkeletonKind.Necromancer ? 1.05f : 1f;

            var visual = MeshKit.Pivot(root, "SkeletonVisual", Vector3.zero);
            visual.localScale = Vector3.one * s;
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = king ? new Color(0.4f, 0.75f, 1f)
                : kind == SkeletonKind.Gravewarden ? new Color(1f, 0.55f, 0.15f)
                : kind == SkeletonKind.Lich ? new Color(0.3f, 1f, 0.7f)
                : kind == SkeletonKind.Cardinal ? new Color(0.8f, 0.55f, 0.22f)
                : kind == SkeletonKind.Ghoul || kind == SkeletonKind.Plaguebearer ? new Color(0.6f, 1f, 0.2f)
                : kind == SkeletonKind.FrostWight ? new Color(0.55f, 0.85f, 1f)
                : kind == SkeletonKind.Hexer ? new Color(0.7f, 0.3f, 1f)
                : kind == SkeletonKind.Orbweaver ? new Color(1f, 0.62f, 0.2f)
                : kind == SkeletonKind.ChoirAcolyte ? new Color(1f, 0.82f, 0.4f)
                : kind == SkeletonKind.BonePiper ? new Color(0.4f, 1f, 0.9f)
                : kind == SkeletonKind.Cantor || kind == SkeletonKind.Bellringer ? new Color(0.78f, 0.78f, 1f)
                : kind == SkeletonKind.RimeMagus ? new Color(0.65f, 0.92f, 1f)
                : kind == SkeletonKind.InkLamprey ? new Color(0.6f, 0.4f, 0.95f)
                : kind == SkeletonKind.WitheringHag ? new Color(0.62f, 0.9f, 0.25f)
                : kind == SkeletonKind.Necromancer ? new Color(0.75f, 0.35f, 1f)
                : kind == SkeletonKind.Brute ? new Color(1f, 0.4f, 0.1f)
                : new Color(1f, 0.25f, 0.1f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.92f, 0f));
            MeshKit.Part(rig.Hips, "Pelvis", MeshKit.Lathe("pelvis", new[]
            {
                new Vector2(0.08f, -0.08f), new Vector2(0.17f, -0.02f), new Vector2(0.15f, 0.05f), new Vector2(0.06f, 0.06f),
            }, 6), Mat.Bone, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.6f));
            BuildBoneLegs(rig);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);

            for (int v = 0; v < 8; v++)
                MeshKit.Part(rig.Torso, "Vertebra" + v, MeshKit.Lathe("vertebra", new[] { new Vector2(0.018f, 0f), new Vector2(0.03f, 0.012f), new Vector2(0.03f, 0.03f), new Vector2(0.018f, 0.042f) }, 6),
                    Mat.Bone, new Vector3(0f, 0.04f + v * 0.068f, -0.07f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
            MeshKit.Part(rig.Torso, "Sternum", MeshKit.Box(new Vector3(0.04f, 0.26f, 0.02f)), Mat.Bone, new Vector3(0f, 0.43f, 0.105f), new Vector3(-6f, 0f, 0f));

            for (int i = 0; i < 5; i++)
            {
                float y = 0.26f + i * 0.065f;
                float r = 0.12f + i * 0.012f;
                MeshKit.Part(rig.Torso, "Rib" + i, MeshKit.Lathe("rib" + i, new[]
                {
                    new Vector2(r, 0f), new Vector2(r + 0.015f, 0.015f), new Vector2(r, 0.03f),
                }, 8, true, 300f), Mat.Bone, new Vector3(0f, y, 0f), new Vector3(0f, 210f, 0f), new Vector3(1f, 1f, 0.75f));
            }
            MeshKit.Part(rig.Torso, "Collar", MeshKit.Box(new Vector3(0.42f, 0.04f, 0.06f)), Mat.Bone, new Vector3(0f, 0.6f, 0f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.66f, 0f));
            MeshKit.Part(rig.Head, "Neck", MeshKit.Cylinder(0.025f, 0.025f, 0.08f, 5), Mat.Bone, new Vector3(0f, -0.05f, 0f));
            BuildSkullDetail(rig.Head);

            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.012f, 6, 4), Mat.Glow, new Vector3(-0.045f, 0.128f, 0.118f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.012f, 6, 4), Mat.Glow, new Vector3(0.045f, 0.128f, 0.118f));

            rig.ArmR = BuildBoneArm(rig.Torso, "ArmR", 1f);
            rig.ArmL = BuildBoneArm(rig.Torso, "ArmL", -1f);

            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.6f, 0f), new Vector3(80f, 0f, 0f));

            if (king)
            {
                MeshKit.Part(rig.Head, "Crown", MeshKit.Cylinder(0.12f, 0.13f, 0.06f, 8), Mat.Gold, new Vector3(0f, 0.22f, 0f));
                var glow = new System.Collections.Generic.List<Renderer> { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f;
                    var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    MeshKit.Part(rig.Head, "CrownPoint" + i, MeshKit.Lathe("crownPoint", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.09f) }, 4),
                        Mat.Gold, new Vector3(0f, 0.28f, 0f) + dir * 0.12f);
                    if (i % 2 == 0)
                    {
                        var gem = MeshKit.Part(rig.Head, "Gem" + i, MeshKit.Ball(0.016f, 6, 4), Mat.Glow, new Vector3(0f, 0.25f, 0f) + dir * 0.13f);
                        glow.Add(gem.GetComponent<Renderer>());
                    }
                }
                rig.GlowRenderers = glow.ToArray();

                WeakPoint.Add(rig.Head, new Vector3(0f, 0.2f, 0.13f), 0.05f, new Color(0.45f, 0.75f, 1f));

                GothicKit.Cloak(rig.Torso, new Color(0.3f, 0.07f, 0.34f), 1.15f, 0.28f, new Color(0.85f, 0.65f, 0.25f), true);
                var mantle = MeshKit.Part(rig.Torso, "Mantle", MeshKit.Lathe("royalMantle", new[]
                {
                    new Vector2(0.2f, 0.46f), new Vector2(0.31f, 0.52f), new Vector2(0.3f, 0.6f), new Vector2(0.2f, 0.66f), new Vector2(0.12f, 0.68f),
                }, 12), Mat.Cloth, Vector3.zero, Vector3.zero, new Vector3(1.05f, 1f, 0.85f));
                ArtLibrary.Tint(mantle.GetComponent<Renderer>(), new Color(0.86f, 0.83f, 0.76f));
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 36f * Mathf.Deg2Rad;
                    MeshKit.Part(rig.Torso, "Ermine" + i, MeshKit.Box(new Vector3(0.022f, 0.045f, 0.012f)), Mat.Dark,
                        new Vector3(Mathf.Sin(a) * 0.31f * 1.05f, 0.535f + (i % 2) * 0.03f, Mathf.Cos(a) * 0.31f * 0.85f), new Vector3(0f, i * 36f, 0f));
                }

                MeshKit.Part(rig.Head, "CrownBand", MeshKit.Cylinder(0.135f, 0.135f, 0.035f, 12), Mat.Gold, new Vector3(0f, 0.2f, 0f));
                for (int i = 0; i < 5; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 72f + 36f, 0f) * Vector3.forward;
                    MeshKit.Part(rig.Head, "Tine" + i, MeshKit.Lathe("crownTine", new[] { new Vector2(0.028f, 0f), new Vector2(0.02f, 0.07f), new Vector2(0f, 0.16f) }, 4),
                        Mat.Gold, new Vector3(0f, 0.25f, 0f) + dir * 0.125f, new Vector3(0f, 0f, 0f));
                    MeshKit.Part(rig.Head, "TineBall" + i, MeshKit.Ball(0.016f, 6, 4), Mat.Gold, new Vector3(0f, 0.415f, 0f) + dir * 0.125f);
                }

                MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.025f, 0.025f, 0.3f, 6), Mat.Leather, new Vector3(0f, -0.12f, 0f));
                MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.36f, 0.05f, 0.06f)), Mat.Gold, new Vector3(0f, 0.04f, 0f));
                MeshKit.Part(grip, "Blade", MeshKit.Box(new Vector3(0.1f, 1.15f, 0.02f)), Mat.Steel, new Vector3(0f, 0.64f, 0f));
                var rune = MeshKit.Part(grip, "Rune", MeshKit.Box(new Vector3(0.025f, 0.9f, 0.024f)), Mat.Glow, new Vector3(0f, 0.6f, 0f));
                glow.Add(rune.GetComponent<Renderer>());
                rig.GlowRenderers = glow.ToArray();
                rig.StrideLength = 1.4f;
            }
            else if (kind == SkeletonKind.Hoarder)
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
                WeakPoint.Add(rig.Torso, new Vector3(0.05f, 0.62f, -0.5f), 0.12f, new Color(1f, 0.82f, 0.3f));
                var sack = MeshKit.Part(rig.Torso, "Sack", MeshKit.Ball(0.36f, 10, 8), Mat.Cloth, new Vector3(0.05f, 0.55f, -0.32f), new Vector3(10f, 0f, 15f), new Vector3(1f, 1.15f, 0.9f));
                ArtLibrary.Tint(sack.GetComponent<Renderer>(), new Color(0.45f, 0.32f, 0.16f));
                MeshKit.Part(rig.Torso, "SackTie", MeshKit.Cylinder(0.08f, 0.06f, 0.15f, 6), Mat.Leather, new Vector3(0.12f, 0.92f, -0.25f), new Vector3(30f, 0f, 20f));
                for (int i = 0; i < 6; i++)
                {
                    var coin = MeshKit.Part(rig.Torso, "Coin" + i, MeshKit.Cylinder(0.05f, 0.05f, 0.012f, 8), Mat.Gold,
                        new Vector3(-0.15f + i * 0.07f, 0.85f + (i % 2) * 0.05f, -0.2f - (i % 3) * 0.05f), new Vector3(70f, i * 30f, 0f));
                }
                MeshKit.Part(rig.Head, "Crown", MeshKit.Cylinder(0.11f, 0.12f, 0.05f, 8), Mat.Gold, new Vector3(0.02f, 0.23f, 0f), new Vector3(0f, 0f, 12f));
                MeshKit.Part(rig.ArmR, "Pouch", MeshKit.Ball(0.1f, 6, 4), Mat.Leather, new Vector3(0f, -0.65f, 0.05f));
                rig.StrideLength = 1.6f;
            }
            else if (kind == SkeletonKind.Shieldbearer)
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
                var shield = MeshKit.Pivot(rig.ArmL, "TowerShield", new Vector3(-0.06f, -0.45f, 0.16f));
                MeshKit.Part(shield, "Face", MeshKit.Box(new Vector3(0.55f, 1.05f, 0.05f)), Mat.Steel, Vector3.zero);
                MeshKit.Part(shield, "RimTop", MeshKit.Box(new Vector3(0.59f, 0.06f, 0.08f)), Mat.Wood, new Vector3(0f, 0.53f, 0f));
                MeshKit.Part(shield, "RimBottom", MeshKit.Box(new Vector3(0.59f, 0.06f, 0.08f)), Mat.Wood, new Vector3(0f, -0.53f, 0f));
                foreach (float sx in new[] { -0.29f, 0.29f })
                    MeshKit.Part(shield, "RimSide", MeshKit.Box(new Vector3(0.05f, 1.1f, 0.08f)), Mat.Wood, new Vector3(sx, 0f, 0f));
                MeshKit.Part(shield, "Boss", MeshKit.Ball(0.08f, 8, 6), Mat.Steel, new Vector3(0f, 0.05f, 0.04f));
                MeshKit.Part(shield, "SigilV", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.02f)), Mat.Bone, new Vector3(0f, 0.05f, 0.035f));
                MeshKit.Part(shield, "SigilH", MeshKit.Box(new Vector3(0.32f, 0.05f, 0.02f)), Mat.Bone, new Vector3(0f, 0.16f, 0.035f));

                rig.SetGuardShield(shield, new Vector3(-0.08f, 0.12f, 0.58f), new Vector3(-3f, 8f, 0f));
                MeshKit.Part(grip, "Shaft", MeshKit.Cylinder(0.02f, 0.02f, 1.5f, 6), Mat.Wood, new Vector3(0f, -0.5f, 0f));
                MeshKit.Part(grip, "Spearhead", MeshKit.Lathe("spearhead", new[] { new Vector2(0.04f, 0f), new Vector2(0.03f, 0.08f), new Vector2(0f, 0.24f) }, 4), Mat.Steel, new Vector3(0f, 1f, 0f));
                MeshKit.Part(rig.Head, "Helm", MeshKit.Lathe("kettleHelm", new[] { new Vector2(0.21f, 0.06f), new Vector2(0.15f, 0.09f), new Vector2(0.14f, 0.2f), new Vector2(0.06f, 0.27f), new Vector2(0f, 0.28f) }, 10), Mat.Steel, Vector3.zero);
                rig.OffhandItem = null;
                rig.StrideLength = 1.1f;
            }
            else if (kind == SkeletonKind.Gravewarden)
            {
                BuildGravewarden(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());

                WeakPoint.Add(rig.Torso, new Vector3(0f, 0.45f, -0.22f), 0.1f, new Color(1f, 0.55f, 0.2f));
            }
            else if (kind == SkeletonKind.Cardinal)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.42f, 0.05f, 0.08f), new Color(1f, 0.8f, 0.35f), false);
                var hood = rig.Head.Find("Hood");
                if (hood != null) Object.DestroyImmediate(hood.gameObject);
                var ivory = new Color(0.88f, 0.84f, 0.74f);
                MeshKit.Part(rig.Head, "MitreBand", MeshKit.Cylinder(0.15f, 0.14f, 0.07f, 12), Mat.Gold, new Vector3(0f, 0.2f, 0.01f));
                for (int side = -1; side <= 1; side += 2)
                {
                    var plate = MeshKit.Pivot(rig.Head, side > 0 ? "MitreFront" : "MitreBack", new Vector3(0f, 0.26f, 0.01f + side * 0.05f));
                    plate.localRotation = Quaternion.Euler(-side * 7f, 0f, 0f);
                    var body = MeshKit.Part(plate, "Plate", MeshKit.Box(new Vector3(0.26f, 0.16f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.08f, 0f));
                    ArtLibrary.Tint(body.GetComponent<Renderer>(), ivory);
                    var peak = MeshKit.Part(plate, "Peak", MeshKit.Box(new Vector3(0.13f, 0.13f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.16f, 0f), new Vector3(0f, 0f, 45f));
                    ArtLibrary.Tint(peak.GetComponent<Renderer>(), ivory);
                    MeshKit.Part(plate, "Trim", MeshKit.Box(new Vector3(0.27f, 0.025f, 0.025f)), Mat.Gold, new Vector3(0f, 0.01f, 0f));
                    if (side > 0)
                    {
                        MeshKit.Part(plate, "CrossV", MeshKit.Box(new Vector3(0.03f, 0.16f, 0.025f)), Mat.Gold, new Vector3(0f, 0.11f, 0.005f));
                        MeshKit.Part(plate, "CrossH", MeshKit.Box(new Vector3(0.11f, 0.03f, 0.025f)), Mat.Gold, new Vector3(0f, 0.13f, 0.005f));
                    }
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    var lappet = MeshKit.Part(rig.Head, "Lappet", MeshKit.Box(new Vector3(0.05f, 0.26f, 0.015f)), Mat.Cloth, new Vector3(side * 0.07f, 0.06f, -0.15f), new Vector3(10f, 0f, 0f));
                    ArtLibrary.Tint(lappet.GetComponent<Renderer>(), new Color(0.5f, 0.06f, 0.08f));
                }
                MeshKit.Part(rig.Torso, "PectoralV", MeshKit.Box(new Vector3(0.035f, 0.14f, 0.02f)), Mat.Gold, new Vector3(0f, 0.42f, 0.23f));
                MeshKit.Part(rig.Torso, "PectoralH", MeshKit.Box(new Vector3(0.09f, 0.03f, 0.02f)), Mat.Gold, new Vector3(0f, 0.45f, 0.23f));
                var stoleCol = new Color(0.85f, 0.65f, 0.25f);
                var stole = MeshKit.Part(rig.Torso, "CardinalStole", MeshKit.Box(new Vector3(0.12f, 0.95f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.12f, 0.2f));
                ArtLibrary.Tint(stole.GetComponent<Renderer>(), stoleCol);
                GothicKit.Cloak(rig.Torso, new Color(0.3f, 0.03f, 0.05f), 1.3f, 0.3f, stoleCol);
            }
            else if (kind == SkeletonKind.Lich)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.2f, 0.36f, 0.34f), new Color(0.3f, 1f, 0.7f), true);

                var lichHood = rig.Head.Find("Hood");
                if (lichHood != null) Object.DestroyImmediate(lichHood.gameObject);
            }
            else if (kind == SkeletonKind.Necromancer)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.24f, 0.12f, 0.3f), new Color(0.75f, 0.35f, 1f), false);

                CasterHorns(rig.Head, new Color(0.78f, 0.72f, 0.6f));
                for (int i = 0; i < 5; i++)
                {
                    float a = (i - 2) * 24f;
                    MeshKit.Part(rig.Torso, "MantleSpine", MeshKit.Cylinder(0.025f, 0f, 0.42f - Mathf.Abs(i - 2) * 0.07f, 5), Mat.Bone,
                        new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.2f, 0.56f, -0.12f), new Vector3(-28f, 0f, -a * 0.9f));
                }
            }
            else if (kind == SkeletonKind.Plaguebearer)
            {
                BuildGhoul(rig, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());
                foreach (var r in rig.GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("Bone")) ArtLibrary.Tint(r, new Color(0.45f, 0.6f, 0.32f));

                var skin = new Color(0.4f, 0.47f, 0.26f);
                var sack = new[]
                {
                    new Vector2(0f, -0.2f), new Vector2(0.16f, -0.16f), new Vector2(0.29f, -0.04f), new Vector2(0.33f, 0.1f), new Vector2(0.3f, 0.24f), new Vector2(0.2f, 0.38f), new Vector2(0.1f, 0.46f),
                };
                float SackR(float y) { for (int q = 1; q < sack.Length; q++) if (y <= sack[q].y) return Mathf.Lerp(sack[q - 1].x, sack[q].x, Mathf.InverseLerp(sack[q - 1].y, sack[q].y, y)); return 0.1f; }
                var belly = MeshKit.Part(rig.Torso, "Belly", MeshKit.Lathe("plagueSack", sack, 12), Mat.Leather, new Vector3(0f, 0.04f, 0.11f), Vector3.zero, new Vector3(1.05f, 1f, 1f));
                ArtLibrary.Tint(belly.GetComponent<Renderer>(), skin);

                for (int k = 0; k < 7; k++)
                {
                    float y = 0.34f - k * 0.07f;
                    MeshKit.Part(rig.Torso, "Stitch", MeshKit.Box(new Vector3(0.07f, 0.012f, 0.014f)), Mat.Dark, new Vector3(0f, 0.04f + y, 0.11f + SackR(y) - 0.002f), new Vector3(0f, 0f, k % 2 == 0 ? 20f : -20f));
                }

                ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Strap", MeshKit.Cylinder(0.345f, 0.345f, 0.06f, 12), Mat.Cloth, new Vector3(0f, 0.12f, 0.11f), new Vector3(0f, 0f, 8f), new Vector3(1.05f, 1f, 1f)).GetComponent<Renderer>(), new Color(0.3f, 0.26f, 0.18f));
                for (int i = 0; i < 7; i++)
                {
                    var dir = Quaternion.Euler(Random.Range(-35f, 25f), -100f + i * 33f, 0f) * Vector3.forward;
                    var pus = MeshKit.Part(rig.Torso, "Pustule" + i, MeshKit.Ball(0.035f + (i % 3) * 0.012f, 6, 4), Mat.Glow, new Vector3(0f, 0.14f, 0.11f) + dir * SackR(0.1f + dir.y * 0.32f));
                    ArtLibrary.SetGlow(pus.GetComponent<Renderer>(), new Color(0.5f, 0.95f, 0.2f) * 1.1f);
                }

                WeakPoint.Add(rig.Torso, new Vector3(0f, 0.2f, 0.42f), 0.11f, new Color(0.55f, 1f, 0.2f));
            }
            else if (kind == SkeletonKind.FrostWight)
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
                foreach (var r in rig.GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("Bone")) ArtLibrary.Tint(r, new Color(0.75f, 0.88f, 1f));
                for (int i = 0; i < 5; i++)
                {
                    float a = -50f + i * 25f;
                    var shard = MeshKit.Part(rig.Head, "IceShard" + i, MeshKit.Lathe("iceShard", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.18f + (i % 2) * 0.08f) }, 4), Mat.Glow,
                        new Vector3(0f, 0.24f, 0f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.09f), new Vector3(15f, a, 0f));
                    ArtLibrary.SetGlow(shard.GetComponent<Renderer>(), new Color(0.5f, 0.8f, 1f) * 0.9f);
                }
                MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.02f, 0.02f, 0.14f, 5), Mat.Leather, new Vector3(0f, -0.05f, 0f));
                var iceBlade = MeshKit.Part(grip, "IceBlade", MeshKit.Box(new Vector3(0.07f, 0.75f, 0.02f)), Mat.Glow, new Vector3(0f, 0.42f, 0f));
                ArtLibrary.SetGlow(iceBlade.GetComponent<Renderer>(), new Color(0.45f, 0.75f, 1f) * 0.8f);
                foreach (float sx in new[] { -1f, 1f })
                {
                    var frost = MeshKit.Part(rig.Torso, "FrostMantle", MeshKit.Box(new Vector3(0.16f, 0.06f, 0.2f)), Mat.Glow, new Vector3(sx * 0.2f, 0.6f, 0f), new Vector3(0f, 0f, sx * 20f));
                    ArtLibrary.SetGlow(frost.GetComponent<Renderer>(), new Color(0.55f, 0.85f, 1f) * 0.6f);
                }
                rig.StrideLength = 1.2f;
            }
            else if (kind == SkeletonKind.Hexer)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.14f, 0.2f, 0.1f), new Color(0.65f, 0.25f, 0.95f), false);

                ArtLibrary.Tint(MeshKit.Part(rig.Head, "HexMask", MeshKit.Box(new Vector3(0.17f, 0.3f, 0.03f)), Mat.Wood, new Vector3(0f, 0.06f, 0.15f), new Vector3(-4f, 0f, 0f)).GetComponent<Renderer>(), new Color(0.82f, 0.78f, 0.68f));
                foreach (var (x, y) in new[] { (-0.04f, 0.08f), (0.04f, 0.08f), (0f, 0.15f) })
                {
                    var eye = MeshKit.Part(rig.Head, "HexEye", MeshKit.Box(new Vector3(0.028f, 0.018f, 0.01f)), Mat.Glow, new Vector3(x, y, 0.168f));
                    ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.65f, 0.25f, 0.95f) * 1.5f);
                }
                for (int i = 0; i < 6; i++)
                {
                    float a = (i - 2.5f) * 22f;
                    var twig = MeshKit.Pivot(rig.Head, "Twig", new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.1f, 0.2f, -0.02f), new Vector3(-10f, 0f, -a));
                    MeshKit.Part(twig, "Stick", MeshKit.Cylinder(0.012f, 0.005f, 0.3f, 4), Mat.Dark, Vector3.zero);
                    MeshKit.Part(twig, "Fork", MeshKit.Cylinder(0.007f, 0.003f, 0.12f, 4), Mat.Dark, Vector3.up * 0.16f, new Vector3(0f, 0f, i % 2 == 0 ? 35f : -35f));
                }
            }
            else if (kind == SkeletonKind.Orbweaver)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.24f, 0.13f, 0.08f), new Color(1f, 0.62f, 0.2f), false);
                var hand = MeshKit.Part(rig.ArmL, "OrbInHand", MeshKit.Ball(0.09f, 8, 6), Mat.Glow, new Vector3(0f, -0.72f, 0.12f));
                ArtLibrary.SetGlow(hand.GetComponent<Renderer>(), new Color(1f, 0.62f, 0.2f) * 1.3f);
                var ring = MeshKit.Part(rig.ArmL, "OrbRing", MeshKit.Lathe("orbRingSmall", new[] { new Vector2(0.15f, -0.01f), new Vector2(0.17f, 0f), new Vector2(0.15f, 0.01f) }, 16, false), Mat.Glow, new Vector3(0f, -0.72f, 0.12f), new Vector3(70f, 0f, 0f));
                ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.62f, 0.2f));

                var arm = MeshKit.Pivot(rig.Torso, "Armillary", new Vector3(0f, 0.72f, -0.22f), new Vector3(0f, 0f, 0f));
                foreach (var (rot, r) in new[] { (new Vector3(90f, 0f, 0f), 0.42f), (new Vector3(90f, 0f, 35f), 0.36f), (new Vector3(90f, 0f, -35f), 0.36f) })
                    MeshKit.Part(arm, "ArmRing", MeshKit.Lathe("armillaryRing" + r.ToString("F2"), new[] { new Vector2(r - 0.012f, -0.008f), new Vector2(r, 0f), new Vector2(r - 0.012f, 0.008f) }, 24, false), Mat.Gold, Vector3.zero, rot);
                for (int i = 0; i < 5; i++)
                {
                    float a = (90f + i * 72f) * Mathf.Deg2Rad;
                    var o = MeshKit.Part(arm, "ArmOrb", MeshKit.Ball(0.05f, 8, 6), Mat.Glow, new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(a) * 0.42f, 0f));
                    ArtLibrary.SetGlow(o.GetComponent<Renderer>(), new Color(1f, 0.62f, 0.2f) * 1.2f);
                }
            }
            else if (kind == SkeletonKind.ChoirAcolyte)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.62f, 0.58f, 0.48f), new Color(1f, 0.82f, 0.4f), false);
                var censer = MeshKit.Pivot(rig.ArmL, "Censer", new Vector3(0f, -0.66f, 0.08f));
                MeshKit.Part(censer, "Chain", MeshKit.Box(new Vector3(0.012f, 0.3f, 0.012f)), Mat.Steel, new Vector3(0f, -0.15f, 0f));
                MeshKit.Part(censer, "Pot", MeshKit.Lathe("censerPot", new[] { new Vector2(0f, -0.08f), new Vector2(0.07f, -0.06f), new Vector2(0.08f, 0f), new Vector2(0.05f, 0.05f), new Vector2(0f, 0.08f) }, 10), Mat.Gold, new Vector3(0f, -0.38f, 0f));
                var coal = MeshKit.Part(censer, "Coal", MeshKit.Ball(0.04f, 6, 4), Mat.Glow, new Vector3(0f, -0.36f, 0f));
                ArtLibrary.SetGlow(coal.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 1.4f);

                var collar = MeshKit.Part(rig.Torso, "ChoirCollar", MeshKit.Lathe("choirCollar", new[] { new Vector2(0.17f, 0f), new Vector2(0.26f, 0.05f), new Vector2(0.25f, 0.08f) }, 14, true, 300f), Mat.Cloth, new Vector3(0f, 0.55f, 0f), new Vector3(0f, 30f, 0f));
                ArtLibrary.Tint(collar.GetComponent<Renderer>(), new Color(0.92f, 0.9f, 0.84f));

                var nimbus = MeshKit.Pivot(rig.Head, "Nimbus", new Vector3(0f, 0.14f, -0.14f));
                MeshKit.Part(nimbus, "Ring", MeshKit.Lathe("nimbusRing", new[] { new Vector2(0.21f, -0.012f), new Vector2(0.24f, 0f), new Vector2(0.21f, 0.012f) }, 20, false), Mat.Gold, Vector3.zero, new Vector3(90f, 0f, 0f));
                for (int i = 0; i < 12; i++)
                    MeshKit.Part(nimbus, "Ray", MeshKit.Box(new Vector3(0.012f, 0.09f, 0.008f)), Mat.Gold, Quaternion.Euler(0f, 0f, i * 30f) * Vector3.up * 0.28f, new Vector3(0f, 0f, i * 30f));
            }
            else if (kind == SkeletonKind.RimeMagus)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.42f, 0.55f, 0.68f), new Color(0.65f, 0.92f, 1f), false);
                var ice = new Color(0.7f, 0.9f, 1f);
                for (int i = 0; i < 7; i++)
                {
                    float a = (i - 3) * 22f;
                    var shard = MeshKit.Part(rig.Head, "IceShard", MeshKit.Box(new Vector3(0.035f, 0.12f + 0.05f * (3 - Mathf.Abs(i - 3)), 0.035f)), Mat.Glow,
                        Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.2f, 0.12f), new Vector3(-12f, a, 0f));
                    ArtLibrary.SetGlow(shard.GetComponent<Renderer>(), ice * 0.9f);
                }
                for (int sx = -1; sx <= 1; sx += 2)
                    ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Rime", MeshKit.Ball(0.1f, 6, 4), Mat.Cloth, new Vector3(sx * 0.22f, 0.55f, 0f), Vector3.zero, new Vector3(1.2f, 0.45f, 1f)).GetComponent<Renderer>(), new Color(0.88f, 0.95f, 1f));

                for (int sx = -1; sx <= 1; sx += 2)
                    for (int k = 0; k < 3; k++)
                    {
                        var big = MeshKit.Part(rig.Torso, "ShoulderIce", MeshKit.Lathe("iceSpike", new[] { new Vector2(0.05f, 0f), new Vector2(0.035f, 0.2f), new Vector2(0f, 0.38f) }, 4), Mat.Glow,
                            new Vector3(sx * (0.17f + k * 0.05f), 0.56f, -0.04f - k * 0.04f), new Vector3(-10f - k * 8f, 45f, -sx * (18f + k * 16f)), Vector3.one * (1f - k * 0.2f));
                        ArtLibrary.SetGlow(big.GetComponent<Renderer>(), ice * 0.75f);
                    }
            }
            else if (kind == SkeletonKind.InkLamprey)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.06f, 0.07f, 0.09f), new Color(0.6f, 0.4f, 0.95f), false);
                var mouth = MeshKit.Pivot(rig.Head, "LampreyMouth", new Vector3(0f, 0.02f, 0.15f));
                ArtLibrary.Tint(MeshKit.Part(mouth, "Lip", MeshKit.Cylinder(0.16f, 0.16f, 0.06f, 16), Mat.Leather, Vector3.zero, new Vector3(90f, 0f, 0f)).GetComponent<Renderer>(), new Color(0.62f, 0.32f, 0.36f));
                ArtLibrary.Tint(MeshKit.Part(mouth, "Maw", MeshKit.Cylinder(0.085f, 0.085f, 0.052f, 12), Mat.Dark, new Vector3(0f, 0f, 0.004f), new Vector3(90f, 0f, 0f)).GetComponent<Renderer>(), new Color(0.05f, 0.01f, 0.04f));
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 30f * Mathf.Deg2Rad;
                    MeshKit.Part(mouth, "Tooth", MeshKit.Box(new Vector3(0.022f, 0.055f, 0.02f)), Mat.Bone,
                        new Vector3(Mathf.Sin(a) * 0.115f, Mathf.Cos(a) * 0.115f, 0.035f), new Vector3(0f, 0f, -i * 30f));
                }
                var weed = new Color(0.08f, 0.16f, 0.1f);
                for (int i = 0; i < 6; i++)
                {
                    float a = (i * 60f + 20f) * Mathf.Deg2Rad;
                    ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Weed", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.012f)), Mat.Cloth,
                        new Vector3(Mathf.Sin(a) * 0.26f, 0.15f, Mathf.Cos(a) * 0.2f), new Vector3(Random.Range(-8f, 8f), i * 60f + 20f, Random.Range(-6f, 6f))).GetComponent<Renderer>(), weed);
                }
            }
            else if (kind == SkeletonKind.WitheringHag)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.22f, 0.27f, 0.16f), new Color(0.62f, 0.9f, 0.25f), false);
                var hair = new Color(0.62f, 0.62f, 0.58f);
                for (int i = 0; i < 7; i++)
                {
                    float x = (i - 3) * 0.045f;
                    ArtLibrary.Tint(MeshKit.Part(rig.Head, "Hair", MeshKit.Box(new Vector3(0.035f, 0.7f, 0.02f)), Mat.Cloth,
                        new Vector3(x, -0.22f, -0.13f - Mathf.Abs(x) * 0.3f), new Vector3(8f, 0f, x * 30f)).GetComponent<Renderer>(), hair * (0.85f + 0.05f * (i % 3)));
                }
                ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Shawl", MeshKit.Lathe("hagShawl", new[] { new Vector2(0.33f, 0.3f), new Vector2(0.3f, 0.48f), new Vector2(0.15f, 0.62f) }, 10, true, 290f), Mat.Cloth, Vector3.zero, new Vector3(0f, 215f, 0f)).GetComponent<Renderer>(), new Color(0.3f, 0.36f, 0.2f));
                var charm = MeshKit.Pivot(rig.ArmL, "Charm", new Vector3(0f, -0.72f, 0.05f));
                MeshKit.Part(charm, "Cord", MeshKit.Box(new Vector3(0.01f, 0.2f, 0.01f)), Mat.Leather, new Vector3(0f, -0.1f, 0f));
                for (int k = 0; k < 3; k++)
                    MeshKit.Part(charm, "Knucklebone", MeshKit.Box(new Vector3(0.05f, 0.025f, 0.025f)), Mat.Bone, new Vector3(0f, -0.2f - k * 0.04f, 0f), new Vector3(0f, k * 50f, 20f));

                var hat = MeshKit.Pivot(rig.Head, "HagHat", new Vector3(0f, 0.22f, -0.01f), new Vector3(-8f, 0f, 6f));
                hat.localScale = Vector3.one * 1.7f;
                var felt = new Color(0.14f, 0.16f, 0.1f);
                ArtLibrary.Tint(MeshKit.Part(hat, "Brim", MeshKit.Lathe("hagBrim", new[] { new Vector2(0.36f, -0.05f), new Vector2(0.3f, -0.01f), new Vector2(0.14f, 0.01f) }, 14), Mat.Cloth, Vector3.zero).GetComponent<Renderer>(), felt);
                ArtLibrary.Tint(MeshKit.Part(hat, "Crown", MeshKit.Lathe("hagCrown", new[] { new Vector2(0.14f, 0f), new Vector2(0.11f, 0.14f), new Vector2(0.06f, 0.26f), new Vector2(0f, 0.34f) }, 10), Mat.Cloth, Vector3.zero, new Vector3(-14f, 0f, 10f)).GetComponent<Renderer>(), felt * 1.15f);
                MeshKit.Part(hat, "HatBand", MeshKit.Cylinder(0.142f, 0.135f, 0.03f, 10), Mat.Leather, new Vector3(0f, 0.025f, 0f));
                rig.Torso.localRotation = Quaternion.Euler(14f, 0f, 0f) * rig.Torso.localRotation;
            }
            else if (kind == SkeletonKind.Bellringer)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.28f, 0.19f, 0.14f), new Color(0.78f, 0.78f, 1f), false);
                MeshKit.Part(rig.Torso, "Yoke", MeshKit.Box(new Vector3(0.72f, 0.07f, 0.09f)), Mat.Wood, new Vector3(0f, 0.6f, -0.08f));

                var bell = MeshKit.Pivot(rig.Torso, "BackBell", new Vector3(0f, 1.02f, -0.34f));
                bell.localScale = Vector3.one * 1.35f;
                MeshKit.Part(rig.Torso, "BellPost", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.05f)), Mat.Wood, new Vector3(0f, 0.85f, -0.3f));
                var cup = MeshKit.Part(bell, "Bell", MeshKit.Lathe("ringerBell", new[] { new Vector2(0.04f, 0f), new Vector2(0.14f, -0.06f), new Vector2(0.19f, -0.25f), new Vector2(0.24f, -0.42f), new Vector2(0.27f, -0.46f) }, 16, true), Mat.Gold, Vector3.zero);
                ArtLibrary.Tint(cup.GetComponent<Renderer>(), new Color(0.48f, 0.36f, 0.22f));
                MeshKit.Part(bell, "Crack", MeshKit.Box(new Vector3(0.012f, 0.22f, 0.012f)), Mat.Dark, new Vector3(0.12f, -0.3f, 0.17f), new Vector3(0f, 35f, 12f));
                MeshKit.Part(bell, "BellStrap", MeshKit.Box(new Vector3(0.05f, 0.14f, 0.05f)), Mat.Leather, new Vector3(0f, 0.04f, 0f));

                foreach (float sx in new[] { -1f, 1f })
                {
                    var side = MeshKit.Pivot(rig.Torso, "YokeBell", new Vector3(sx * 0.38f, 0.56f, -0.08f));
                    MeshKit.Part(side, "Rope", MeshKit.Box(new Vector3(0.015f, 0.12f, 0.015f)), Mat.Leather, new Vector3(0f, -0.06f, 0f));
                    ArtLibrary.Tint(MeshKit.Part(side, "Cup", MeshKit.Lathe("yokeBell", new[] { new Vector2(0.025f, 0f), new Vector2(0.07f, -0.04f), new Vector2(0.09f, -0.14f), new Vector2(0.12f, -0.2f) }, 12, true), Mat.Gold,
                        new Vector3(0f, -0.1f, 0f)).GetComponent<Renderer>(), new Color(0.66f, 0.5f, 0.28f));
                }
            }
            else if (kind == SkeletonKind.Cantor)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.22f, 0.22f, 0.27f), new Color(0.78f, 0.78f, 1f), false);
                var linen = new Color(0.78f, 0.74f, 0.64f);
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "Gag", MeshKit.Box(new Vector3(0.2f, 0.05f, 0.2f)), Mat.Cloth, new Vector3(0f, -0.07f, 0.03f)).GetComponent<Renderer>(), linen);
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "GagKnot", MeshKit.Ball(0.03f, 6, 4), Mat.Cloth, new Vector3(0f, -0.06f, -0.09f)).GetComponent<Renderer>(), linen * 0.85f);

                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    MeshKit.Part(rig.Head, "CageBar", MeshKit.Box(new Vector3(0.016f, 0.36f, 0.016f)), Mat.Steel, new Vector3(Mathf.Sin(a) * 0.15f, 0.1f, Mathf.Cos(a) * 0.15f));
                }
                foreach (float y in new[] { -0.06f, 0.27f })
                    MeshKit.Part(rig.Head, "CageHoop", MeshKit.Lathe("cageHoop", new[] { new Vector2(0.145f, -0.012f), new Vector2(0.16f, 0f), new Vector2(0.145f, 0.012f) }, 12, false), Mat.Steel, new Vector3(0f, y, 0f));
                MeshKit.Part(rig.Head, "CageRing", MeshKit.Lathe("cageTopRing", new[] { new Vector2(0.05f, -0.01f), new Vector2(0.062f, 0f), new Vector2(0.05f, 0.01f) }, 10, false), Mat.Steel, new Vector3(0f, 0.33f, 0f), new Vector3(90f, 0f, 0f));
                for (int sx = -1; sx <= 1; sx += 2)
                    ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Stole", MeshKit.Box(new Vector3(0.06f, 0.55f, 0.02f)), Mat.Cloth, new Vector3(sx * 0.09f, 0.3f, 0.2f), new Vector3(-6f, 0f, sx * 4f)).GetComponent<Renderer>(), new Color(0.62f, 0.62f, 0.72f));
                var bell = MeshKit.Pivot(rig.ArmL, "HandBell", new Vector3(0f, -0.7f, 0.06f));
                MeshKit.Part(bell, "BellGrip", MeshKit.Cylinder(0.015f, 0.015f, 0.12f, 6), Mat.Wood, new Vector3(0f, 0.04f, 0f));
                var cup = MeshKit.Part(bell, "BellCup", MeshKit.Lathe("cantorBell", new[] { new Vector2(0.02f, 0f), new Vector2(0.05f, -0.04f), new Vector2(0.065f, -0.11f), new Vector2(0.08f, -0.13f) }, 12, true), Mat.Gold, Vector3.zero);
                ArtLibrary.Tint(cup.GetComponent<Renderer>(), new Color(0.55f, 0.5f, 0.42f));
                ArtLibrary.Tint(MeshKit.Part(bell, "Muffle", MeshKit.Ball(0.03f, 6, 4), Mat.Cloth, new Vector3(0f, -0.1f, 0f)).GetComponent<Renderer>(), linen);
            }
            else if (kind == SkeletonKind.BonePiper)
            {
                var soul = new Color(0.4f, 1f, 0.9f);
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
                GothicKit.Cloak(rig.Torso, new Color(0.2f, 0.3f, 0.3f), 0.75f, 0.12f, null, tattered: true);
                var felt = new Color(0.16f, 0.13f, 0.12f);
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "HatBrim", MeshKit.Cylinder(0.27f, 0.27f, 0.018f, 16), Mat.Leather, new Vector3(0f, 0.2f, 0f), new Vector3(-6f, 0f, 4f)).GetComponent<Renderer>(), felt);
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "HatCrown", MeshKit.Cylinder(0.13f, 0.1f, 0.13f, 12), Mat.Leather, new Vector3(0f, 0.27f, 0f), new Vector3(-6f, 0f, 4f)).GetComponent<Renderer>(), felt);
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "HatBand", MeshKit.Cylinder(0.132f, 0.128f, 0.025f, 12), Mat.Cloth, new Vector3(0f, 0.225f, 0f), new Vector3(-6f, 0f, 4f)).GetComponent<Renderer>(), new Color(0.15f, 0.45f, 0.42f));
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "Feather", MeshKit.Box(new Vector3(0.03f, 0.26f, 0.008f)), Mat.Cloth, new Vector3(0.12f, 0.33f, -0.04f), new Vector3(-25f, 20f, -30f)).GetComponent<Renderer>(), new Color(0.88f, 0.9f, 0.86f));

                var bag = MeshKit.Part(rig.Torso, "PipeBag", MeshKit.Ball(0.17f, 10, 8), Mat.Leather, new Vector3(-0.22f, 0.36f, 0.06f), new Vector3(0f, 0f, 15f), new Vector3(0.8f, 1f, 1.1f));
                ArtLibrary.Tint(bag.GetComponent<Renderer>(), new Color(0.36f, 0.26f, 0.2f));
                WeakPoint.Add(rig.Torso, new Vector3(-0.24f, 0.36f, 0.14f), 0.08f, soul);
                for (int i = 0; i < 3; i++)
                {
                    float len = 0.42f + i * 0.12f;
                    var drone = MeshKit.Pivot(rig.Torso, "Drone" + i, new Vector3(-0.2f + i * 0.035f, 0.44f, 0.02f - i * 0.03f), new Vector3(-28f - i * 6f, 0f, 12f - i * 6f));
                    MeshKit.Part(drone, "DroneBone", MeshKit.Cylinder(0.018f, 0.014f, len, 6), Mat.Bone, new Vector3(0f, len * 0.5f, 0f));
                    MeshKit.Part(drone, "DroneKnob", MeshKit.Ball(0.03f, 6, 4), Mat.Bone, new Vector3(0f, len, 0f));
                    var mouth = MeshKit.Part(drone, "DroneGlow", MeshKit.Ball(0.018f, 6, 4), Mat.Glow, new Vector3(0f, len + 0.025f, 0f));
                    ArtLibrary.SetGlow(mouth.GetComponent<Renderer>(), soul * 1.2f);
                }

                var chanter = MeshKit.Pivot(rig.Head, "Chanter", new Vector3(0f, 0.03f, 0.13f), new Vector3(58f, 0f, 0f));
                MeshKit.Part(chanter, "ChanterBone", MeshKit.Cylinder(0.016f, 0.024f, 0.34f, 6), Mat.Bone, new Vector3(0f, 0.17f, 0f));
                for (int i = 0; i < 4; i++)
                {
                    var hole = MeshKit.Part(chanter, "Hole", MeshKit.Ball(0.009f, 5, 3), Mat.Glow, new Vector3(0f, 0.1f + i * 0.055f, 0.02f));
                    ArtLibrary.SetGlow(hole.GetComponent<Renderer>(), soul * 1.3f);
                }
                var bell = MeshKit.Part(chanter, "ChanterBell", MeshKit.Cylinder(0.025f, 0.045f, 0.05f, 8), Mat.Bone, new Vector3(0f, 0.35f, 0f));
                rig.StrideLength = 1.05f;
            }
            else if (kind == SkeletonKind.Ghoul)
            {
                BuildGhoul(rig, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());
            }
            else if (kind == SkeletonKind.Brute)
            {
                BuildBrute(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());

                WeakPoint.Add(rig.Torso, new Vector3(0f, 0.4f, -0.24f), 0.13f);
            }
            else if (kind == CharacterArt.SkeletonKind.Archer)
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };

                var bowGrip = MeshKit.Pivot(rig.ArmL, "BowGrip", new Vector3(0f, -0.6f, 0.02f));
                for (int i = -3; i <= 3; i++)
                {
                    float a = i * 13f * Mathf.Deg2Rad;
                    var p = new Vector3(0f, Mathf.Sin(a) * 0.5f, -(1f - Mathf.Cos(a)) * 0.5f);
                    MeshKit.Part(bowGrip, "Limb" + i, MeshKit.Box(new Vector3(0.03f, 0.13f, 0.035f)), Mat.Wood, p, new Vector3(-i * 13f, 0f, 0f));
                }
                MeshKit.Part(bowGrip, "Grip", MeshKit.Box(new Vector3(0.04f, 0.1f, 0.045f)), Mat.Leather, Vector3.zero);
                MeshKit.Part(bowGrip, "String", MeshKit.Box(new Vector3(0.006f, 0.64f, 0.006f)), Mat.Bone, new Vector3(0f, 0f, -0.111f));
                rig.OffhandItem = bowGrip;
                MeshKit.Part(rig.Torso, "Quiver", MeshKit.Cylinder(0.06f, 0.07f, 0.5f, 6), Mat.Leather, new Vector3(0.1f, 0.25f, -0.16f), new Vector3(-15f, 0f, 20f));

                for (int i = 0; i < 6; i++)
                {
                    var tip = new Vector3(0.12f + (i % 3) * 0.025f, 0.62f + (i / 3) * 0.03f, -0.24f + (i % 2) * 0.02f);
                    MeshKit.Part(rig.Torso, "Shaft" + i, MeshKit.Cylinder(0.008f, 0.008f, 0.22f, 4), Mat.Wood, tip, new Vector3(-15f, 0f, 20f + (i - 2.5f) * 3f));
                    ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Fletch" + i, MeshKit.Box(new Vector3(0.012f, 0.1f, 0.04f)), Mat.Cloth,
                        tip + new Vector3(0.05f, 0.18f, -0.02f), new Vector3(-15f, i * 30f, 20f)).GetComponent<Renderer>(), i % 2 == 0 ? new Color(0.85f, 0.82f, 0.74f) : new Color(0.55f, 0.18f, 0.14f));
                }
                ArtLibrary.Tint(MeshKit.Part(rig.ArmL, "Bracer", MeshKit.Cylinder(0.05f, 0.045f, 0.16f, 8), Mat.Leather, new Vector3(0f, -0.48f, 0f)).GetComponent<Renderer>(), new Color(0.32f, 0.22f, 0.14f));
                var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("archerHood", new[]
                {
                    new Vector2(0.15f, -0.02f), new Vector2(0.16f, 0.1f), new Vector2(0.12f, 0.2f), new Vector2(0.03f, 0.26f), new Vector2(0f, 0.27f),
                }, 8, true, 280f), Mat.Cloth, new Vector3(0f, 0.05f, -0.01f), new Vector3(0f, 40f, 0f));
                var vr = new System.Random(SkeletonVariant);
                var hoodCols = new[] { new Color(0.22f, 0.2f, 0.16f), new Color(0.18f, 0.22f, 0.17f), new Color(0.26f, 0.17f, 0.14f), new Color(0.16f, 0.16f, 0.2f) };
                var hoodCol = hoodCols[vr.Next(hoodCols.Length)];
                ArtLibrary.Tint(hood.GetComponent<Renderer>(), hoodCol);
                GothicKit.Cloak(rig.Torso, hoodCol * 0.85f, 0.62f, 0.1f, null, tattered: true);

                ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Mantle", MeshKit.Lathe("archerMantle", new[] { new Vector2(0.3f, 0.36f), new Vector2(0.27f, 0.5f), new Vector2(0.14f, 0.62f) }, 10, true, 300f), Mat.Cloth,
                    Vector3.zero, new Vector3(0f, 30f, 0f), new Vector3(1.05f, 1f, 0.95f)).GetComponent<Renderer>(), hoodCol * 1.1f);
                if (vr.NextDouble() < 0.5)
                {
                    var scarf = MeshKit.Part(rig.Torso, "Scarf", MeshKit.Cylinder(0.13f, 0.11f, 0.08f, 8), Mat.Cloth, new Vector3(0f, 0.58f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.85f));
                    ArtLibrary.Tint(scarf.GetComponent<Renderer>(), hoodCols[vr.Next(hoodCols.Length)] * 1.3f);
                }
                rig.StrideLength = 1.2f;
            }
            else
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
                DressWarrior(rig, grip, new System.Random(SkeletonVariant));
                rig.StrideLength = 1.2f;
            }

            return rig;
        }

        private static void DressWarrior(HumanoidRig rig, Transform grip, System.Random vr)
        {
            var rust = new Color(0.6f, 0.47f, 0.37f);
            var oldSteel = new Color(0.55f, 0.5f, 0.46f);
            int weapon = vr.Next(4);
            switch (weapon)
            {
                case 0:
                    MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.018f, 0.018f, 0.13f, 5), Mat.Leather, new Vector3(0f, -0.05f, 0f));
                    MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.16f, 0.03f, 0.04f)), Mat.Steel, new Vector3(0f, 0.03f, 0f));
                    ArtLibrary.Tint(MeshKit.Part(grip, "Blade", MeshKit.Box(new Vector3(0.055f, 0.65f, 0.012f)), Mat.Steel, new Vector3(0f, 0.37f, 0f)).GetComponent<Renderer>(), rust);
                    break;
                case 1:
                    MeshKit.Part(grip, "Haft", MeshKit.Cylinder(0.02f, 0.018f, 0.78f, 6), Mat.Wood, new Vector3(0f, -0.15f, 0f));
                    ArtLibrary.Tint(MeshKit.Part(grip, "AxeHead", MeshKit.Box(new Vector3(0.2f, 0.15f, 0.02f)), Mat.Steel, new Vector3(0.09f, 0.55f, 0f), new Vector3(0f, 0f, -8f)).GetComponent<Renderer>(), rust);
                    ArtLibrary.Tint(MeshKit.Part(grip, "AxeBack", MeshKit.Box(new Vector3(0.06f, 0.05f, 0.03f)), Mat.Steel, new Vector3(-0.03f, 0.56f, 0f)).GetComponent<Renderer>(), rust);
                    break;
                case 2:
                    MeshKit.Part(grip, "Haft", MeshKit.Cylinder(0.02f, 0.02f, 0.6f, 6), Mat.Wood, new Vector3(0f, -0.1f, 0f));
                    ArtLibrary.Tint(MeshKit.Part(grip, "MaceHead", MeshKit.Ball(0.065f, 8, 6), Mat.Steel, new Vector3(0f, 0.52f, 0f)).GetComponent<Renderer>(), oldSteel);
                    for (int f = 0; f < 6; f++)
                        ArtLibrary.Tint(MeshKit.Part(grip, "Flange", MeshKit.Box(new Vector3(0.015f, 0.12f, 0.05f)), Mat.Steel,
                            new Vector3(0f, 0.52f, 0f) + Quaternion.Euler(0f, f * 60f, 0f) * new Vector3(0f, 0f, 0.06f), new Vector3(0f, f * 60f, 0f)).GetComponent<Renderer>(), oldSteel);
                    break;
                default:
                    MeshKit.Part(grip, "Shaft", MeshKit.Cylinder(0.018f, 0.018f, 1.5f, 6), Mat.Wood, new Vector3(0f, -0.55f, 0f));
                    ArtLibrary.Tint(MeshKit.Part(grip, "Spearhead", MeshKit.Lathe("skelSpear", new[] { new Vector2(0.035f, 0f), new Vector2(0.03f, 0.07f), new Vector2(0f, 0.22f) }, 4), Mat.Steel, new Vector3(0f, 0.95f, 0f)).GetComponent<Renderer>(), rust);
                    break;
            }

            int helmet = vr.Next(3);
            if (helmet == 0)
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "BrokenHelm", MeshKit.Lathe("skelHelm", new[] { new Vector2(0.14f, 0f), new Vector2(0.145f, 0.06f), new Vector2(0.11f, 0.12f), new Vector2(0f, 0.14f) }, 7, true, 290f),
                    Mat.Steel, new Vector3(0f, 0.15f, 0f), new Vector3(0f, 35f, 0f)).GetComponent<Renderer>(), rust * 0.95f);
            else if (helmet == 1)
            {
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "KettleHelm", MeshKit.Lathe("skelKettle", new[] { new Vector2(0.22f, 0.12f), new Vector2(0.16f, 0.14f), new Vector2(0.145f, 0.22f), new Vector2(0.06f, 0.28f), new Vector2(0f, 0.29f) }, 10),
                    Mat.Steel, new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0f, (float)vr.NextDouble() * 10f - 5f)).GetComponent<Renderer>(), oldSteel);
            }

            if (vr.NextDouble() < 0.4)
            {
                var arm = vr.NextDouble() < 0.5 ? rig.ArmL : rig.ArmR;
                float side = arm == rig.ArmL ? -1f : 1f;
                ArtLibrary.Tint(MeshKit.Part(arm, "Pauldron", MeshKit.Lathe("pauldron", new[] { new Vector2(0.17f, -0.10f), new Vector2(0.16f, 0.0f), new Vector2(0.12f, 0.08f), new Vector2(0.0f, 0.11f) }, 8),
                    Mat.Steel, new Vector3(0.02f * side, 0.02f, 0f), new Vector3(0f, 0f, -18f * side), Vector3.one * 0.7f).GetComponent<Renderer>(), rust);
            }

            if (vr.NextDouble() < 0.45)
            {
                var cols = new[] { new Color(0.32f, 0.1f, 0.08f), new Color(0.24f, 0.2f, 0.12f), new Color(0.14f, 0.18f, 0.14f), new Color(0.16f, 0.14f, 0.22f) };
                var col = cols[vr.Next(cols.Length)];
                ArtLibrary.Tint(MeshKit.Part(rig.Torso, "Tabard", MeshKit.Box(new Vector3(0.24f, 0.6f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.18f, 0.16f), new Vector3(4f, 0f, (float)vr.NextDouble() * 6f - 3f)).GetComponent<Renderer>(), col);
                ArtLibrary.Tint(MeshKit.Part(rig.Torso, "TabardBack", MeshKit.Box(new Vector3(0.22f, 0.5f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.22f, -0.16f), new Vector3(-4f, 0f, 0f)).GetComponent<Renderer>(), col * 0.85f);
            }

            if (weapon != 3 && vr.NextDouble() < 0.3)
            {
                var mount = MeshKit.Pivot(rig.ArmL, "RoundShield", new Vector3(-0.07f, -0.42f, 0.08f), new Vector3(0f, -90f, 0f));
                MeshKit.Part(mount, "Board", MeshKit.Cylinder(0.26f, 0.26f, 0.04f, 12), Mat.Wood, Vector3.zero, new Vector3(90f, 0f, 0f));
                ArtLibrary.Tint(MeshKit.Part(mount, "Rim", MeshKit.Cylinder(0.275f, 0.275f, 0.03f, 12), Mat.Steel, new Vector3(0f, 0f, -0.005f), new Vector3(90f, 0f, 0f)).GetComponent<Renderer>(), rust);
                ArtLibrary.Tint(MeshKit.Part(mount, "Boss", MeshKit.Ball(0.06f, 8, 6), Mat.Steel, new Vector3(0f, 0f, 0.03f), Vector3.zero, new Vector3(1f, 1f, 0.6f)).GetComponent<Renderer>(), oldSteel);
            }
        }

        private static void BuildGravewarden(HumanoidRig rig, Transform grip, Renderer eyeL, Renderer eyeR)
        {
            var rust = new Color(0.62f, 0.52f, 0.45f);

            var chest = MeshKit.Part(rig.Torso, "Breastplate", MeshKit.Lathe("wardenChest", new[]
            {
                new Vector2(0.17f, 0.15f), new Vector2(0.22f, 0.3f), new Vector2(0.24f, 0.5f), new Vector2(0.2f, 0.62f), new Vector2(0.08f, 0.66f),
            }, 8), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.8f));
            ArtLibrary.Tint(chest.GetComponent<Renderer>(), rust);
            var tasset = MeshKit.Part(rig.Hips, "Tassets", MeshKit.Lathe("wardenSkirt", new[]
            {
                new Vector2(0.24f, -0.3f), new Vector2(0.2f, 0f), new Vector2(0.17f, 0.08f),
            }, 8), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.8f));
            ArtLibrary.Tint(tasset.GetComponent<Renderer>(), rust);

            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                if (leg == null) continue;
                var cuisse = MeshKit.Part(leg, "Cuisse", MeshKit.Lathe("wardenCuisse", new[] { new Vector2(0.075f, -0.42f), new Vector2(0.095f, -0.2f), new Vector2(0.1f, -0.02f) }, 8), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.9f));
                ArtLibrary.Tint(cuisse.GetComponent<Renderer>(), rust);
                var cop = MeshKit.Part(leg, "KneeCop", MeshKit.Ball(0.07f, 8, 6), Mat.Steel, new Vector3(0f, -0.45f, 0.04f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
                ArtLibrary.Tint(cop.GetComponent<Renderer>(), rust * 0.9f);
                var greave = MeshKit.Part(leg, "Greave", MeshKit.Lathe("wardenGreave", new[] { new Vector2(0.06f, -0.85f), new Vector2(0.08f, -0.65f), new Vector2(0.07f, -0.48f) }, 8), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.95f));
                ArtLibrary.Tint(greave.GetComponent<Renderer>(), rust);
                var sabaton = MeshKit.Part(leg, "Sabaton", MeshKit.Box(new Vector3(0.12f, 0.07f, 0.22f)), Mat.Steel, new Vector3(0f, -0.87f, 0.05f));
                ArtLibrary.Tint(sabaton.GetComponent<Renderer>(), rust * 0.85f);
            }

            var helm = MeshKit.Part(rig.Head, "Helm", MeshKit.Lathe("wardenHelm", new[]
            {
                new Vector2(0.15f, 0f), new Vector2(0.155f, 0.14f), new Vector2(0.12f, 0.24f), new Vector2(0.03f, 0.29f), new Vector2(0f, 0.3f),
            }, 8), Mat.Steel, new Vector3(0f, 0.02f, 0f));
            ArtLibrary.Tint(helm.GetComponent<Renderer>(), rust);
            MeshKit.Part(rig.Head, "Slit", MeshKit.Box(new Vector3(0.18f, 0.03f, 0.05f)), Mat.Dark, new Vector3(0f, 0.13f, 0.135f));

            eyeL.transform.localPosition = new Vector3(-0.04f, 0.13f, 0.15f);
            eyeR.transform.localPosition = new Vector3(0.04f, 0.13f, 0.15f);

            for (int i = 0; i < 2; i++)
            {
                var arm = i == 0 ? rig.ArmL : rig.ArmR;
                var pauldron = MeshKit.Part(arm, "Pauldron", MeshKit.Lathe("pauldron", new[]
                {
                    new Vector2(0.17f, -0.10f), new Vector2(0.16f, 0.0f), new Vector2(0.12f, 0.08f), new Vector2(0.0f, 0.11f),
                }, 8), Mat.Steel, Vector3.zero, new Vector3(0f, 0f, i == 0 ? 18f : -18f), Vector3.one * 0.85f);
                ArtLibrary.Tint(pauldron.GetComponent<Renderer>(), rust);
            }

            var mount = MeshKit.Pivot(rig.ArmL, "ShieldMount", new Vector3(-0.06f, -0.4f, 0.12f), new Vector3(0f, -90f, 0f));
            MeshKit.Part(mount, "Board", MeshKit.Box(new Vector3(0.55f, 1.0f, 0.06f)), Mat.Wood, Vector3.zero);
            var face = MeshKit.Part(mount, "Face", MeshKit.Box(new Vector3(0.5f, 0.95f, 0.03f)), Mat.Steel, new Vector3(0f, 0f, 0.04f));
            ArtLibrary.Tint(face.GetComponent<Renderer>(), rust);
            MeshKit.Part(mount, "Boss", MeshKit.Ball(0.08f, 8, 4), Mat.Steel, new Vector3(0f, 0.05f, 0.07f), Vector3.zero, new Vector3(1f, 1f, 0.6f));
            for (int i = 0; i < 3; i++)
            {
                MeshKit.Part(mount, "Band" + i, MeshKit.Box(new Vector3(0.56f, 0.05f, 0.04f)), Mat.Steel, new Vector3(0f, -0.4f + i * 0.4f, 0.06f));
            }
            MeshKit.Part(mount, "Emblem", MeshKit.Ball(0.07f, 8, 6), Mat.Bone, new Vector3(0f, 0.28f, 0.08f), Vector3.zero, new Vector3(1f, 1f, 0.5f));

            MeshKit.Part(grip, "Haft", MeshKit.Cylinder(0.025f, 0.025f, 0.6f, 6), Mat.Wood, new Vector3(0f, -0.1f, 0f));
            MeshKit.Part(grip, "Head", MeshKit.Ball(0.09f, 8, 6), Mat.Steel, new Vector3(0f, 0.55f, 0f));
            for (int i = 0; i < 6; i++)
            {
                MeshKit.Part(grip, "Flange" + i, MeshKit.Box(new Vector3(0.02f, 0.16f, 0.07f)), Mat.Steel,
                    new Vector3(0f, 0.55f, 0f) + Quaternion.Euler(0f, i * 60f, 0f) * new Vector3(0f, 0f, 0.09f), new Vector3(0f, i * 60f, 0f));
            }

            rig.GlowRenderers = new[] { eyeL, eyeR };
            rig.StrideLength = 2.6f;
        }

        private static void BuildLich(HumanoidRig rig, Transform grip, Renderer eyeL, Renderer eyeR, Color robeColor, Color orbColor, bool coralCrown)
        {
            var robe = MeshKit.Part(rig.Hips, "Robe", MeshKit.Lathe("lichRobe", new[]
            {
                new Vector2(0.38f, -0.92f), new Vector2(0.3f, -0.5f), new Vector2(0.22f, -0.05f), new Vector2(0.2f, 0.1f),
            }, 9), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(robe.GetComponent<Renderer>(), robeColor);
            var mantle = MeshKit.Part(rig.Torso, "Mantle", MeshKit.Lathe("lichMantle", new[]
            {
                new Vector2(0.3f, 0.35f), new Vector2(0.27f, 0.5f), new Vector2(0.16f, 0.62f), new Vector2(0.08f, 0.66f),
            }, 9), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(mantle.GetComponent<Renderer>(), robeColor * 0.8f);
            var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("lichHood", new[]
            {
                new Vector2(0.17f, -0.03f), new Vector2(0.18f, 0.12f), new Vector2(0.13f, 0.24f), new Vector2(0.04f, 0.31f), new Vector2(0f, 0.32f),
            }, 9, true, 270f), Mat.Cloth, new Vector3(0f, 0.04f, -0.02f), new Vector3(0f, 45f, 0f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), robeColor * 0.7f);

            var glow = new System.Collections.Generic.List<Renderer> { eyeL, eyeR };
            for (int i = 0; i < (coralCrown ? 5 : 0); i++)
            {
                float a = -60f + i * 30f;
                var c = MeshKit.Part(rig.Head, "Coral" + i, MeshKit.Lathe("coral", new[] { new Vector2(0.018f, 0f), new Vector2(0f, 0.12f) }, 5), Mat.Bone,
                    new Vector3(0f, 0.27f, 0.02f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.1f), new Vector3(20f, a, 0f));
                ArtLibrary.Tint(c.GetComponent<Renderer>(), new Color(0.95f, 0.45f, 0.45f));
            }

            WeakPoint.Add(rig.Torso, new Vector3(0f, 0.42f, 0.22f), 0.08f, orbColor);

            MeshKit.Part(grip, "Staff", MeshKit.Lathe("lichStaff", new[] { new Vector2(0.03f, -1.35f), new Vector2(0.025f, -0.2f), new Vector2(0.028f, 0.6f), new Vector2(0.024f, 0.78f), new Vector2(0.045f, 0.82f) }, 7), Mat.Wood, Vector3.zero);
            MeshKit.Part(grip, "Knop", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, new Vector3(0f, 0.83f, 0f), Vector3.zero, new Vector3(1f, 0.6f, 1f));

            var orb = MeshKit.Part(grip, "Orb", MeshKit.Lathe("soulCrystal", new[] { new Vector2(0f, 0f), new Vector2(0.045f, 0.05f), new Vector2(0.04f, 0.12f), new Vector2(0f, 0.19f) }, 6), Mat.Glow, new Vector3(0f, 0.86f, 0f));
            glow.Add(orb.GetComponent<Renderer>());
            for (int i = 0; i < 4; i++)
            {
                var finger = MeshKit.Pivot(grip, "Claw" + i, new Vector3(0f, 0.85f, 0f), new Vector3(0f, i * 90f + 45f, 0f));
                MeshKit.Part(finger, "Low", MeshKit.Box(new Vector3(0.018f, 0.1f, 0.018f)), Mat.Bone, new Vector3(0f, 0.04f, 0.055f), new Vector3(25f, 0f, 0f));
                MeshKit.Part(finger, "High", MeshKit.Box(new Vector3(0.015f, 0.09f, 0.015f)), Mat.Bone, new Vector3(0f, 0.13f, 0.065f), new Vector3(-30f, 0f, 0f));
                MeshKit.Part(finger, "Tip", MeshKit.Lathe("clawTip", new[] { new Vector2(0.01f, 0f), new Vector2(0f, 0.04f) }, 4), Mat.Bone, new Vector3(0f, 0.18f, 0.04f), new Vector3(-70f, 0f, 0f));
            }
            var light = new GameObject("OrbLight").AddComponent<Light>();
            light.transform.SetParent(orb.transform, false);
            light.type = LightType.Point;
            light.color = orbColor;
            light.range = coralCrown ? 9f : 5f;
            light.intensity = coralCrown ? 5f : 2.5f;

            rig.GlowRenderers = glow.ToArray();
            rig.StrideLength = 1.6f;
        }

        private static void CasterHorns(Transform head, Color bone)
        {
            if (head == null) return;
            foreach (float sx in new[] { -1f, 1f })
            {
                var at = MeshKit.Pivot(head, "Horn", new Vector3(sx * 0.1f, 0.17f, -0.02f), new Vector3(-30f, 0f, -sx * 28f));
                float r = 0.05f;
                for (int k = 0; k < 5; k++)
                {
                    ArtLibrary.Tint(MeshKit.Part(at, "HornSeg", MeshKit.Cylinder(r, r * 0.8f, 0.16f, 6), Mat.Bone, Vector3.zero).GetComponent<Renderer>(), bone * (1f - k * 0.05f));
                    at = MeshKit.Pivot(at, "HornJoint", Vector3.up * 0.15f, new Vector3(-18f, 0f, -sx * 6f));
                    r *= 0.78f;
                }
            }
        }

        private static void BuildGhoul(HumanoidRig rig, Renderer eyeL, Renderer eyeR)
        {
            var flesh = new Color(0.55f, 0.62f, 0.5f);
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (ArtLibrary.Is(r.sharedMaterial, Mat.Bone)) ArtLibrary.Tint(r, flesh);
            }
            for (int i = 0; i < 2; i++)
            {
                var arm = i == 0 ? rig.ArmL : rig.ArmR;
                for (int c = 0; c < 3; c++)
                {
                    var claw = MeshKit.Part(arm, "Claw" + c, MeshKit.Lathe("ghoulClaw", new[] { new Vector2(0.018f, 0f), new Vector2(0.012f, 0.12f), new Vector2(0f, 0.26f) }, 4), Mat.Bone,
                        new Vector3(-0.03f + c * 0.03f, -0.66f, 0.03f), new Vector3(150f + c * 6f, 0f, (c - 1) * 10f));
                    ArtLibrary.Tint(claw.GetComponent<Renderer>(), new Color(0.24f, 0.22f, 0.17f));
                }
            }

            var hide = new Color(0.34f, 0.4f, 0.29f);
            var t = rig.Torso;
            if (t != null)
            {
                ArtLibrary.Tint(MeshKit.Part(t, "Hide", MeshKit.Lathe("ghoulHide", new[]
                {
                    new Vector2(0.12f, 0.02f), new Vector2(0.16f, 0.2f), new Vector2(0.18f, 0.38f), new Vector2(0.16f, 0.5f), new Vector2(0.08f, 0.56f),
                }, 10, true, 290f), Mat.Leather, new Vector3(0f, 0f, -0.01f), new Vector3(0f, 35f, 0f), new Vector3(1.05f, 1f, 0.85f)).GetComponent<Renderer>(), hide);

                ArtLibrary.Tint(MeshKit.Part(t, "Hump", MeshKit.Ball(0.17f, 10, 8), Mat.Leather, new Vector3(0f, 0.56f, -0.12f), Vector3.zero, new Vector3(1.45f, 1.05f, 1f)).GetComponent<Renderer>(), hide * 0.9f);

                ArtLibrary.Tint(MeshKit.Part(t, "Gut", MeshKit.Ball(0.13f, 10, 8), Mat.Leather, new Vector3(0f, 0.1f, 0.03f), Vector3.zero, new Vector3(1.05f, 0.85f, 0.95f)).GetComponent<Renderer>(), hide * 1.08f);
                for (int k = 0; k < 6; k++)
                    ArtLibrary.Tint(MeshKit.Part(t, "SpineKnob", MeshKit.Ball(0.032f - k * 0.002f, 6, 4), Mat.Bone, new Vector3(0f, 0.56f - k * 0.09f, -0.2f + k * 0.012f - (k == 1 ? 0.05f : 0f))).GetComponent<Renderer>(), flesh * 1.15f);
                foreach (float side in new[] { -1f, 1f })
                    ArtLibrary.Tint(MeshKit.Part(t, "Blade", MeshKit.Box(new Vector3(0.13f, 0.17f, 0.025f)), Mat.Bone, new Vector3(side * 0.11f, 0.4f, -0.17f), new Vector3(-20f, side * 25f, side * 12f)).GetComponent<Renderer>(), flesh);
            }
            if (rig.Head != null)
                for (int k = 0; k < 11; k++)
                {
                    float a = (-120f + k * 24f) * Mathf.Deg2Rad;
                    ArtLibrary.Tint(MeshKit.Part(rig.Head, "Hair", MeshKit.Box(new Vector3(0.018f, 0.24f + (k % 3) * 0.06f, 0.012f)), Mat.Cloth,
                        new Vector3(Mathf.Sin(a) * 0.1f, 0.06f - (k % 3) * 0.03f, -Mathf.Cos(a) * 0.08f - 0.02f), new Vector3(-12f, 0f, Mathf.Sin(a) * 8f)).GetComponent<Renderer>(), new Color(0.1f, 0.1f, 0.09f));
                }
            var rags = MeshKit.Part(rig.Hips, "Rags", MeshKit.Lathe("ghoulRags", new[]
            {
                new Vector2(0.18f, -0.35f), new Vector2(0.17f, 0f), new Vector2(0.15f, 0.06f),
            }, 7), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(rags.GetComponent<Renderer>(), new Color(0.25f, 0.22f, 0.18f));
            rig.GlowRenderers = new[] { eyeL, eyeR };
            rig.StrideLength = 1.0f;
        }

        private static void BuildBrute(HumanoidRig rig, Transform grip, Renderer eyeL, Renderer eyeR)
        {
            var old = new Color(0.62f, 0.55f, 0.45f);
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (ArtLibrary.Is(r.sharedMaterial, Mat.Bone)) ArtLibrary.Tint(r, old);
            }
            for (int i = 0; i < 2; i++)
            {
                var arm = i == 0 ? rig.ArmL : rig.ArmR;
                MeshKit.Part(arm, "Pad", MeshKit.Lathe("pauldron", new[]
                {
                    new Vector2(0.17f, -0.10f), new Vector2(0.16f, 0.0f), new Vector2(0.12f, 0.08f), new Vector2(0.0f, 0.11f),
                }, 8), Mat.Rubble, Vector3.zero, new Vector3(0f, 0f, i == 0 ? 18f : -18f));
                for (int k = 0; k < 3; k++)
                {
                    MeshKit.Part(arm, "Spike" + k, MeshKit.Lathe("spike", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.14f) }, 4), Mat.Bone,
                        new Vector3((i == 0 ? -0.06f : 0.06f), 0.06f, -0.06f + k * 0.06f), new Vector3(0f, 0f, i == 0 ? 35f : -35f));
                }
            }
            MeshKit.Part(rig.Torso, "Belt", MeshKit.Cylinder(0.2f, 0.2f, 0.09f, 8), Mat.Leather, new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.7f));

            MeshKit.Part(grip, "Haft", MeshKit.Cylinder(0.035f, 0.035f, 0.95f, 6), Mat.Wood, new Vector3(0f, -0.15f, 0f));

            ArtLibrary.Tint(MeshKit.Part(grip, "Head", MeshKit.Box(new Vector3(0.38f, 0.26f, 0.26f)), Mat.Rubble, new Vector3(0f, 0.85f, 0f), new Vector3(0f, 12f, 4f)).GetComponent<Renderer>(), new Color(0.45f, 0.41f, 0.37f));
            MeshKit.Part(grip, "Band", MeshKit.Box(new Vector3(0.4f, 0.06f, 0.28f)), Mat.Steel, new Vector3(0f, 0.85f, 0f), new Vector3(0f, 12f, 4f));

            var head = MeshKit.Pivot(grip, "MaulHead", new Vector3(0f, 0.85f, 0f), new Vector3(0f, 12f, 4f));
            foreach (float x in new[] { -0.17f, 0.17f })
                MeshKit.Part(head, "EndBand", MeshKit.Box(new Vector3(0.05f, 0.28f, 0.28f)), Mat.Steel, new Vector3(x, 0f, 0f));
            var spike = MeshKit.Lathe("maulSpike", new[] { new Vector2(0.035f, 0f), new Vector2(0.025f, 0.04f), new Vector2(0f, 0.12f) }, 5);
            foreach (var (pos, rot) in new[] { (new Vector3(-0.08f, 0.13f, 0f), new Vector3(0f, 0f, 0f)), (new Vector3(0.08f, 0.13f, 0f), new Vector3(0f, 0f, 0f)),
                (new Vector3(0f, 0f, 0.13f), new Vector3(90f, 0f, 0f)), (new Vector3(0f, 0f, -0.13f), new Vector3(-90f, 0f, 0f)),
                (new Vector3(-0.08f, -0.13f, 0f), new Vector3(180f, 0f, 0f)), (new Vector3(0.08f, -0.13f, 0f), new Vector3(180f, 0f, 0f)) })
                MeshKit.Part(head, "Spike", spike, Mat.Steel, pos, rot);
            for (int w = 0; w < 4; w++)
                MeshKit.Part(grip, "HaftWrap" + w, MeshKit.Cylinder(0.042f, 0.042f, 0.03f, 6), Mat.Leather, new Vector3(0f, -0.25f + w * 0.07f, 0f));
            rig.GlowRenderers = new[] { eyeL, eyeR };
            rig.StrideLength = 1.9f;
        }

        private static GameObject LongBone(Transform parent, string name, Vector3 top, float length, float thick)
        {
            var mesh = MeshKit.Lathe("longBone", new[]
            {
                new Vector2(0.38f, 0f), new Vector2(0.5f, 0.03f), new Vector2(0.42f, 0.09f), new Vector2(0.24f, 0.2f),
                new Vector2(0.2f, 0.5f), new Vector2(0.24f, 0.8f), new Vector2(0.44f, 0.92f), new Vector2(0.5f, 0.97f), new Vector2(0.3f, 1f),
            }, 6);
            return MeshKit.Part(parent, name, mesh, Mat.Bone, top - new Vector3(0f, length, 0f), Vector3.zero, new Vector3(thick, length, thick));
        }

        private static void BuildSkullDetail(Transform head)
        {
            MeshKit.Part(head, "Cranium", MeshKit.Lathe("cranium", new[]
            {
                new Vector2(0.07f, 0.02f), new Vector2(0.105f, 0.06f), new Vector2(0.125f, 0.12f), new Vector2(0.12f, 0.18f), new Vector2(0.09f, 0.24f), new Vector2(0f, 0.265f),
            }, 9, false), Mat.Bone, new Vector3(0f, 0.01f, -0.01f), Vector3.zero, new Vector3(0.92f, 1f, 1.1f));
            MeshKit.Part(head, "Face", MeshKit.Box(new Vector3(0.17f, 0.11f, 0.06f)), Mat.Bone, new Vector3(0f, 0.1f, 0.085f));
            MeshKit.Part(head, "Brow", MeshKit.Box(new Vector3(0.19f, 0.03f, 0.05f)), Mat.Bone, new Vector3(0f, 0.165f, 0.1f), new Vector3(-8f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(head, "Cheek", MeshKit.Box(new Vector3(0.05f, 0.03f, 0.07f)), Mat.Bone, new Vector3(s * 0.085f, 0.085f, 0.07f), new Vector3(0f, s * 18f, s * -10f));
                MeshKit.Part(head, s < 0 ? "SocketL" : "SocketR", MeshKit.Ball(0.034f, 7, 5), Mat.Dark, new Vector3(s * 0.045f, 0.128f, 0.108f), Vector3.zero, new Vector3(1.1f, 0.9f, 0.6f));
            }
            MeshKit.Part(head, "Nose", MeshKit.Lathe("nasal", new[] { new Vector2(0.016f, 0f), new Vector2(0f, 0.035f) }, 3), Mat.Dark, new Vector3(0f, 0.07f, 0.114f), new Vector3(0f, 0f, 180f), new Vector3(1f, 1f, 0.5f));
            for (int t = 0; t < 6; t++)
                MeshKit.Part(head, "ToothU" + t, MeshKit.Box(new Vector3(0.017f, 0.022f, 0.012f)), Mat.Bone, new Vector3(-0.045f + t * 0.018f, 0.04f, 0.112f));

            var hinge = MeshKit.Pivot(head, "JawHinge", new Vector3(0f, 0.06f, 0.0f));
            MeshKit.Part(hinge, "Jaw", MeshKit.Box(new Vector3(0.15f, 0.035f, 0.12f)), Mat.Bone, new Vector3(0f, -0.04f, 0.06f), new Vector3(8f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
                MeshKit.Part(hinge, "Ramus", MeshKit.Box(new Vector3(0.02f, 0.06f, 0.035f)), Mat.Bone, new Vector3(s * 0.07f, -0.015f, 0.01f));
            for (int t = 0; t < 6; t++)
                MeshKit.Part(hinge, "ToothL" + t, MeshKit.Box(new Vector3(0.016f, 0.02f, 0.011f)), Mat.Bone, new Vector3(-0.045f + t * 0.018f, -0.012f, 0.112f));
            hinge.gameObject.AddComponent<JawChatter>();
        }

        private static Transform BuildBoneArm(Transform torso, string name, float side)
        {
            var shoulder = MeshKit.Pivot(torso, name, new Vector3(0.21f * side, 0.58f, 0f));
            MeshKit.Part(shoulder, "Joint", MeshKit.Ball(0.042f, 7, 5), Mat.Bone, Vector3.zero);

            MeshKit.Part(shoulder, "Scapula", MeshKit.Box(new Vector3(0.1f, 0.13f, 0.02f)), Mat.Bone, new Vector3(-0.05f * side, -0.06f, -0.08f), new Vector3(0f, 0f, side * 12f));
            LongBone(shoulder, "Humerus", new Vector3(0f, -0.02f, 0f), 0.3f, 0.05f);
            MeshKit.Part(shoulder, "Elbow", MeshKit.Ball(0.03f, 6, 4), Mat.Bone, new Vector3(0f, -0.32f, 0f));
            LongBone(shoulder, "Radius", new Vector3(0.012f, -0.33f, 0f), 0.26f, 0.034f);
            LongBone(shoulder, "Ulna", new Vector3(-0.012f, -0.33f, 0f), 0.26f, 0.03f);
            MeshKit.Part(shoulder, "Hand", MeshKit.Box(new Vector3(0.06f, 0.05f, 0.025f)), Mat.Bone, new Vector3(0f, -0.61f, 0f));
            for (int f = 0; f < 4; f++)
                MeshKit.Part(shoulder, "Finger" + f, MeshKit.Box(new Vector3(0.011f, 0.06f, 0.011f)), Mat.Bone, new Vector3(-0.022f + f * 0.0145f, -0.665f, 0.008f), new Vector3(14f + f * 3f, 0f, 0f));
            MeshKit.Part(shoulder, "Thumb", MeshKit.Box(new Vector3(0.011f, 0.045f, 0.011f)), Mat.Bone, new Vector3(0.036f * side, -0.62f, 0.015f), new Vector3(0f, 0f, side * 30f));
            return shoulder;
        }

        private static void BuildBoneLegs(HumanoidRig rig)
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var hip = MeshKit.Pivot(rig.Hips, i == 0 ? "LegL" : "LegR", new Vector3(0.1f * side, -0.04f, 0f));
                LongBone(hip, "Femur", new Vector3(0f, -0.01f, 0f), 0.43f, 0.058f);
                MeshKit.Part(hip, "Knee", MeshKit.Ball(0.036f, 7, 5), Mat.Bone, new Vector3(0f, -0.44f, 0.015f));
                LongBone(hip, "Tibia", new Vector3(0.008f, -0.45f, 0f), 0.4f, 0.046f);
                LongBone(hip, "Fibula", new Vector3(-0.022f * side, -0.46f, -0.01f), 0.37f, 0.024f);
                MeshKit.Part(hip, "Heel", MeshKit.Box(new Vector3(0.05f, 0.04f, 0.06f)), Mat.Bone, new Vector3(0f, -0.86f, -0.01f));
                MeshKit.Part(hip, "Foot", MeshKit.Box(new Vector3(0.07f, 0.025f, 0.11f)), Mat.Bone, new Vector3(0f, -0.875f, 0.07f), new Vector3(6f, 0f, 0f));
                for (int t = 0; t < 3; t++)
                    MeshKit.Part(hip, "Toe" + t, MeshKit.Box(new Vector3(0.016f, 0.016f, 0.04f)), Mat.Bone, new Vector3(-0.022f + t * 0.022f, -0.88f, 0.14f));
                if (i == 0) rig.LegL = hip; else rig.LegR = hip;
            }
        }

        private static HumanoidRig BuildWraith(Transform root)
        {
            var visual = MeshKit.Pivot(root, "WraithVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(0.45f, 0.75f, 1f);
            var shroud = new Color(0.16f, 0.2f, 0.3f, 0.55f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));

            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.1f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.1f, 0f, 0f));
            var skirt = MeshKit.Part(rig.Hips, "Shroud", MeshKit.Lathe("wraithShroud", new[]
            {
                new Vector2(0.05f, -0.85f), new Vector2(0.3f, -0.7f), new Vector2(0.34f, -0.35f), new Vector2(0.24f, 0f), new Vector2(0.2f, 0.08f),
            }, 10), Mat.Smoke, Vector3.zero);
            ArtLibrary.Tint(skirt.GetComponent<Renderer>(), shroud);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var rag = MeshKit.Part(rig.Hips, "Tatter" + i, MeshKit.Box(new Vector3(0.1f, 0.35f + (i % 3) * 0.1f, 0.015f)), Mat.Smoke,
                    new Vector3(Mathf.Cos(a) * 0.24f, -0.85f, Mathf.Sin(a) * 0.24f), new Vector3(10f, -i * 45f + 90f, (i % 2 == 0 ? 8f : -8f)));
                ArtLibrary.Tint(rag.GetComponent<Renderer>(), shroud * 0.9f);
            }

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            var robe = MeshKit.Part(rig.Torso, "Robe", MeshKit.Lathe("wraithRobe", new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.24f, 0.25f), new Vector2(0.25f, 0.45f), new Vector2(0.18f, 0.6f), new Vector2(0.08f, 0.66f),
            }, 10), Mat.Smoke, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.8f));
            ArtLibrary.Tint(robe.GetComponent<Renderer>(), shroud);
            MeshKit.Part(rig.Torso, "Spine", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.05f)), Mat.Bone, new Vector3(0f, 0.27f, -0.04f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(rig.Torso, "Rib" + i, MeshKit.Lathe("rib" + i, new[] { new Vector2(0.12f + i * 0.012f, 0f), new Vector2(0.135f + i * 0.012f, 0.015f), new Vector2(0.12f + i * 0.012f, 0.03f) }, 8, true, 300f),
                    Mat.Bone, new Vector3(0f, 0.3f + i * 0.07f, 0f), new Vector3(0f, 210f, 0f), new Vector3(1f, 1f, 0.75f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.68f, 0f));
            var skull = MeshKit.Part(rig.Head, "Skull", MeshKit.Ball(0.12f, 8, 6), Mat.Bone, new Vector3(0f, 0.13f, 0.02f), Vector3.zero, new Vector3(0.9f, 1f, 1.05f));
            ArtLibrary.Tint(skull.GetComponent<Renderer>(), new Color(0.75f, 0.85f, 0.95f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(-0.042f, 0.14f, 0.12f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(0.042f, 0.14f, 0.12f));
            var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("mageHood", new[]
            {
                new Vector2(0.17f, -0.02f), new Vector2(0.18f, 0.12f), new Vector2(0.14f, 0.24f), new Vector2(0.05f, 0.28f), new Vector2(0f, 0.29f),
            }, 9, true, 280f), Mat.Smoke, new Vector3(0f, 0.03f, -0.04f), new Vector3(0f, 40f, 0f), new Vector3(1.25f, 1.25f, 1.3f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), new Color(0.1f, 0.12f, 0.18f, 0.75f));

            rig.ArmR = BuildBoneArm(rig.Torso, "ArmR", 1f);
            rig.ArmL = BuildBoneArm(rig.Torso, "ArmL", -1f);
            foreach (var arm in new[] { rig.ArmR, rig.ArmL })
            {
                var sleeve = MeshKit.Part(arm, "Sleeve", MeshKit.Cylinder(0.12f, 0.08f, 0.45f, 8), Mat.Smoke, new Vector3(0f, -0.45f, 0f));
                ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), shroud);

                for (int f = 0; f < 4; f++)
                    MeshKit.Part(arm, "Claw" + f, MeshKit.Lathe("wraithFinger", new[] { new Vector2(0.012f, 0f), new Vector2(0.008f, 0.16f), new Vector2(0f, 0.3f) }, 4), Mat.Bone,
                        new Vector3((f - 1.5f) * 0.026f, -0.66f, 0.02f), new Vector3(170f + f * 4f, 0f, (f - 1.5f) * 9f));
            }

            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                if (Mathf.Cos(a * Mathf.Deg2Rad) > 0.6f) continue;
                float back = Mathf.Cos(a * Mathf.Deg2Rad) < 0f ? 1f : 0f;
                float len = 0.5f + back * 0.35f + (i % 3) * 0.08f;
                var strip = MeshKit.Part(rig.Torso, "Mantle" + i, MeshKit.Box(new Vector3(0.12f, len, 0.012f)), Mat.Smoke,
                    Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.26f) + new Vector3(0f, 0.62f - len * 0.5f, 0f), new Vector3(back * 6f - 4f, a, (i % 2 == 0 ? 5f : -5f)));
                ArtLibrary.Tint(strip.GetComponent<Renderer>(), shroud * (0.8f + 0.1f * (i % 3)));
            }
            var glow = new GameObject("WraithGlow").AddComponent<Light>();
            glow.transform.SetParent(visual, false);
            glow.transform.localPosition = new Vector3(0f, 1.6f, 0.3f);
            glow.type = LightType.Point;
            glow.color = new Color(0.45f, 0.7f, 1f);
            glow.range = 3.5f;
            glow.intensity = 1.2f;
            glow.shadows = LightShadows.None;

            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 1.4f;
            return rig;
        }

        private static HumanoidRig BuildGargoyle(Transform root)
        {
            var stone = new Color(0.58f, 0.58f, 0.62f);
            var visual = MeshKit.Pivot(root, "GargoyleVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(1f, 0.3f, 0.12f);
            Renderer Stone(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, stone); return r; }

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.75f, 0f));
            Stone(MeshKit.Part(rig.Hips, "Pelvis", MeshKit.Box(new Vector3(0.42f, 0.25f, 0.3f)), Mat.Bone, Vector3.zero));
            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.16f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.16f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                Stone(MeshKit.Part(leg, "Thigh", MeshKit.Box(new Vector3(0.16f, 0.38f, 0.18f)), Mat.Bone, new Vector3(0f, -0.15f, 0.08f), new Vector3(-30f, 0f, 0f)));
                Stone(MeshKit.Part(leg, "Shin", MeshKit.Box(new Vector3(0.12f, 0.38f, 0.12f)), Mat.Bone, new Vector3(0f, -0.48f, 0.02f), new Vector3(25f, 0f, 0f)));
                Stone(MeshKit.Part(leg, "Foot", MeshKit.Box(new Vector3(0.16f, 0.06f, 0.26f)), Mat.Bone, new Vector3(0f, -0.72f, 0.1f)));
            }
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", new Vector3(0f, 0.1f, 0f));
            Stone(MeshKit.Part(rig.Torso, "Chest", MeshKit.Box(new Vector3(0.55f, 0.5f, 0.38f)), Mat.Bone, new Vector3(0f, 0.3f, 0.06f), new Vector3(20f, 0f, 0f)));
            Stone(MeshKit.Part(rig.Torso, "Hump", MeshKit.Ball(0.25f, 8, 6), Mat.Bone, new Vector3(0f, 0.5f, -0.1f)));

            foreach (float s in new[] { -1f, 1f })
            {
                var wing = MeshKit.Pivot(rig.Torso, s < 0 ? "WingL" : "WingR", new Vector3(s * 0.18f, 0.55f, -0.18f));
                Stone(MeshKit.Part(wing, "Bone", MeshKit.Box(new Vector3(0.9f, 0.07f, 0.07f)), Mat.Bone, new Vector3(s * 0.45f, 0.15f, -0.05f), new Vector3(0f, 0f, s * 25f)));
                for (int i = 0; i < 3; i++)
                    Stone(MeshKit.Part(wing, "Vane" + i, MeshKit.Box(new Vector3(0.32f, 0.6f - i * 0.12f, 0.02f)), Mat.Bone,
                        new Vector3(s * (0.25f + i * 0.28f), -0.05f - i * 0.06f, -0.06f), new Vector3(0f, 0f, s * (10f - i * 12f))));
                wing.localRotation = Quaternion.Euler(0f, s * 20f, s * -25f);
            }
            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.62f, 0.22f));
            Stone(MeshKit.Part(rig.Head, "Skull", MeshKit.Box(new Vector3(0.28f, 0.24f, 0.28f)), Mat.Bone, new Vector3(0f, 0.08f, 0f)));
            Stone(MeshKit.Part(rig.Head, "Snout", MeshKit.Box(new Vector3(0.18f, 0.12f, 0.2f)), Mat.Bone, new Vector3(0f, 0.02f, 0.2f)));
            foreach (float s in new[] { -1f, 1f })
                Stone(MeshKit.Part(rig.Head, "Horn", MeshKit.Lathe("gargHorn", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.28f) }, 5), Mat.Bone,
                    new Vector3(s * 0.1f, 0.2f, -0.05f), new Vector3(-35f, 0f, s * -25f)));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.03f, 6, 4), Mat.Glow, new Vector3(-0.07f, 0.12f, 0.145f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.03f, 6, 4), Mat.Glow, new Vector3(0.07f, 0.12f, 0.145f));
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = MeshKit.Pivot(rig.Torso, s < 0 ? "ArmL" : "ArmR", new Vector3(s * 0.32f, 0.45f, 0.08f));
                Stone(MeshKit.Part(arm, "Upper", MeshKit.Box(new Vector3(0.14f, 0.42f, 0.14f)), Mat.Bone, new Vector3(0f, -0.21f, 0f)));
                Stone(MeshKit.Part(arm, "Fore", MeshKit.Box(new Vector3(0.12f, 0.4f, 0.12f)), Mat.Bone, new Vector3(0f, -0.58f, 0.04f)));
                for (int f = 0; f < 3; f++)
                    Stone(MeshKit.Part(arm, "Claw" + f, MeshKit.Lathe("gargClaw", new[] { new Vector2(0.025f, 0f), new Vector2(0f, 0.14f) }, 4), Mat.Bone,
                        new Vector3((f - 1) * 0.04f, -0.8f, 0.06f), new Vector3(160f, 0f, (f - 1) * 10f)));
                if (s < 0) rig.ArmL = arm; else rig.ArmR = arm;
            }
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 1.3f;
            return rig;
        }

        private static HumanoidRig BuildUnsung(Transform root)
        {
            var bone = new Color(0.62f, 0.6f, 0.66f);
            var violet = new Color(0.6f, 0.3f, 1f);
            var visual = MeshKit.Pivot(root, "UnsungVisual", Vector3.zero);
            visual.localScale = Vector3.one * 2.1f;
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = violet;
            Renderer Bone(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, bone); return r; }

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.9f, 0f));

            for (int i = 0; i < 7; i++)
                Bone(MeshKit.Part(rig.Hips, "Vertebra" + i, MeshKit.Ball(0.07f - i * 0.006f, 8, 6), Mat.Bone, new Vector3(0f, -0.1f - i * 0.12f, -0.02f * i), default, new Vector3(1.2f, 0.8f, 1.2f)));
            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.1f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.1f, 0f, 0f));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", new Vector3(0f, 0.05f, 0f));
            Bone(MeshKit.Part(rig.Torso, "Spine", MeshKit.Cylinder(0.05f, 0.05f, 0.75f, 6), Mat.Bone, new Vector3(0f, 0f, -0.14f)));
            Bone(MeshKit.Part(rig.Torso, "Sternum", MeshKit.Box(new Vector3(0.07f, 0.4f, 0.04f)), Mat.Bone, new Vector3(0f, 0.38f, 0.18f)));
            for (int i = 0; i < 6; i++)
            {
                float y = 0.12f + i * 0.09f, w = 0.26f - Mathf.Abs(i - 2.5f) * 0.025f;
                foreach (float s in new[] { -1f, 1f })
                    Bone(MeshKit.Part(rig.Torso, "Rib" + i, MeshKit.Box(new Vector3(w, 0.025f, 0.03f)), Mat.Bone, new Vector3(s * w * 0.45f, y, 0.03f), new Vector3(0f, s * -38f, s * -12f)));
            }

            var heart = MeshKit.Part(rig.Torso, "Heart", MeshKit.Ball(0.08f, 10, 8), Mat.Glow, new Vector3(0f, 0.4f, 0.02f));
            ArtLibrary.SetGlow(heart.GetComponent<Renderer>(), violet * 1.4f);
            WeakPoint.Add(rig.Torso, new Vector3(0f, 0.4f, 0.04f), 0.07f, violet);
            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.84f, 0.04f));
            Bone(MeshKit.Part(rig.Head, "Cranium", MeshKit.Ball(0.17f, 12, 10), Mat.Bone, new Vector3(0f, 0.08f, 0f), default, new Vector3(1.05f, 1f, 1.15f)));
            Bone(MeshKit.Part(rig.Head, "Jaw", MeshKit.Box(new Vector3(0.2f, 0.06f, 0.16f)), Mat.Bone, new Vector3(0f, -0.1f, 0.06f), new Vector3(18f, 0f, 0f)));
            var eyes = new List<Renderer>();
            foreach (var e in new[] { new Vector3(-0.065f, 0.09f, 0.16f), new Vector3(0.065f, 0.09f, 0.16f), new Vector3(0f, 0.17f, 0.17f) })
            {
                MeshKit.Part(rig.Head, "Socket", MeshKit.Ball(0.04f, 8, 6), Mat.Dark, e - new Vector3(0f, 0f, 0.01f));
                var eye = MeshKit.Part(rig.Head, "Eye", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, e + new Vector3(0f, 0f, 0.02f));
                eyes.Add(eye.GetComponent<Renderer>());
            }
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = MeshKit.Pivot(rig.Torso, s < 0 ? "ArmL" : "ArmR", new Vector3(s * 0.26f, 0.66f, 0f));
                Bone(MeshKit.Part(arm, "Upper", MeshKit.Cylinder(0.03f, 0.025f, 0.5f, 6), Mat.Bone, new Vector3(0f, -0.5f, 0f)));
                Bone(MeshKit.Part(arm, "Fore", MeshKit.Cylinder(0.025f, 0.02f, 0.5f, 6), Mat.Bone, new Vector3(0f, -1.0f, 0.05f)));
                for (int f = 0; f < 4; f++)
                    Bone(MeshKit.Part(arm, "Claw" + f, MeshKit.Lathe("unsungClaw", new[] { new Vector2(0.015f, 0f), new Vector2(0f, 0.18f) }, 4), Mat.Bone,
                        new Vector3((f - 1.5f) * 0.03f, -1.02f, 0.08f), new Vector3(170f, 0f, (f - 1.5f) * 9f)));
                if (s < 0) rig.ArmL = arm; else rig.ArmR = arm;
            }

            foreach (var (name, r, y) in new[] { ("BoneRingA", 0.62f, 0.45f), ("BoneRingB", 0.78f, 0.25f) })
            {
                var ring = MeshKit.Pivot(visual, name, new Vector3(0f, 0.9f + y, 0f));
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.PI * 2f / 14f;
                    Bone(MeshKit.Part(ring, "Bone" + i, MeshKit.Cylinder(0.018f, 0.018f, 0.16f, 5), Mat.Bone, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), new Vector3(90f, -a * Mathf.Rad2Deg, 0f)));
                }
            }
            var glow = new GameObject("UnsungLight").AddComponent<Light>();
            glow.transform.SetParent(visual, false);
            glow.transform.localPosition = new Vector3(0f, 1.3f, 0.5f);
            glow.color = violet;

            glow.range = 11f;
            glow.intensity = 3.2f;
            glow.shadows = LightShadows.None;
            rig.GlowRenderers = eyes.ToArray();
            rig.StrideLength = 2f;

            GuardianKit.Unsung(rig);
            return rig;
        }

        private static HumanoidRig BuildEntling(Transform root)
        {
            var bark = new Color(0.5f, 0.37f, 0.25f);
            var moss = new Color(0.32f, 0.48f, 0.18f);
            var visual = MeshKit.Pivot(root, "EntlingVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(1f, 0.7f, 0.2f);
            Renderer Bark(GameObject g, float k = 1f) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, bark * k); return r; }

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.55f, 0f));

            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.13f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.13f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                Bark(MeshKit.Part(leg, "Root", MeshKit.Cylinder(0.075f, 0.05f, 0.5f, 6), Mat.Wood, new Vector3(0f, -0.26f, 0f), new Vector3(0f, 0f, 6f)));
                Bark(MeshKit.Part(leg, "Strand", MeshKit.Cylinder(0.04f, 0.03f, 0.48f, 5), Mat.Wood, new Vector3(0.04f, -0.25f, 0.03f), new Vector3(8f, 30f, -10f)), 0.85f);
                for (int t = -1; t <= 1; t++)
                    Bark(MeshKit.Part(leg, "Toe", MeshKit.Cylinder(0.03f, 0.008f, 0.22f, 4), Mat.Wood, new Vector3(t * 0.06f, -0.52f, 0.08f), new Vector3(80f, t * 30f, 0f)), 0.9f);
            }

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", new Vector3(0f, 0.02f, 0f));
            Bark(MeshKit.Part(rig.Torso, "Trunk", MeshKit.Lathe("entTrunk", new[]
            {
                new Vector2(0.2f, -0.05f), new Vector2(0.26f, 0.12f), new Vector2(0.24f, 0.35f), new Vector2(0.19f, 0.55f), new Vector2(0.1f, 0.66f),
            }, 9), Mat.Wood, Vector3.zero, new Vector3(12f, 0f, 0f), new Vector3(1f, 1f, 0.85f)));
            for (int i = 0; i < 5; i++)
                Bark(MeshKit.Part(rig.Torso, "Ridge" + i, MeshKit.Box(new Vector3(0.035f, 0.5f, 0.04f)), Mat.Wood, Quaternion.Euler(0f, i * 72f + 20f, 0f) * new Vector3(0f, 0f, 0.235f) + Vector3.up * 0.28f,
                    new Vector3(12f, i * 72f + 20f, (i % 2 == 0 ? 4f : -4f))), 0.7f);
            var mossPatch = MeshKit.Part(rig.Torso, "Moss", MeshKit.Ball(0.14f, 7, 5), Mat.Cloth, new Vector3(-0.12f, 0.5f, 0.1f), default, new Vector3(1.2f, 0.45f, 1f));
            ArtLibrary.Tint(mossPatch.GetComponent<Renderer>(), moss);

            WeakPoint.Add(rig.Torso, new Vector3(0f, 0.3f, 0.23f), 0.06f, new Color(1f, 0.6f, 0.15f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.68f, 0.08f));
            Bark(MeshKit.Part(rig.Head, "Knot", MeshKit.Ball(0.17f, 9, 7), Mat.Wood, new Vector3(0f, 0.08f, 0f), default, new Vector3(1f, 0.85f, 0.95f)));
            Bark(MeshKit.Part(rig.Head, "Brow", MeshKit.Box(new Vector3(0.3f, 0.05f, 0.08f)), Mat.Wood, new Vector3(0f, 0.13f, 0.13f), new Vector3(-15f, 0f, 0f)), 0.75f);
            var mouth = MeshKit.Part(rig.Head, "Mouth", MeshKit.Box(new Vector3(0.14f, 0.035f, 0.03f)), Mat.Dark, new Vector3(0f, 0.0f, 0.15f));

            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.034f, 6, 4), Mat.Glow, new Vector3(-0.065f, 0.095f, 0.15f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.034f, 6, 4), Mat.Glow, new Vector3(0.065f, 0.095f, 0.15f));
            foreach (var (pos, rot, len) in new[] { (new Vector3(-0.08f, 0.42f, 0.25f), new Vector3(-8f, 0f, 14f), 0.26f), (new Vector3(0.09f, 0.18f, 0.26f), new Vector3(-4f, 0f, -18f), 0.22f), (new Vector3(-0.13f, 0.12f, 0.23f), new Vector3(0f, 25f, 8f), 0.18f), (new Vector3(0.12f, 0.5f, 0.21f), new Vector3(-10f, -20f, -10f), 0.16f) })
            {
                var sap = MeshKit.Part(rig.Torso, "Sap", MeshKit.Box(new Vector3(0.018f, len, 0.012f)), Mat.Glow, pos, rot);
                ArtLibrary.SetGlow(sap.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.15f) * 0.9f);
            }

            for (int i = 0; i < 7; i++)
            {
                float a = i * 51f;
                var twig = MeshKit.Part(rig.Head, "Twig" + i, MeshKit.Cylinder(0.02f, 0.005f, 0.3f + (i % 3) * 0.08f, 4), Mat.Wood,
                    new Vector3(0f, 0.2f, 0f) + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.08f, new Vector3(-25f - (i % 2) * 15f, a, 0f));
                Bark(twig, 0.8f);
                if (i % 2 == 0)
                {
                    var leaf = MeshKit.Part(rig.Head, "Leaf" + i, MeshKit.Box(new Vector3(0.09f, 0.01f, 0.13f)), Mat.Cloth,
                        new Vector3(0f, 0.38f, 0f) + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.17f, new Vector3(25f, a, 20f));
                    ArtLibrary.Tint(leaf.GetComponent<Renderer>(), i == 4 ? new Color(0.45f, 0.35f, 0.1f) : moss);
                }
            }

            foreach (float s in new[] { -1f, 1f })
            {
                var arm = MeshKit.Pivot(rig.Torso, s < 0 ? "ArmL" : "ArmR", new Vector3(s * 0.24f, 0.5f, 0.02f));
                Bark(MeshKit.Part(arm, "Upper", MeshKit.Cylinder(0.055f, 0.04f, 0.42f, 6), Mat.Wood, new Vector3(0f, -0.21f, 0f), new Vector3(0f, 0f, s * 8f)));
                Bark(MeshKit.Part(arm, "Lower", MeshKit.Cylinder(0.04f, 0.028f, 0.38f, 5), Mat.Wood, new Vector3(s * 0.03f, -0.58f, 0.04f), new Vector3(-12f, 0f, s * -4f)), 0.9f);
                for (int c = -1; c <= 1; c++)
                    Bark(MeshKit.Part(arm, "Claw", MeshKit.Cylinder(0.018f, 0.004f, 0.2f, 4), Mat.Wood, new Vector3(s * 0.03f + c * 0.03f, -0.82f, 0.06f), new Vector3(-30f, c * 25f, 0f)), 0.8f);
                if (s < 0) rig.ArmL = arm; else rig.ArmR = arm;
            }
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 0.9f;
            return rig;
        }

        private static HumanoidRig BuildMourner(Transform root)
        {
            var stone = new Color(0.92f, 0.94f, 1f);
            var visual = MeshKit.Pivot(root, "MournerVisual", Vector3.zero);
            visual.localScale = Vector3.one * 1.12f;
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(0.55f, 0.75f, 1f);
            Renderer Stone(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, stone); return r; }

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));

            Stone(MeshKit.Part(rig.Hips, "Robe", MeshKit.Lathe("mournerRobe", new[] { new Vector2(0.44f, -0.95f), new Vector2(0.36f, -0.6f), new Vector2(0.24f, -0.1f), new Vector2(0.2f, 0.12f) }, 10), Mat.Marble, Vector3.zero));
            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.1f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.1f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
                Stone(MeshKit.Part(leg, "Foot", MeshKit.Box(new Vector3(0.1f, 0.06f, 0.2f)), Mat.Marble, new Vector3(0f, -0.92f, 0.12f)));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", new Vector3(0f, 0.12f, 0f));
            Stone(MeshKit.Part(rig.Torso, "Chest", MeshKit.Lathe("mournerChest", new[] { new Vector2(0.2f, 0f), new Vector2(0.23f, 0.25f), new Vector2(0.2f, 0.5f), new Vector2(0.08f, 0.6f) }, 10), Mat.Marble, Vector3.zero, default, new Vector3(1f, 1f, 0.75f)));

            foreach (float s in new[] { -1f, 1f })
            {
                var wing = MeshKit.Pivot(rig.Torso, s < 0 ? "WingL" : "WingR", new Vector3(s * 0.12f, 0.48f, -0.14f));
                for (int i = 0; i < 6; i++)
                    Stone(MeshKit.Part(wing, "Feather" + i, MeshKit.Lathe("mournerFeather", new[] { new Vector2(0.035f, 0f), new Vector2(0.03f, 0.6f), new Vector2(0f, 0.9f) }, 4), Mat.Marble,
                        new Vector3(s * (0.04f + i * 0.05f), -0.75f + i * 0.08f, -0.04f - i * 0.015f), new Vector3(-6f, 0f, s * (4f + i * 6f)), new Vector3(1f, 1f - i * 0.1f, 0.4f)));
                wing.localRotation = Quaternion.Euler(0f, s * 25f, 0f);
            }
            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.66f, 0.02f));
            Stone(MeshKit.Part(rig.Head, "Face", MeshKit.Ball(0.13f, 10, 8), Mat.Marble, new Vector3(0f, 0.1f, 0f), default, new Vector3(0.9f, 1.1f, 0.95f)));
            Stone(MeshKit.Part(rig.Head, "Nose", MeshKit.Box(new Vector3(0.03f, 0.06f, 0.04f)), Mat.Marble, new Vector3(0f, 0.09f, 0.12f)));

            for (int i = -2; i <= 2; i++)
                Stone(MeshKit.Part(rig.Head, "Hair" + i, MeshKit.Box(new Vector3(0.05f, 0.42f, 0.04f)), Mat.Marble, new Vector3(i * 0.045f, -0.06f, -0.1f), new Vector3(10f, 0f, i * 3f)));
            Stone(MeshKit.Part(rig.Head, "Wreath", MeshKit.Cylinder(0.13f, 0.13f, 0.035f, 12), Mat.Marble, new Vector3(0f, 0.2f, 0f), new Vector3(-12f, 0f, 0f)));

            var tearL = MeshKit.Part(rig.Head, "TearL", MeshKit.Box(new Vector3(0.012f, 0.09f, 0.01f)), Mat.Glow, new Vector3(-0.045f, 0.06f, 0.115f));
            var tearR = MeshKit.Part(rig.Head, "TearR", MeshKit.Box(new Vector3(0.012f, 0.09f, 0.01f)), Mat.Glow, new Vector3(0.045f, 0.06f, 0.115f));
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = MeshKit.Pivot(rig.Torso, s < 0 ? "ArmL" : "ArmR", new Vector3(s * 0.22f, 0.5f, 0f));
                Stone(MeshKit.Part(arm, "Sleeve", MeshKit.Cylinder(0.07f, 0.09f, 0.55f, 8), Mat.Marble, new Vector3(0f, -0.55f, 0f)));
                Stone(MeshKit.Part(arm, "Hand", MeshKit.Box(new Vector3(0.07f, 0.13f, 0.04f)), Mat.Marble, new Vector3(0f, -0.62f, 0.02f)));
                if (s < 0) rig.ArmL = arm; else rig.ArmR = arm;
            }
            WeakPoint.Add(rig.Torso, new Vector3(-0.06f, 0.36f, 0.16f), 0.05f, new Color(0.55f, 0.8f, 1f));
            rig.GlowRenderers = new[] { tearL.GetComponent<Renderer>(), tearR.GetComponent<Renderer>() };
            rig.StrideLength = 1.1f;
            return rig;
        }

        private static HumanoidRig BuildMimic(Transform root)
        {
            var visual = MeshKit.Pivot(root, "MimicVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(1f, 0.55f, 0.2f);
            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.55f, 0f));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);

            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.4f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.4f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
                foreach (float z in new[] { -0.25f, 0.25f })
                {
                    MeshKit.Part(leg, "Thigh", MeshKit.Box(new Vector3(0.06f, 0.35f, 0.06f)), Mat.Bone, new Vector3(0f, -0.15f, z), new Vector3(z > 0 ? -25f : 25f, 0f, 0f));
                    MeshKit.Part(leg, "Claw", MeshKit.Box(new Vector3(0.05f, 0.3f, 0.05f)), Mat.Bone, new Vector3(0f, -0.42f, z * 1.5f), new Vector3(z > 0 ? 20f : -20f, 0f, 0f));
                }

            MeshKit.Part(rig.Torso, "Body", MeshKit.Box(new Vector3(1.1f, 0.6f, 0.7f)), Mat.Wood, new Vector3(0f, 0.3f, 0f));
            foreach (float x in new[] { -0.38f, 0.38f })
                MeshKit.Part(rig.Torso, "Band", MeshKit.Box(new Vector3(0.08f, 0.62f, 0.72f)), Mat.Gold, new Vector3(x, 0.31f, 0f));
            var maw = MeshKit.Part(rig.Torso, "Maw", MeshKit.Box(new Vector3(1f, 0.05f, 0.6f)), Mat.Dark, new Vector3(0f, 0.59f, 0f));
            for (int i = 0; i < 7; i++)
                MeshKit.Part(rig.Torso, "LowerTooth" + i, MeshKit.Lathe("tooth", new[] { new Vector2(0.035f, 0f), new Vector2(0f, 0.11f) }, 4), Mat.Bone, new Vector3(-0.45f + i * 0.15f, 0.6f, 0.32f));
            WeakPoint.Add(rig.Torso, new Vector3(0f, 0.62f, 0.05f), 0.13f, new Color(1f, 0.3f, 0.35f));
            var tongue = MeshKit.Part(rig.Torso, "Tongue", MeshKit.Box(new Vector3(0.18f, 0.05f, 0.6f)), Mat.Leather, new Vector3(0f, 0.63f, 0.25f), new Vector3(-12f, 0f, 0f));
            ArtLibrary.Tint(tongue.GetComponent<Renderer>(), new Color(0.6f, 0.15f, 0.18f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Lid", new Vector3(0f, 0.6f, -0.35f));
            MeshKit.Part(rig.Head, "LidTop", MeshKit.Box(new Vector3(1.12f, 0.22f, 0.72f)), Mat.Wood, new Vector3(0f, 0.11f, 0.35f));
            MeshKit.Part(rig.Head, "LidBand", MeshKit.Box(new Vector3(1.14f, 0.06f, 0.1f)), Mat.Gold, new Vector3(0f, 0.12f, 0.7f));
            for (int i = 0; i < 7; i++)
                MeshKit.Part(rig.Head, "UpperTooth" + i, MeshKit.Lathe("tooth", new[] { new Vector2(0.035f, 0f), new Vector2(0f, 0.11f) }, 4), Mat.Bone,
                    new Vector3(-0.45f + i * 0.15f, 0f, 0.67f), new Vector3(180f, 0f, 0f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.04f, 6, 4), Mat.Glow, new Vector3(-0.2f, -0.02f, 0.3f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.04f, 6, 4), Mat.Glow, new Vector3(0.2f, -0.02f, 0.3f));
            rig.ArmL = MeshKit.Pivot(rig.Torso, "ArmL", new Vector3(-0.5f, 0.3f, 0f));
            rig.ArmR = MeshKit.Pivot(rig.Torso, "ArmR", new Vector3(0.5f, 0.3f, 0f));
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 0.7f;
            return rig;
        }

        private static void TintLegs(HumanoidRig rig, Color legs, Color boots)
        {
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                if (leg == null) continue;
                foreach (var r in leg.GetComponentsInChildren<Renderer>())
                {
                    var n = r.gameObject.name;
                    if (n == "Thigh") ArtLibrary.Tint(r, legs);
                    else if (n == "Boot" || n == "Toe" || n == "BootCuff") ArtLibrary.Tint(r, boots);
                }
            }
        }

        private static void BuildLegs(HumanoidRig rig, float spacing, float hipHeight, string armour, string under, float width)
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var hip = MeshKit.Pivot(rig.Hips, i == 0 ? "LegL" : "LegR", new Vector3(spacing * side, 0f, 0f));
                MeshKit.Part(hip, "Thigh", MeshKit.Cylinder(width * 0.5f, width * 0.62f, 0.45f, 8), under, new Vector3(0f, -0.45f, 0f));
                MeshKit.Part(hip, "Knee", MeshKit.Ball(0.08f, 8, 6), armour, new Vector3(0f, -0.46f, 0.05f), Vector3.zero, new Vector3(1f, 0.9f, 1.1f));
                MeshKit.Part(hip, "Greave", MeshKit.Cylinder(0.075f, 0.095f, 0.38f, 8), armour, new Vector3(0f, -0.86f, 0f));
                MeshKit.Part(hip, "Boot", MeshKit.Box(new Vector3(0.14f, 0.1f, 0.22f)), under, new Vector3(0f, -0.9f, 0.02f));
                MeshKit.Part(hip, "Toe", MeshKit.Lathe("bootToe", new[] { new Vector2(0.07f, 0f), new Vector2(0.06f, 0.06f), new Vector2(0f, 0.1f) }, 8), under,
                    new Vector3(0f, -0.9f, 0.13f), new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 0.7f));
                MeshKit.Part(hip, "BootCuff", MeshKit.Cylinder(0.1f, 0.1f, 0.05f, 8), under, new Vector3(0f, -0.66f, 0f));
                if (i == 0) rig.LegL = hip; else rig.LegR = hip;
            }
        }
    }
}
