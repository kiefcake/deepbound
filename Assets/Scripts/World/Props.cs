using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Dungeon dressing. Static props go into a <see cref="MeshKit.Batcher"/> so they cost
    /// nothing per-object; anything that animates (torches, braziers) is a real object.
    /// </summary>
    public static class Props
    {
        private static readonly Vector2[] PillarProfile =
        {
            new Vector2(0.75f, 0f), new Vector2(0.75f, 0.35f), new Vector2(0.55f, 0.5f),
            new Vector2(0.48f, 0.6f), new Vector2(0.45f, 1f),
        };

        /// <summary>A stone pillar from floor to ceiling, with a stepped base and capital.</summary>
        public static void Pillar(MeshKit.Batcher b, Vector3 floor, float height)
        {
            b.Add(MeshKit.Lathe("pillarBase", PillarProfile, 8), Mat.StoneWall, Matrix4x4.Translate(floor));
            b.Add(MeshKit.Cylinder(0.45f, 0.45f, height - 1.6f, 8), Mat.StoneWall, Matrix4x4.Translate(floor + Vector3.up * 0.8f));
            // Capital: the base profile flipped upside down.
            b.Add(MeshKit.Lathe("pillarBase", PillarProfile, 8), Mat.StoneWall,
                Matrix4x4.TRS(floor + Vector3.up * height, Quaternion.Euler(180f, 0f, 0f), Vector3.one));
        }

        public static void Crate(MeshKit.Batcher b, Vector3 floor, float size, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var c = floor + Vector3.up * size / 2f;
            b.AddBox(c, Vector3.one * size, Mat.Wood, rot);

            // Edge battens, slightly proud of the faces — what makes a box read as a crate.
            float h = size / 2f, t = size * 0.12f;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1;
                b.AddBox(c + rot * new Vector3(sx * (h - t / 2f + 0.01f), 0f, sz * (h - t / 2f + 0.01f)), new Vector3(t, size + 0.01f, t), Mat.Wood, rot);
            }
            b.AddBox(c + Vector3.up * (h - t / 2f + 0.005f), new Vector3(size + 0.02f, t, size + 0.02f) * 1f, Mat.Wood, rot);
        }

        public static void Barrel(MeshKit.Batcher b, Vector3 floor)
        {
            b.Add(MeshKit.Lathe("barrel", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.32f, 0f), new Vector2(0.4f, 0.3f), new Vector2(0.42f, 0.5f),
                new Vector2(0.4f, 0.7f), new Vector2(0.32f, 1f), new Vector2(0f, 1f),
            }, 10), Mat.Wood, Matrix4x4.Translate(floor));
            b.Add(MeshKit.Cylinder(0.41f, 0.43f, 0.06f, 10), Mat.Steel, Matrix4x4.Translate(floor + Vector3.up * 0.22f));
            b.Add(MeshKit.Cylinder(0.43f, 0.41f, 0.06f, 10), Mat.Steel, Matrix4x4.Translate(floor + Vector3.up * 0.72f));
        }

        /// <summary>A scatter of bones and a skull — evidence of the last party that came through.</summary>
        public static void BonePile(MeshKit.Batcher b, Vector3 floor, System.Random rng)
        {
            for (int i = 0; i < 6; i++)
            {
                var p = floor + new Vector3((float)rng.NextDouble() - 0.5f, 0.03f, (float)rng.NextDouble() - 0.5f) * 1.2f;
                p.y = floor.y + 0.03f;
                b.AddBox(p, new Vector3(0.05f, 0.05f, 0.3f + (float)rng.NextDouble() * 0.2f), Mat.Bone,
                    Quaternion.Euler(0f, rng.Next(360), rng.Next(-15, 15)));
            }
            b.Add(MeshKit.Ball(0.12f, 8, 6), Mat.Bone, Matrix4x4.TRS(floor + new Vector3(0.2f, 0.1f, 0.1f), Quaternion.Euler(-20f, rng.Next(360), 70f), Vector3.one));
        }

        public static void Rubble(MeshKit.Batcher b, Vector3 floor, System.Random rng)
        {
            for (int i = 0; i < 5; i++)
            {
                float s = 0.25f + (float)rng.NextDouble() * 0.45f;
                var p = floor + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 1.8f;
                b.AddBox(p + Vector3.up * s * 0.3f, new Vector3(s, s * 0.7f, s * 0.9f), Mat.Rubble,
                    Quaternion.Euler(rng.Next(-25, 25), rng.Next(360), rng.Next(-25, 25)));
            }
        }

        /// <summary>The boss's throne: a stepped dais and a high-backed stone seat.</summary>
        public static void Throne(MeshKit.Batcher b, Vector3 floor, Quaternion facing)
        {
            b.AddBox(floor + facing * new Vector3(0f, 0.2f, 0f), new Vector3(6f, 0.4f, 4f), Mat.StoneWall, facing);
            b.AddBox(floor + facing * new Vector3(0f, 0.6f, -0.4f), new Vector3(4.5f, 0.4f, 3f), Mat.StoneWall, facing);
            b.AddBox(floor + facing * new Vector3(0f, 1.3f, -0.6f), new Vector3(2.2f, 1f, 1.6f), Mat.StoneWall, facing);
            b.AddBox(floor + facing * new Vector3(0f, 3.2f, -1.3f), new Vector3(2.4f, 4.8f, 0.4f), Mat.StoneWall, facing);
            b.AddBox(floor + facing * new Vector3(0f, 5.7f, -1.3f), new Vector3(1.4f, 0.4f, 0.45f), Mat.Gold, facing);
            b.AddBox(floor + facing * new Vector3(1.2f, 2.2f, -0.6f), new Vector3(0.3f, 0.9f, 1.6f), Mat.Gold, facing);
            b.AddBox(floor + facing * new Vector3(-1.2f, 2.2f, -0.6f), new Vector3(0.3f, 0.9f, 1.6f), Mat.Gold, facing);
        }

        /// <summary>
        /// The wandering merchant: a hooded, stooped figure with a lantern behind a trestle
        /// table of wares. Purely visual; trading is handled by PlayerProgress and the HUD.
        /// </summary>
        public static void Merchant(Transform parent, Vector3 floor, Vector3 lookAt, MeshKit.Batcher b)
        {
            var root = new GameObject("Merchant").transform;
            root.SetParent(parent, false);
            root.position = floor;
            var facing = lookAt - floor;
            facing.y = 0f;
            root.rotation = Quaternion.LookRotation(facing);

            var robe = MeshKit.Part(root, "Robe", MeshKit.Lathe("merchantRobe", new[]
            {
                new Vector2(0.42f, 0f), new Vector2(0.36f, 0.5f), new Vector2(0.3f, 1.05f), new Vector2(0.26f, 1.3f), new Vector2(0.12f, 1.42f), new Vector2(0f, 1.44f),
            }, 9), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(robe.GetComponent<Renderer>(), new Color(0.2f, 0.3f, 0.18f));

            var hood = MeshKit.Part(root, "Hood", MeshKit.Lathe("merchantHood", new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.22f, 0.12f), new Vector2(0.18f, 0.26f), new Vector2(0.08f, 0.34f), new Vector2(0f, 0.38f),
            }, 9, true, 300f), Mat.Cloth, new Vector3(0f, 1.32f, 0.04f), new Vector3(16f, 210f, 0f));
            ArtLibrary.Tint(hood.GetComponent<Renderer>(), new Color(0.16f, 0.24f, 0.14f));
            MeshKit.Part(root, "Face", MeshKit.Ball(0.14f, 8, 6), Mat.Dark, new Vector3(0f, 1.46f, 0.08f));
            var eyeL = MeshKit.Part(root, "EyeL", MeshKit.Ball(0.015f, 6, 4), Mat.Glow, new Vector3(-0.045f, 1.48f, 0.2f));
            var eyeR = MeshKit.Part(root, "EyeR", MeshKit.Ball(0.015f, 6, 4), Mat.Glow, new Vector3(0.045f, 1.48f, 0.2f));
            ArtLibrary.SetGlow(eyeL.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.3f) * 2f);
            ArtLibrary.SetGlow(eyeR.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.3f) * 2f);
            MeshKit.Part(root, "Pack", MeshKit.Box(new Vector3(0.5f, 0.6f, 0.35f)), Mat.Leather, new Vector3(0f, 1.05f, -0.35f), new Vector3(-8f, 0f, 0f));
            MeshKit.Part(root, "Bedroll", MeshKit.Cylinder(0.12f, 0.12f, 0.6f, 8), Mat.Cloth, new Vector3(-0.3f, 1.4f, -0.38f), new Vector3(0f, 0f, 90f));

            // Lantern on a crook staff.
            MeshKit.Part(root, "Staff", MeshKit.Cylinder(0.025f, 0.03f, 2.1f, 6), Mat.Wood, new Vector3(0.48f, 0f, 0.15f));
            MeshKit.Part(root, "Crook", MeshKit.Box(new Vector3(0.3f, 0.04f, 0.04f)), Mat.Wood, new Vector3(0.6f, 2.05f, 0.15f));
            var lantern = MeshKit.Pivot(root, "Lantern", new Vector3(0.72f, 1.78f, 0.15f));
            MeshKit.Part(lantern, "Cage", MeshKit.Cylinder(0.09f, 0.08f, 0.22f, 6), Mat.Steel, Vector3.zero);
            AddFlame(lantern, new Vector3(0f, 0.04f, 0f), 0.45f, new Color(1f, 0.75f, 0.35f), 5f, 9f, true);

            // Trestle table of wares between him and the party.
            var table = root.position + root.forward * 0.9f;
            b.AddBox(table + Vector3.up * 0.85f, new Vector3(1.8f, 0.08f, 0.7f), Mat.Wood, root.rotation);
            b.AddBox(table + root.right * 0.75f + Vector3.up * 0.42f, new Vector3(0.08f, 0.84f, 0.6f), Mat.Wood, root.rotation);
            b.AddBox(table - root.right * 0.75f + Vector3.up * 0.42f, new Vector3(0.08f, 0.84f, 0.6f), Mat.Wood, root.rotation);
            for (int i = 0; i < 5; i++)
            {
                var p = table + root.right * (-0.6f + i * 0.3f) + Vector3.up * 0.89f;
                var flask = MeshKit.Part(root, "Ware" + i, MeshKit.Lathe("flask", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.08f, 0.02f), new Vector2(0.11f, 0.08f), new Vector2(0.1f, 0.15f),
                    new Vector2(0.04f, 0.2f), new Vector2(0.035f, 0.28f), new Vector2(0f, 0.28f),
                }, 10), Mat.Glow, root.InverseTransformPoint(p));
                ArtLibrary.SetGlow(flask.GetComponent<Renderer>(), Loot.LootInfo.PotionColor((Core.StatType)(new[] { 0, 2, 3, 4, 5 }[i])) * 1.4f);
            }
        }

        /// <summary>A rack of spare arms against a wall: swords and a spear.</summary>
        public static void WeaponRack(MeshKit.Batcher b, Vector3 floor, Quaternion facing)
        {
            Matrix4x4 T(Vector3 local, Vector3 euler) => Matrix4x4.TRS(floor + facing * local, facing * Quaternion.Euler(euler), Vector3.one);

            b.Add(MeshKit.Box(new Vector3(2.2f, 0.12f, 0.12f)), Mat.Wood, T(new Vector3(0f, 0.5f, 0f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(2.2f, 0.12f, 0.12f)), Mat.Wood, T(new Vector3(0f, 1.5f, -0.1f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(0.12f, 1.7f, 0.12f)), Mat.Wood, T(new Vector3(-1.05f, 0.85f, -0.05f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(0.12f, 1.7f, 0.12f)), Mat.Wood, T(new Vector3(1.05f, 0.85f, -0.05f), Vector3.zero));
            for (int i = 0; i < 4; i++)
            {
                float x = -0.7f + i * 0.45f;
                b.Add(MeshKit.Box(new Vector3(0.06f, 1.2f, 0.014f)), Mat.Steel, T(new Vector3(x, 1.05f, 0.05f), new Vector3(-6f, 0f, 0f)));
                b.Add(MeshKit.Box(new Vector3(0.22f, 0.035f, 0.045f)), Mat.Gold, T(new Vector3(x, 0.42f, 0.03f), new Vector3(-6f, 0f, 0f)));
            }
            b.Add(MeshKit.Cylinder(0.025f, 0.025f, 2.4f, 6), Mat.Wood, T(new Vector3(0.95f, 0f, 0.15f), new Vector3(-4f, 0f, 0f)));
            b.Add(MeshKit.Lathe("spearTip", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.25f) }, 4), Mat.Steel, T(new Vector3(0.95f, 2.4f, 0.32f), new Vector3(-4f, 0f, 0f)));
        }

        /// <summary>
        /// The carriage that carries the party down: a covered wagon on four spoked wheels,
        /// drawn by a skeletal horse with cold blue eyes. Forward is local +Z (horse end).
        /// </summary>
        public static void Carriage(Transform parent, MeshKit.Batcher b, Vector3 floor, Quaternion facing)
        {
            Matrix4x4 T(Vector3 local, Vector3 euler) => Matrix4x4.TRS(floor + facing * local, facing * Quaternion.Euler(euler), Vector3.one);
            Matrix4x4 TQ(Vector3 local, Quaternion rot) => Matrix4x4.TRS(floor + facing * local, facing * rot, Vector3.one);

            // Lathe shells spin about Y; this lays the axis along the wagon (Z) and turns
            // the open half of the shell downward, so the arc is the roof.
            var roof = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);

            // Bed and sides.
            b.Add(MeshKit.Box(new Vector3(2.2f, 0.15f, 3.8f)), Mat.Wood, T(new Vector3(0f, 1f, 0f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(0.1f, 0.6f, 3.8f)), Mat.Wood, T(new Vector3(1.05f, 1.35f, 0f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(0.1f, 0.6f, 3.8f)), Mat.Wood, T(new Vector3(-1.05f, 1.35f, 0f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(2.2f, 0.6f, 0.1f)), Mat.Wood, T(new Vector3(0f, 1.35f, 1.85f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(2.3f, 0.08f, 3.9f)), Mat.Steel, T(new Vector3(0f, 1.66f, 0f), Vector3.zero));

            // Canvas cover on hoops: half-cylinder shells.
            for (int i = 0; i < 4; i++)
            {
                float z = -1.6f + i * 1.07f;
                b.Add(MeshKit.Lathe("hoop", new[] { new Vector2(1.08f, 0f), new Vector2(1.12f, 0.06f), new Vector2(1.08f, 0.12f) }, 12, true, 180f),
                    Mat.Wood, TQ(new Vector3(0f, 1.66f, z), roof));
            }
            b.Add(MeshKit.Lathe("canvas", new[] { new Vector2(1.06f, -1.8f), new Vector2(1.06f, 1.8f) }, 12, true, 180f),
                Mat.Cloth, TQ(new Vector3(0f, 1.66f, 0f), roof));

            // Wheels: a rim, a hub and spokes.
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                float z = i < 2 ? 1.2f : -1.2f;
                var hub = new Vector3(1.2f * side, 0.62f, z);
                b.Add(MeshKit.Lathe("wheelRim", new[] { new Vector2(0.62f, -0.06f), new Vector2(0.62f, 0.06f), new Vector2(0.54f, 0.06f), new Vector2(0.54f, -0.06f), new Vector2(0.62f, -0.06f) }, 14),
                    Mat.Wood, T(hub, new Vector3(0f, 0f, 90f)));
                b.Add(MeshKit.Cylinder(0.1f, 0.1f, 0.22f, 8), Mat.Steel, T(hub - new Vector3(0.11f * side, 0f, 0f), new Vector3(0f, 0f, 90f * side)));
                for (int k = 0; k < 6; k++)
                {
                    b.Add(MeshKit.Box(new Vector3(0.05f, 1.1f, 0.05f)), Mat.Wood, T(hub, new Vector3(k * 30f, 0f, 0f)));
                }
            }

            // Shafts to the horse, and the driver's bench.
            b.Add(MeshKit.Box(new Vector3(0.08f, 0.08f, 2.6f)), Mat.Wood, T(new Vector3(0.5f, 1.05f, 3.1f), new Vector3(-6f, 0f, 0f)));
            b.Add(MeshKit.Box(new Vector3(0.08f, 0.08f, 2.6f)), Mat.Wood, T(new Vector3(-0.5f, 1.05f, 3.1f), new Vector3(-6f, 0f, 0f)));
            b.Add(MeshKit.Box(new Vector3(1.9f, 0.1f, 0.5f)), Mat.Wood, T(new Vector3(0f, 1.9f, 1.75f), Vector3.zero));

            // Rear step: where the party climbs aboard.
            b.Add(MeshKit.Box(new Vector3(1.2f, 0.1f, 0.4f)), Mat.Wood, T(new Vector3(0f, 0.45f, -2.1f), Vector3.zero));

            SkeletalHorse(b, floor + facing * new Vector3(0f, 0f, 4.3f), facing);

            // Lanterns on hooks at the front corners.
            for (int i = 0; i < 2; i++)
            {
                var hook = new GameObject("CarriageLantern").transform;
                hook.SetParent(parent, false);
                hook.position = floor + facing * new Vector3(i == 0 ? 1.15f : -1.15f, 2.3f, 1.8f);
                MeshKit.Part(hook, "Cage", MeshKit.Cylinder(0.09f, 0.08f, 0.22f, 6), Mat.Steel, Vector3.zero);
                AddFlame(hook, new Vector3(0f, 0.04f, 0f), 0.4f, new Color(1f, 0.7f, 0.35f), 4f, 7f, false);
            }
        }

        private static void SkeletalHorse(MeshKit.Batcher b, Vector3 floor, Quaternion facing)
        {
            Matrix4x4 T(Vector3 local, Vector3 euler, float s = 1f) => Matrix4x4.TRS(floor + facing * local, facing * Quaternion.Euler(euler), Vector3.one * s);

            // Spine, ribs, pelvis, neck, skull, four legs.
            b.Add(MeshKit.Box(new Vector3(0.08f, 0.08f, 1.7f)), Mat.Bone, T(new Vector3(0f, 1.55f, 0f), Vector3.zero));
            for (int i = 0; i < 5; i++)
            {
                b.Add(MeshKit.Lathe("horseRib", new[] { new Vector2(0.32f, 0f), new Vector2(0.34f, 0.03f), new Vector2(0.32f, 0.06f) }, 10, true, 220f),
                    Mat.Bone, Matrix4x4.TRS(floor + facing * new Vector3(0f, 1.55f, 0.45f - i * 0.18f),
                        // Axis along the spine, arc centred under it so the ribs hang down.
                        facing * Quaternion.Euler(0f, 0f, -110f) * Quaternion.Euler(90f, 0f, 0f), Vector3.one));
            }
            b.Add(MeshKit.Box(new Vector3(0.45f, 0.2f, 0.3f)), Mat.Bone, T(new Vector3(0f, 1.5f, -0.85f), Vector3.zero));
            b.Add(MeshKit.Box(new Vector3(0.09f, 0.09f, 0.8f)), Mat.Bone, T(new Vector3(0f, 1.9f, 1.05f), new Vector3(-50f, 0f, 0f)));
            b.Add(MeshKit.Box(new Vector3(0.2f, 0.22f, 0.6f)), Mat.Bone, T(new Vector3(0f, 2.15f, 1.5f), new Vector3(25f, 0f, 0f)));
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? 0.2f : -0.2f;
                float z = i < 2 ? 0.65f : -0.75f;
                b.Add(MeshKit.Box(new Vector3(0.07f, 1.5f, 0.07f)), Mat.Bone, T(new Vector3(side, 0.75f, z), new Vector3(i < 2 ? 4f : -6f, 0f, 0f)));
                b.Add(MeshKit.Box(new Vector3(0.12f, 0.08f, 0.16f)), Mat.Bone, T(new Vector3(side, 0.04f, z + 0.03f), Vector3.zero));
            }
            // Harness collar.
            b.Add(MeshKit.Lathe("collar", new[] { new Vector2(0.18f, 0f), new Vector2(0.22f, 0.05f), new Vector2(0.18f, 0.1f) }, 10), Mat.Leather,
                T(new Vector3(0f, 1.72f, 0.75f), new Vector3(40f, 0f, 0f)));
        }

        /// <summary>A gold rune ring on the floor: "stand here to board".</summary>
        public static void BoardingCircle(Transform parent, Vector3 center, float radius)
        {
            var root = new GameObject("BoardingCircle").transform;
            root.SetParent(parent, false);
            root.position = center + Vector3.up * 0.03f;
            var ring = MeshKit.Part(root, "Ring", MeshKit.Lathe("boardRing", new[]
            {
                new Vector2(0.94f, 0f), new Vector2(1f, 0.015f), new Vector2(0.94f, 0.03f),
            }, 48, false), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius));
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.3f) * 1.6f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var rune = MeshKit.Part(root, "Rune" + i, MeshKit.Box(new Vector3(0.12f, 0.02f, 0.35f)), Mat.Glow,
                    Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius * 0.82f), new Vector3(0f, a, 0f));
                ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.3f) * 1.2f);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = Vector3.up * 1.2f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.35f);
            light.range = radius * 2.2f;
            light.intensity = 2f;
        }

        // ------------------------------------------------------------------
        // Live props
        // ------------------------------------------------------------------

        /// <summary>
        /// A wall torch. <paramref name="outward"/> points away from the wall, into the room.
        /// </summary>
        public static Torch WallTorch(Transform parent, Vector3 wallPoint, Vector3 outward, bool shadows)
        {
            var root = new GameObject("Torch").transform;
            root.SetParent(parent, false);
            root.position = wallPoint;
            root.rotation = Quaternion.LookRotation(outward);

            MeshKit.Part(root, "Bracket", MeshKit.Box(new Vector3(0.08f, 0.25f, 0.05f)), Mat.Steel, new Vector3(0f, 0f, 0.03f));
            MeshKit.Part(root, "Arm", MeshKit.Box(new Vector3(0.04f, 0.04f, 0.25f)), Mat.Steel, new Vector3(0f, -0.05f, 0.15f));
            MeshKit.Part(root, "Shaft", MeshKit.Cylinder(0.03f, 0.04f, 0.45f, 6), Mat.Wood, new Vector3(0f, -0.15f, 0.27f), new Vector3(15f, 0f, 0f));
            MeshKit.Part(root, "Cup", MeshKit.Cylinder(0.05f, 0.08f, 0.1f, 6), Mat.Steel, new Vector3(0f, 0.25f, 0.33f), new Vector3(15f, 0f, 0f));

            return AddFlame(root, new Vector3(0f, 0.36f, 0.36f), 0.7f, new Color(1f, 0.62f, 0.32f), 7f, 10f, shadows);
        }

        /// <summary>A free-standing iron brazier. Boss rooms use cold blue fire.</summary>
        public static Torch Brazier(Transform parent, Vector3 floor, Color fire, bool shadows)
        {
            var root = new GameObject("Brazier").transform;
            root.SetParent(parent, false);
            root.position = floor;

            for (int i = 0; i < 3; i++)
            {
                MeshKit.Part(root, "Leg" + i, MeshKit.Box(new Vector3(0.06f, 1.0f, 0.06f)), Mat.Steel,
                    Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0.5f, 0.25f), new Vector3(-12f, i * 120f, 0f));
            }
            MeshKit.Part(root, "Bowl", MeshKit.Lathe("brazierBowl", new[]
            {
                new Vector2(0.1f, 0f), new Vector2(0.42f, 0.15f), new Vector2(0.5f, 0.3f), new Vector2(0.44f, 0.3f), new Vector2(0f, 0.18f),
            }, 10), Mat.Steel, new Vector3(0f, 0.95f, 0f));
            MeshKit.Part(root, "Coals", MeshKit.Cylinder(0.4f, 0.38f, 0.06f, 10), Mat.Glow, new Vector3(0f, 1.1f, 0f));
            ArtLibrary.SetGlow(root.Find("Coals").GetComponent<Renderer>(), fire * 1.4f);

            return AddFlame(root, new Vector3(0f, 1.15f, 0f), 1.6f, fire, 12f, 14f, shadows);
        }

        private static Torch AddFlame(Transform root, Vector3 at, float size, Color color, float intensity, float range, bool shadows)
        {
            var flame = MeshKit.Pivot(root, "Flame", at);
            flame.localScale = Vector3.one * size;
            var outer = MeshKit.Part(flame, "Outer", MeshKit.Lathe("flame", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.07f, 0.05f), new Vector2(0.08f, 0.12f), new Vector2(0.04f, 0.24f), new Vector2(0f, 0.32f),
            }, 6, false), Mat.Glow, Vector3.zero);
            // Keep flame emission modest and saturated: at high HDR values ACES + bloom
            // desaturate it to white, and fire stops reading as fire.
            ArtLibrary.SetGlow(outer.GetComponent<Renderer>(), new Color(color.r, color.g * 0.75f, color.b * 0.6f) * 1.6f);
            outer.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var inner = MeshKit.Part(flame, "Inner", MeshKit.Lathe("flameInner", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.04f, 0.04f), new Vector2(0.045f, 0.09f), new Vector2(0f, 0.18f),
            }, 6, false), Mat.Glow, new Vector3(0f, 0.01f, 0f));
            ArtLibrary.SetGlow(inner.GetComponent<Renderer>(), Color.Lerp(color, new Color(1f, 0.9f, 0.6f), 0.5f) * 2.4f);
            inner.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = at + Vector3.up * 0.25f;
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowNearPlane = 0.3f;

            var torch = root.gameObject.AddComponent<Torch>();
            torch.Light = light;
            torch.Flame = flame;
            torch.BaseIntensity = intensity;
            return torch;
        }
    }
}
