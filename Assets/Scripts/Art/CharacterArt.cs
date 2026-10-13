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
            MeshKit.Part(rig.Head, "Helm", MeshKit.Lathe("greatHelm", new[]
            {
                new Vector2(0.15f, 0.00f), new Vector2(0.165f, 0.06f), new Vector2(0.17f, 0.24f),
                new Vector2(0.15f, 0.30f), new Vector2(0.0f, 0.31f),
            }, 8), Mat.Steel, Vector3.zero);
            MeshKit.Part(rig.Head, "VisorSlit", MeshKit.Box(new Vector3(0.24f, 0.025f, 0.04f)), Mat.Dark, new Vector3(0f, 0.17f, 0.15f));
            MeshKit.Part(rig.Head, "Breaths", MeshKit.Box(new Vector3(0.02f, 0.08f, 0.04f)), Mat.Dark, new Vector3(0.05f, 0.09f, 0.155f));
            MeshKit.Part(rig.Head, "Breaths2", MeshKit.Box(new Vector3(0.02f, 0.08f, 0.04f)), Mat.Dark, new Vector3(-0.05f, 0.09f, 0.155f));
            MeshKit.Part(rig.Head, "Crest", MeshKit.Box(new Vector3(0.025f, 0.05f, 0.3f)), Mat.Gold, new Vector3(0f, 0.32f, 0f));
            var plume = MeshKit.Part(rig.Head, "Plume", MeshKit.Box(new Vector3(0.05f, 0.12f, 0.28f)), Mat.Cloth, new Vector3(0f, 0.39f, -0.04f), new Vector3(-12f, 0f, 0f));
            ArtLibrary.Tint(plume.GetComponent<Renderer>(), teamColor);

            rig.ArmR = BuildKnightArm(rig.Torso, "ArmR", 1f);
            rig.ArmL = BuildKnightArm(rig.Torso, "ArmL", -1f);

            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.62f, 0.02f), new Vector3(80f, 0f, 0f));
            BuildSword(grip, 1f);

            var shieldMount = MeshKit.Pivot(rig.ArmL, "ShieldMount", new Vector3(-0.08f, -0.42f, 0.04f), new Vector3(0f, -90f, 0f));
            BuildShield(shieldMount, teamColor, 1f);

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
            MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.02f * scale, 0.02f * scale, 0.16f * scale, 6), Mat.Leather, new Vector3(0f, -0.06f * scale, 0f));
            MeshKit.Part(grip, "Pommel", MeshKit.Ball(0.035f * scale, 6, 4), Mat.Gold, new Vector3(0f, -0.08f * scale, 0f));
            MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.24f, 0.035f, 0.045f) * scale), Mat.Gold, new Vector3(0f, 0.11f * scale, 0f));
            MeshKit.Part(grip, "Blade", MeshKit.Box(new Vector3(0.06f, 0.78f, 0.014f) * scale), Mat.Steel, new Vector3(0f, 0.5f * scale, 0f));
            MeshKit.Part(grip, "Tip", MeshKit.Lathe("swordTip", new[] { new Vector2(0.042f, 0f), new Vector2(0f, 0.09f) }, 4), Mat.Steel,
                new Vector3(0f, 0.89f * scale, 0f), new Vector3(0f, 45f, 0f), new Vector3(scale, scale, scale * 0.25f));
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
            public Transform SwordPivot;
            public Transform ShieldPivot;
        }

        public static ViewModel BuildKnightViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);

            var swordPivot = MeshKit.Pivot(root, "SwordHand", new Vector3(0.38f, -0.4f, 0.55f), new Vector3(-28f, -12f, -18f));
            MeshKit.Part(swordPivot, "Gauntlet", MeshKit.Ball(0.07f, 6, 4), Mat.Steel, Vector3.zero, Vector3.zero, new Vector3(1f, 0.9f, 1.3f));
            MeshKit.Part(swordPivot, "Bracer", MeshKit.Cylinder(0.07f, 0.08f, 0.35f, 6), Mat.Steel, new Vector3(0f, -0.03f, -0.05f), new Vector3(-100f, 0f, 0f));
            var grip = MeshKit.Pivot(swordPivot, "Grip", new Vector3(0f, 0.02f, 0.02f), new Vector3(-10f, 0f, 0f));
            BuildSword(grip, 0.75f);

            var shieldPivot = MeshKit.Pivot(root, "ShieldHand", new Vector3(-0.55f, -0.5f, 0.7f), new Vector3(8f, 28f, 6f));
            var mount = MeshKit.Pivot(shieldPivot, "Mount", Vector3.zero, new Vector3(0f, 180f, 0f));
            BuildShield(mount, teamColor, 0.62f);

            // View-model geometry must not cast shadows: they would be placed at the
            // camera, not at the body, and look detached.
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return new ViewModel { SwordPivot = swordPivot, ShieldPivot = shieldPivot };
        }

        // ------------------------------------------------------------------
        // Skeletons
        // ------------------------------------------------------------------

        /// <summary>APPEND ONLY: serialised as an int on enemy prefabs.</summary>
        public enum SkeletonKind { Warrior, King, Archer }

        /// <summary>
        /// The dungeon's rank-and-file enemy, and — scaled up, crowned and caped — its
        /// first boss. Glowing eyes double as the attack telegraph: they flare during
        /// wind-ups (see <see cref="HumanoidRig"/>).
        /// </summary>
        public static HumanoidRig BuildSkeleton(Transform root, SkeletonKind kind)
        {
            bool king = kind == SkeletonKind.King;
            float s = king ? 1.75f : 1f;

            var visual = MeshKit.Pivot(root, "SkeletonVisual", Vector3.zero);
            visual.localScale = Vector3.one * s;
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            rig.GlowColor = king ? new Color(0.4f, 0.75f, 1f) : new Color(1f, 0.25f, 0.1f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.92f, 0f));
            MeshKit.Part(rig.Hips, "Pelvis", MeshKit.Lathe("pelvis", new[]
            {
                new Vector2(0.08f, -0.08f), new Vector2(0.17f, -0.02f), new Vector2(0.15f, 0.05f), new Vector2(0.06f, 0.06f),
            }, 6), Mat.Bone, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.6f));
            BuildBoneLegs(rig);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            MeshKit.Part(rig.Torso, "Spine", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.05f)), Mat.Bone, new Vector3(0f, 0.27f, -0.06f));
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
            MeshKit.Part(rig.Head, "Skull", MeshKit.Ball(0.13f, 8, 6), Mat.Bone, new Vector3(0f, 0.13f, 0f), Vector3.zero, new Vector3(0.9f, 1f, 1.05f));
            MeshKit.Part(rig.Head, "Jaw", MeshKit.Box(new Vector3(0.15f, 0.05f, 0.12f)), Mat.Bone, new Vector3(0f, 0.02f, 0.05f), new Vector3(12f, 0f, 0f));
            MeshKit.Part(rig.Head, "SocketL", MeshKit.Ball(0.035f, 6, 4), Mat.Dark, new Vector3(-0.045f, 0.13f, 0.1f));
            MeshKit.Part(rig.Head, "SocketR", MeshKit.Ball(0.035f, 6, 4), Mat.Dark, new Vector3(0.045f, 0.13f, 0.1f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.018f, 6, 4), Mat.Glow, new Vector3(-0.045f, 0.13f, 0.12f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.018f, 6, 4), Mat.Glow, new Vector3(0.045f, 0.13f, 0.12f));

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

                // Tattered royal cape.
                var cape = MeshKit.Part(rig.Torso, "Cape", MeshKit.Box(new Vector3(0.5f, 0.95f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.15f, -0.14f), new Vector3(8f, 0f, 0f));
                ArtLibrary.Tint(cape.GetComponent<Renderer>(), new Color(0.28f, 0.08f, 0.32f));
                MeshKit.Part(rig.Torso, "Mantle", MeshKit.Box(new Vector3(0.55f, 0.08f, 0.22f)), Mat.Gold, new Vector3(0f, 0.6f, -0.03f));

                // Greatsword, held point-up.
                MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.025f, 0.025f, 0.3f, 6), Mat.Leather, new Vector3(0f, -0.12f, 0f));
                MeshKit.Part(grip, "Guard", MeshKit.Box(new Vector3(0.36f, 0.05f, 0.06f)), Mat.Gold, new Vector3(0f, 0.04f, 0f));
                MeshKit.Part(grip, "Blade", MeshKit.Box(new Vector3(0.1f, 1.15f, 0.02f)), Mat.Steel, new Vector3(0f, 0.64f, 0f));
                var rune = MeshKit.Part(grip, "Rune", MeshKit.Box(new Vector3(0.025f, 0.9f, 0.024f)), Mat.Glow, new Vector3(0f, 0.6f, 0f));
                glow.Add(rune.GetComponent<Renderer>());
                rig.GlowRenderers = glow.ToArray();
                rig.StrideLength = 1.4f;
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
                }, 8, true, 280f), Mat.Cloth, new Vector3(0f, 0.05f, -0.01f), new Vector3(0f, 220f, 0f));
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
                }, 7, true, 290f), Mat.Steel, new Vector3(0f, 0.15f, 0f), new Vector3(0f, 215f, 0f));
                ArtLibrary.Tint(helm.GetComponent<Renderer>(), new Color(0.55f, 0.45f, 0.38f));
                rig.StrideLength = 1.2f;
            }

            return rig;
        }

        private static Transform BuildBoneArm(Transform torso, string name, float side)
        {
            var shoulder = MeshKit.Pivot(torso, name, new Vector3(0.21f * side, 0.58f, 0f));
            MeshKit.Part(shoulder, "Joint", MeshKit.Ball(0.04f, 6, 4), Mat.Bone, Vector3.zero);
            MeshKit.Part(shoulder, "Humerus", MeshKit.Box(new Vector3(0.04f, 0.3f, 0.04f)), Mat.Bone, new Vector3(0f, -0.16f, 0f));
            MeshKit.Part(shoulder, "Elbow", MeshKit.Ball(0.03f, 6, 4), Mat.Bone, new Vector3(0f, -0.32f, 0f));
            MeshKit.Part(shoulder, "Radius", MeshKit.Box(new Vector3(0.03f, 0.26f, 0.03f)), Mat.Bone, new Vector3(0.012f, -0.46f, 0f));
            MeshKit.Part(shoulder, "Ulna", MeshKit.Box(new Vector3(0.025f, 0.26f, 0.025f)), Mat.Bone, new Vector3(-0.012f, -0.46f, 0f));
            MeshKit.Part(shoulder, "Hand", MeshKit.Box(new Vector3(0.06f, 0.08f, 0.03f)), Mat.Bone, new Vector3(0f, -0.62f, 0f));
            return shoulder;
        }

        private static void BuildBoneLegs(HumanoidRig rig)
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var hip = MeshKit.Pivot(rig.Hips, i == 0 ? "LegL" : "LegR", new Vector3(0.1f * side, -0.04f, 0f));
                MeshKit.Part(hip, "Femur", MeshKit.Box(new Vector3(0.045f, 0.42f, 0.045f)), Mat.Bone, new Vector3(0f, -0.22f, 0f));
                MeshKit.Part(hip, "Knee", MeshKit.Ball(0.035f, 6, 4), Mat.Bone, new Vector3(0f, -0.44f, 0.01f));
                MeshKit.Part(hip, "Tibia", MeshKit.Box(new Vector3(0.035f, 0.4f, 0.035f)), Mat.Bone, new Vector3(0f, -0.65f, 0f));
                MeshKit.Part(hip, "Foot", MeshKit.Box(new Vector3(0.07f, 0.04f, 0.16f)), Mat.Bone, new Vector3(0f, -0.86f, 0.04f));
                if (i == 0) rig.LegL = hip; else rig.LegR = hip;
            }
        }

        private static void BuildLegs(HumanoidRig rig, float spacing, float hipHeight, string armour, string under, float width)
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var hip = MeshKit.Pivot(rig.Hips, i == 0 ? "LegL" : "LegR", new Vector3(spacing * side, 0f, 0f));
                MeshKit.Part(hip, "Thigh", MeshKit.Box(new Vector3(width, 0.45f, width + 0.02f)), under, new Vector3(0f, -0.22f, 0f));
                MeshKit.Part(hip, "Knee", MeshKit.Ball(0.08f, 6, 4), armour, new Vector3(0f, -0.46f, 0.05f));
                MeshKit.Part(hip, "Greave", MeshKit.Cylinder(0.085f, 0.095f, 0.38f, 6), armour, new Vector3(0f, -0.86f, 0f));
                MeshKit.Part(hip, "Boot", MeshKit.Box(new Vector3(0.15f, 0.1f, 0.27f)), under, new Vector3(0f, -0.9f, 0.04f));
                if (i == 0) rig.LegL = hip; else rig.LegR = hip;
            }
        }
    }
}
