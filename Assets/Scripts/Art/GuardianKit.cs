using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Bodies for the guardians that still read as a thin skeleton blown up (2026-11-20 visual pass, the user's notes: bosses
    /// must not look "just like a skeleton", they should be "chunky", "bigger", "scary"). Each one gets a silhouette of its own
    /// on top of its kind's rig: Marrowgut a hunched mass of fused bone, the Drowned Knight corroded plate under weed, the
    /// Hollow King black royal plate round an open chest of light, Gorr a bloated hulk hung with hooks. Every peer runs these
    /// before the rig is merged, so the detail costs a few meshes. Coordinates are the stock skeleton's (shoulders at torso
    /// y 0.58, head pivot 0.66, arm and leg pivots hanging down), as in <see cref="PlateKit"/>.
    /// </summary>
    public static class GuardianKit
    {
        private static Renderer Tint(GameObject g, Color c) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, c); return r; }

        private static Renderer Glow(GameObject g, Color c, List<Renderer> into)
        {
            var r = g.GetComponent<Renderer>();
            ArtLibrary.SetGlow(r, c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            into?.Add(r);
            return r;
        }

        private static void AddGlow(HumanoidRig rig, List<Renderer> more)
        {
            var all = new List<Renderer>();
            if (rig.GlowRenderers != null) all.AddRange(rig.GlowRenderers);
            all.AddRange(more);
            rig.GlowRenderers = all.ToArray();
        }

        private static void Strip(Transform root, params string[] prefixes)
        {
            var doomed = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                foreach (var p in prefixes)
                    if (t != root && t.name.StartsWith(p)) { doomed.Add(t.gameObject); break; }
            foreach (var g in doomed) if (g != null) Object.DestroyImmediate(g);
        }

        /// <summary>A curved horn or tusk in three tapering segments.</summary>
        private static void Horn(Transform parent, string name, Vector3 at, Vector3 euler, float length, float root, float curl, Color c, string mat)
        {
            var a = MeshKit.Pivot(parent, name, at, euler);
            Tint(MeshKit.Part(a, name + "A", MeshKit.Cylinder(root, root * 0.75f, length, 7), mat, Vector3.zero), c);
            var b = MeshKit.Pivot(a, name + "B", new Vector3(0f, length * 0.95f, 0f), new Vector3(curl, 0f, 0f));
            Tint(MeshKit.Part(b, name + "B", MeshKit.Cylinder(root * 0.75f, root * 0.45f, length, 7), mat, Vector3.zero), c);
            var t = MeshKit.Pivot(b, name + "C", new Vector3(0f, length * 0.95f, 0f), new Vector3(curl, 0f, 0f));
            Tint(MeshKit.Part(t, name + "C", MeshKit.Cylinder(root * 0.45f, 0f, length * 0.9f, 6), mat, Vector3.zero), c);
        }

        // ------------------------------------------------------------------
        // Marrowgut, the Bone Colossus (on a Brute)
        // ------------------------------------------------------------------

        /// <summary>
        /// A hunched mountain of fused bone: a humped carapace of packed bone over the back, great curved ribs round a chest
        /// that burns through the gaps, shoulders of fused skulls, forearms swollen into bone clubs, and a beast's skull for a
        /// face with tusks and curling horns.
        /// </summary>
        public static void Colossus(HumanoidRig rig)
        {
            var t = rig.Torso; var head = rig.Head;
            if (t == null || head == null) return;
            var glow = new List<Renderer>();
            var bone = new Color(0.78f, 0.7f, 0.56f);
            var old = new Color(0.58f, 0.5f, 0.4f);
            var fire = new Color(1f, 0.45f, 0.12f);

            // The hump: a great carapace of bone over the shoulders and back.
            Tint(MeshKit.Part(t, "BoneHump", MeshKit.Lathe("colossusHump", new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.27f, 0.15f), new Vector2(0.33f, 0.36f), new Vector2(0.34f, 0.52f), new Vector2(0.26f, 0.68f), new Vector2(0.1f, 0.76f), new Vector2(0f, 0.77f),
            }, 10), Mat.Bone, new Vector3(0f, 0.02f, -0.1f), new Vector3(-12f, 0f, 0f), new Vector3(1.25f, 1f, 0.95f)), old);
            // Plates of bone overlapping down the hump like a shell.
            for (int i = 0; i < 4; i++)
                Tint(MeshKit.Part(t, "HumpPlate", MeshKit.Cylinder(0.3f - i * 0.035f, 0.33f - i * 0.035f, 0.06f, 9), Mat.Bone,
                    new Vector3(0f, 0.22f + i * 0.13f, -0.2f + i * 0.02f), new Vector3(-62f, 0f, 0f), new Vector3(1.2f, 1f, 0.55f)), i % 2 == 0 ? bone : old);

            // A furnace in the chest, seen through great ribs.
            Glow(MeshKit.Part(t, "ChestFire", MeshKit.Ball(0.17f, 9, 7), Mat.Glow, new Vector3(0f, 0.36f, 0.06f), Vector3.zero, new Vector3(1.3f, 1.2f, 0.8f)), fire * 1.3f, glow);
            for (int i = 0; i < 5; i++)
            {
                float y = 0.18f + i * 0.085f, r = 0.24f + Mathf.Sin(i / 4f * Mathf.PI) * 0.06f;
                Tint(MeshKit.Part(t, "GreatRib", MeshKit.Lathe("colossusRib" + i, new[]
                {
                    new Vector2(r, 0f), new Vector2(r + 0.04f, 0.025f), new Vector2(r, 0.05f),
                }, 10, true, 230f), Mat.Bone, new Vector3(0f, y, 0.02f), new Vector3(0f, -25f, 0f), new Vector3(1.15f, 1f, 0.95f)), bone);
            }
            Tint(MeshKit.Part(t, "Keel", MeshKit.Box(new Vector3(0.07f, 0.42f, 0.05f)), Mat.Bone, new Vector3(0f, 0.38f, 0.27f), new Vector3(-8f, 0f, 0f)), bone);

            // Shoulders of fused skulls, staring every way.
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 3; k++)
                {
                    var at = new Vector3(s * (0.27f + k * 0.04f), 0.62f - k * 0.05f, -0.05f + k * 0.07f);
                    Tint(MeshKit.Part(t, "ShoulderSkull", MeshKit.Ball(0.085f, 8, 6), Mat.Bone, at, new Vector3(0f, s * (30f + k * 25f), 0f), new Vector3(1f, 1.05f, 1.15f)), k == 1 ? old : bone);
                    var eyeAt = at + Quaternion.Euler(0f, s * (30f + k * 25f), 0f) * new Vector3(0f, 0.01f, 0.085f);
                    Glow(MeshKit.Part(t, "ShoulderEye", MeshKit.Ball(0.016f, 5, 4), Mat.Glow, eyeAt), fire * 1.6f, glow);
                }

            // Forearms swollen into clubs of bone, knuckled with spurs.
            foreach (var arm in new[] { rig.ArmL, rig.ArmR })
            {
                if (arm == null) continue;
                float s = arm == rig.ArmL ? -1f : 1f;
                Tint(MeshKit.Part(arm, "UpperMass", MeshKit.Cylinder(0.09f, 0.11f, 0.24f, 8), Mat.Bone, new Vector3(0f, -0.28f, 0f)), old);
                Tint(MeshKit.Part(arm, "BoneClub", MeshKit.Lathe("colossusClub", new[]
                {
                    new Vector2(0.08f, 0f), new Vector2(0.13f, 0.08f), new Vector2(0.16f, 0.2f), new Vector2(0.12f, 0.3f), new Vector2(0f, 0.32f),
                }, 9), Mat.Bone, new Vector3(0f, -0.4f, 0f), new Vector3(180f, 0f, 0f)), bone);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * 90f + 45f;
                    Tint(MeshKit.Part(arm, "Spur", MeshKit.Lathe("colossusSpur", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.13f) }, 5), Mat.Bone,
                        new Vector3(0f, -0.62f, 0f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.12f), new Vector3(70f, a, 0f)), old);
                }
                Tint(MeshKit.Part(arm, "Elbow", MeshKit.Lathe("colossusElbow", new[] { new Vector2(0.04f, 0f), new Vector2(0f, 0.18f) }, 5), Mat.Bone,
                    new Vector3(0f, -0.36f, -0.08f), new Vector3(-120f, 0f, 0f)), bone);
                Tint(MeshKit.Part(arm, "ShoulderKnot", MeshKit.Ball(0.13f, 8, 6), Mat.Bone, new Vector3(0.02f * s, -0.02f, 0f)), old);
            }

            // Thick legs: knotted bone round the thighs and shins.
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                if (leg == null) continue;
                Tint(MeshKit.Part(leg, "ThighMass", MeshKit.Cylinder(0.1f, 0.12f, 0.32f, 8), Mat.Bone, new Vector3(0f, -0.42f, 0f)), old);
                Tint(MeshKit.Part(leg, "ShinMass", MeshKit.Cylinder(0.08f, 0.11f, 0.3f, 8), Mat.Bone, new Vector3(0f, -0.84f, 0f)), bone);
                Tint(MeshKit.Part(leg, "Hoof", MeshKit.Box(new Vector3(0.16f, 0.08f, 0.24f)), Mat.Bone, new Vector3(0f, -0.86f, 0.04f)), old);
            }

            // The face: a beast's skull with tusks and great curling horns over the man's.
            Tint(MeshKit.Part(head, "BeastSkull", MeshKit.Ball(0.15f, 10, 8), Mat.Bone, new Vector3(0f, 0.15f, 0.04f), new Vector3(-8f, 0f, 0f), new Vector3(1.05f, 0.95f, 1.35f)), bone);
            Tint(MeshKit.Part(head, "Snout", MeshKit.Box(new Vector3(0.16f, 0.1f, 0.14f)), Mat.Bone, new Vector3(0f, 0.08f, 0.17f), new Vector3(10f, 0f, 0f)), old);
            MeshKit.Part(head, "Maw", MeshKit.Box(new Vector3(0.13f, 0.035f, 0.02f)), Mat.Dark, new Vector3(0f, 0.04f, 0.245f));
            for (int i = 0; i < 5; i++)
                Tint(MeshKit.Part(head, "Fang", MeshKit.Lathe("colossusFang", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.045f) }, 4), Mat.Bone,
                    new Vector3(-0.05f + i * 0.025f, 0.06f, 0.245f), new Vector3(180f, 0f, 0f)), bone);
            foreach (float x in new[] { -0.055f, 0.055f })
            {
                MeshKit.Part(head, "DeepSocket", MeshKit.Ball(0.035f, 6, 5), Mat.Dark, new Vector3(x, 0.17f, 0.19f));
                Glow(MeshKit.Part(head, "SocketFire", MeshKit.Ball(0.018f, 6, 4), Mat.Glow, new Vector3(x, 0.17f, 0.215f)), fire * 2.2f, glow);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                Horn(head, "Tusk", new Vector3(s * 0.08f, 0.05f, 0.2f), new Vector3(70f, 0f, s * -20f), 0.08f, 0.025f, -35f, bone, Mat.Bone);
                Horn(head, "RamHorn", new Vector3(s * 0.13f, 0.24f, -0.02f), new Vector3(-20f, 0f, s * -80f), 0.14f, 0.05f, -55f, old, Mat.Bone);
            }
            rig.StrideLength = 1.5f;
            AddGlow(rig, glow);
        }

        // ------------------------------------------------------------------
        // The Drowned Knight (on a Frost Wight)
        // ------------------------------------------------------------------

        /// <summary>
        /// A knight who walked into the sea in full plate and came back: corroded verdigris armour, a great helm with a
        /// drowned-green light behind the slit, a cloak gone to rotten weed, barnacles crusting every plate, an anchor chain
        /// wound round the chest.
        /// </summary>
        public static void Drowned(HumanoidRig rig)
        {
            var t = rig.Torso; var head = rig.Head;
            if (t == null || head == null) return;
            Strip(head, "IceShard");
            Strip(t, "FrostMantle");
            var sea = new Color(0.3f, 0.95f, 0.85f);
            var glow = PlateKit.Armour(rig, new PlateKit.Look { Plate = new Color(0.3f, 0.4f, 0.36f), Trim = new Color(0.52f, 0.4f, 0.2f), Glow = sea, ClosedHelm = true });
            GothicKit.Cloak(t, new Color(0.1f, 0.2f, 0.15f), 1.2f, 0.32f, new Color(0.3f, 0.32f, 0.2f), true);
            // Barnacles and rust blooms crusting the plate.
            var rng = new System.Random(7);
            foreach (var part in new[] { t, rig.ArmL, rig.ArmR, rig.LegL, rig.LegR, head })
            {
                if (part == null) continue;
                for (int i = 0; i < 6; i++)
                {
                    float a = (float)rng.NextDouble() * 360f;
                    bool body = part == t;
                    var at = body ? new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.22f, 0.1f + (float)rng.NextDouble() * 0.45f, Mathf.Cos(a * Mathf.Deg2Rad) * 0.18f)
                        : part == head ? new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.16f, 0.02f + (float)rng.NextDouble() * 0.22f, Mathf.Cos(a * Mathf.Deg2Rad) * 0.16f)
                        : new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.09f, -0.2f - (float)rng.NextDouble() * 0.6f, Mathf.Cos(a * Mathf.Deg2Rad) * 0.09f);
                    Tint(MeshKit.Part(part, "Barnacle", MeshKit.Lathe("barnacle", new[] { new Vector2(0.03f, 0f), new Vector2(0.024f, 0.025f), new Vector2(0.012f, 0.03f) }, 6, false), Mat.Rubble,
                        at, Quaternion.LookRotation(new Vector3(at.x, 0f, at.z).normalized + Vector3.up * 0.01f).eulerAngles + new Vector3(90f, 0f, 0f)), new Color(0.72f, 0.7f, 0.6f));
                }
            }
            // An anchor chain wound round the chest, the anchor hanging at the hip.
            for (int i = 0; i < 14; i++)
            {
                float a = i * 360f / 14f;
                var at = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.25f, 0.18f + i * 0.02f, Mathf.Cos(a * Mathf.Deg2Rad) * 0.21f);
                Tint(MeshKit.Part(t, "ChainLink", MeshKit.Box(new Vector3(0.05f, 0.03f, 0.02f)), Mat.Steel, at, new Vector3(0f, a + 90f, i % 2 == 0 ? 0f : 90f)), new Color(0.32f, 0.26f, 0.2f));
            }
            if (rig.Hips != null)
            {
                var anchor = MeshKit.Pivot(rig.Hips, "Anchor", new Vector3(0.24f, -0.2f, -0.05f), new Vector3(0f, 0f, 8f));
                Tint(MeshKit.Part(anchor, "Shank", MeshKit.Box(new Vector3(0.04f, 0.42f, 0.04f)), Mat.Steel, new Vector3(0f, -0.2f, 0f)), new Color(0.3f, 0.36f, 0.32f));
                Tint(MeshKit.Part(anchor, "Fluke", MeshKit.Lathe("anchorFluke", new[] { new Vector2(0.17f, 0f), new Vector2(0.19f, 0.03f), new Vector2(0.17f, 0.05f) }, 10, true, 160f), Mat.Steel,
                    new Vector3(0f, -0.38f, 0f), new Vector3(90f, 0f, 100f)), new Color(0.3f, 0.36f, 0.32f));
            }
            // Water pouring from the helm's slit: a thin glowing sheet, the sea still in him.
            Glow(MeshKit.Part(head, "Spill", MeshKit.Box(new Vector3(0.12f, 0.1f, 0.006f)), Mat.Glow, new Vector3(0f, 0.07f, 0.17f)), sea * 0.35f, glow);
            AddGlow(rig, glow);
            rig.GlowColor = sea;
            rig.StrideLength = 1.4f;
        }

        // ------------------------------------------------------------------
        // Ossric, the Hollow King (on the King)
        // ------------------------------------------------------------------

        /// <summary>
        /// Black royal plate with gold, the crowned skull bare above it, and the breastplate torn open on a hollow where his
        /// heart was: cold light pours out of it. Great spiked pauldrons and a skirt of plate make the silhouette a wall.
        /// </summary>
        public static void HollowKing(HumanoidRig rig)
        {
            var t = rig.Torso;
            if (t == null || rig.Head == null) return;
            var cold = new Color(0.4f, 0.75f, 1f);
            var glow = PlateKit.Armour(rig, new PlateKit.Look { Plate = new Color(0.17f, 0.17f, 0.2f), Trim = new Color(0.82f, 0.62f, 0.25f), Glow = cold, NoHelm = true });
            // The hollow: a ring of torn plate round a pit of light.
            Glow(MeshKit.Part(t, "Hollow", MeshKit.Ball(0.1f, 9, 7), Mat.Glow, new Vector3(0f, 0.4f, 0.17f), Vector3.zero, new Vector3(1f, 1.2f, 0.5f)), cold * 2f, glow);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 360f / 7f;
                Tint(MeshKit.Part(t, "TornEdge", MeshKit.Lathe("tornEdge", new[] { new Vector2(0.025f, 0f), new Vector2(0f, 0.08f) }, 4), Mat.Steel,
                    new Vector3(0f, 0.4f, 0.2f) + Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0f, a + 180f)), new Color(0.17f, 0.17f, 0.2f));
            }
            // A crown-of-thorns gorget and a high collar standing behind the skull.
            Tint(MeshKit.Part(t, "HighCollar", MeshKit.Lathe("kingCollar", new[] { new Vector2(0.16f, 0f), new Vector2(0.2f, 0.12f), new Vector2(0.24f, 0.24f) }, 12, true, 200f), Mat.Steel,
                new Vector3(0f, 0.6f, -0.02f), new Vector3(0f, 170f, 0f)), new Color(0.17f, 0.17f, 0.2f));
            for (int i = 0; i < 5; i++)
            {
                float a = -60f + i * 30f;
                Tint(MeshKit.Part(t, "CollarSpike", MeshKit.Lathe("collarSpike", new[] { new Vector2(0.025f, 0f), new Vector2(0f, 0.16f) }, 4), Mat.Gold,
                    new Vector3(0f, 0.82f, 0f) + Quaternion.Euler(0f, a + 180f, 0f) * new Vector3(0f, 0f, 0.22f), new Vector3(-20f, a + 180f, 0f)), new Color(0.82f, 0.62f, 0.25f));
            }
            AddGlow(rig, glow);
            rig.StrideLength = 1.5f;
        }

        // ------------------------------------------------------------------
        // The Unsung (its own floating rig)
        // ------------------------------------------------------------------

        /// <summary>
        /// The last song no one sang: great curved ribs closing round the dark heart, shoulder blades like shields, a black
        /// shroud torn to ribbons hanging from the shoulders, heavy bone arms, and behind it a fan of cracked organ pipes
        /// rising like wings, violet light breathing from their mouths. Coordinates are those of CharacterArt.BuildUnsung.
        /// </summary>
        public static void Unsung(HumanoidRig rig)
        {
            var t = rig.Torso; var head = rig.Head;
            if (t == null || head == null) return;
            var glow = new List<Renderer>();
            var bone = new Color(0.62f, 0.6f, 0.66f);
            var dark = new Color(0.36f, 0.34f, 0.4f);
            var violet = new Color(0.6f, 0.3f, 1f);
            // The thin ribs give way to great curved ones.
            Strip(t, "Rib");
            for (int i = 0; i < 6; i++)
            {
                float y = 0.12f + i * 0.09f, r = 0.2f + Mathf.Sin(i / 5f * Mathf.PI) * 0.07f;
                Tint(MeshKit.Part(t, "GreatRib", MeshKit.Lathe("unsungRib" + i, new[]
                {
                    new Vector2(r, 0f), new Vector2(r + 0.045f, 0.02f), new Vector2(r, 0.04f),
                }, 10, true, 250f), Mat.Bone, new Vector3(0f, y, -0.04f), new Vector3(-8f, -35f, 0f), new Vector3(1.15f, 1f, 1f)), bone);
            }
            // Shoulder blades like shields, a collar of vertebrae.
            foreach (float s in new[] { -1f, 1f })
            {
                Tint(MeshKit.Part(t, "Blade", MeshKit.Box(new Vector3(0.24f, 0.32f, 0.04f)), Mat.Bone, new Vector3(s * 0.17f, 0.56f, -0.2f), new Vector3(10f, s * -20f, s * 8f)), dark);
                Tint(MeshKit.Part(t, "ShoulderKnob", MeshKit.Ball(0.09f, 8, 6), Mat.Bone, new Vector3(s * 0.27f, 0.66f, 0f)), bone);
            }
            for (int i = 0; i < 5; i++)
                Tint(MeshKit.Part(t, "Collar", MeshKit.Ball(0.05f, 6, 5), Mat.Bone, new Vector3(-0.16f + i * 0.08f, 0.72f, -0.02f - Mathf.Abs(i - 2) * 0.02f), Vector3.zero, new Vector3(1f, 0.7f, 1f)), bone);
            // The shroud: black ribbons from the shoulders, the torn hem trailing below the spine.
            for (int i = 0; i < 11; i++)
            {
                float x = -0.3f + i * 0.06f;
                float len = 0.9f + (i % 3) * 0.25f + Mathf.Abs(i - 5) * 0.04f;
                Tint(MeshKit.Part(t, "Shroud", MeshKit.Box(new Vector3(0.07f, len, 0.012f)), Mat.Cloth,
                    new Vector3(x, 0.66f - len / 2f, -0.24f - Mathf.Abs(i - 5) * 0.012f), new Vector3(6f, 0f, (i - 5) * 2.5f)), new Color(0.06f, 0.05f, 0.08f));
            }
            // Heavier arms: thick bone over the stick limbs, a knuckled claw.
            foreach (var arm in new[] { rig.ArmL, rig.ArmR })
            {
                if (arm == null) continue;
                Tint(MeshKit.Part(arm, "UpperBone", MeshKit.Cylinder(0.055f, 0.045f, 0.5f, 7), Mat.Bone, new Vector3(0f, -0.5f, 0f)), bone);
                Tint(MeshKit.Part(arm, "Elbow", MeshKit.Ball(0.065f, 7, 5), Mat.Bone, new Vector3(0f, -0.52f, 0.02f)), dark);
                Tint(MeshKit.Part(arm, "ForeBone", MeshKit.Cylinder(0.045f, 0.035f, 0.5f, 7), Mat.Bone, new Vector3(0f, -1.0f, 0.05f)), bone);
                Tint(MeshKit.Part(arm, "Knuckles", MeshKit.Box(new Vector3(0.14f, 0.06f, 0.08f)), Mat.Bone, new Vector3(0f, -1.0f, 0.07f)), dark);
            }
            // The pipes: a fan of cracked organ pipes rising behind it, violet breath in their mouths.
            var fan = MeshKit.Pivot(t, "PipeFan", new Vector3(0f, 0.5f, -0.3f), new Vector3(-12f, 0f, 0f));
            for (int i = 0; i < 9; i++)
            {
                float k = i - 4f;
                float len = 1.25f - Mathf.Abs(k) * 0.13f;
                float rr = 0.05f - Mathf.Abs(k) * 0.003f;
                var pipe = MeshKit.Pivot(fan, "Pipe" + i, new Vector3(k * 0.1f, 0f, Mathf.Abs(k) * 0.02f), new Vector3(0f, 0f, -k * 9f));
                Tint(MeshKit.Part(pipe, "PipeBody", MeshKit.Cylinder(rr, rr, len, 8), Mat.Steel, Vector3.zero), new Color(0.62f, 0.6f, 0.68f));
                Tint(MeshKit.Part(pipe, "PipeBand", MeshKit.Cylinder(rr * 1.25f, rr * 1.25f, 0.03f, 8), Mat.Gold, new Vector3(0f, len * 0.3f, 0f)), new Color(0.55f, 0.45f, 0.3f));
                MeshKit.Part(pipe, "PipeMouth", MeshKit.Box(new Vector3(rr * 1.2f, rr * 1.4f, 0.01f)), Mat.Dark, new Vector3(0f, len * 0.22f, rr + 0.002f));
                Glow(MeshKit.Part(pipe, "PipeBreath", MeshKit.Box(new Vector3(rr * 1.1f, rr * 1.3f, 0.01f)), Mat.Glow, new Vector3(0f, len * 0.22f, rr + 0.006f)), violet * 1.2f, glow);
                Tint(MeshKit.Part(pipe, "PipeTip", MeshKit.Cylinder(rr, 0f, rr * 3f, 8), Mat.Steel, new Vector3(0f, len, 0f)), new Color(0.62f, 0.6f, 0.68f));
            }
            // A crown of broken bell-rims round the skull.
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                Tint(MeshKit.Part(head, "BellShard", MeshKit.Lathe("bellShard" + (i % 2), new[] { new Vector2(0.03f, 0f), new Vector2(0.005f, 0.14f + (i % 2) * 0.07f) }, 4), Mat.Gold,
                    new Vector3(0f, 0.22f, 0f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.14f), new Vector3(18f, a, 0f)), new Color(0.5f, 0.42f, 0.3f));
            }
            AddGlow(rig, glow);
        }

        // ------------------------------------------------------------------
        // Mother Marrow (a lair keeper, on a Withering Hag)
        // ------------------------------------------------------------------

        /// <summary>
        /// The bone-knitter: a great hump of woven bone on her back like a basket she never puts down, a headdress of long
        /// finger bones, a heavy layered shawl, and a cradle of small skulls held to her chest, their eyes lit like hers.
        /// </summary>
        public static void Marrow(HumanoidRig rig, Color light)
        {
            var t = rig.Torso; var head = rig.Head;
            if (t == null || head == null) return;
            var glow = new List<Renderer>();
            var bone = new Color(0.8f, 0.74f, 0.62f);
            var old = new Color(0.6f, 0.54f, 0.44f);
            // The hump: a basket of woven bone, ribbed.
            Tint(MeshKit.Part(t, "BoneBasket", MeshKit.Lathe("marrowBasket", new[]
            {
                new Vector2(0.16f, 0f), new Vector2(0.26f, 0.12f), new Vector2(0.3f, 0.3f), new Vector2(0.27f, 0.46f), new Vector2(0.18f, 0.56f), new Vector2(0f, 0.6f),
            }, 9), Mat.Bone, new Vector3(0f, 0.2f, -0.22f), new Vector3(-20f, 0f, 0f), new Vector3(1.1f, 1f, 0.85f)), old);
            for (int i = 0; i < 4; i++)
                Tint(MeshKit.Part(t, "BasketRib", MeshKit.Cylinder(0.25f + Mathf.Sin(i / 3f * Mathf.PI) * 0.06f, 0.25f + Mathf.Sin(i / 3f * Mathf.PI) * 0.06f, 0.03f, 10), Mat.Bone,
                    new Vector3(0f, 0.28f + i * 0.11f, -0.25f + i * 0.03f), new Vector3(-20f, 0f, 0f), new Vector3(1.12f, 1f, 0.88f)), bone);
            // Long bones sticking out of the basket.
            for (int i = 0; i < 5; i++)
                Tint(MeshKit.Part(t, "BasketBone", MeshKit.Cylinder(0.022f, 0.016f, 0.5f + i * 0.06f, 5), Mat.Bone,
                    new Vector3(-0.16f + i * 0.08f, 0.62f, -0.36f), new Vector3(-35f + i * 4f, 0f, (i - 2) * 14f)), bone);
            // A heavy shawl in three layers.
            for (int k = 0; k < 3; k++)
                Tint(MeshKit.Part(t, "Shawl", MeshKit.Cylinder(0.27f - k * 0.025f, 0.3f - k * 0.02f, 0.09f, 12), Mat.Cloth,
                    new Vector3(0f, 0.5f - k * 0.08f, 0f), Vector3.zero, new Vector3(1.05f, 1f, 0.9f)), new Color(0.22f - k * 0.03f, 0.18f - k * 0.02f, 0.15f));
            // A headdress of long finger bones fanned behind the head.
            for (int i = 0; i < 7; i++)
            {
                float a = -60f + i * 20f;
                Tint(MeshKit.Part(head, "FingerBone", MeshKit.Cylinder(0.014f, 0.008f, 0.28f + (i % 2) * 0.1f, 5), Mat.Bone,
                    new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.1f, 0.18f, -0.06f), new Vector3(-25f, 0f, -a * 0.6f)), bone);
            }
            // The cradle of skulls held to her chest.
            Tint(MeshKit.Part(t, "Cradle", MeshKit.Lathe("marrowCradle", new[] { new Vector2(0.05f, 0f), new Vector2(0.16f, 0.03f), new Vector2(0.18f, 0.08f) }, 10), Mat.Bone,
                new Vector3(0f, 0.28f, 0.2f), new Vector3(10f, 0f, 0f), new Vector3(1.2f, 1f, 0.8f)), old);
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(-0.08f + i * 0.08f, 0.36f + (i == 1 ? 0.03f : 0f), 0.22f);
                Tint(MeshKit.Part(t, "BabySkull", MeshKit.Ball(0.055f, 8, 6), Mat.Bone, at, Vector3.zero, new Vector3(1f, 0.95f, 1.05f)), bone);
                foreach (float x in new[] { -0.018f, 0.018f })
                    Glow(MeshKit.Part(t, "BabyEye", MeshKit.Ball(0.009f, 5, 3), Mat.Glow, at + new Vector3(x, 0.005f, 0.05f)), light * 1.4f, glow);
            }
            AddGlow(rig, glow);
        }

        // ------------------------------------------------------------------
        // Gorr, the Rot Below (on a Plaguebearer)
        // ------------------------------------------------------------------

        /// <summary>
        /// A bloated hulk of rot: a gut that hangs to the knees in folds, slabs of grey flesh over the shoulders, arms thick as a
        /// man, a lower jaw torn wide on a mouthful of teeth, and the butcher's hooks still sunk in his back, trailing chain.
        /// </summary>
        public static void Rot(HumanoidRig rig)
        {
            var t = rig.Torso; var head = rig.Head;
            if (t == null || head == null) return;
            var glow = new List<Renderer>();
            var flesh = new Color(0.46f, 0.5f, 0.34f);
            var dark = new Color(0.3f, 0.32f, 0.22f);
            var bile = new Color(0.5f, 0.95f, 0.2f);
            // A head that holds its own over the bulk; then the gut, in folds.
            head.localScale *= 1.4f;
            head.localPosition += new Vector3(0f, 0.04f, 0.06f);
            Tint(MeshKit.Part(t, "Gut", MeshKit.Ball(0.33f, 12, 9), Mat.Leather, new Vector3(0f, 0.1f, 0.12f), Vector3.zero, new Vector3(1.15f, 1f, 1f)), flesh);
            Tint(MeshKit.Part(t, "GutFold", MeshKit.Cylinder(0.37f, 0.33f, 0.05f, 12), Mat.Leather, new Vector3(0f, 0.02f, 0.12f), new Vector3(8f, 0f, 0f), new Vector3(1.15f, 1f, 1f)), dark);
            Tint(MeshKit.Part(t, "Chest", MeshKit.Ball(0.3f, 10, 8), Mat.Leather, new Vector3(0f, 0.45f, 0f), Vector3.zero, new Vector3(1.25f, 0.85f, 0.95f)), flesh);
            // Stitches across the gut, as if someone tried to close it once.
            for (int i = 0; i < 6; i++)
                Tint(MeshKit.Part(t, "Stitch", MeshKit.Box(new Vector3(0.012f, 0.07f, 0.01f)), Mat.Leather, new Vector3(-0.12f + i * 0.05f, 0.14f, 0.48f), new Vector3(-10f, 0f, i % 2 == 0 ? 20f : -20f)), new Color(0.15f, 0.1f, 0.08f));
            Glow(MeshKit.Part(t, "Wound", MeshKit.Box(new Vector3(0.28f, 0.025f, 0.02f)), Mat.Glow, new Vector3(0f, 0.14f, 0.475f), new Vector3(-10f, 0f, 0f)), bile * 0.9f, glow);
            // Slabs of flesh over the shoulders and thick arms.
            foreach (float s in new[] { -1f, 1f })
                Tint(MeshKit.Part(t, "ShoulderSlab", MeshKit.Ball(0.17f, 9, 7), Mat.Leather, new Vector3(s * 0.26f, 0.6f, -0.02f), Vector3.zero, new Vector3(1.1f, 0.8f, 1f)), flesh);
            foreach (var arm in new[] { rig.ArmL, rig.ArmR })
            {
                if (arm == null) continue;
                Tint(MeshKit.Part(arm, "FatArm", MeshKit.Cylinder(0.1f, 0.12f, 0.3f, 9), Mat.Leather, new Vector3(0f, -0.33f, 0f)), flesh);
                Tint(MeshKit.Part(arm, "FatFore", MeshKit.Cylinder(0.08f, 0.1f, 0.25f, 9), Mat.Leather, new Vector3(0f, -0.62f, 0f)), dark);
            }
            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                if (leg == null) continue;
                Tint(MeshKit.Part(leg, "FatLeg", MeshKit.Cylinder(0.11f, 0.14f, 0.35f, 9), Mat.Leather, new Vector3(0f, -0.42f, 0f)), flesh);
                Tint(MeshKit.Part(leg, "FatShin", MeshKit.Cylinder(0.09f, 0.12f, 0.3f, 9), Mat.Leather, new Vector3(0f, -0.85f, 0f)), dark);
            }
            // The jaw torn wide, teeth all the way round.
            Tint(MeshKit.Part(head, "TornJaw", MeshKit.Box(new Vector3(0.18f, 0.04f, 0.14f)), Mat.Bone, new Vector3(0f, -0.04f, 0.1f), new Vector3(28f, 0f, 0f)), new Color(0.62f, 0.62f, 0.45f));
            Glow(MeshKit.Part(head, "Gullet", MeshKit.Box(new Vector3(0.12f, 0.05f, 0.02f)), Mat.Glow, new Vector3(0f, 0.02f, 0.11f)), bile * 1.2f, glow);
            for (int i = 0; i < 6; i++)
                Tint(MeshKit.Part(head, "Tooth", MeshKit.Lathe("rotTooth", new[] { new Vector2(0.01f, 0f), new Vector2(0f, 0.04f) }, 4), Mat.Bone,
                    new Vector3(-0.07f + i * 0.028f, -0.01f, 0.17f), new Vector3(-62f, 0f, 0f)), new Color(0.75f, 0.72f, 0.5f));
            // The butcher's hooks in his back, each trailing a length of chain.
            for (int h = 0; h < 3; h++)
            {
                var at = new Vector3(-0.16f + h * 0.16f, 0.5f - (h % 2) * 0.12f, -0.27f);
                var hook = MeshKit.Pivot(t, "Hook", at, new Vector3(0f, 0f, (h - 1) * 12f));
                Tint(MeshKit.Part(hook, "HookCurve", MeshKit.Lathe("meatHook", new[] { new Vector2(0.09f, 0f), new Vector2(0.105f, 0.012f), new Vector2(0.09f, 0.024f) }, 8, true, 220f), Mat.Steel,
                    Vector3.zero, new Vector3(90f, 0f, 0f)), new Color(0.35f, 0.25f, 0.2f));
                for (int k = 0; k < 6; k++)
                    Tint(MeshKit.Part(hook, "HookChain", MeshKit.Box(new Vector3(0.025f, 0.05f, 0.012f)), Mat.Steel,
                        new Vector3(0f, -0.05f - k * 0.055f, -0.1f - k * 0.01f), new Vector3(0f, k % 2 == 0 ? 0f : 90f, 0f)), new Color(0.3f, 0.24f, 0.2f));
            }
            AddGlow(rig, glow);
            rig.StrideLength = 1.35f;
        }
    }
}
