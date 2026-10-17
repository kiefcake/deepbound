using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Builds every character in the game out of <see cref="MeshKit"/> parts.
    ///
    /// <para>
    /// Every humanoid shares one skeleton layout — hips, torso, head, two shoulders, two
    /// legs — so <see cref="HumanoidRig"/> can animate any of them. Limbs hang straight down
    /// from their pivots (-Y); a negative X rotation swings a limb forward.
    /// </para>
    ///
    /// Style target: chunky, faceted, readable at a distance. Class identity has to come
    /// through the silhouette (League-style kit clarity), so each build exaggerates its
    /// defining shapes — the Knight's pauldrons and shield, the King's crown and greatsword.
    /// </summary>
    public static class CharacterArt
    {
        // ------------------------------------------------------------------
        // Knight
        // ------------------------------------------------------------------

        public static HumanoidRig BuildKnight(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "KnightVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            BuildLegs(rig, 0.13f, 0.95f, Mat.Steel, Mat.Leather, 0.19f);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            // Breastplate: a barrel that widens to the chest and narrows at the neck.
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
            // Great helm: flat-topped cylinder with a slight taper — the classic crusader read.
            // Bascinet: a skull rising to a point, a mail aventail below, and the pointed
            // "hound's snout" visor — the most gothic helm there is.
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

            // Surcoat cross, plate details, and a long cloak in a darker shade of the colours.
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

            GothicKit.BlackenSteel(visual, new Color(0.58f, 0.58f, 0.64f));
            rig.StrideLength = 1.5f;
            return rig;
        }

        private static Transform BuildKnightArm(Transform torso, string name, float side)
        {
            var shoulder = MeshKit.Pivot(torso, name, new Vector3(0.29f * side, 0.5f, 0f));

            // Pauldron: a half dome sitting over the shoulder — the Knight's silhouette.
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

        /// <summary>Arming sword. Grip pivot at the hand; blade extends along local +Y.</summary>
        public static void BuildSword(Transform grip, float scale)
        {
            // Grip: a leather-wrapped handle (raised wraps), a wheel pommel with a gold cap.
            MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.019f * scale, 0.022f * scale, 0.16f * scale, 8), Mat.Leather, new Vector3(0f, -0.06f * scale, 0f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(grip, "Wrap" + i, MeshKit.Cylinder(0.024f * scale, 0.024f * scale, 0.012f * scale, 8), Mat.Leather, new Vector3(0f, (-0.045f + i * 0.04f) * scale, 0f), new Vector3(0f, 0f, i % 2 == 0 ? 8f : -8f));
            MeshKit.Part(grip, "Pommel", MeshKit.Cylinder(0.042f * scale, 0.042f * scale, 0.026f * scale, 12), Mat.Steel, new Vector3(0f, -0.085f * scale, 0f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(grip, "PommelCap", MeshKit.Ball(0.018f * scale, 6, 4), Mat.Gold, new Vector3(0f, -0.085f * scale, 0f));
            // Crossguard: a gently curved bar, thickening at the quillon ends.
            MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.2f, 0.03f, 0.04f) * scale), Mat.Gold, new Vector3(0f, 0.11f * scale, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(grip, "GuardArm", MeshKit.Box(new Vector3(0.06f, 0.026f, 0.036f) * scale), Mat.Gold, new Vector3(s * 0.12f * scale, 0.118f * scale, 0f), new Vector3(0f, 0f, s * 14f));
                MeshKit.Part(grip, "Quillon", MeshKit.Ball(0.022f * scale, 6, 4), Mat.Gold, new Vector3(s * 0.155f * scale, 0.128f * scale, 0f));
            }
            MeshKit.Part(grip, "Ricasso", MeshKit.Box(new Vector3(0.05f, 0.05f, 0.022f) * scale), Mat.Steel, new Vector3(0f, 0.15f * scale, 0f));
            // The blade: a diamond section (a four-sided lathe flattened), a dark fuller down the middle,
            // and a bevelled point.
            MeshKit.Part(grip, "Blade", MeshKit.Lathe("swordBlade", new[]
            {
                new Vector2(0.034f, 0f), new Vector2(0.032f, 0.5f), new Vector2(0.028f, 0.68f), new Vector2(0f, 0.8f),
            }, 4), Mat.Steel, new Vector3(0f, 0.17f * scale, 0f), new Vector3(0f, 45f, 0f), new Vector3(scale, scale, scale * 0.28f));
            var fuller = MeshKit.Part(grip, "Fuller", MeshKit.Box(new Vector3(0.012f, 0.46f, 0.016f) * scale), Mat.Dark, new Vector3(0f, 0.43f * scale, 0f));
            fuller.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Heater shield facing local +Z.</summary>
        public static void BuildShield(Transform mount, Color teamColor, float scale)
        {
            MeshKit.Part(mount, "ShieldBoard", MeshKit.Box(new Vector3(0.52f, 0.62f, 0.05f) * scale), Mat.Wood, Vector3.zero);
            var face = MeshKit.Part(mount, "ShieldFace", MeshKit.Box(new Vector3(0.44f, 0.54f, 0.02f) * scale), Mat.Cloth, new Vector3(0f, 0f, 0.03f * scale));
            ArtLibrary.Tint(face.GetComponent<Renderer>(), teamColor);
            // Tapered lower point, so it reads as a heater shield rather than a door.
            MeshKit.Part(mount, "ShieldPoint", MeshKit.Box(new Vector3(0.37f, 0.37f, 0.05f) * scale), Mat.Wood,
                new Vector3(0f, -0.31f * scale, 0f), new Vector3(0f, 0f, 45f));
            MeshKit.Part(mount, "RimTop", MeshKit.Box(new Vector3(0.56f, 0.04f, 0.07f) * scale), Mat.Steel, new Vector3(0f, 0.31f * scale, 0f));
            MeshKit.Part(mount, "Boss", MeshKit.Ball(0.07f * scale, 8, 4), Mat.Gold, new Vector3(0f, 0.02f * scale, 0.05f * scale), Vector3.zero, new Vector3(1f, 1f, 0.6f));
            MeshKit.Part(mount, "CrossV", MeshKit.Box(new Vector3(0.06f, 0.5f, 0.025f) * scale), Mat.Gold, new Vector3(0f, -0.02f * scale, 0.045f * scale));
            MeshKit.Part(mount, "CrossH", MeshKit.Box(new Vector3(0.38f, 0.06f, 0.025f) * scale), Mat.Gold, new Vector3(0f, 0.1f * scale, 0.045f * scale));
        }

        // ------------------------------------------------------------------
        // First-person view model
        // ------------------------------------------------------------------

        /// <summary>What the local Knight sees of themselves: sword in the right hand, shield in the left.</summary>
        public class ViewModel
        {
            public Transform Root;
            /// <summary>Main hand: the Knight's sword, the Mage's staff.</summary>
            public Transform SwordPivot;
            /// <summary>Off hand: the Knight's shield, the Mage's casting hand.</summary>
            public Transform ShieldPivot;
            /// <summary>Glowing spell light in the off hand (Mage only).</summary>
            public Renderer SpellGlow;
            /// <summary>A small point light the palm flame throws on the hands (casters).</summary>
            public Light SpellLight;
            /// <summary>The Knight's slash arc: a crescent in view space the animator wipes and fades during a cut.</summary>
            public Renderer Slash;
        }

        public static ViewModel BuildKnightViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);

            // Held low at the right, the blade raised and leaning in so it sits in view.
            var swordPivot = MeshKit.Pivot(root, "SwordHand", new Vector3(0.4f, -0.47f, 0.62f), new Vector3(-24f, -10f, 16f));
            // The hand: an articulated gauntlet (knuckle plates over a mail glove) and a sleeved forearm.
            MeshKit.Part(swordPivot, "Gauntlet", MeshKit.Ball(0.068f, 8, 6), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 0.85f, 1.25f));
            for (int k = 0; k < 4; k++)
                MeshKit.Part(swordPivot, "Knuckle" + k, MeshKit.Box(new Vector3(0.022f, 0.02f, 0.03f)), Mat.Steel, new Vector3(-0.03f + k * 0.02f, 0.03f, 0.055f), new Vector3(20f, 0f, 0f));
            MeshKit.Part(swordPivot, "Cuff", MeshKit.Cylinder(0.08f, 0.07f, 0.08f, 8), Mat.Steel, new Vector3(0f, -0.03f, -0.07f), new Vector3(-100f, 0f, 0f));
            MeshKit.Part(swordPivot, "Bracer", MeshKit.Cylinder(0.072f, 0.082f, 0.32f, 8), Mat.Steel, new Vector3(0f, -0.06f, -0.14f), new Vector3(-100f, 0f, 0f));
            var sleeve = MeshKit.Part(swordPivot, "Sleeve", MeshKit.Cylinder(0.09f, 0.1f, 0.3f, 8), Mat.Cloth, new Vector3(0f, -0.11f, -0.38f), new Vector3(-100f, 0f, 0f));
            ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), teamColor * 0.8f);
            var grip = MeshKit.Pivot(swordPivot, "Grip", new Vector3(0f, 0.02f, 0.02f), new Vector3(-10f, 0f, 0f));
            BuildSword(grip, 0.62f);

            var shieldPivot = MeshKit.Pivot(root, "ShieldHand", new Vector3(-0.55f, -0.5f, 0.7f), new Vector3(8f, 28f, 6f));
            var mount = MeshKit.Pivot(shieldPivot, "Mount", Vector3.zero, new Vector3(0f, 180f, 0f));
            BuildShield(mount, teamColor, 0.62f);

            // View-model geometry must not cast shadows: they would be placed at the
            // camera, not at the body, and look detached.
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // The slash: a thin crescent (a flat ring sector) hung in front of the view, invisible until a cut.
            var slashPivot = MeshKit.Pivot(root, "SlashPivot", new Vector3(0f, -0.22f, 0.8f));
            var slash = MeshKit.Part(slashPivot, "Slash", MeshKit.Lathe("slashArc", new[] { new Vector2(0.5f, 0f), new Vector2(0.455f, 0f) }, 16, false, 100f), Mat.Smoke,
                Vector3.zero, new Vector3(-90f, 0f, 0f));
            var slashR = slash.GetComponent<Renderer>();
            slashR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            slashR.enabled = false;
            return new ViewModel { Root = root, SwordPivot = swordPivot, ShieldPivot = shieldPivot, Slash = slashR };
        }

        // ------------------------------------------------------------------
        // Mage
        // ------------------------------------------------------------------

        /// <summary>
        /// The Mage: robe and sleeves in the party colour, a hood shadowing a faceless face
        /// with two faint arcane eyes, a silver circlet and a high collar — the silhouette that
        /// says "caster" from across any room.
        /// </summary>
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

            // A sorcerer's cowl rising to a tall point behind the head, a silver circlet with
            // a glowing brow-stone, and a high collar: dark, tall, unmistakably a caster.
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

        /// <summary>
        /// The Cleric: white-and-gold vestments with a party-coloured stole, a tall mitre (its
        /// silhouette), a sun-disc sceptre and a book of rites on the hip.
        /// </summary>
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
            // A censer swinging from the belt, embers glowing in it, and a string of beads.
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
            // Mitre: a tall, slightly flattened cone, gold-trimmed.
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

        /// <summary>First-person Cleric: the sun sceptre in the right hand, a light-filled left palm.</summary>
        public static ViewModel BuildClericViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var white = new Color(0.88f, 0.85f, 0.78f);

            var hand = MeshKit.Pivot(root, "SceptreHand", new Vector3(0.36f, -0.4f, 0.62f), new Vector3(16f, -8f, -12f));
            CasterArm(hand, white, teamColor, true);
            // A gold sceptre: knopped shaft, then a sun of eight rays round a glowing boss.
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

        /// <summary>
        /// The Rogue: dark leathers, a deep hood and a mask, a short cloak in the party colour,
        /// and a dagger in each hand. Lean and low — the least armoured silhouette in the game.
        /// </summary>
        public static HumanoidRig BuildRogue(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "RogueVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var leather = new Color(0.32f, 0.27f, 0.24f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.93f, 0f));
            BuildLegs(rig, 0.11f, 0.93f, Mat.Leather, Mat.Leather, 0.15f);

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
            // A bandolier across the chest, throwing knives slotted along it.
            MeshKit.Part(rig.Torso, "Bandolier", MeshKit.Box(new Vector3(0.06f, 0.62f, 0.4f)), Mat.Leather, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0f, -34f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(rig.Torso, "Knife" + i, MeshKit.Box(new Vector3(0.025f, 0.11f, 0.008f)), Mat.Steel,
                    new Vector3(-0.1f + i * 0.065f, 0.18f + i * 0.09f, 0.21f), new Vector3(0f, 0f, -34f));
            MeshKit.Part(rig.Hips, "Belt", MeshKit.Cylinder(0.19f, 0.19f, 0.06f, 8), Mat.Leather, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
            MeshKit.Part(rig.Hips, "Pouch", MeshKit.Box(new Vector3(0.1f, 0.1f, 0.06f)), Mat.Leather, new Vector3(0.17f, -0.03f, 0.1f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.6f, 0f));
            MeshKit.Part(rig.Head, "Face", MeshKit.Ball(0.12f, 8, 6), Mat.Dark, new Vector3(0f, 0.13f, 0.01f));
            var mask = MeshKit.Part(rig.Head, "Mask", MeshKit.Box(new Vector3(0.2f, 0.08f, 0.04f)), Mat.Cloth, new Vector3(0f, 0.08f, 0.1f));
            ArtLibrary.Tint(mask.GetComponent<Renderer>(), new Color(0.12f, 0.1f, 0.1f));
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

        /// <summary>
        /// The Ranger: a forest-green hooded cloak over leathers, a quiver on the back and a
        /// recurve bow in the left hand (the same bow the skeleton archers carry, kept bright).
        /// </summary>
        public static HumanoidRig BuildRanger(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "RangerVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var green = new Color(0.25f, 0.38f, 0.22f);
            var leather = new Color(0.42f, 0.32f, 0.22f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.94f, 0f));
            BuildLegs(rig, 0.11f, 0.94f, Mat.Leather, Mat.Leather, 0.15f);
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

        /// <summary>First-person Ranger: the bow held out in the left hand, an arrow nocked on it.</summary>
        public static ViewModel BuildRangerViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var leather = new Color(0.42f, 0.32f, 0.22f);

            var bowHand = MeshKit.Pivot(root, "BowHand", new Vector3(-0.27f, -0.23f, 0.7f), new Vector3(0f, 30f, -20f));
            CasterArm(bowHand, leather, teamColor, true);
            // A longbow upright in view: tapered limbs curving away from you (+Z), horn nocks at the tips.
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

            // The draw forearm angles down and out to the right, out of the line of sight.
            var drawEuler = new Vector3(-28f, -38f, 0f);
            var drawHand = MeshKit.Pivot(root, "DrawHand", new Vector3(-0.235f, -0.235f, 0.57f), drawEuler);
            CasterArm(drawHand, leather, teamColor, true);
            // The arrow rides on the draw hand, so it comes back with the string; counter-rotated to point straight ahead.
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
            // The string: two thin segments, re-laid every frame from the tips to the nock.
            var seg = MeshKit.Box(new Vector3(0.004f, 0.004f, 1f));
            var upper = MeshKit.Part(root, "StringUpper", seg, Mat.Bone, Vector3.zero).transform;
            var lower = MeshKit.Part(root, "StringLower", seg, Mat.Bone, Vector3.zero).transform;
            var bs = root.gameObject.AddComponent<BowString>();
            bs.TipTop = tipTop; bs.TipBottom = tipBottom; bs.Nock = nock; bs.Upper = upper; bs.Lower = lower;

            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = drawHand, ShieldPivot = bowHand };
        }

        /// <summary>First-person Rogue: a reversed-grip dagger in each hand.</summary>
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
                return hand;
            }
            var right = Hand("RightHand", new Vector3(0.3f, -0.36f, 0.5f), new Vector3(-25f, -10f, -15f));
            var left = Hand("LeftHand", new Vector3(-0.3f, -0.36f, 0.5f), new Vector3(-25f, 10f, 15f));
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = right, ShieldPivot = left };
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

        /// <summary>First-person Mage: the staff in the right hand, a spell-lit left palm.</summary>
        public static ViewModel BuildMageViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var robe = Color.Lerp(teamColor, new Color(0.13f, 0.1f, 0.18f), 0.78f);
            robe.a = 1f;

            var staffHand = MeshKit.Pivot(root, "StaffHand", new Vector3(0.36f, -0.4f, 0.62f), new Vector3(16f, -8f, -12f));
            CasterArm(staffHand, robe, teamColor, true);
            // Short in view: only the top of the staff is on screen, leaning away. Gnarled shaft with
            // bound grips, then a carved head: three gold claws caging a cold crystal.
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

        /// <summary>A robed caster's forearm and bare-knuckled glove; <paramref name="grip"/> curls the fingers round a staff, else an open palm.</summary>
        private static void CasterArm(Transform hand, Color robe, Color trim, bool grip)
        {
            var sleeve = MeshKit.Part(hand, "Sleeve", MeshKit.Cylinder(0.075f, 0.11f, 0.42f, 9), Mat.Cloth, new Vector3(0f, -0.06f, -0.24f), new Vector3(-100f, 0f, 0f));
            ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), robe);
            // A flared cuff with a trim band in the party colour.
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

        /// <summary>A living flame cupped over an open palm: a teardrop core in a softer halo, lit by a small light.</summary>
        private static Renderer PalmFlame(Transform hand, Color color, out Light light)
        {
            var at = MeshKit.Pivot(hand, "Flame", new Vector3(0.005f, 0.05f, 0.05f));
            at.localScale = Vector3.one * 0.65f;
            var core = MeshKit.Part(at, "Core", MeshKit.Lathe("vmFlame", new[] { new Vector2(0f, -0.02f), new Vector2(0.026f, 0.0f), new Vector2(0.022f, 0.035f), new Vector2(0.008f, 0.075f), new Vector2(0f, 0.09f) }, 8), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), color * 1.2f);
            // Two small licks of flame curling off the core.
            foreach (float side in new[] { -1f, 1f })
            {
                var lick = MeshKit.Part(at, "Lick", MeshKit.Lathe("vmFlame", new[] { new Vector2(0f, -0.02f), new Vector2(0.026f, 0.0f), new Vector2(0.022f, 0.035f), new Vector2(0.008f, 0.075f), new Vector2(0f, 0.09f) }, 8), Mat.Glow,
                    new Vector3(side * 0.018f, -0.005f, 0.004f), new Vector3(0f, 0f, -side * 24f), Vector3.one * 0.55f);
                ArtLibrary.SetGlow(lick.GetComponent<Renderer>(), color * 0.9f);
            }
            // No light here: a point light centimetres from the glove floods the hand white.
            light = null;
            return core.GetComponent<Renderer>();
        }

        // ------------------------------------------------------------------
        // Skeletons
        // ------------------------------------------------------------------

        /// <summary>APPEND ONLY: serialised as an int on enemy prefabs.</summary>
        public enum SkeletonKind { Warrior, King, Archer, Gravewarden, Lich, Ghoul, Brute, Necromancer, Wraith, Cardinal, Shieldbearer, Mimic, Hoarder, Gargoyle, Plaguebearer, FrostWight, Hexer, Mourner, Unsung }

        /// <summary>
        /// The dungeon's rank-and-file enemy, and — scaled up, crowned and caped — its
        /// first boss. Glowing eyes double as the attack telegraph: they flare during
        /// wind-ups (see <see cref="HumanoidRig"/>).
        /// </summary>
        public static HumanoidRig BuildSkeleton(Transform root, SkeletonKind kind)
        {
            if (kind == SkeletonKind.Wraith) return BuildWraith(root);
            if (kind == SkeletonKind.Mimic) return BuildMimic(root);
            if (kind == SkeletonKind.Gargoyle) return BuildGargoyle(root);
            if (kind == SkeletonKind.Mourner) return BuildMourner(root);
            if (kind == SkeletonKind.Unsung) return BuildUnsung(root);
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
            // Spine: a column of knobbed vertebrae, a sternum down the front of the ribs.
            for (int v = 0; v < 8; v++)
                MeshKit.Part(rig.Torso, "Vertebra" + v, MeshKit.Lathe("vertebra", new[] { new Vector2(0.018f, 0f), new Vector2(0.03f, 0.012f), new Vector2(0.03f, 0.03f), new Vector2(0.018f, 0.042f) }, 6),
                    Mat.Bone, new Vector3(0f, 0.04f + v * 0.068f, -0.07f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
            MeshKit.Part(rig.Torso, "Sternum", MeshKit.Box(new Vector3(0.04f, 0.26f, 0.02f)), Mat.Bone, new Vector3(0f, 0.43f, 0.105f), new Vector3(-6f, 0f, 0f));
            // Ribcage: stacked thin rings, narrowing downward.
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
            // Small points of light deep in the sockets (big ones bloom into white discs).
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.012f, 6, 4), Mat.Glow, new Vector3(-0.045f, 0.128f, 0.118f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.012f, 6, 4), Mat.Glow, new Vector3(0.045f, 0.128f, 0.118f));

            rig.ArmR = BuildBoneArm(rig.Torso, "ArmR", 1f);
            rig.ArmL = BuildBoneArm(rig.Torso, "ArmL", -1f);

            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.6f, 0f), new Vector3(80f, 0f, 0f));

            if (king)
            {
                // Crown of jagged gold points with glowing gems.
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

                // A tattered royal cloak (it sways), under an ermine mantle: white fur flecked with black.
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
                // A taller crown: a jewelled band and five tines rising from it.
                MeshKit.Part(rig.Head, "CrownBand", MeshKit.Cylinder(0.135f, 0.135f, 0.035f, 12), Mat.Gold, new Vector3(0f, 0.2f, 0f));
                for (int i = 0; i < 5; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 72f + 36f, 0f) * Vector3.forward;
                    MeshKit.Part(rig.Head, "Tine" + i, MeshKit.Lathe("crownTine", new[] { new Vector2(0.028f, 0f), new Vector2(0.02f, 0.07f), new Vector2(0f, 0.16f) }, 4),
                        Mat.Gold, new Vector3(0f, 0.25f, 0f) + dir * 0.125f, new Vector3(0f, 0f, 0f));
                    MeshKit.Part(rig.Head, "TineBall" + i, MeshKit.Ball(0.016f, 6, 4), Mat.Gold, new Vector3(0f, 0.415f, 0f) + dir * 0.125f);
                }

                // Greatsword, held point-up.
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
                // A sack of stolen gold over its shoulder, a crooked crown, coins spilling.
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
                // A tower shield on the left arm (rusted iron face, wooden rim, a bone sigil),
                // a short spear in the right, and a dented kettle helm.
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
                MeshKit.Part(grip, "Shaft", MeshKit.Cylinder(0.02f, 0.02f, 1.5f, 6), Mat.Wood, new Vector3(0f, -0.5f, 0f));
                MeshKit.Part(grip, "Spearhead", MeshKit.Lathe("spearhead", new[] { new Vector2(0.04f, 0f), new Vector2(0.03f, 0.08f), new Vector2(0f, 0.24f) }, 4), Mat.Steel, new Vector3(0f, 1f, 0f));
                MeshKit.Part(rig.Head, "Helm", MeshKit.Lathe("kettleHelm", new[] { new Vector2(0.21f, 0.06f), new Vector2(0.15f, 0.09f), new Vector2(0.14f, 0.2f), new Vector2(0.06f, 0.27f), new Vector2(0f, 0.28f) }, 10), Mat.Steel, Vector3.zero);
                rig.OffhandItem = null;
                rig.StrideLength = 1.1f;
            }
            else if (kind == SkeletonKind.Gravewarden)
            {
                BuildGravewarden(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());
                // A crack in its back plate, glowing: the shield guards the front, not this.
                WeakPoint.Add(rig.Torso, new Vector3(0f, 0.45f, -0.22f), 0.1f, new Color(1f, 0.55f, 0.2f));
            }
            else if (kind == SkeletonKind.Cardinal)
            {
                // Crimson vestments, a gold-rayed orb, a bare skull face under a broad two-peaked
                // mitre: flat front and back plates with a gold cross, never a pointed hood.
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
                // A boss never wears a pointed hood over its face: the Tide Lich goes bare-skulled under its coral crown.
                var lichHood = rig.Head.Find("Hood");
                if (lichHood != null) Object.DestroyImmediate(lichHood.gameObject);
            }
            else if (kind == SkeletonKind.Necromancer)
            {
                BuildLich(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>(), new Color(0.24f, 0.12f, 0.3f), new Color(0.75f, 0.35f, 1f), false);
            }
            else if (kind == SkeletonKind.Plaguebearer)
            {
                // A ghoul swollen with plague: a bloated green belly, weeping pustules.
                BuildGhoul(rig, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());
                foreach (var r in rig.GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("Bone")) ArtLibrary.Tint(r, new Color(0.45f, 0.6f, 0.32f));
                var belly = MeshKit.Part(rig.Torso, "Belly", MeshKit.Ball(0.3f, 10, 8), Mat.Cloth, new Vector3(0f, 0.22f, 0.1f), Vector3.zero, new Vector3(1.1f, 1f, 1.05f));
                ArtLibrary.Tint(belly.GetComponent<Renderer>(), new Color(0.42f, 0.5f, 0.25f));
                for (int i = 0; i < 6; i++)
                {
                    var dir = Quaternion.Euler(Random.Range(-40f, 40f), i * 60f, 0f) * Vector3.forward;
                    var pus = MeshKit.Part(rig.Torso, "Pustule" + i, MeshKit.Ball(0.045f, 6, 4), Mat.Glow, new Vector3(0f, 0.25f, 0.1f) + dir * 0.31f);
                    ArtLibrary.SetGlow(pus.GetComponent<Renderer>(), new Color(0.5f, 0.95f, 0.2f) * 1.1f);
                }
                // The plague sac on its belly: burst it.
                WeakPoint.Add(rig.Torso, new Vector3(0f, 0.2f, 0.42f), 0.11f, new Color(0.55f, 1f, 0.2f));
            }
            else if (kind == SkeletonKind.FrostWight)
            {
                // Rimed bone, an ice crown of shards, a blade of black ice.
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
            }
            else if (kind == SkeletonKind.Ghoul)
            {
                BuildGhoul(rig, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());
            }
            else if (kind == SkeletonKind.Brute)
            {
                BuildBrute(rig, grip, eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>());
                // A rune burned into its back: get behind it.
                WeakPoint.Add(rig.Torso, new Vector3(0f, 0.4f, -0.24f), 0.13f);
            }
            else if (kind == CharacterArt.SkeletonKind.Archer)
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };

                // Bow in the left hand, upright at the archer's side: limbs along Y, tips
                // curving back (-Z). HumanoidRig swings it level into firing position
                // during the draw (OffhandItem).
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
                for (int i = 0; i < 3; i++)
                {
                    MeshKit.Part(rig.Torso, "Fletch" + i, MeshKit.Box(new Vector3(0.015f, 0.12f, 0.04f)), Mat.Cloth,
                        new Vector3(0.14f + i * 0.02f, 0.76f, -0.25f + i * 0.02f), new Vector3(-15f, 0f, 20f));
                }
                var hood = MeshKit.Part(rig.Head, "Hood", MeshKit.Lathe("archerHood", new[]
                {
                    new Vector2(0.15f, -0.02f), new Vector2(0.16f, 0.1f), new Vector2(0.12f, 0.2f), new Vector2(0.03f, 0.26f), new Vector2(0f, 0.27f),
                }, 8, true, 280f), Mat.Cloth, new Vector3(0f, 0.05f, -0.01f), new Vector3(0f, 40f, 0f));
                ArtLibrary.Tint(hood.GetComponent<Renderer>(), new Color(0.22f, 0.2f, 0.16f));
                rig.StrideLength = 1.2f;
            }
            else
            {
                rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };

                // Rusted sword and a scrap of armour, so they are not all bare bones.
                MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.018f, 0.018f, 0.13f, 5), Mat.Leather, new Vector3(0f, -0.05f, 0f));
                MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.16f, 0.03f, 0.04f)), Mat.Steel, new Vector3(0f, 0.03f, 0f));
                var blade = MeshKit.Part(grip, "Blade", MeshKit.Box(new Vector3(0.055f, 0.65f, 0.012f)), Mat.Steel, new Vector3(0f, 0.37f, 0f));
                ArtLibrary.Tint(blade.GetComponent<Renderer>(), new Color(0.6f, 0.45f, 0.35f));
                var helm = MeshKit.Part(rig.Head, "BrokenHelm", MeshKit.Lathe("skelHelm", new[]
                {
                    new Vector2(0.14f, 0f), new Vector2(0.145f, 0.06f), new Vector2(0.11f, 0.12f), new Vector2(0f, 0.14f),
                }, 7, true, 290f), Mat.Steel, new Vector3(0f, 0.15f, 0f), new Vector3(0f, 35f, 0f));
                ArtLibrary.Tint(helm.GetComponent<Renderer>(), new Color(0.55f, 0.45f, 0.38f));
                rig.StrideLength = 1.2f;
            }

            return rig;
        }

        /// <summary>
        /// The Gravewarden: a skeleton in a gaoler's plate, behind a tower shield, with a
        /// flanged mace. The shield is the silhouette — it reads "you cannot hit me from
        /// the front" before the player has been told anything.
        /// </summary>
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
            // Plate legs: a giant should not stand on bare bone. Cuisse, knee cop, greave, sabaton.
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
            // Eyes sit behind the visor slit: just the glow showing through.
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

            // Tower shield on the left forearm.
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

            // Flanged mace.
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

        /// <summary>
        /// The Tide Lich: a drowned sorcerer in a waterlogged robe, crowned with coral, with
        /// a staff whose orb glows the colour of its spells. Robed so it reads as a caster
        /// — not a fighter — at any distance.
        /// </summary>
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

            // Coral crown: branching red-pink spikes.
            var glow = new System.Collections.Generic.List<Renderer> { eyeL, eyeR };
            for (int i = 0; i < (coralCrown ? 5 : 0); i++)
            {
                float a = -60f + i * 30f;
                var c = MeshKit.Part(rig.Head, "Coral" + i, MeshKit.Lathe("coral", new[] { new Vector2(0.018f, 0f), new Vector2(0f, 0.12f) }, 5), Mat.Bone,
                    new Vector3(0f, 0.27f, 0.02f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.1f), new Vector3(20f, a, 0f));
                ArtLibrary.Tint(c.GetComponent<Renderer>(), new Color(0.95f, 0.45f, 0.45f));
            }

            // The phylactery: a soul gem on the chest, where the unlife is kept.
            WeakPoint.Add(rig.Torso, new Vector3(0f, 0.42f, 0.22f), 0.08f, orbColor);

            // Staff with a glowing orb, held upright.
            MeshKit.Part(grip, "Staff", MeshKit.Lathe("lichStaff", new[] { new Vector2(0.03f, -1.35f), new Vector2(0.025f, -0.2f), new Vector2(0.028f, 0.6f), new Vector2(0.024f, 0.78f), new Vector2(0.045f, 0.82f) }, 7), Mat.Wood, Vector3.zero);
            MeshKit.Part(grip, "Knop", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, new Vector3(0f, 0.83f, 0f), Vector3.zero, new Vector3(1f, 0.6f, 1f));
            // A cut soul-crystal (not a ball of light) gripped in four curled bone fingers.
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

        /// <summary>
        /// Ghoul: not clean bone but grey-green dried flesh over it, long hooked claws, no
        /// weapon. Slightly smaller than a skeleton, so a pack reads as a swarm.
        /// </summary>
        private static void BuildGhoul(HumanoidRig rig, Renderer eyeL, Renderer eyeR)
        {
            var flesh = new Color(0.55f, 0.62f, 0.5f);
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial == ArtLibrary.Get(Mat.Bone)) ArtLibrary.Tint(r, flesh);
            }
            for (int i = 0; i < 2; i++)
            {
                var arm = i == 0 ? rig.ArmL : rig.ArmR;
                for (int c = 0; c < 3; c++)
                {
                    var claw = MeshKit.Part(arm, "Claw" + c, MeshKit.Lathe("claw", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.16f) }, 4), Mat.Bone,
                        new Vector3(-0.025f + c * 0.025f, -0.66f, 0.02f), new Vector3(160f, 0f, 0f));
                    ArtLibrary.Tint(claw.GetComponent<Renderer>(), new Color(0.3f, 0.28f, 0.22f));
                }
            }
            var rags = MeshKit.Part(rig.Hips, "Rags", MeshKit.Lathe("ghoulRags", new[]
            {
                new Vector2(0.18f, -0.35f), new Vector2(0.17f, 0f), new Vector2(0.15f, 0.06f),
            }, 7), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(rags.GetComponent<Renderer>(), new Color(0.25f, 0.22f, 0.18f));
            rig.GlowRenderers = new[] { eyeL, eyeR };
            rig.StrideLength = 1.0f;
        }

        /// <summary>Bone Brute: a hulking skeleton with spiked pauldrons and a stone maul.</summary>
        private static void BuildBrute(HumanoidRig rig, Transform grip, Renderer eyeL, Renderer eyeR)
        {
            var old = new Color(0.62f, 0.55f, 0.45f);
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial == ArtLibrary.Get(Mat.Bone)) ArtLibrary.Tint(r, old);
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
            // Maul: a thick haft and a rough stone block.
            MeshKit.Part(grip, "Haft", MeshKit.Cylinder(0.035f, 0.035f, 0.95f, 6), Mat.Wood, new Vector3(0f, -0.15f, 0f));
            MeshKit.Part(grip, "Head", MeshKit.Box(new Vector3(0.38f, 0.26f, 0.26f)), Mat.Rubble, new Vector3(0f, 0.85f, 0f), new Vector3(0f, 12f, 4f));
            MeshKit.Part(grip, "Band", MeshKit.Box(new Vector3(0.4f, 0.06f, 0.28f)), Mat.Steel, new Vector3(0f, 0.85f, 0f), new Vector3(0f, 12f, 4f));
            // Iron bands at both ends and spikes driven through: a maul, not a brick on a stick.
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

        /// <summary>A long bone hanging down from <paramref name="top"/>: a turned shaft with knobbed ends.</summary>
        private static GameObject LongBone(Transform parent, string name, Vector3 top, float length, float thick)
        {
            var mesh = MeshKit.Lathe("longBone", new[]
            {
                new Vector2(0.38f, 0f), new Vector2(0.5f, 0.03f), new Vector2(0.42f, 0.09f), new Vector2(0.24f, 0.2f),
                new Vector2(0.2f, 0.5f), new Vector2(0.24f, 0.8f), new Vector2(0.44f, 0.92f), new Vector2(0.5f, 0.97f), new Vector2(0.3f, 1f),
            }, 6);
            return MeshKit.Part(parent, name, mesh, Mat.Bone, top - new Vector3(0f, length, 0f), Vector3.zero, new Vector3(thick, length, thick));
        }

        /// <summary>A skull worth looking at: domed cranium, brow ridge, cheekbones, a nasal hole, a row of teeth and a hinged jaw that chatters.</summary>
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
            // The jaw hangs from a hinge so it can work.
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
            // Shoulder blade behind the joint.
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

        /// <summary>
        /// The Wraith: no legs, just a tattered translucent shroud trailing into nothing, a
        /// pale skull under a deep hood with cold blue eyes, and long bony claws.
        /// </summary>
        private static HumanoidRig BuildWraith(Transform root)
        {
            var visual = MeshKit.Pivot(root, "WraithVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(0.45f, 0.75f, 1f);
            var shroud = new Color(0.16f, 0.2f, 0.3f, 0.55f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            // Legs exist for the animator but carry nothing: the shroud is the lower body.
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
                for (int f = 0; f < 3; f++)
                    MeshKit.Part(arm, "Claw" + f, MeshKit.Box(new Vector3(0.015f, 0.16f, 0.015f)), Mat.Bone, new Vector3((f - 1) * 0.03f, -0.7f, 0.02f), new Vector3(15f, 0f, (f - 1) * 8f));
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

        /// <summary>
        /// A Gargoyle: hunched grey stone, a horned snouted head, long clawed arms and great
        /// bat wings folded at its back. Its eyes are coals that only light when it wakes.
        /// </summary>
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
                // Digitigrade: thigh forward, shin back, a clawed foot.
                Stone(MeshKit.Part(leg, "Thigh", MeshKit.Box(new Vector3(0.16f, 0.38f, 0.18f)), Mat.Bone, new Vector3(0f, -0.15f, 0.08f), new Vector3(-30f, 0f, 0f)));
                Stone(MeshKit.Part(leg, "Shin", MeshKit.Box(new Vector3(0.12f, 0.38f, 0.12f)), Mat.Bone, new Vector3(0f, -0.48f, 0.02f), new Vector3(25f, 0f, 0f)));
                Stone(MeshKit.Part(leg, "Foot", MeshKit.Box(new Vector3(0.16f, 0.06f, 0.26f)), Mat.Bone, new Vector3(0f, -0.72f, 0.1f)));
            }
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", new Vector3(0f, 0.1f, 0f));
            Stone(MeshKit.Part(rig.Torso, "Chest", MeshKit.Box(new Vector3(0.55f, 0.5f, 0.38f)), Mat.Bone, new Vector3(0f, 0.3f, 0.06f), new Vector3(20f, 0f, 0f)));
            Stone(MeshKit.Part(rig.Torso, "Hump", MeshKit.Ball(0.25f, 8, 6), Mat.Bone, new Vector3(0f, 0.5f, -0.1f)));
            // Wings: two pivots on the back, each a fan of thin stone vanes.
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

        /// <summary>
        /// The Unsung: a colossal floating ribcage on a trailing spine, under a cracked skull with
        /// three burning violet eye sockets and a hanging jaw; long arms of bone end in claws; two
        /// rings of bone orbit it. A dark heart glows inside the ribs (the weak point). No hood, no robe.
        /// </summary>
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
            // The trailing spine: vertebrae tapering down into nothing.
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
            // The dark heart in the cage.
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
            // Two rings of bone, orbiting (turned by the brain).
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
            glow.range = 8f;
            glow.intensity = 2f;
            glow.shadows = LightShadows.None;
            rig.GlowRenderers = eyes.ToArray();
            rig.StrideLength = 2f;
            return rig;
        }

        /// <summary>
        /// The Mourner: a tall weeping figure of pale stone. A bare stone face (no hood) under long
        /// carved hair, a wreath, a long robe to the floor, folded feathered wings. Its weak point
        /// is a glowing crack over the heart, where the stone wept through.
        /// </summary>
        private static HumanoidRig BuildMourner(Transform root)
        {
            var stone = new Color(0.92f, 0.94f, 1f);
            var visual = MeshKit.Pivot(root, "MournerVisual", Vector3.zero);
            visual.localScale = Vector3.one * 1.12f;
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(0.55f, 0.75f, 1f);
            Renderer Stone(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, stone); return r; }

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            // The robe: one long carved bell to the floor (the legs are hidden inside it).
            Stone(MeshKit.Part(rig.Hips, "Robe", MeshKit.Lathe("mournerRobe", new[] { new Vector2(0.44f, -0.95f), new Vector2(0.36f, -0.6f), new Vector2(0.24f, -0.1f), new Vector2(0.2f, 0.12f) }, 10), Mat.Marble, Vector3.zero));
            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.1f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.1f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
                Stone(MeshKit.Part(leg, "Foot", MeshKit.Box(new Vector3(0.1f, 0.06f, 0.2f)), Mat.Marble, new Vector3(0f, -0.92f, 0.12f)));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", new Vector3(0f, 0.12f, 0f));
            Stone(MeshKit.Part(rig.Torso, "Chest", MeshKit.Lathe("mournerChest", new[] { new Vector2(0.2f, 0f), new Vector2(0.23f, 0.25f), new Vector2(0.2f, 0.5f), new Vector2(0.08f, 0.6f) }, 10), Mat.Marble, Vector3.zero, default, new Vector3(1f, 1f, 0.75f)));
            // Folded wings: tall feathered vanes along the back.
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
            // Long hair down the back, and a stone wreath.
            for (int i = -2; i <= 2; i++)
                Stone(MeshKit.Part(rig.Head, "Hair" + i, MeshKit.Box(new Vector3(0.05f, 0.42f, 0.04f)), Mat.Marble, new Vector3(i * 0.045f, -0.06f, -0.1f), new Vector3(10f, 0f, i * 3f)));
            Stone(MeshKit.Part(rig.Head, "Wreath", MeshKit.Cylinder(0.13f, 0.13f, 0.035f, 12), Mat.Marble, new Vector3(0f, 0.2f, 0f), new Vector3(-12f, 0f, 0f)));
            // Tear streaks: faintly glowing lines down the cheeks, the only light on it.
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

        /// <summary>
        /// The Mimic: the chest it pretends to be, on four bony legs, its lid a jaw — rows of
        /// teeth, a long tongue, and two eyes in the dark inside.
        /// </summary>
        private static HumanoidRig BuildMimic(Transform root)
        {
            var visual = MeshKit.Pivot(root, "MimicVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = new Color(1f, 0.55f, 0.2f);
            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.55f, 0f));
            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            // Legs: two pivots, each carrying a front and a back leg (they walk in pairs).
            rig.LegL = MeshKit.Pivot(rig.Hips, "LegL", new Vector3(-0.4f, 0f, 0f));
            rig.LegR = MeshKit.Pivot(rig.Hips, "LegR", new Vector3(0.4f, 0f, 0f));
            foreach (var leg in new[] { rig.LegL, rig.LegR })
                foreach (float z in new[] { -0.25f, 0.25f })
                {
                    MeshKit.Part(leg, "Thigh", MeshKit.Box(new Vector3(0.06f, 0.35f, 0.06f)), Mat.Bone, new Vector3(0f, -0.15f, z), new Vector3(z > 0 ? -25f : 25f, 0f, 0f));
                    MeshKit.Part(leg, "Claw", MeshKit.Box(new Vector3(0.05f, 0.3f, 0.05f)), Mat.Bone, new Vector3(0f, -0.42f, z * 1.5f), new Vector3(z > 0 ? 20f : -20f, 0f, 0f));
                }
            // The body: the chest itself.
            MeshKit.Part(rig.Torso, "Body", MeshKit.Box(new Vector3(1.1f, 0.6f, 0.7f)), Mat.Wood, new Vector3(0f, 0.3f, 0f));
            foreach (float x in new[] { -0.38f, 0.38f })
                MeshKit.Part(rig.Torso, "Band", MeshKit.Box(new Vector3(0.08f, 0.62f, 0.72f)), Mat.Gold, new Vector3(x, 0.31f, 0f));
            var maw = MeshKit.Part(rig.Torso, "Maw", MeshKit.Box(new Vector3(1f, 0.05f, 0.6f)), Mat.Dark, new Vector3(0f, 0.59f, 0f));
            for (int i = 0; i < 7; i++)
                MeshKit.Part(rig.Torso, "LowerTooth" + i, MeshKit.Lathe("tooth", new[] { new Vector2(0.035f, 0f), new Vector2(0f, 0.11f) }, 4), Mat.Bone, new Vector3(-0.45f + i * 0.15f, 0.6f, 0.32f));
            WeakPoint.Add(rig.Torso, new Vector3(0f, 0.62f, 0.05f), 0.13f, new Color(1f, 0.3f, 0.35f));
            var tongue = MeshKit.Part(rig.Torso, "Tongue", MeshKit.Box(new Vector3(0.18f, 0.05f, 0.6f)), Mat.Leather, new Vector3(0f, 0.63f, 0.25f), new Vector3(-12f, 0f, 0f));
            ArtLibrary.Tint(tongue.GetComponent<Renderer>(), new Color(0.6f, 0.15f, 0.18f));
            // The lid is the head: it hinges at the back and is the jaw.
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
