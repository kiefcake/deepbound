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
            }, 9, true, 300f), Mat.Cloth, new Vector3(0f, 1.32f, 0.04f), new Vector3(16f, 30f, 0f));
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
            // His wares: two healing draughts, a bowl of embers, a sigil tablet and a charm.
            Vector3 Local(float along) => root.InverseTransformPoint(table + root.right * along + Vector3.up * 0.89f);
            foreach (float along in new[] { -0.65f, -0.4f })
            {
                var flask = MeshKit.Part(root, "Draught" + along, MeshKit.Lathe("flask", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.08f, 0.02f), new Vector2(0.11f, 0.08f), new Vector2(0.1f, 0.15f),
                    new Vector2(0.04f, 0.2f), new Vector2(0.035f, 0.28f), new Vector2(0f, 0.28f),
                }, 10), Mat.Glow, Local(along));
                ArtLibrary.SetGlow(flask.GetComponent<Renderer>(), new Color(0.95f, 0.15f, 0.15f) * 1.4f);
            }
            MeshKit.Part(root, "EmberBowl", MeshKit.Lathe("emberBowl", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.14f, 0.01f), new Vector2(0.18f, 0.09f), new Vector2(0f, 0.06f),
            }, 10), Mat.Wood, Local(-0.05f));
            for (int i = 0; i < 4; i++)
            {
                var ember = MeshKit.Part(root, "BowlEmber" + i, MeshKit.Ball(0.04f, 5, 4), Mat.Glow, Local(-0.05f) + new Vector3(Mathf.Cos(i * 1.6f) * 0.07f, 0.08f, Mathf.Sin(i * 1.6f) * 0.07f));
                ArtLibrary.SetGlow(ember.GetComponent<Renderer>(), Loot.LootInfo.EmberColor * 1.6f);
            }
            MeshKit.Part(root, "SigilTablet", MeshKit.Box(new Vector3(0.18f, 0.24f, 0.05f)), Mat.StoneWall, Local(0.3f) + new Vector3(0f, 0.12f, 0f), new Vector3(-12f, 0f, 0f));
            var glyph = MeshKit.Part(root, "SigilGlyph", MeshKit.Box(new Vector3(0.04f, 0.16f, 0.06f)), Mat.Glow, Local(0.3f) + new Vector3(0f, 0.12f, 0.005f), new Vector3(-12f, 0f, 0f));
            ArtLibrary.SetGlow(glyph.GetComponent<Renderer>(), Loot.LootInfo.SigilColor * 1.6f);
            MeshKit.Part(root, "Charm", MeshKit.Cylinder(0.08f, 0.08f, 0.03f, 12), Mat.Gold, Local(0.6f) + new Vector3(0f, 0.02f, 0f));
            var stone = MeshKit.Part(root, "CharmStone", MeshKit.Ball(0.05f, 8, 6), Mat.Glow, Local(0.6f) + new Vector3(0f, 0.05f, 0f));
            ArtLibrary.SetGlow(stone.GetComponent<Renderer>(), new Color(0.45f, 0.7f, 1f) * 1.6f);
            Art.FolkIdle.Attach(root, 1.36f, "Hood", "Face", "EyeL", "EyeR");
        }

        public enum Folk { Smith, Priest, Tailor, Driver }

        /// <summary>
        /// The camp's people: a simple standing figure with a bare face (no hoods), dressed by trade:
        /// the smith in a leather apron with a hammer, the priest tonsured in grey with a censer, the
        /// tailor in plum with a measuring tape, the driver in a long coat and a tall hat.
        /// </summary>
        public static void Townsfolk(Transform parent, Vector3 floor, Vector3 lookAt, Folk folk)
        {
            var root = new GameObject(folk.ToString()).transform;
            root.SetParent(parent, false);
            root.position = floor;
            var facing = lookAt - floor;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f) root.rotation = Quaternion.LookRotation(facing);
            var skin = new Color(0.78f, 0.62f, 0.5f);
            Color coat = folk == Folk.Smith ? new Color(0.35f, 0.28f, 0.22f) : folk == Folk.Priest ? new Color(0.42f, 0.42f, 0.45f)
                : folk == Folk.Tailor ? new Color(0.42f, 0.14f, 0.4f) : new Color(0.12f, 0.12f, 0.16f);
            float bulk = folk == Folk.Smith ? 1.25f : 1f;
            var body = MeshKit.Part(root, "Body", MeshKit.Lathe("folkBody", new[]
            {
                new Vector2(0.3f, 0f), new Vector2(0.28f, 0.6f), new Vector2(0.24f, 1.1f), new Vector2(0.27f, 1.35f), new Vector2(0.12f, 1.48f), new Vector2(0f, 1.5f),
            }, 10), Mat.Cloth, Vector3.zero, default, new Vector3(bulk, 1f, bulk * 0.85f));
            ArtLibrary.Tint(body.GetComponent<Renderer>(), coat);
            var head = MeshKit.Part(root, "Head", MeshKit.Ball(0.13f, 10, 8), Mat.Leather, new Vector3(0f, 1.62f, 0.02f));
            ArtLibrary.Tint(head.GetComponent<Renderer>(), skin);
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(root, "Eye", MeshKit.Ball(0.016f, 5, 4), Mat.Dark, new Vector3(s * 0.045f, 1.65f, 0.13f));
                var arm = MeshKit.Part(root, "Arm", MeshKit.Cylinder(0.06f * bulk, 0.05f, 0.62f, 6), Mat.Cloth, new Vector3(s * 0.3f * bulk, 0.8f, 0.02f), new Vector3(0f, 0f, s * 6f));
                ArtLibrary.Tint(arm.GetComponent<Renderer>(), coat * 0.9f);
                var hand = MeshKit.Part(root, "Hand", MeshKit.Ball(0.05f, 6, 4), Mat.Leather, new Vector3(s * 0.31f * bulk, 0.78f, 0.04f));
                ArtLibrary.Tint(hand.GetComponent<Renderer>(), skin);
            }
            switch (folk)
            {
                case Folk.Smith:
                    var apron = MeshKit.Part(root, "Apron", MeshKit.Box(new Vector3(0.46f, 0.8f, 0.03f)), Mat.Leather, new Vector3(0f, 0.85f, 0.3f));
                    ArtLibrary.Tint(apron.GetComponent<Renderer>(), new Color(0.3f, 0.2f, 0.12f));
                    var beard = MeshKit.Part(root, "Beard", MeshKit.Ball(0.1f, 8, 6), Mat.Cloth, new Vector3(0f, 1.53f, 0.09f), default, new Vector3(1f, 1.2f, 0.8f));
                    ArtLibrary.Tint(beard.GetComponent<Renderer>(), new Color(0.35f, 0.22f, 0.12f));
                    MeshKit.Part(root, "Haft", MeshKit.Cylinder(0.025f, 0.025f, 0.6f, 6), Mat.Wood, new Vector3(0.4f, 0.45f, 0.1f));
                    MeshKit.Part(root, "HammerHead", MeshKit.Box(new Vector3(0.18f, 0.1f, 0.1f)), Mat.Steel, new Vector3(0.4f, 1.05f, 0.1f));
                    break;
                case Folk.Priest:
                    // A tonsure: a ring of grey hair round a bare crown.
                    var hair = MeshKit.Part(root, "Tonsure", MeshKit.Cylinder(0.135f, 0.135f, 0.06f, 12), Mat.Cloth, new Vector3(0f, 1.66f, 0.01f));
                    ArtLibrary.Tint(hair.GetComponent<Renderer>(), new Color(0.65f, 0.65f, 0.62f));
                    var sash = MeshKit.Part(root, "Stole", MeshKit.Box(new Vector3(0.1f, 0.9f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.95f, 0.27f));
                    ArtLibrary.Tint(sash.GetComponent<Renderer>(), new Color(0.75f, 0.6f, 0.25f));
                    MeshKit.Part(root, "Chain", MeshKit.Cylinder(0.008f, 0.008f, 0.35f, 4), Mat.Gold, new Vector3(0.34f, 0.6f, 0.08f));
                    var censer = MeshKit.Part(root, "Censer", MeshKit.Ball(0.06f, 8, 6), Mat.Gold, new Vector3(0.34f, 0.4f, 0.08f));
                    break;
                case Folk.Tailor:
                    var bun = MeshKit.Part(root, "Hair", MeshKit.Ball(0.11f, 8, 6), Mat.Cloth, new Vector3(0f, 1.7f, -0.06f));
                    ArtLibrary.Tint(bun.GetComponent<Renderer>(), new Color(0.12f, 0.08f, 0.06f));
                    var tape = MeshKit.Part(root, "Tape", MeshKit.Box(new Vector3(0.03f, 0.7f, 0.02f)), Mat.Cloth, new Vector3(-0.12f, 1.1f, 0.25f), new Vector3(0f, 0f, 8f));
                    ArtLibrary.Tint(tape.GetComponent<Renderer>(), new Color(0.9f, 0.82f, 0.4f));
                    break;
                default:
                    var brim = MeshKit.Part(root, "Brim", MeshKit.Cylinder(0.2f, 0.2f, 0.02f, 12), Mat.Cloth, new Vector3(0f, 1.74f, 0f));
                    var crown = MeshKit.Part(root, "Hat", MeshKit.Cylinder(0.12f, 0.13f, 0.28f, 12), Mat.Cloth, new Vector3(0f, 1.75f, 0f));
                    ArtLibrary.Tint(brim.GetComponent<Renderer>(), new Color(0.08f, 0.08f, 0.1f));
                    ArtLibrary.Tint(crown.GetComponent<Renderer>(), new Color(0.08f, 0.08f, 0.1f));
                    MeshKit.Part(root, "Whip", MeshKit.Cylinder(0.01f, 0.006f, 1.1f, 4), Mat.Leather, new Vector3(0.32f, 0.8f, 0.2f), new Vector3(30f, 0f, 0f));
                    break;
            }
            Art.FolkIdle.Attach(root, 1.52f, "Head", "Eye", "Beard", "Tonsure", "Hair", "Brim", "Hat");
        }

        /// <summary>A crouching gargoyle on a plinth: hunched body, folded wings, horned head.</summary>
        public static void Gargoyle(MeshKit.Batcher b, Vector3 floor, Quaternion facing)
        {
            Matrix4x4 T(Vector3 local, Vector3 euler, Vector3 scale) => Matrix4x4.TRS(floor + facing * local, facing * Quaternion.Euler(euler), scale);
            b.Add(MeshKit.Box(new Vector3(0.9f, 1.1f, 0.9f)), Mat.StoneWall, T(new Vector3(0f, 0.55f, 0f), Vector3.zero, Vector3.one));
            b.Add(MeshKit.Box(new Vector3(1f, 0.12f, 1f)), Mat.StoneWall, T(new Vector3(0f, 1.15f, 0f), Vector3.zero, Vector3.one));
            b.Add(MeshKit.Ball(0.38f, 8, 6), Mat.StoneWall, T(new Vector3(0f, 1.55f, 0f), new Vector3(20f, 0f, 0f), new Vector3(1f, 0.9f, 1.25f)));
            b.Add(MeshKit.Ball(0.2f, 8, 6), Mat.StoneWall, T(new Vector3(0f, 1.92f, 0.28f), Vector3.zero, new Vector3(1f, 0.9f, 1.15f)));
            foreach (float s in new[] { -1f, 1f })
            {
                b.Add(MeshKit.Lathe("gargHorn", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.18f) }, 5), Mat.StoneWall, T(new Vector3(0.1f * s, 2.06f, 0.22f), new Vector3(-30f, 0f, -25f * s), Vector3.one));
                b.Add(MeshKit.Box(new Vector3(0.06f, 0.7f, 0.5f)), Mat.StoneWall, T(new Vector3(0.36f * s, 1.75f, -0.15f), new Vector3(-25f, 0f, 22f * s), Vector3.one));
                b.Add(MeshKit.Box(new Vector3(0.14f, 0.3f, 0.16f)), Mat.StoneWall, T(new Vector3(0.22f * s, 1.32f, 0.3f), new Vector3(15f, 0f, 0f), Vector3.one));
            }
        }

        /// <summary>A standing iron candelabra: a tall stem and a crown of candles.</summary>
        public static void Candelabra(Transform parent, MeshKit.Batcher b, Vector3 floor, Color fire)
        {
            b.Add(MeshKit.Cylinder(0.25f, 0.18f, 0.12f, 8), Mat.Steel, Matrix4x4.Translate(floor));
            b.Add(MeshKit.Cylinder(0.04f, 0.035f, 1.9f, 6), Mat.Steel, Matrix4x4.Translate(floor + Vector3.up * 0.1f));
            b.Add(MeshKit.Cylinder(0.32f, 0.32f, 0.04f, 10), Mat.Steel, Matrix4x4.Translate(floor + Vector3.up * 1.95f));
            for (int i = 0; i < 5; i++)
            {
                var p = floor + Vector3.up * 2f + (i == 0 ? Vector3.zero : Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 0.25f);
                b.Add(MeshKit.Cylinder(0.03f, 0.03f, 0.16f + (i == 0 ? 0.06f : 0f), 6), Mat.Bone, Matrix4x4.Translate(p));
                b.Add(MeshKit.Ball(0.025f, 5, 4), Mat.Flame, Matrix4x4.Translate(p + Vector3.up * (0.2f + (i == 0 ? 0.06f : 0f))));
            }
            var l = new GameObject("CandelabraLight").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.position = floor + Vector3.up * 2.3f;
            l.type = LightType.Point;
            l.color = fire;
            l.range = 5f;
            l.intensity = 1.4f;
            l.shadows = LightShadows.None;
        }

        /// <summary>
        /// A gothic lancet window set into a wall: stone frame, a pointed head, and panes of
        /// coloured glass glowing as if lit from beyond, throwing coloured light into the room.
        /// </summary>
        public static void StainedGlass(Transform parent, MeshKit.Batcher b, Vector3 wallFace, Vector3 inward, float height, int seed)
        {
            var rot = Quaternion.LookRotation(inward);
            var right = rot * Vector3.right;
            var center = wallFace + inward * 0.05f + Vector3.up * height;
            b.AddBox(center - inward * 0.05f, new Vector3(1.5f, 3.4f, 0.12f), Mat.StoneWall, rot);
            b.AddBox(center + Vector3.up * 1.9f + right * -0.35f - inward * 0.05f, new Vector3(0.9f, 0.18f, 0.14f), Mat.StoneWall, rot * Quaternion.Euler(0f, 0f, 38f));
            b.AddBox(center + Vector3.up * 1.9f + right * 0.35f - inward * 0.05f, new Vector3(0.9f, 0.18f, 0.14f), Mat.StoneWall, rot * Quaternion.Euler(0f, 0f, -38f));
            var palette = new[] { new Color(0.75f, 0.12f, 0.12f), new Color(0.15f, 0.25f, 0.8f), new Color(0.85f, 0.65f, 0.15f), new Color(0.2f, 0.55f, 0.3f), new Color(0.5f, 0.2f, 0.65f) };
            var rng = new System.Random(seed);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 2; x++)
                {
                    var pane = MeshKit.Part(parent, "Pane", MeshKit.Box(new Vector3(0.55f, 0.55f, 0.03f)), Mat.Glow,
                        center + right * (x - 0.5f) * 0.6f + Vector3.up * (-1.25f + y * 0.62f) + inward * 0.03f, rot.eulerAngles);
                    ArtLibrary.SetGlow(pane.GetComponent<Renderer>(), palette[rng.Next(palette.Length)] * 1.1f);
                }
            var top = MeshKit.Part(parent, "PaneTop", MeshKit.Box(new Vector3(0.5f, 0.5f, 0.03f)), Mat.Glow, center + Vector3.up * 1.7f + inward * 0.03f, (rot * Quaternion.Euler(0f, 0f, 45f)).eulerAngles);
            ArtLibrary.SetGlow(top.GetComponent<Renderer>(), palette[rng.Next(palette.Length)] * 1.1f);
            // Lead cames between the panes.
            b.AddBox(center + inward * 0.05f, new Vector3(0.05f, 3.2f, 0.04f), Mat.Dark, rot);
            var l = new GameObject("GlassLight").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.position = center + inward * 1.6f;
            l.type = LightType.Point;
            l.color = palette[rng.Next(palette.Length)];
            l.range = 6f;
            l.intensity = 1.2f;
            l.shadows = LightShadows.None;
        }

        /// <summary>Cobweb strands across an upper corner (translucent, catches the torchlight).</summary>
        public static void Cobweb(Transform parent, Vector3 corner, Vector3 alongA, Vector3 alongB, float size)
        {
            var strand = MeshKit.Box(new Vector3(0.008f, 0.008f, 1f));
            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f;
                var dir = (alongA * (1f - t) + alongB * t + Vector3.down * 0.6f).normalized;
                var s = MeshKit.Part(parent, "Web", strand, Mat.Smoke, corner + dir * size * 0.5f, Quaternion.LookRotation(dir).eulerAngles, new Vector3(1f, 1f, size));
                ArtLibrary.Tint(s.GetComponent<Renderer>(), new Color(0.75f, 0.75f, 0.72f, 0.45f));
            }
            for (int ring = 1; ring <= 3; ring++)
            {
                float r = size * ring / 3.5f;
                var a = corner + (alongA + Vector3.down * 0.6f).normalized * r;
                var c = corner + (alongB + Vector3.down * 0.6f).normalized * r;
                var mid = (a + c) / 2f;
                var s = MeshKit.Part(parent, "WebRing", strand, Mat.Smoke, mid, Quaternion.LookRotation(c - a).eulerAngles, new Vector3(1f, 1f, Vector3.Distance(a, c)));
                ArtLibrary.Tint(s.GetComponent<Renderer>(), new Color(0.75f, 0.75f, 0.72f, 0.4f));
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

        /// <summary>An iron ring chandelier on a chain, with candle flames around its rim.</summary>
        public static void Chandelier(Transform parent, MeshKit.Batcher b, Vector3 at, float chainLength, Color fire)
        {
            b.Add(MeshKit.Cylinder(0.03f, 0.03f, chainLength, 4), Mat.Steel, Matrix4x4.Translate(at));
            b.Add(MeshKit.Lathe("chandelierRing", new[] { new Vector2(1.1f, 0f), new Vector2(1.2f, 0.06f), new Vector2(1.1f, 0.12f) }, 16), Mat.Steel, Matrix4x4.Translate(at));
            var root = new GameObject("Chandelier").transform;
            root.SetParent(parent, false);
            root.position = at;
            for (int i = 0; i < 6; i++)
            {
                var p = Quaternion.Euler(0f, i * 60f, 0f) * new Vector3(0f, 0.12f, 1.15f);
                MeshKit.Part(root, "Candle" + i, MeshKit.Cylinder(0.04f, 0.04f, 0.18f, 6), Mat.Bone, p);
            }
            for (int i = 0; i < 6; i++)
            {
                var wick = MeshKit.Part(root, "Wick" + i, MeshKit.Ball(0.03f, 6, 4), Mat.Glow, Quaternion.Euler(0f, i * 60f, 0f) * new Vector3(0f, 0.33f, 1.15f));
                ArtLibrary.SetGlow(wick.GetComponent<Renderer>(), fire * 1.8f);
            }
            // One light for the whole ring, at its centre (an invisible flame drives the flicker).
            AddFlame(root, new Vector3(0f, 0.2f, 0f), 0.01f, fire, 9f, 16f, false);
        }

        /// <summary>A long cloth banner hanging from a wall rod.</summary>
        public static void Banner(Transform parent, Vector3 at, Vector3 outward, Color color)
        {
            var root = new GameObject("Banner").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(at, Quaternion.LookRotation(outward));
            MeshKit.Part(root, "Rod", MeshKit.Cylinder(0.03f, 0.03f, 1.45f, 8), Mat.Wood, new Vector3(-0.725f, 0f, 0.1f), new Vector3(0f, 0f, -90f));
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(root, "Finial", MeshKit.Ball(0.05f, 8, 6), Mat.Gold, new Vector3(s * 0.74f, 0f, 0.1f));
                var tassel = MeshKit.Part(root, "Tassel", MeshKit.Cylinder(0.03f, 0.012f, 0.28f, 6), Mat.Cloth, new Vector3(s * 0.74f, -0.3f, 0.1f));
                ArtLibrary.Tint(tassel.GetComponent<Renderer>(), new Color(0.85f, 0.65f, 0.25f));
            }
            // The cloth hangs from a pivot at the rod so it can stir.
            var hang = MeshKit.Pivot(root, "Hang", new Vector3(0f, 0f, 0.1f));
            var cloth = MeshKit.Part(hang, "Cloth", MeshKit.Box(new Vector3(1.2f, 2.3f, 0.03f)), Mat.Cloth, new Vector3(0f, -1.15f, 0f));
            ArtLibrary.Tint(cloth.GetComponent<Renderer>(), color);
            // Swallowtail: two hanging points below the body.
            foreach (float s in new[] { -1f, 1f })
            {
                var tail = MeshKit.Part(hang, "Tail", MeshKit.Box(new Vector3(0.6f, 0.6f, 0.03f)), Mat.Cloth, new Vector3(s * 0.3f, -2.3f, 0f), new Vector3(0f, 0f, 45f));
                tail.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
                ArtLibrary.Tint(tail.GetComponent<Renderer>(), color * 0.95f);
            }
            var gold = new Color(0.9f, 0.7f, 0.3f);
            MeshKit.Part(hang, "TrimTop", MeshKit.Box(new Vector3(1.22f, 0.1f, 0.04f)), Mat.Gold, new Vector3(0f, -0.08f, 0f));
            foreach (float s in new[] { -1f, 1f })
                MeshKit.Part(hang, "TrimSide", MeshKit.Box(new Vector3(0.05f, 2.25f, 0.04f)), Mat.Gold, new Vector3(s * 0.585f, -1.15f, 0f));
            // The emblem: a gold skull over crossed bones, the arms of Cathedra.
            MeshKit.Part(hang, "EmblemSkull", MeshKit.Ball(0.16f, 10, 8), Mat.Gold, new Vector3(0f, -0.95f, 0.025f), default, new Vector3(1f, 1.05f, 0.35f));
            MeshKit.Part(hang, "EmblemJaw", MeshKit.Box(new Vector3(0.16f, 0.07f, 0.04f)), Mat.Gold, new Vector3(0f, -1.13f, 0.025f));
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(hang, "EmblemEye", MeshKit.Ball(0.04f, 6, 4), Mat.Dark, new Vector3(s * 0.06f, -0.93f, 0.06f), default, new Vector3(1f, 1f, 0.4f));
                MeshKit.Part(hang, "EmblemBone", MeshKit.Box(new Vector3(0.62f, 0.05f, 0.03f)), Mat.Gold, new Vector3(0f, -1.42f, 0.02f), new Vector3(0f, 0f, s * 32f));
            }
            hang.gameObject.AddComponent<BannerSway>();
        }

        /// <summary>A cluster of melted candles with one shared light.</summary>
        public static void Candles(Transform parent, Vector3 floor, Color fire)
        {
            var root = new GameObject("Candles").transform;
            root.SetParent(parent, false);
            root.position = floor;
            var rng = new System.Random(Mathf.RoundToInt(floor.x * 31f + floor.z * 17f));
            for (int i = 0; i < 5; i++)
            {
                var p = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.6f;
                float hgt = 0.12f + (float)rng.NextDouble() * 0.2f;
                MeshKit.Part(root, "Candle" + i, MeshKit.Cylinder(0.035f, 0.035f, hgt, 6), Mat.Bone, p);
                var f = MeshKit.Part(root, "Wick" + i, MeshKit.Ball(0.025f, 6, 4), Mat.Glow, p + Vector3.up * (hgt + 0.03f));
                ArtLibrary.SetGlow(f.GetComponent<Renderer>(), fire * 1.6f);
            }
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = Vector3.up * 0.5f;
            light.type = LightType.Point;
            light.color = fire;
            light.range = 6f;
            light.intensity = 2.2f;
            root.gameObject.AddComponent<Torch>().Light = light;
            root.GetComponent<Torch>().BaseIntensity = 2.2f;
        }

        // ------------------------------------------------------------------
        // Live props
        // ------------------------------------------------------------------

        /// <summary>
        /// A wall torch. <paramref name="outward"/> points away from the wall, into the room.
        /// </summary>
        public static Torch WallTorch(Transform parent, Vector3 wallPoint, Vector3 outward, bool shadows, Color? fire = null)
        {
            var root = new GameObject("Torch").transform;
            root.SetParent(parent, false);
            root.position = wallPoint;
            root.rotation = Quaternion.LookRotation(outward);

            MeshKit.Part(root, "Bracket", MeshKit.Box(new Vector3(0.08f, 0.25f, 0.05f)), Mat.Steel, new Vector3(0f, 0f, 0.03f));
            MeshKit.Part(root, "Arm", MeshKit.Box(new Vector3(0.04f, 0.04f, 0.25f)), Mat.Steel, new Vector3(0f, -0.05f, 0.15f));
            MeshKit.Part(root, "Shaft", MeshKit.Cylinder(0.03f, 0.04f, 0.45f, 6), Mat.Wood, new Vector3(0f, -0.15f, 0.27f), new Vector3(15f, 0f, 0f));
            MeshKit.Part(root, "Cup", MeshKit.Cylinder(0.05f, 0.08f, 0.1f, 6), Mat.Steel, new Vector3(0f, 0.25f, 0.33f), new Vector3(15f, 0f, 0f));

            return AddFlame(root, new Vector3(0f, 0.36f, 0.36f), 0.7f, fire ?? new Color(1f, 0.62f, 0.32f), 7f, 10f, shadows);
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
            // Flames keep their colour: a pale fire bleaches to white under bloom, so saturate it first.
            Color.RGBToHSV(color, out float fh, out float fs, out float fv);
            var hot = Color.HSVToRGB(fh, Mathf.Max(fs, 0.82f), 1f);
            var outer = MeshKit.Part(flame, "Outer", MeshKit.Lathe("flame", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.07f, 0.05f), new Vector2(0.08f, 0.12f), new Vector2(0.04f, 0.24f), new Vector2(0f, 0.32f),
            }, 6, false), Mat.Glow, Vector3.zero);
            // Keep flame emission modest and saturated: at high HDR values ACES + bloom
            // desaturate it to white, and fire stops reading as fire.
            ArtLibrary.SetGlow(outer.GetComponent<Renderer>(), hot * 1.25f);
            outer.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var inner = MeshKit.Part(flame, "Inner", MeshKit.Lathe("flameInner", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.04f, 0.04f), new Vector2(0.045f, 0.09f), new Vector2(0f, 0.18f),
            }, 6, false), Mat.Glow, new Vector3(0f, 0.01f, 0f));
            ArtLibrary.SetGlow(inner.GetComponent<Renderer>(), Color.Lerp(hot, Color.white, 0.35f) * 1.6f);
            inner.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Tongues: three thinner licks round the core, each flickering on its own (see Torch).
            for (int i = 0; i < 3; i++)
            {
                var tongue = MeshKit.Pivot(flame, "Tongue" + i, Quaternion.Euler(0f, i * 120f + 30f, 0f) * new Vector3(0f, 0f, 0.035f), new Vector3(0f, i * 120f + 30f, 0f));
                var lick = MeshKit.Part(tongue, "Lick", MeshKit.Lathe("flame", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.07f, 0.05f), new Vector2(0.08f, 0.12f), new Vector2(0.04f, 0.24f), new Vector2(0f, 0.32f),
                }, 6, false), Mat.Glow, Vector3.zero, new Vector3(14f, 0f, 0f), new Vector3(0.45f, 0.75f, 0.45f));
                ArtLibrary.SetGlow(lick.GetComponent<Renderer>(), Color.Lerp(hot, hot * hot, 0.5f) * 1.2f);
                lick.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

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
