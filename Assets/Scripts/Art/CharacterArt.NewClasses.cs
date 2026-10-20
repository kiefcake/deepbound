using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>The Flagellant and the Plague Doctor (2026-10-20): bodies and first-person hands.</summary>
    public static partial class CharacterArt
    {
        private static readonly Color PaleSkin = new Color(0.78f, 0.66f, 0.58f);
        private static readonly Color PlagueGreen = new Color(0.55f, 1f, 0.35f);

        // ------------------------------------------------------------------
        // Flagellant: a penitent zealot, scarred and bare to the waist
        // ------------------------------------------------------------------

        /// <summary>
        /// The Flagellant: bare scarred torso crossed by chains and a rope, a sackcloth skirt in the
        /// party colour, an iron crown of thorns on a shaved head, a spiked flail in the right hand
        /// and a knotted scourge in the left. Broad and hunched: a brawler, not a knight.
        /// </summary>
        public static HumanoidRig BuildFlagellant(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "FlagellantVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var sack = Color.Lerp(teamColor, new Color(0.35f, 0.28f, 0.2f), 0.6f);
            sack.a = 1f;

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.94f, 0f));
            BuildLegs(rig, 0.12f, 0.94f, Mat.Leather, Mat.Cloth, 0.18f);
            var skirt = MeshKit.Part(rig.Hips, "Sackcloth", MeshKit.Lathe("flagSkirt", new[]
            {
                new Vector2(0.3f, -0.6f), new Vector2(0.27f, -0.3f), new Vector2(0.22f, 0f), new Vector2(0.21f, 0.06f),
            }, 9), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(skirt.GetComponent<Renderer>(), sack);
            for (int i = 0; i < 7; i++)
            {
                // Torn strips hanging from the hem.
                float a = (i * 51f + 10f) * Mathf.Deg2Rad;
                var strip = MeshKit.Part(rig.Hips, "Tatter" + i, MeshKit.Box(new Vector3(0.08f, 0.16f, 0.015f)), Mat.Cloth, new Vector3(Mathf.Sin(a) * 0.29f, -0.66f, Mathf.Cos(a) * 0.29f), new Vector3(0f, i * 51f + 10f, 0f));
                ArtLibrary.Tint(strip.GetComponent<Renderer>(), sack * 0.8f);
            }
            MeshKit.Part(rig.Hips, "Rope", MeshKit.Cylinder(0.225f, 0.225f, 0.05f, 10), Mat.Wood, new Vector3(0f, 0.03f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.82f));
            MeshKit.Part(rig.Hips, "RopeKnot", MeshKit.Ball(0.04f, 6, 4), Mat.Wood, new Vector3(0.1f, 0f, 0.18f));
            MeshKit.Part(rig.Hips, "RopeEnd", MeshKit.Box(new Vector3(0.025f, 0.3f, 0.025f)), Mat.Wood, new Vector3(0.11f, -0.16f, 0.19f));

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            // A broad bare chest, leaning forward; welts across the back, chains crossing the front.
            var chest = MeshKit.Part(rig.Torso, "Chest", MeshKit.Lathe("flagChest", new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.23f, 0.18f), new Vector2(0.27f, 0.4f), new Vector2(0.25f, 0.54f), new Vector2(0.1f, 0.62f),
            }, 9), Mat.Marble, Vector3.zero, new Vector3(8f, 0f, 0f), new Vector3(1f, 1f, 0.8f));
            ArtLibrary.Tint(chest.GetComponent<Renderer>(), PaleSkin);
            for (int i = 0; i < 6; i++)
            {
                var welt = MeshKit.Part(rig.Torso, "Welt" + i, MeshKit.Box(new Vector3(0.34f, 0.018f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.16f + i * 0.065f, -0.19f), new Vector3(0f, 0f, (i % 2 == 0 ? 1f : -1f) * 22f));
                ArtLibrary.Tint(welt.GetComponent<Renderer>(), new Color(0.55f, 0.08f, 0.06f));
            }
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 7; k++)
                    MeshKit.Part(rig.Torso, "Chain", MeshKit.Box(new Vector3(0.035f, 0.05f, 0.02f)), Mat.Steel, new Vector3(s * (-0.17f + k * 0.055f), 0.12f + k * 0.065f, 0.2f), new Vector3(0f, 0f, s * 38f + (k % 2) * 90f));
            MeshKit.Part(rig.Torso, "Collar", MeshKit.Cylinder(0.11f, 0.11f, 0.06f, 10), Mat.Steel, new Vector3(0f, 0.6f, 0.01f));
            var tabard = MeshKit.Part(rig.Torso, "Penance", MeshKit.Box(new Vector3(0.14f, 0.24f, 0.012f)), Mat.Cloth, new Vector3(0f, 0.28f, 0.215f));
            ArtLibrary.Tint(tabard.GetComponent<Renderer>(), teamColor);
            MeshKit.Part(rig.Torso, "PenanceMark", MeshKit.Box(new Vector3(0.025f, 0.14f, 0.015f)), Mat.Gold, new Vector3(0f, 0.29f, 0.222f));
            MeshKit.Part(rig.Torso, "PenanceMarkH", MeshKit.Box(new Vector3(0.09f, 0.025f, 0.015f)), Mat.Gold, new Vector3(0f, 0.33f, 0.222f));

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.62f, 0.03f));
            var head = MeshKit.Part(rig.Head, "Scalp", MeshKit.Ball(0.125f, 9, 7), Mat.Marble, new Vector3(0f, 0.14f, 0.01f));
            ArtLibrary.Tint(head.GetComponent<Renderer>(), PaleSkin * 0.95f);
            MeshKit.Part(rig.Head, "Brow", MeshKit.Box(new Vector3(0.2f, 0.035f, 0.05f)), Mat.Marble, new Vector3(0f, 0.17f, 0.1f));
            MeshKit.Part(rig.Head, "Shadow", MeshKit.Box(new Vector3(0.17f, 0.05f, 0.03f)), Mat.Dark, new Vector3(0f, 0.135f, 0.11f));
            var eyeL = MeshKit.Part(rig.Head, "EyeL", MeshKit.Ball(0.014f, 6, 4), Mat.Glow, new Vector3(-0.04f, 0.14f, 0.12f));
            var eyeR = MeshKit.Part(rig.Head, "EyeR", MeshKit.Ball(0.014f, 6, 4), Mat.Glow, new Vector3(0.04f, 0.14f, 0.12f));
            var beard = MeshKit.Part(rig.Head, "Beard", MeshKit.Lathe("flagBeard", new[] { new Vector2(0.09f, 0f), new Vector2(0.07f, -0.08f), new Vector2(0f, -0.14f) }, 7), Mat.Cloth, new Vector3(0f, 0.07f, 0.05f), Vector3.zero, new Vector3(1f, 1f, 0.7f));
            ArtLibrary.Tint(beard.GetComponent<Renderer>(), new Color(0.2f, 0.16f, 0.13f));
            // The crown of thorns: an iron band with spikes.
            MeshKit.Part(rig.Head, "Crown", MeshKit.Cylinder(0.13f, 0.13f, 0.035f, 12), Mat.Steel, new Vector3(0f, 0.2f, 0f));
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad;
                MeshKit.Part(rig.Head, "Thorn", MeshKit.Lathe("thorn", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.07f) }, 4), Mat.Steel, new Vector3(Mathf.Sin(a) * 0.13f, 0.21f, Mathf.Cos(a) * 0.13f), new Vector3(Mathf.Cos(a) * 25f, 0f, -Mathf.Sin(a) * 25f));
            }

            rig.ArmR = BuildBareArm(rig.Torso, "ArmR", 1f);
            rig.ArmL = BuildBareArm(rig.Torso, "ArmL", -1f);
            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.6f, 0.02f), new Vector3(80f, 0f, 0f));
            BuildFlail(grip, 1f, out _, out _);
            var scourge = MeshKit.Pivot(rig.ArmL, "Scourge", new Vector3(0f, -0.62f, 0.02f), new Vector3(20f, 0f, 0f));
            BuildScourge(scourge, 1f);

            rig.GlowColor = new Color(1f, 0.35f, 0.2f);
            rig.GlowRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
            rig.StrideLength = 1.45f;
            return rig;
        }

        /// <summary>A bare, scarred arm with a leather wrap at the wrist.</summary>
        private static Transform BuildBareArm(Transform torso, string name, float side)
        {
            var shoulder = MeshKit.Pivot(torso, name, new Vector3(0.29f * side, 0.5f, 0f));
            var upper = MeshKit.Part(shoulder, "Upper", MeshKit.Cylinder(0.08f, 0.095f, 0.34f, 8), Mat.Marble, new Vector3(0f, -0.18f, 0f));
            ArtLibrary.Tint(upper.GetComponent<Renderer>(), PaleSkin);
            var fore = MeshKit.Part(shoulder, "Fore", MeshKit.Cylinder(0.065f, 0.08f, 0.26f, 8), Mat.Marble, new Vector3(0f, -0.46f, 0f));
            ArtLibrary.Tint(fore.GetComponent<Renderer>(), PaleSkin * 0.95f);
            MeshKit.Part(shoulder, "Wrap", MeshKit.Cylinder(0.075f, 0.075f, 0.1f, 8), Mat.Leather, new Vector3(0f, -0.55f, 0f));
            MeshKit.Part(shoulder, "Spike", MeshKit.Lathe("flagSpike", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.07f) }, 4), Mat.Steel, new Vector3(0.07f * side, -0.55f, 0f), new Vector3(0f, 0f, -90f * side));
            var hand = MeshKit.Part(shoulder, "Hand", MeshKit.Ball(0.065f, 6, 4), Mat.Marble, new Vector3(0f, -0.62f, 0.01f));
            ArtLibrary.Tint(hand.GetComponent<Renderer>(), PaleSkin * 0.9f);
            return shoulder;
        }

        /// <summary>A haft, a short chain and a spiked iron ball. The ball hangs along local +Y from the head of the haft.</summary>
        public static void BuildFlail(Transform grip, float scale, out Transform ball, out Transform tip)
        {
            MeshKit.Part(grip, "Haft", MeshKit.Cylinder(0.022f * scale, 0.026f * scale, 0.46f * scale, 8), Mat.Wood, new Vector3(0f, 0.12f * scale, 0f));
            MeshKit.Part(grip, "Pommel", MeshKit.Ball(0.035f * scale, 6, 4), Mat.Steel, new Vector3(0f, -0.11f * scale, 0f));
            MeshKit.Part(grip, "Cap", MeshKit.Cylinder(0.035f * scale, 0.03f * scale, 0.05f * scale, 8), Mat.Steel, new Vector3(0f, 0.36f * scale, 0f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(grip, "Link" + i, MeshKit.Box(new Vector3(0.03f, 0.05f, 0.012f) * scale), Mat.Steel, new Vector3(0f, (0.41f + i * 0.045f) * scale, 0f), new Vector3(0f, i % 2 == 0 ? 0f : 90f, 0f));
            ball = MeshKit.Pivot(grip, "Ball", new Vector3(0f, 0.63f * scale, 0f));
            MeshKit.Part(ball, "Head", MeshKit.Ball(0.085f * scale, 10, 8), Mat.Steel, Vector3.zero);
            for (int i = 0; i < 14; i++)
            {
                var d = Quaternion.Euler(i * 57f, i * 101f, 0f) * Vector3.up;
                MeshKit.Part(ball, "Spike", MeshKit.Lathe("flailSpike", new[] { new Vector2(0.02f * scale, 0f), new Vector2(0f, 0.07f * scale) }, 4), Mat.Steel, d * 0.075f * scale, Quaternion.FromToRotation(Vector3.up, d).eulerAngles);
            }
            tip = MeshKit.Pivot(ball, "TipMark", Vector3.zero);
        }

        /// <summary>A short knotted scourge: a handle and five cords with lead tips.</summary>
        private static void BuildScourge(Transform grip, float scale)
        {
            MeshKit.Part(grip, "Handle", MeshKit.Cylinder(0.018f * scale, 0.02f * scale, 0.16f * scale, 6), Mat.Leather, Vector3.zero);
            for (int i = 0; i < 5; i++)
            {
                float a = (i - 2) * 9f;
                var cord = MeshKit.Pivot(grip, "Cord" + i, new Vector3(0f, -0.08f * scale, 0f), new Vector3(0f, i * 72f, a));
                MeshKit.Part(cord, "Lash", MeshKit.Box(new Vector3(0.008f, 0.32f, 0.008f) * scale), Mat.Leather, new Vector3(0f, -0.16f * scale, 0f));
                MeshKit.Part(cord, "Lead", MeshKit.Ball(0.014f * scale, 5, 3), Mat.Steel, new Vector3(0f, -0.32f * scale, 0f));
            }
        }

        /// <summary>First-person Flagellant: the flail in the right fist, the scourge in the left, bare scarred forearms.</summary>
        public static ViewModel BuildFlagellantViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            Transform BareHand(string name, Vector3 pos, Vector3 euler)
            {
                var hand = MeshKit.Pivot(root, name, pos, euler);
                var fist = MeshKit.Part(hand, "Fist", MeshKit.Ball(0.065f, 8, 6), Mat.Marble, Vector3.zero, Vector3.zero, new Vector3(1f, 0.85f, 1.2f));
                ArtLibrary.Tint(fist.GetComponent<Renderer>(), PaleSkin * 0.9f);
                MeshKit.Part(hand, "Wrap", MeshKit.Cylinder(0.07f, 0.065f, 0.12f, 8), Mat.Leather, new Vector3(0f, -0.03f, -0.09f), new Vector3(-100f, 0f, 0f));
                var fore = MeshKit.Part(hand, "Forearm", MeshKit.Cylinder(0.065f, 0.085f, 0.4f, 8), Mat.Marble, new Vector3(0f, -0.08f, -0.32f), new Vector3(-100f, 0f, 0f));
                ArtLibrary.Tint(fore.GetComponent<Renderer>(), PaleSkin);
                for (int i = 0; i < 3; i++)
                {
                    var scar = MeshKit.Part(hand, "Scar" + i, MeshKit.Box(new Vector3(0.13f, 0.01f, 0.012f)), Mat.Cloth, new Vector3(0f, -0.02f - i * 0.02f, -0.2f - i * 0.08f), new Vector3(-10f, 0f, 25f));
                    ArtLibrary.Tint(scar.GetComponent<Renderer>(), new Color(0.5f, 0.08f, 0.06f));
                }
                return hand;
            }
            var right = BareHand("FlailHand", new Vector3(0.38f, -0.46f, 0.6f), new Vector3(-24f, -10f, 16f));
            var grip = MeshKit.Pivot(right, "Grip", new Vector3(0f, 0.02f, 0.02f), new Vector3(-10f, 0f, 0f));
            BuildFlail(grip, 0.75f, out var ball, out var tip);
            var edge = MeshKit.Pivot(grip, "EdgeMark", new Vector3(0f, 0.4f * 0.75f, 0f));
            var left = BareHand("ScourgeHand", new Vector3(-0.42f, -0.48f, 0.62f), new Vector3(-30f, 20f, 8f));
            var sc = MeshKit.Pivot(left, "Scourge", new Vector3(0f, 0.02f, 0.04f), new Vector3(-150f, 0f, 0f));
            BuildScourge(sc, 0.8f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = right, ShieldPivot = left, TipR = tip, EdgeR = edge };
        }

        // ------------------------------------------------------------------
        // Plague Doctor: beaked mask, wide hat, long waxed coat
        // ------------------------------------------------------------------

        /// <summary>
        /// The Plague Doctor: a wide-brimmed hat over a long beaked mask with round glass eyes that
        /// glow green, a waxed leather greatcoat to the ankles, a bandolier of glowing vials, a cane
        /// with a skull head in the right hand. Tall, narrow, the beak is the silhouette.
        /// </summary>
        public static HumanoidRig BuildPlagueDoctor(Transform root, Color teamColor)
        {
            var visual = MeshKit.Pivot(root, "PlagueDoctorVisual", Vector3.zero);
            var rig = visual.gameObject.AddComponent<HumanoidRig>();
            var coat = new Color(0.16f, 0.14f, 0.12f);

            rig.Hips = MeshKit.Pivot(visual, "Hips", new Vector3(0f, 0.95f, 0f));
            BuildLegs(rig, 0.11f, 0.95f, Mat.Leather, Mat.Leather, 0.15f);
            var skirt = MeshKit.Part(rig.Hips, "CoatSkirt", MeshKit.Lathe("pdSkirt", new[]
            {
                new Vector2(0.34f, -0.86f), new Vector2(0.28f, -0.45f), new Vector2(0.22f, 0f), new Vector2(0.2f, 0.06f),
            }, 10, true, 320f), Mat.Cloth, Vector3.zero, new Vector3(0f, 20f, 0f));
            ArtLibrary.Tint(skirt.GetComponent<Renderer>(), coat);

            rig.Torso = MeshKit.Pivot(rig.Hips, "Torso", Vector3.zero);
            var body = MeshKit.Part(rig.Torso, "Coat", MeshKit.Lathe("pdCoat", new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.22f, 0.2f), new Vector2(0.23f, 0.42f), new Vector2(0.21f, 0.56f), new Vector2(0.1f, 0.62f),
            }, 10), Mat.Cloth, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(body.GetComponent<Renderer>(), coat);
            for (int i = 0; i < 5; i++)
                MeshKit.Part(rig.Torso, "Button" + i, MeshKit.Ball(0.016f, 5, 3), Mat.Gold, new Vector3(0.04f, 0.1f + i * 0.1f, 0.19f));
            // A bandolier of glowing vials, a belt with a satchel.
            MeshKit.Part(rig.Torso, "Bandolier", MeshKit.Box(new Vector3(0.06f, 0.62f, 0.4f)), Mat.Leather, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0f, 34f));
            var vials = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 4; i++)
            {
                var v = MeshKit.Part(rig.Torso, "Vial" + i, MeshKit.Cylinder(0.022f, 0.022f, 0.08f, 6), Mat.Glow, new Vector3(0.1f - i * 0.065f, 0.18f + i * 0.09f, 0.21f), new Vector3(0f, 0f, 34f));
                ArtLibrary.SetGlow(v.GetComponent<Renderer>(), PlagueGreen * 0.8f);
                vials.Add(v.GetComponent<Renderer>());
            }
            MeshKit.Part(rig.Hips, "Belt", MeshKit.Cylinder(0.21f, 0.21f, 0.06f, 9), Mat.Leather, new Vector3(0f, 0.04f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.82f));
            MeshKit.Part(rig.Hips, "Buckle", MeshKit.Box(new Vector3(0.06f, 0.05f, 0.02f)), Mat.Gold, new Vector3(0f, 0.04f, 0.18f));
            var satchel = MeshKit.Part(rig.Hips, "Satchel", MeshKit.Box(new Vector3(0.18f, 0.16f, 0.08f)), Mat.Leather, new Vector3(-0.24f, -0.08f, 0.04f), new Vector3(0f, 0f, 6f));
            ArtLibrary.Tint(satchel.GetComponent<Renderer>(), new Color(0.35f, 0.24f, 0.15f));
            var mantle = MeshKit.Part(rig.Torso, "Mantle", MeshKit.Cylinder(0.3f, 0.22f, 0.18f, 10), Mat.Cloth, new Vector3(0f, 0.5f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.85f));
            ArtLibrary.Tint(mantle.GetComponent<Renderer>(), teamColor * 0.55f);
            var cape = teamColor * 0.3f;
            cape.a = 1f;
            GothicKit.Cloak(rig.Torso, cape, 1.25f, 0.2f, teamColor * 0.7f);

            rig.Head = MeshKit.Pivot(rig.Torso, "Head", new Vector3(0f, 0.62f, 0f));
            var hood = MeshKit.Part(rig.Head, "Coif", MeshKit.Ball(0.135f, 9, 7), Mat.Leather, new Vector3(0f, 0.14f, -0.01f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), coat * 1.2f);
            // The mask: a pale leather face, two round glass eyes, and the long curved beak.
            var face = MeshKit.Part(rig.Head, "MaskFace", MeshKit.Ball(0.12f, 9, 7), Mat.Leather, new Vector3(0f, 0.14f, 0.04f), Vector3.zero, new Vector3(1f, 1f, 0.7f));
            ArtLibrary.Tint(face.GetComponent<Renderer>(), new Color(0.75f, 0.68f, 0.55f));
            var beak = MeshKit.Part(rig.Head, "Beak", MeshKit.Lathe("pdBeak", new[]
            {
                new Vector2(0.065f, 0f), new Vector2(0.055f, 0.1f), new Vector2(0.035f, 0.22f), new Vector2(0.012f, 0.32f), new Vector2(0f, 0.34f),
            }, 9), Mat.Leather, new Vector3(0f, 0.11f, 0.11f), new Vector3(100f, 0f, 0f), new Vector3(1f, 1f, 0.85f));
            ArtLibrary.Tint(beak.GetComponent<Renderer>(), new Color(0.72f, 0.64f, 0.5f));
            MeshKit.Part(rig.Head, "BeakStrap", MeshKit.Cylinder(0.07f, 0.07f, 0.025f, 9), Mat.Leather, new Vector3(0f, 0.11f, 0.15f), new Vector3(100f, 0f, 0f));
            var lensL = MeshKit.Part(rig.Head, "LensL", MeshKit.Cylinder(0.032f, 0.032f, 0.02f, 10), Mat.Glow, new Vector3(-0.05f, 0.17f, 0.115f), new Vector3(90f, 0f, 0f));
            var lensR = MeshKit.Part(rig.Head, "LensR", MeshKit.Cylinder(0.032f, 0.032f, 0.02f, 10), Mat.Glow, new Vector3(0.05f, 0.17f, 0.115f), new Vector3(90f, 0f, 0f));
            foreach (float x in new[] { -0.05f, 0.05f })
                MeshKit.Part(rig.Head, "Rim", MeshKit.Cylinder(0.038f, 0.038f, 0.012f, 10), Mat.Gold, new Vector3(x, 0.17f, 0.11f), new Vector3(90f, 0f, 0f));
            // The hat: a flat crown and a wide brim, a band in the party colour.
            MeshKit.Part(rig.Head, "Brim", MeshKit.Cylinder(0.3f, 0.3f, 0.02f, 16), Mat.Leather, new Vector3(0f, 0.25f, 0f), new Vector3(-4f, 0f, 0f));
            MeshKit.Part(rig.Head, "Crown", MeshKit.Cylinder(0.14f, 0.15f, 0.16f, 12), Mat.Leather, new Vector3(0f, 0.33f, -0.01f));
            var band = MeshKit.Part(rig.Head, "Band", MeshKit.Cylinder(0.152f, 0.152f, 0.035f, 12), Mat.Cloth, new Vector3(0f, 0.28f, -0.01f));
            ArtLibrary.Tint(band.GetComponent<Renderer>(), teamColor);

            rig.ArmR = BuildSleeveArm(rig.Torso, "ArmR", 1f, coat);
            rig.ArmL = BuildSleeveArm(rig.Torso, "ArmL", -1f, coat);
            var grip = MeshKit.Pivot(rig.ArmR, "Grip", new Vector3(0f, -0.6f, 0.02f), new Vector3(80f, 0f, 0f));
            MeshKit.Part(grip, "Cane", MeshKit.Cylinder(0.016f, 0.02f, 1.05f, 7), Mat.Wood, new Vector3(0f, -0.35f, 0f));
            MeshKit.Part(grip, "CaneSkull", MeshKit.Ball(0.045f, 7, 5), Mat.Bone, new Vector3(0f, 0.2f, 0f));
            var vialHand = MeshKit.Part(rig.ArmL, "HeldVial", MeshKit.Ball(0.035f, 7, 5), Mat.Glow, new Vector3(0f, -0.64f, 0.05f));
            ArtLibrary.SetGlow(vialHand.GetComponent<Renderer>(), PlagueGreen);
            vials.Add(vialHand.GetComponent<Renderer>());

            rig.GlowColor = PlagueGreen;
            vials.Insert(0, lensR.GetComponent<Renderer>());
            vials.Insert(0, lensL.GetComponent<Renderer>());
            rig.GlowRenderers = vials.ToArray();
            rig.StrideLength = 1.4f;
            return rig;
        }

        /// <summary>First-person Plague Doctor: the skull cane in the right hand, a glowing vial in the left.</summary>
        public static ViewModel BuildPlagueDoctorViewModel(Transform camera, Color teamColor)
        {
            var root = MeshKit.Pivot(camera, "ViewModel", Vector3.zero);
            var coat = new Color(0.16f, 0.14f, 0.12f);
            var caneHand = MeshKit.Pivot(root, "CaneHand", new Vector3(0.36f, -0.42f, 0.62f), new Vector3(16f, -8f, -12f));
            CasterArm(caneHand, coat, teamColor, true);
            MeshKit.Part(caneHand, "Cane", MeshKit.Cylinder(0.014f, 0.018f, 0.9f, 7), Mat.Wood, new Vector3(0f, -0.2f, 0f));
            MeshKit.Part(caneHand, "Collar", MeshKit.Cylinder(0.024f, 0.022f, 0.03f, 8), Mat.Gold, new Vector3(0f, 0.26f, 0f));
            MeshKit.Part(caneHand, "Skull", MeshKit.Ball(0.04f, 8, 6), Mat.Bone, new Vector3(0f, 0.31f, 0f), Vector3.zero, new Vector3(1f, 1.1f, 1.15f));
            foreach (float x in new[] { -0.014f, 0.014f })
            {
                var eye = MeshKit.Part(caneHand, "SkullEye", MeshKit.Ball(0.009f, 5, 3), Mat.Glow, new Vector3(x, 0.315f, 0.035f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), PlagueGreen * 0.9f);
            }

            var vialHand = MeshKit.Pivot(root, "VialHand", new Vector3(-0.38f, -0.39f, 0.58f), new Vector3(-30f, 24f, 12f));
            CasterArm(vialHand, coat, teamColor, true);
            var flask = MeshKit.Pivot(vialHand, "Flask", new Vector3(0.005f, 0.06f, 0.05f));
            var glass = MeshKit.Part(flask, "Glass", MeshKit.Lathe("pdFlask", new[] { new Vector2(0f, 0f), new Vector2(0.04f, 0.01f), new Vector2(0.045f, 0.05f), new Vector2(0.014f, 0.08f), new Vector2(0.014f, 0.11f) }, 10), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), PlagueGreen * 1.05f);
            MeshKit.Part(flask, "Cork", MeshKit.Cylinder(0.016f, 0.016f, 0.025f, 6), Mat.Wood, new Vector3(0f, 0.12f, 0f));
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ViewModel { Root = root, SwordPivot = caneHand, ShieldPivot = vialHand, SpellGlow = glass.GetComponent<Renderer>() };
        }
    }
}
