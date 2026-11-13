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

            // Plank seams on each face, a cross-brace on two of them and iron corner caps: a plain box read as a block.
            float seam = size * 0.025f;
            for (int f = 0; f < 4; f++)
            {
                var fr = rot * Quaternion.Euler(0f, f * 90f, 0f);
                var face = c + fr * new Vector3(0f, 0f, h + 0.004f);
                for (int k = -1; k <= 1; k += 2)
                    b.AddBox(face + Vector3.up * (k * size * 0.17f), new Vector3(size - 2f * t, seam, 0.01f), Mat.Dark, fr);
                if ((f & 1) == 0)
                    b.AddBox(face + fr * new Vector3(0f, 0f, 0.012f), new Vector3((size - 2f * t) * 1.32f, t * 0.7f, 0.02f), Mat.Wood, fr * Quaternion.Euler(0f, 0f, 45f));
            }
            float cap = t * 1.25f;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1;
                b.AddBox(c + rot * new Vector3(sx * (h - cap / 2f + 0.02f), h - cap / 2f + 0.02f, sz * (h - cap / 2f + 0.02f)), Vector3.one * cap, Mat.Steel, rot);
            }
        }

        public static void Barrel(MeshKit.Batcher b, Vector3 floor)
        {
            StavedBarrel((mesh, mat, pos, yaw) => b.Add(mesh, mat, Matrix4x4.TRS(floor + pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one)),
                1f, 0.32f, 0.42f, new[] { 0.22f, 0.72f }, (int)(floor.x * 7f + floor.z * 13f));
        }

        /// <summary>
        /// A coopered barrel (2026-11-13 asset pass; it was one smooth lathe): twelve staves with dark seams between them over a
        /// dark core, iron hoops with rivets, a sunken lid of planks with a bung. <paramref name="add"/> places one part
        /// (mesh, material, local position, yaw), so it serves the static batcher and loose props alike.
        /// </summary>
        public static void StavedBarrel(System.Action<Mesh, string, Vector3, float> add, float height, float endR, float bellyR, float[] hoops, int seed)
        {
            const int Staves = 12;
            float step = 360f / Staves;
            var profile = new[]
            {
                new Vector2(endR, 0f), new Vector2(Mathf.Lerp(endR, bellyR, 0.8f), height * 0.3f), new Vector2(bellyR, height * 0.5f),
                new Vector2(Mathf.Lerp(endR, bellyR, 0.8f), height * 0.7f), new Vector2(endR, height),
            };
            string key = "stave" + height.ToString("F2") + endR.ToString("F2") + bellyR.ToString("F2");
            var stave = MeshKit.Lathe(key, profile, 3, false, step - 2.2f);
            var core = new Vector2[profile.Length + 2];
            core[0] = new Vector2(0f, 0.01f);
            for (int i = 0; i < profile.Length; i++) core[i + 1] = new Vector2(profile[i].x * 0.965f, Mathf.Clamp(profile[i].y, 0.01f, height - 0.01f));
            core[core.Length - 1] = new Vector2(0f, height - 0.01f);
            add(MeshKit.Lathe(key + "core", core, 12), Mat.Dark, Vector3.zero, 0f);
            float twist = (seed & 7) * 3.7f;
            for (int i = 0; i < Staves; i++) add(stave, Mat.Wood, Vector3.zero, twist + i * step);
            float RadiusAt(float y)
            {
                for (int i = 0; i < profile.Length - 1; i++)
                    if (y <= profile[i + 1].y) return Mathf.Lerp(profile[i].x, profile[i + 1].x, Mathf.InverseLerp(profile[i].y, profile[i + 1].y, y));
                return endR;
            }
            foreach (float hy in hoops)
            {
                float y = hy * height, r = RadiusAt(y) + 0.012f;
                add(MeshKit.Cylinder(r, r, 0.05f, 16), Mat.Steel, new Vector3(0f, y - 0.025f, 0f), 0f);
                for (int k = 0; k < 4; k++)
                {
                    float a = (twist + 45f + k * 90f) * Mathf.Deg2Rad;
                    add(MeshKit.Box(new Vector3(0.022f, 0.022f, 0.022f)), Mat.Steel, new Vector3(Mathf.Sin(a) * (r + 0.006f), y, Mathf.Cos(a) * (r + 0.006f)), 0f);
                }
            }
            // The lid sits a little down inside the chime, planked, with a bung.
            float lidY = height - 0.035f, lidR = endR * 0.93f;
            add(MeshKit.Cylinder(lidR, lidR, 0.02f, 14), Mat.Wood, new Vector3(0f, lidY - 0.01f, 0f), twist);
            for (int k = -1; k <= 1; k += 2)
                add(MeshKit.Box(new Vector3(lidR * 1.8f, 0.012f, 0.012f)), Mat.Dark, new Vector3(0f, lidY + 0.004f, 0f) + Quaternion.Euler(0f, twist, 0f) * new Vector3(0f, 0f, k * lidR * 0.33f), twist);
            add(MeshKit.Cylinder(0.035f, 0.03f, 0.03f, 8), Mat.Wood, new Vector3(lidR * 0.45f, lidY + 0.01f, 0f), 0f);
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
            // A travelling pack: a rounded leather sack with a buckled flap, shoulder straps, a side
            // pouch and a dented pot, and a bedroll strapped across the top.
            var pack = MeshKit.Pivot(root, "Pack", new Vector3(0f, 1.0f, -0.4f), new Vector3(-6f, 0f, 0f));
            var leather = new Color(0.42f, 0.29f, 0.17f);
            ArtLibrary.Tint(MeshKit.Part(pack, "Sack", MeshKit.Ball(0.5f, 12, 10), Mat.Leather, Vector3.zero, default, new Vector3(0.46f, 0.56f, 0.3f)).GetComponent<Renderer>(), leather);
            ArtLibrary.Tint(MeshKit.Part(pack, "Flap", MeshKit.Ball(0.5f, 12, 8), Mat.Leather, new Vector3(0f, 0.17f, -0.03f), new Vector3(-12f, 0f, 0f), new Vector3(0.44f, 0.2f, 0.3f)).GetComponent<Renderer>(), leather * 0.8f);
            MeshKit.Part(pack, "Buckle", MeshKit.Box(new Vector3(0.06f, 0.05f, 0.02f)), Mat.Gold, new Vector3(0f, 0.09f, -0.16f));
            ArtLibrary.Tint(MeshKit.Part(pack, "Pouch", MeshKit.Ball(0.5f, 8, 6), Mat.Leather, new Vector3(0.22f, -0.08f, 0f), default, new Vector3(0.12f, 0.2f, 0.16f)).GetComponent<Renderer>(), leather * 0.9f);
            MeshKit.Part(pack, "Pot", MeshKit.Cylinder(0.07f, 0.065f, 0.09f, 10), Mat.Steel, new Vector3(-0.24f, -0.12f, 0.02f), new Vector3(0f, 0f, 90f));
            foreach (float sx in new[] { -0.12f, 0.12f })
                ArtLibrary.Tint(MeshKit.Part(pack, "Strap", MeshKit.Box(new Vector3(0.045f, 0.5f, 0.02f)), Mat.Leather, new Vector3(sx, 0.08f, 0.17f), new Vector3(-8f, 0f, 0f)).GetComponent<Renderer>(), leather * 0.6f);
            var roll = MeshKit.Pivot(pack, "Bedroll", new Vector3(0f, 0.33f, 0f), new Vector3(0f, 0f, 90f));
            ArtLibrary.Tint(MeshKit.Part(roll, "Roll", MeshKit.Cylinder(0.1f, 0.1f, 0.5f, 10), Mat.Cloth, new Vector3(0f, -0.25f, 0f)).GetComponent<Renderer>(), new Color(0.45f, 0.38f, 0.28f));
            foreach (float ry in new[] { -0.15f, 0.15f })
                ArtLibrary.Tint(MeshKit.Part(roll, "Tie", MeshKit.Cylinder(0.106f, 0.106f, 0.03f, 10), Mat.Leather, new Vector3(0f, ry, 0f)).GetComponent<Renderer>(), leather * 0.55f);
            MeshKit.MergeParts(pack);

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
            // His wares stay on the table when he walks off on an errand (2026-11-08: they used to walk with him).
            var wares = new GameObject("SilasWares").transform;
            wares.SetParent(root.parent, false);
            wares.SetPositionAndRotation(root.position, root.rotation);
            // His wares: two healing draughts, a bowl of embers, a sigil tablet and a charm.
            Vector3 Local(float along) => root.InverseTransformPoint(table + root.right * along + Vector3.up * 0.89f);
            foreach (float along in new[] { -0.65f, -0.4f })
            {
                var flask = MeshKit.Part(wares, "Draught" + along, MeshKit.Lathe("flask", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.08f, 0.02f), new Vector2(0.11f, 0.08f), new Vector2(0.1f, 0.15f),
                    new Vector2(0.04f, 0.2f), new Vector2(0.035f, 0.28f), new Vector2(0f, 0.28f),
                }, 10), Mat.Glow, Local(along));
                ArtLibrary.SetGlow(flask.GetComponent<Renderer>(), new Color(0.95f, 0.15f, 0.15f) * 1.4f);
            }
            MeshKit.Part(wares, "EmberBowl", MeshKit.Lathe("emberBowl", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.14f, 0.01f), new Vector2(0.18f, 0.09f), new Vector2(0f, 0.06f),
            }, 10), Mat.Wood, Local(-0.05f));
            for (int i = 0; i < 4; i++)
            {
                var ember = MeshKit.Part(wares, "BowlEmber" + i, MeshKit.Ball(0.04f, 5, 4), Mat.Glow, Local(-0.05f) + new Vector3(Mathf.Cos(i * 1.6f) * 0.07f, 0.08f, Mathf.Sin(i * 1.6f) * 0.07f));
                ArtLibrary.SetGlow(ember.GetComponent<Renderer>(), Loot.LootInfo.EmberColor * 1.6f);
            }
            MeshKit.Part(wares, "SigilTablet", MeshKit.Box(new Vector3(0.18f, 0.24f, 0.05f)), Mat.StoneWall, Local(0.3f) + new Vector3(0f, 0.12f, 0f), new Vector3(-12f, 0f, 0f));
            var glyph = MeshKit.Part(wares, "SigilGlyph", MeshKit.Box(new Vector3(0.04f, 0.16f, 0.06f)), Mat.Glow, Local(0.3f) + new Vector3(0f, 0.12f, 0.005f), new Vector3(-12f, 0f, 0f));
            ArtLibrary.SetGlow(glyph.GetComponent<Renderer>(), Loot.LootInfo.SigilColor * 1.6f);
            MeshKit.Part(wares, "Charm", MeshKit.Cylinder(0.08f, 0.08f, 0.03f, 12), Mat.Gold, Local(0.6f) + new Vector3(0f, 0.02f, 0f));
            var stone = MeshKit.Part(wares, "CharmStone", MeshKit.Ball(0.05f, 8, 6), Mat.Glow, Local(0.6f) + new Vector3(0f, 0.05f, 0f));
            ArtLibrary.SetGlow(stone.GetComponent<Renderer>(), new Color(0.45f, 0.7f, 1f) * 1.6f);
            // A little rack of pendants at the table's end (he sells them since 2026-10-20).
            var rack = root.InverseTransformPoint(table + root.right * 0.95f + Vector3.up * 0.89f);
            MeshKit.Part(wares, "PendantRackPost", MeshKit.Box(new Vector3(0.03f, 0.45f, 0.03f)), Mat.Wood, rack + new Vector3(0f, 0.22f, 0f));
            MeshKit.Part(wares, "PendantRackBar", MeshKit.Box(new Vector3(0.36f, 0.025f, 0.025f)), Mat.Wood, rack + new Vector3(0f, 0.44f, 0f));
            var gems = new[] { new Color(0.9f, 0.2f, 0.25f), new Color(0.35f, 0.9f, 0.5f), new Color(0.6f, 0.45f, 1f) };
            for (int i = 0; i < 3; i++)
            {
                var hang = rack + new Vector3(-0.13f + i * 0.13f, 0.44f, 0f);
                MeshKit.Part(wares, "PendantChain", MeshKit.Box(new Vector3(0.006f, 0.16f, 0.006f)), Mat.Gold, hang + Vector3.down * 0.08f);
                MeshKit.Part(wares, "PendantSetting", MeshKit.Cylinder(0.03f, 0.03f, 0.012f, 10), Mat.Gold, hang + Vector3.down * 0.17f, new Vector3(90f, 0f, 0f));
                var gem = MeshKit.Part(wares, "PendantGem", MeshKit.Ball(0.018f, 6, 4), Mat.Glow, hang + new Vector3(0f, -0.17f, 0.008f));
                ArtLibrary.SetGlow(gem.GetComponent<Renderer>(), gems[i] * 1.4f);
            }
            var silas = Art.FolkIdle.Attach(root, 1.36f, "Hood", "Face", "EyeL", "EyeR");
            silas.Who = World.FolkId.Silas; silas.DisplayName = "Silas Crane"; silas.Trade = "Merchant";
        }

        public enum Folk { Smith, Priest, Tailor, Driver, Sergeant, Runewright, Barkeep, Lutist, Fiddler, Drummer, Drinker }

        /// <summary>
        /// The camp's people: a simple standing figure with a bare face (no hoods), dressed by trade:
        /// the smith in a leather apron with a hammer, the priest tonsured in grey with a censer, the
        /// tailor in plum with a measuring tape, the driver in a long coat and a tall hat.
        /// </summary>
        public static Transform Townsfolk(Transform parent, Vector3 floor, Vector3 lookAt, Folk folk)
        {
            var root = new GameObject(folk.ToString()).transform;
            root.SetParent(parent, false);
            root.position = floor;
            var facing = lookAt - floor;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f) root.rotation = Quaternion.LookRotation(facing);
            var skin = new Color(0.78f, 0.62f, 0.5f);
            Color coat = folk == Folk.Smith ? new Color(0.35f, 0.28f, 0.22f) : folk == Folk.Priest ? new Color(0.42f, 0.42f, 0.45f)
                : folk == Folk.Tailor ? new Color(0.42f, 0.14f, 0.4f) : folk == Folk.Sergeant ? new Color(0.38f, 0.22f, 0.16f)
                : folk == Folk.Runewright ? new Color(0.14f, 0.2f, 0.3f) : folk == Folk.Barkeep ? new Color(0.36f, 0.16f, 0.12f)
                : folk == Folk.Lutist ? new Color(0.2f, 0.32f, 0.2f) : folk == Folk.Fiddler ? new Color(0.45f, 0.32f, 0.12f)
                : folk == Folk.Drummer ? new Color(0.22f, 0.2f, 0.32f) : folk == Folk.Drinker ? new Color(0.3f, 0.26f, 0.2f) : new Color(0.12f, 0.12f, 0.16f);
            float bulk = folk == Folk.Smith || folk == Folk.Sergeant ? 1.2f : 1f;
            FolkFigure(root, folk, coat, skin, bulk);
            foreach (float s in new[] { -1f, 1f })
            {
                if (folk == Folk.Priest)
                {
                    // Hands pressed together in prayer at the chest: upper arm down and forward, forearm up to the hands.
                    var shoulder = new Vector3(s * 0.27f, 1.3f, 0f);
                    var elbow = new Vector3(s * 0.22f, 1.02f, 0.17f);
                    var wrist = new Vector3(s * 0.035f, 1.14f, 0.28f);
                    ArtLibrary.Tint(Limb(root, "UpperArm", shoulder, elbow, 0.06f).GetComponent<Renderer>(), coat * 0.9f);
                    ArtLibrary.Tint(Limb(root, "Forearm", elbow, wrist, 0.052f).GetComponent<Renderer>(), coat * 0.9f);
                    var palm = MeshKit.Part(root, "Hand", MeshKit.Box(new Vector3(0.035f, 0.11f, 0.07f)), Mat.Bone, new Vector3(s * 0.018f, 1.2f, 0.3f), new Vector3(-10f, 0f, s * -6f));
                    ArtLibrary.Tint(palm.GetComponent<Renderer>(), skin);
                    continue;
                }
                // Arms that hang from the shoulders (they used to float below them), bent a little at the elbow.
                var sh = new Vector3(s * 0.27f * bulk, 1.39f, 0f);
                var el = new Vector3(s * 0.31f * bulk, 1.07f, 0.02f);
                var wr = new Vector3(s * 0.31f * bulk, 0.83f, 0.08f);
                ArtLibrary.Tint(Limb(root, "UpperArm", sh, el, 0.058f * bulk).GetComponent<Renderer>(), coat * 0.9f);
                ArtLibrary.Tint(Limb(root, "Forearm", el, wr, 0.05f * bulk).GetComponent<Renderer>(), coat * 0.85f);
                var cuff = MeshKit.Part(root, "Cuff", MeshKit.Cylinder(0.058f * bulk, 0.058f * bulk, 0.05f, 8), Mat.Cloth, Vector3.Lerp(el, wr, 0.85f));
                ArtLibrary.Tint(cuff.GetComponent<Renderer>(), coat * 0.6f);
                var hand = MeshKit.Part(root, "Hand", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, wr + new Vector3(0f, -0.04f, 0.01f), default, new Vector3(0.8f, 1.15f, 1f));
                ArtLibrary.Tint(hand.GetComponent<Renderer>(), skin);
                var thumb = MeshKit.Part(root, "Thumb", MeshKit.Box(new Vector3(0.02f, 0.045f, 0.02f)), Mat.Bone, wr + new Vector3(-s * 0.025f, -0.03f, 0.04f), new Vector3(-20f, 0f, s * 20f));
                ArtLibrary.Tint(thumb.GetComponent<Renderer>(), skin);
            }
            switch (folk)
            {
                case Folk.Smith:
                    var apron = MeshKit.Part(root, "Apron", MeshKit.Box(new Vector3(0.46f, 0.8f, 0.03f)), Mat.Leather, new Vector3(0f, 0.85f, 0.3f));
                    ArtLibrary.Tint(apron.GetComponent<Renderer>(), new Color(0.3f, 0.2f, 0.12f));
                    var beard = MeshKit.Part(root, "Beard", MeshKit.Ball(0.1f, 8, 6), Mat.Cloth, new Vector3(0f, 1.53f, 0.09f), default, new Vector3(1f, 1.2f, 0.8f));
                    ArtLibrary.Tint(beard.GetComponent<Renderer>(), new Color(0.35f, 0.22f, 0.12f));
                    // The hammer in his right hand (he works the anvil with it), a leather bracer on each forearm, apron straps.
                    MeshKit.Part(root, "Haft", MeshKit.Cylinder(0.022f, 0.022f, 0.5f, 6), Mat.Wood, new Vector3(0.31f * bulk, 0.79f, 0.1f), new Vector3(90f, 0f, 0f));
                    MeshKit.Part(root, "HammerHead", MeshKit.Box(new Vector3(0.17f, 0.08f, 0.09f)), Mat.Steel, new Vector3(0.31f * bulk, 0.79f, 0.58f));
                    foreach (float bs in new[] { -1f, 1f })
                    {
                        var bracer = MeshKit.Part(root, "Bracer", MeshKit.Cylinder(0.062f * bulk, 0.058f * bulk, 0.14f, 8), Mat.Leather, new Vector3(bs * 0.31f * bulk, 0.88f, 0.06f), new Vector3(-8f, 0f, 0f));
                        ArtLibrary.Tint(bracer.GetComponent<Renderer>(), new Color(0.24f, 0.15f, 0.09f));
                        var strap = MeshKit.Part(root, "ApronStrap", MeshKit.Box(new Vector3(0.04f, 0.42f, 0.02f)), Mat.Leather, new Vector3(bs * 0.12f, 1.38f, 0.21f * bulk), new Vector3(0f, 0f, bs * 10f));
                        ArtLibrary.Tint(strap.GetComponent<Renderer>(), new Color(0.3f, 0.2f, 0.12f));
                    }
                    var cap = MeshKit.Part(root, "Hair", MeshKit.Ball(0.135f, 10, 8), Mat.Cloth, new Vector3(0f, 1.67f, -0.015f), default, new Vector3(1f, 0.7f, 1f));
                    ArtLibrary.Tint(cap.GetComponent<Renderer>(), new Color(0.32f, 0.2f, 0.11f));
                    break;
                case Folk.Priest:
                    // A tonsure: a ring of grey hair round a bare crown.
                    var hair = MeshKit.Part(root, "Tonsure", MeshKit.Cylinder(0.135f, 0.135f, 0.06f, 12), Mat.Cloth, new Vector3(0f, 1.66f, 0.01f));
                    ArtLibrary.Tint(hair.GetComponent<Renderer>(), new Color(0.65f, 0.65f, 0.62f));
                    var sash = MeshKit.Part(root, "Stole", MeshKit.Box(new Vector3(0.1f, 0.9f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.95f, 0.27f));
                    ArtLibrary.Tint(sash.GetComponent<Renderer>(), new Color(0.75f, 0.6f, 0.25f));
                    // A fat purse at his belt, which says more about him than the stole.
                    var purse = MeshKit.Part(root, "Purse", MeshKit.Ball(0.07f, 8, 6), Mat.Leather, new Vector3(0.24f, 0.82f, 0.18f), default, new Vector3(1f, 1.2f, 0.9f));
                    ArtLibrary.Tint(purse.GetComponent<Renderer>(), new Color(0.45f, 0.3f, 0.15f));
                    MeshKit.Part(root, "PurseTie", MeshKit.Cylinder(0.025f, 0.025f, 0.02f, 8), Mat.Gold, new Vector3(0.24f, 0.9f, 0.18f));
                    break;
                case Folk.Sergeant:
                {
                    // A drillmaster: a dented kettle helm, a mail collar, a scar of a moustache and a wooden practice sword.
                    MeshKit.Part(root, "Helm", MeshKit.Cylinder(0.14f, 0.15f, 0.12f, 12), Mat.Steel, new Vector3(0f, 1.72f, 0f));
                    MeshKit.Part(root, "HelmBrim", MeshKit.Cylinder(0.22f, 0.22f, 0.02f, 14), Mat.Steel, new Vector3(0f, 1.67f, 0f));
                    MeshKit.Part(root, "Mail", MeshKit.Cylinder(0.2f, 0.26f, 0.14f, 10), Mat.Steel, new Vector3(0f, 1.38f, 0f));
                    var tache = MeshKit.Part(root, "Moustache", MeshKit.Box(new Vector3(0.12f, 0.025f, 0.03f)), Mat.Cloth, new Vector3(0f, 1.57f, 0.12f));
                    ArtLibrary.Tint(tache.GetComponent<Renderer>(), new Color(0.25f, 0.16f, 0.1f));
                    MeshKit.Part(root, "PracticeSword", MeshKit.Box(new Vector3(0.05f, 0.02f, 0.8f)), Mat.Wood, new Vector3(0.31f * bulk, 0.79f, 0.5f));
                    MeshKit.Part(root, "SwordGuard", MeshKit.Box(new Vector3(0.18f, 0.04f, 0.03f)), Mat.Wood, new Vector3(0.31f * bulk, 0.79f, 0.11f));
                    // A drillmaster's tabard: the castle's colours, a white chevron.
                    var tabard = MeshKit.Part(root, "Tabard", MeshKit.Box(new Vector3(0.36f, 0.62f, 0.025f)), Mat.Cloth, new Vector3(0f, 1.0f, 0.25f * bulk));
                    ArtLibrary.Tint(tabard.GetComponent<Renderer>(), new Color(0.42f, 0.08f, 0.08f));
                    var chevron = MeshKit.Part(root, "Chevron", MeshKit.Box(new Vector3(0.22f, 0.05f, 0.01f)), Mat.Cloth, new Vector3(0f, 1.12f, 0.265f * bulk));
                    ArtLibrary.Tint(chevron.GetComponent<Renderer>(), new Color(0.85f, 0.8f, 0.7f));
                    break;
                }
                case Folk.Runewright:
                {
                    // The runewright: a deep-blue robe, silver hair in a long braid, a circlet set with a cold stone,
                    // and runes glowing on her palms.
                    var hairY = MeshKit.Part(root, "Hair", MeshKit.Ball(0.135f, 10, 8), Mat.Cloth, new Vector3(0f, 1.66f, -0.02f));
                    ArtLibrary.Tint(hairY.GetComponent<Renderer>(), new Color(0.78f, 0.78f, 0.82f));
                    var braid = MeshKit.Part(root, "Braid", MeshKit.Cylinder(0.035f, 0.02f, 0.6f, 6), Mat.Cloth, new Vector3(0f, 1.3f, -0.15f));
                    ArtLibrary.Tint(braid.GetComponent<Renderer>(), new Color(0.75f, 0.75f, 0.8f));
                    MeshKit.Part(root, "Circlet", MeshKit.Cylinder(0.14f, 0.14f, 0.02f, 12), Mat.Gold, new Vector3(0f, 1.71f, 0f));
                    var stone = MeshKit.Part(root, "CircletStone", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(0f, 1.71f, 0.14f));
                    ArtLibrary.SetGlow(stone.GetComponent<Renderer>(), new Color(0.4f, 0.8f, 1f) * 1.6f);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var palm = MeshKit.Part(root, "PalmRune", MeshKit.Box(new Vector3(0.05f, 0.05f, 0.005f)), Mat.Glow, new Vector3(s * 0.31f, 0.8f, 0.09f), new Vector3(0f, 0f, 45f));
                        ArtLibrary.SetGlow(palm.GetComponent<Renderer>(), new Color(0.45f, 0.85f, 1f) * 1.5f);
                    }
                    var rsash = MeshKit.Part(root, "Sash", MeshKit.Box(new Vector3(0.08f, 0.9f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.95f, 0.27f));
                    ArtLibrary.Tint(rsash.GetComponent<Renderer>(), new Color(0.6f, 0.7f, 0.9f));
                    break;
                }
                case Folk.Barkeep:
                {
                    // Marta the alewife: copper hair in a kerchief, a white apron, a cloth over her shoulder.
                    var hairB = MeshKit.Part(root, "Hair", MeshKit.Ball(0.13f, 10, 8), Mat.Cloth, new Vector3(0f, 1.67f, -0.03f));
                    ArtLibrary.Tint(hairB.GetComponent<Renderer>(), new Color(0.55f, 0.25f, 0.1f));
                    var kerchief = MeshKit.Part(root, "Kerchief", MeshKit.Cylinder(0.14f, 0.12f, 0.07f, 12), Mat.Cloth, new Vector3(0f, 1.73f, -0.01f));
                    ArtLibrary.Tint(kerchief.GetComponent<Renderer>(), new Color(0.6f, 0.15f, 0.12f));
                    var apronB = MeshKit.Part(root, "Apron", MeshKit.Box(new Vector3(0.44f, 0.75f, 0.03f)), Mat.Cloth, new Vector3(0f, 0.82f, 0.29f));
                    ArtLibrary.Tint(apronB.GetComponent<Renderer>(), new Color(0.82f, 0.78f, 0.68f));
                    var towel = MeshKit.Part(root, "Towel", MeshKit.Box(new Vector3(0.1f, 0.35f, 0.03f)), Mat.Cloth, new Vector3(0.24f, 1.3f, 0.12f), new Vector3(0f, 0f, -10f));
                    ArtLibrary.Tint(towel.GetComponent<Renderer>(), new Color(0.75f, 0.72f, 0.62f));
                    break;
                }
                case Folk.Drinker:
                {
                    // Old Hollis, the regular: a battered felt hat, grey stubble, a patched coat, a pewter tankard.
                    var hatH = MeshKit.Part(root, "Hat", MeshKit.Cylinder(0.14f, 0.125f, 0.15f, 10), Mat.Cloth, new Vector3(0f, 1.71f, -0.01f), new Vector3(-5f, 0f, 4f));
                    ArtLibrary.Tint(hatH.GetComponent<Renderer>(), new Color(0.22f, 0.18f, 0.14f));
                    var brimH = MeshKit.Part(root, "Brim", MeshKit.Cylinder(0.22f, 0.22f, 0.02f, 12), Mat.Cloth, new Vector3(0f, 1.71f, -0.01f), new Vector3(-5f, 0f, 4f));
                    ArtLibrary.Tint(brimH.GetComponent<Renderer>(), new Color(0.19f, 0.15f, 0.12f));
                    var stubble = MeshKit.Part(root, "Beard", MeshKit.Ball(0.095f, 8, 6), Mat.Cloth, new Vector3(0f, 1.52f, 0.075f), default, new Vector3(1f, 0.75f, 0.75f));
                    ArtLibrary.Tint(stubble.GetComponent<Renderer>(), new Color(0.5f, 0.48f, 0.45f));
                    var patch = MeshKit.Part(root, "Patch", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.02f)), Mat.Cloth, new Vector3(-0.1f, 1.12f, 0.25f), new Vector3(0f, 0f, 12f));
                    ArtLibrary.Tint(patch.GetComponent<Renderer>(), new Color(0.38f, 0.3f, 0.18f));
                    var hand = new Vector3(0.31f, 0.83f, 0.08f);
                    MeshKit.Part(root, "Tankard", MeshKit.Cylinder(0.06f, 0.055f, 0.15f, 10), Mat.Steel, hand + new Vector3(0f, -0.1f, 0.07f));
                    MeshKit.Part(root, "Tankard", MeshKit.Box(new Vector3(0.02f, 0.09f, 0.02f)), Mat.Steel, hand + new Vector3(0.07f, -0.03f, 0.07f));
                    var froth = MeshKit.Part(root, "Tankard", MeshKit.Cylinder(0.052f, 0.052f, 0.015f, 10), Mat.Cloth, hand + new Vector3(0f, 0.045f, 0.07f));
                    ArtLibrary.Tint(froth.GetComponent<Renderer>(), new Color(0.85f, 0.8f, 0.65f));
                    break;
                }
                case Folk.Lutist:
                {
                    // A lute across the body: a round back, a long neck angled up to the left, a bent-back pegbox.
                    var feather = MeshKit.Part(root, "Cap", MeshKit.Cylinder(0.15f, 0.12f, 0.08f, 12), Mat.Cloth, new Vector3(0f, 1.73f, 0f));
                    ArtLibrary.Tint(feather.GetComponent<Renderer>(), new Color(0.15f, 0.25f, 0.15f));
                    MeshKit.Part(root, "Feather", MeshKit.Box(new Vector3(0.02f, 0.25f, 0.04f)), Mat.Bone, new Vector3(0.1f, 1.85f, -0.05f), new Vector3(-20f, 0f, -25f));
                    var bowl = MeshKit.Part(root, "LuteBody", MeshKit.Ball(0.19f, 12, 8), Mat.Wood, new Vector3(0.08f, 1.0f, 0.3f), new Vector3(0f, 0f, 35f), new Vector3(1f, 1.25f, 0.55f));
                    ArtLibrary.Tint(bowl.GetComponent<Renderer>(), new Color(0.55f, 0.33f, 0.15f));
                    MeshKit.Part(root, "LuteNeck", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.03f)), Mat.Wood, new Vector3(-0.17f, 1.3f, 0.32f), new Vector3(0f, 0f, 35f));
                    MeshKit.Part(root, "LutePegbox", MeshKit.Box(new Vector3(0.05f, 0.14f, 0.03f)), Mat.Wood, new Vector3(-0.33f, 1.5f, 0.27f), new Vector3(-55f, 0f, 35f));
                    MeshKit.Part(root, "LuteRose", MeshKit.Cylinder(0.04f, 0.04f, 0.005f, 10), Mat.Dark, new Vector3(0.06f, 1.03f, 0.41f), new Vector3(90f, 0f, 0f));
                    break;
                }
                case Folk.Fiddler:
                {
                    // A fiddle under the chin and a bow across it; a shock of grey hair.
                    var hairF = MeshKit.Part(root, "Hair", MeshKit.Ball(0.14f, 10, 8), Mat.Cloth, new Vector3(0f, 1.68f, -0.03f), default, new Vector3(1.1f, 0.9f, 1f));
                    ArtLibrary.Tint(hairF.GetComponent<Renderer>(), new Color(0.62f, 0.6f, 0.58f));
                    var fiddle = MeshKit.Part(root, "Fiddle", MeshKit.Ball(0.1f, 10, 6), Mat.Wood, new Vector3(-0.17f, 1.38f, 0.22f), new Vector3(10f, 30f, 60f), new Vector3(0.75f, 1.4f, 0.4f));
                    ArtLibrary.Tint(fiddle.GetComponent<Renderer>(), new Color(0.6f, 0.28f, 0.1f));
                    MeshKit.Part(root, "FiddleNeck", MeshKit.Box(new Vector3(0.025f, 0.22f, 0.02f)), Mat.Dark, new Vector3(-0.36f, 1.33f, 0.36f), new Vector3(10f, 30f, 60f));
                    MeshKit.Part(root, "Bow", MeshKit.Box(new Vector3(0.012f, 0.6f, 0.012f)), Mat.Wood, new Vector3(0.05f, 1.3f, 0.36f), new Vector3(0f, 0f, -70f));
                    break;
                }
                case Folk.Drummer:
                {
                    // A frame drum held up in the left hand, a beater in the right; a broad-brimmed hat.
                    MeshKit.Part(root, "Brim", MeshKit.Cylinder(0.24f, 0.24f, 0.02f, 14), Mat.Leather, new Vector3(0f, 1.74f, 0f));
                    MeshKit.Part(root, "Hat", MeshKit.Cylinder(0.12f, 0.13f, 0.14f, 12), Mat.Leather, new Vector3(0f, 1.8f, 0f));
                    MeshKit.Part(root, "DrumRim", MeshKit.Cylinder(0.24f, 0.24f, 0.07f, 16), Mat.Wood, new Vector3(-0.2f, 1.1f, 0.3f), new Vector3(80f, 0f, 20f));
                    var drumSkin = MeshKit.Part(root, "DrumSkin", MeshKit.Cylinder(0.23f, 0.23f, 0.075f, 16), Mat.Cloth, new Vector3(-0.2f, 1.1f, 0.31f), new Vector3(80f, 0f, 20f));
                    ArtLibrary.Tint(drumSkin.GetComponent<Renderer>(), new Color(0.85f, 0.75f, 0.55f));
                    MeshKit.Part(root, "Beater", MeshKit.Box(new Vector3(0.02f, 0.25f, 0.02f)), Mat.Wood, new Vector3(0.25f, 1.05f, 0.3f), new Vector3(-50f, 0f, 30f));
                    break;
                }
                case Folk.Tailor:
                    // Dark hair drawn back into a bun, a fringe over the brow, a shawl over the shoulders and a pincushion on her wrist.
                    var bun = MeshKit.Part(root, "Hair", MeshKit.Ball(0.11f, 8, 6), Mat.Cloth, new Vector3(0f, 1.7f, -0.09f));
                    ArtLibrary.Tint(bun.GetComponent<Renderer>(), new Color(0.16f, 0.1f, 0.07f));
                    var hairCap = MeshKit.Part(root, "HairCap", MeshKit.Ball(0.138f, 10, 8), Mat.Cloth, new Vector3(0f, 1.665f, -0.012f), default, new Vector3(1f, 0.78f, 1f));
                    ArtLibrary.Tint(hairCap.GetComponent<Renderer>(), new Color(0.16f, 0.1f, 0.07f));
                    var shawl = MeshKit.Part(root, "Shawl", MeshKit.Lathe("folkShawl", new[] { new Vector2(0.3f, 1.2f), new Vector2(0.28f, 1.36f), new Vector2(0.17f, 1.47f), new Vector2(0.12f, 1.49f) }, 12, false), Mat.Cloth, Vector3.zero, default, new Vector3(1f, 1f, 0.82f));
                    ArtLibrary.Tint(shawl.GetComponent<Renderer>(), new Color(0.75f, 0.68f, 0.5f));
                    var pins = MeshKit.Part(root, "Pincushion", MeshKit.Ball(0.045f, 8, 6), Mat.Cloth, new Vector3(-0.31f, 0.86f, 0.1f), default, new Vector3(1f, 0.7f, 1f));
                    ArtLibrary.Tint(pins.GetComponent<Renderer>(), new Color(0.65f, 0.1f, 0.12f));
                    var tape = MeshKit.Part(root, "Tape", MeshKit.Box(new Vector3(0.03f, 0.7f, 0.02f)), Mat.Cloth, new Vector3(-0.12f, 1.1f, 0.25f), new Vector3(0f, 0f, 8f));
                    ArtLibrary.Tint(tape.GetComponent<Renderer>(), new Color(0.9f, 0.82f, 0.4f));
                    break;
                default:
                    var brim = MeshKit.Part(root, "Brim", MeshKit.Cylinder(0.2f, 0.2f, 0.02f, 12), Mat.Cloth, new Vector3(0f, 1.74f, 0f));
                    var crown = MeshKit.Part(root, "Hat", MeshKit.Cylinder(0.12f, 0.13f, 0.28f, 12), Mat.Cloth, new Vector3(0f, 1.75f, 0f));
                    ArtLibrary.Tint(brim.GetComponent<Renderer>(), new Color(0.08f, 0.08f, 0.1f));
                    ArtLibrary.Tint(crown.GetComponent<Renderer>(), new Color(0.08f, 0.08f, 0.1f));
                    MeshKit.Part(root, "Whip", MeshKit.Cylinder(0.01f, 0.006f, 1.1f, 4), Mat.Leather, new Vector3(0.32f, 0.8f, 0.2f), new Vector3(30f, 0f, 0f));
                    var scarf = MeshKit.Part(root, "Scarf", MeshKit.Cylinder(0.14f, 0.12f, 0.08f, 10), Mat.Cloth, new Vector3(0f, 1.48f, 0f));
                    ArtLibrary.Tint(scarf.GetComponent<Renderer>(), new Color(0.45f, 0.1f, 0.08f));
                    break;
            }
            float neckY = 1.52f;
            if (folk == Folk.Priest)
            {
                // He kneels: everything sinks, the robe pooling on a cushion; a censer smokes on the stones beside him.
                const float kneel = 0.42f;
                foreach (Transform c in root) c.localPosition += Vector3.down * kneel;
                neckY -= kneel;
                // The robe pools round his knees, and his shins and the soles of his sandals stick out behind:
                // from any side he reads as kneeling, not as a short man standing in a hole.
                var hem = MeshKit.Part(root, "Hem", MeshKit.Ball(0.36f, 12, 6), Mat.Cloth, new Vector3(0f, 0.15f, 0.02f), default, new Vector3(1.12f, 0.3f, 1.05f));
                ArtLibrary.Tint(hem.GetComponent<Renderer>(), coat * 0.92f);
                foreach (float s in new[] { -1f, 1f })
                {
                    ArtLibrary.Tint(Limb(root, "Shin", new Vector3(s * 0.12f, 0.13f, 0.02f), new Vector3(s * 0.13f, 0.1f, -0.42f), 0.075f).GetComponent<Renderer>(), coat * 0.85f);
                    var sole = MeshKit.Part(root, "Sole", MeshKit.Box(new Vector3(0.085f, 0.13f, 0.04f)), Mat.Leather, new Vector3(s * 0.13f, 0.08f, -0.47f), new Vector3(-12f, 0f, 0f));
                    ArtLibrary.Tint(sole.GetComponent<Renderer>(), new Color(0.22f, 0.15f, 0.1f));
                }
                var cushion = MeshKit.Part(root, "Cushion", MeshKit.Box(new Vector3(0.62f, 0.1f, 0.48f)), Mat.Cloth, new Vector3(0f, 0.05f, 0.02f));
                ArtLibrary.Tint(cushion.GetComponent<Renderer>(), new Color(0.45f, 0.08f, 0.1f));
                foreach (float cx in new[] { -0.3f, 0.3f })
                    foreach (float cz in new[] { -0.22f, 0.26f })
                        MeshKit.Part(root, "Tassel", MeshKit.Ball(0.025f, 6, 4), Mat.Gold, new Vector3(cx, 0.07f, cz));
                var censerAt = new Vector3(0.55f, 0f, 0.3f);
                MeshKit.Part(root, "CenserFoot", MeshKit.Cylinder(0.06f, 0.05f, 0.05f, 8), Mat.Gold, censerAt);
                MeshKit.Part(root, "Censer", MeshKit.Lathe("censerBowl", new[] { new Vector2(0.03f, 0f), new Vector2(0.08f, 0.04f), new Vector2(0.09f, 0.1f), new Vector2(0.05f, 0.14f), new Vector2(0f, 0.16f) }, 10), Mat.Gold, censerAt + Vector3.up * 0.05f);
                var coals = MeshKit.Part(root, "Coals", MeshKit.Ball(0.03f, 6, 4), Mat.Glow, censerAt + Vector3.up * 0.2f);
                ArtLibrary.SetGlow(coals.GetComponent<Renderer>(), new Color(1f, 0.5f, 0.2f) * 1.2f);
            }
            var idle = Art.FolkIdle.Attach(root, neckY, "Head", "Eye", "Beard", "Tonsure", "Hair", "HairCap", "Brim", "Hat", "Kerchief", "Cap", "Feather",
                "EyeWhite", "Pupil", "Glint", "Cheek", "Brow", "Nose", "Mouth", "Ear", "Jaw", "Helm", "HelmBrim", "Moustache", "Circlet", "CircletStone");
            if (folk == Folk.Priest) { idle.RestPitch = 26f; idle.Prays = true; idle.PrayAt = root.TransformPoint(new Vector3(0f, 1.2f - 0.42f, 0.32f)); idle.IncenseAt = root.TransformPoint(new Vector3(0.55f, 0.25f, 0.3f)); }
            // Who they are (names over heads, greetings) and what they do while nobody is at the counter.
            (idle.Who, idle.DisplayName, idle.Trade, idle.Work) = folk switch
            {
                Folk.Smith => ((World.FolkId?)World.FolkId.Hobb, "Hobb Ironside", "Smith", Art.FolkIdle.WorkKind.Hammer),
                Folk.Priest => (World.FolkId.Aldric, "Brother Aldric", "Shrine", Art.FolkIdle.WorkKind.None),
                Folk.Tailor => (World.FolkId.Vey, "Mistress Vey", "Tailor", Art.FolkIdle.WorkKind.Sew),
                Folk.Driver => (World.FolkId.Corwin, "Corwin", "Carriage driver", Art.FolkIdle.WorkKind.None),
                Folk.Sergeant => (World.FolkId.Brann, "Sergeant Brann", "Drillmaster", Art.FolkIdle.WorkKind.Drill),
                Folk.Runewright => (World.FolkId.Ysolde, "Ysolde", "Runewright", Art.FolkIdle.WorkKind.Trace),
                Folk.Barkeep => (World.FolkId.Marta, "Marta", "Alewife", Art.FolkIdle.WorkKind.Wipe),
                Folk.Lutist => (null, "Pell", "Minstrel", Art.FolkIdle.WorkKind.None),
                Folk.Fiddler => (null, "Old Garrow", "Minstrel", Art.FolkIdle.WorkKind.None),
                Folk.Drummer => (null, "Tam", "Minstrel", Art.FolkIdle.WorkKind.None),
                Folk.Drinker => (null, World.TavernRegular.Name, "Regular", Art.FolkIdle.WorkKind.Drink),
                _ => (null, folk.ToString(), "", Art.FolkIdle.WorkKind.None),
            };
            idle.TurnsBody = folk != Folk.Priest && folk != Folk.Driver && folk != Folk.Lutist && folk != Folk.Fiddler && folk != Folk.Drummer;
            if (idle.Work != Art.FolkIdle.WorkKind.None)
            {
                idle.ArmR = GroupArm(root, 1f, bulk, "Haft", "HammerHead", "PracticeSword", "SwordGuard", "PalmRune", "Bracer", "Tankard");
                idle.ArmL = GroupArm(root, -1f, bulk, "PalmRune", "Bracer", "Pincushion");
                var head = idle.ArmR.Find("HammerHead");
                if (head != null) idle.Striker = head;
            }
            return root;
        }

        /// <summary>
        /// Puts one side's arm (and anything named in <paramref name="extra"/> on that side) under a pivot at the shoulder,
        /// so the arm can swing; returns the pivot.
        /// </summary>
        private static Transform GroupArm(Transform root, float side, float bulk, params string[] extra)
        {
            var pivot = new GameObject(side > 0f ? "ArmR" : "ArmL").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(side * 0.27f * bulk, 1.39f, 0f);
            var names = new System.Collections.Generic.HashSet<string>(extra) { "UpperArm", "Forearm", "Cuff", "Hand", "Thumb" };
            var move = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in root)
                if (c != pivot && names.Contains(c.name) && Mathf.Sign(c.localPosition.x) == Mathf.Sign(side) && Mathf.Abs(c.localPosition.x) > 0.05f) move.Add(c);
            foreach (var c in move) c.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>
        /// The camp folk's figure (rebuilt 2026-10-20, the user: "improve npc design"): legs and boots under a
        /// tunic (or a long robe for the priest, the tailor, the runewright and the driver), a shaped torso
        /// with a belt, a collar and shoulders, a neck, and a face with ears, a nose, brows, eyes with whites
        /// and pupils, and a mouth. The trade details are added by <see cref="Townsfolk"/>.
        /// </summary>
        private static void FolkFigure(Transform root, Folk folk, Color coat, Color skin, float bulk)
        {
            bool robed = folk == Folk.Priest || folk == Folk.Tailor || folk == Folk.Runewright || folk == Folk.Driver;
            var hair = folk == Folk.Priest ? new Color(0.62f, 0.62f, 0.6f) : folk == Folk.Runewright ? new Color(0.8f, 0.8f, 0.84f)
                : folk == Folk.Sergeant ? new Color(0.28f, 0.17f, 0.1f) : folk == Folk.Smith ? new Color(0.32f, 0.2f, 0.11f) : new Color(0.12f, 0.08f, 0.06f);
            var trousers = new Color(0.2f, 0.17f, 0.14f);
            var boots = new Color(0.17f, 0.11f, 0.07f);
            if (robed)
            {
                var skirt = MeshKit.Part(root, "Body", MeshKit.Lathe("folkRobe", new[]
                {
                    new Vector2(0.31f, 0f), new Vector2(0.28f, 0.35f), new Vector2(0.235f, 0.8f), new Vector2(0.22f, 0.95f), new Vector2(0f, 0.96f),
                }, 12), Mat.Cloth, Vector3.zero, default, new Vector3(bulk, 1f, bulk * 0.85f));
                ArtLibrary.Tint(skirt.GetComponent<Renderer>(), coat * 0.92f);
                var hem = MeshKit.Part(root, "Hem", MeshKit.Cylinder(0.315f * bulk, 0.315f * bulk, 0.05f, 12), Mat.Cloth, new Vector3(0f, 0.04f, 0f), default, new Vector3(1f, 1f, 0.85f));
                ArtLibrary.Tint(hem.GetComponent<Renderer>(), coat * 0.55f);
                // Toes of shoes peeping out under the hem.
                foreach (float s in new[] { -1f, 1f })
                {
                    var toe = MeshKit.Part(root, "Shoe", MeshKit.Ball(0.06f, 8, 6), Mat.Leather, new Vector3(s * 0.1f, 0.03f, 0.25f), default, new Vector3(1f, 0.55f, 1.4f));
                    ArtLibrary.Tint(toe.GetComponent<Renderer>(), boots);
                }
            }
            else
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    float x = s * 0.11f * bulk;
                    var thigh = MeshKit.Part(root, "Thigh", MeshKit.Cylinder(0.085f * bulk, 0.07f, 0.42f, 8), Mat.Cloth, new Vector3(x, 0.68f, 0f));
                    ArtLibrary.Tint(thigh.GetComponent<Renderer>(), trousers);
                    var shin = MeshKit.Part(root, "Shin", MeshKit.Cylinder(0.068f, 0.06f, 0.36f, 8), Mat.Cloth, new Vector3(x, 0.3f, 0.01f));
                    ArtLibrary.Tint(shin.GetComponent<Renderer>(), trousers * 0.9f);
                    var boot = MeshKit.Part(root, "Boot", MeshKit.Box(new Vector3(0.13f, 0.13f, 0.25f)), Mat.Leather, new Vector3(x, 0.065f, 0.04f));
                    ArtLibrary.Tint(boot.GetComponent<Renderer>(), boots);
                    var bootTop = MeshKit.Part(root, "BootCuff", MeshKit.Cylinder(0.08f, 0.08f, 0.12f, 8), Mat.Leather, new Vector3(x, 0.2f, 0f));
                    ArtLibrary.Tint(bootTop.GetComponent<Renderer>(), boots * 1.1f);
                }
                var tunic = MeshKit.Part(root, "Body", MeshKit.Lathe("folkTunic", new[]
                {
                    new Vector2(0.27f, 0.62f), new Vector2(0.255f, 0.78f), new Vector2(0.235f, 0.95f), new Vector2(0f, 0.96f),
                }, 12), Mat.Cloth, Vector3.zero, default, new Vector3(bulk, 1f, bulk * 0.85f));
                ArtLibrary.Tint(tunic.GetComponent<Renderer>(), coat * 0.95f);
            }
            var torso = MeshKit.Part(root, "Torso", MeshKit.Lathe("folkTorso", new[]
            {
                new Vector2(0.225f, 0.9f), new Vector2(0.25f, 1.08f), new Vector2(0.275f, 1.3f), new Vector2(0.245f, 1.42f), new Vector2(0.12f, 1.49f), new Vector2(0f, 1.5f),
            }, 12), Mat.Cloth, Vector3.zero, default, new Vector3(bulk, 1f, bulk * 0.78f));
            ArtLibrary.Tint(torso.GetComponent<Renderer>(), coat);
            // Front placket with buttons, a belt and buckle, a collar, and rounded shoulders.
            var placket = MeshKit.Part(root, "Placket", MeshKit.Box(new Vector3(0.05f, 0.5f, 0.012f)), Mat.Cloth, new Vector3(0f, 1.18f, 0.205f * bulk));
            ArtLibrary.Tint(placket.GetComponent<Renderer>(), coat * 0.7f);
            for (int i = 0; i < 4; i++) MeshKit.Part(root, "Button", MeshKit.Ball(0.012f, 5, 3), Mat.Gold, new Vector3(0f, 1.0f + i * 0.11f, 0.214f * bulk));
            var belt = MeshKit.Part(root, "Belt", MeshKit.Cylinder(0.24f * bulk, 0.24f * bulk, 0.06f, 12), Mat.Leather, new Vector3(0f, 0.95f, 0f), default, new Vector3(1f, 1f, 0.8f));
            ArtLibrary.Tint(belt.GetComponent<Renderer>(), new Color(0.22f, 0.14f, 0.08f));
            MeshKit.Part(root, "Buckle", MeshKit.Box(new Vector3(0.06f, 0.05f, 0.015f)), Mat.Gold, new Vector3(0f, 0.95f, 0.195f * bulk));
            var collar = MeshKit.Part(root, "Collar", MeshKit.Cylinder(0.13f, 0.11f, 0.06f, 10), Mat.Cloth, new Vector3(0f, 1.47f, 0f));
            ArtLibrary.Tint(collar.GetComponent<Renderer>(), coat * 0.7f);
            foreach (float s in new[] { -1f, 1f })
            {
                var shoulder = MeshKit.Part(root, "Shoulder", MeshKit.Ball(0.09f * bulk, 8, 6), Mat.Cloth, new Vector3(s * 0.25f * bulk, 1.4f, 0f));
                ArtLibrary.Tint(shoulder.GetComponent<Renderer>(), coat * 0.95f);
            }
            var neck = MeshKit.Part(root, "NeckSkin", MeshKit.Cylinder(0.055f, 0.06f, 0.1f, 8), Mat.Bone, new Vector3(0f, 1.52f, 0.01f));
            ArtLibrary.Tint(neck.GetComponent<Renderer>(), skin * 0.95f);

            // The head and face.
            var head = MeshKit.Part(root, "Head", MeshKit.Ball(0.13f, 12, 10), Mat.Bone, new Vector3(0f, 1.63f, 0.02f), default, new Vector3(0.94f, 1.08f, 1f));
            ArtLibrary.Tint(head.GetComponent<Renderer>(), skin);
            var jaw = MeshKit.Part(root, "Jaw", MeshKit.Ball(0.095f, 10, 8), Mat.Bone, new Vector3(0f, 1.56f, 0.05f), default, new Vector3(1f, 0.7f, 0.95f));
            ArtLibrary.Tint(jaw.GetComponent<Renderer>(), skin * 0.97f);
            var nose = MeshKit.Part(root, "Nose", MeshKit.Lathe("folkNose", new[] { new Vector2(0.022f, 0f), new Vector2(0.016f, 0.03f), new Vector2(0f, 0.045f) }, 6), Mat.Bone, new Vector3(0f, 1.61f, 0.135f), new Vector3(80f, 0f, 0f));
            ArtLibrary.Tint(nose.GetComponent<Renderer>(), skin * 0.93f);
            var mouth = MeshKit.Part(root, "Mouth", MeshKit.Box(new Vector3(0.056f, 0.012f, 0.012f)), Mat.Cloth, new Vector3(0f, 1.565f, 0.13f));
            ArtLibrary.Tint(mouth.GetComponent<Renderer>(), new Color(0.5f, 0.2f, 0.18f));
            foreach (float s in new[] { -1f, 1f })
            {
                var ear = MeshKit.Part(root, "Ear", MeshKit.Ball(0.03f, 6, 5), Mat.Bone, new Vector3(s * 0.122f, 1.625f, 0.01f), default, new Vector3(0.45f, 1f, 0.75f));
                ArtLibrary.Tint(ear.GetComponent<Renderer>(), skin * 0.93f);
                var white = MeshKit.Part(root, "EyeWhite", MeshKit.Ball(0.024f, 8, 6), Mat.Bone, new Vector3(s * 0.044f, 1.655f, 0.112f), default, new Vector3(1.15f, 0.8f, 0.6f));
                ArtLibrary.Tint(white.GetComponent<Renderer>(), new Color(0.95f, 0.93f, 0.88f));
                var iris = MeshKit.Part(root, "Pupil", MeshKit.Ball(0.0135f, 6, 4), Mat.Cloth, new Vector3(s * 0.044f, 1.655f, 0.124f));
                ArtLibrary.Tint(iris.GetComponent<Renderer>(), folk == Folk.Runewright ? new Color(0.35f, 0.55f, 0.75f) : new Color(0.22f, 0.15f, 0.09f));
                var glint = MeshKit.Part(root, "Glint", MeshKit.Ball(0.004f, 4, 3), Mat.Glow, new Vector3(s * 0.044f + 0.005f, 1.66f, 0.1365f));
                ArtLibrary.SetGlow(glint.GetComponent<Renderer>(), new Color(1f, 0.95f, 0.85f) * 0.8f);
                var cheek = MeshKit.Part(root, "Cheek", MeshKit.Ball(0.03f, 6, 4), Mat.Bone, new Vector3(s * 0.07f, 1.6f, 0.1f), default, new Vector3(1f, 0.7f, 0.5f));
                ArtLibrary.Tint(cheek.GetComponent<Renderer>(), Color.Lerp(skin, new Color(0.8f, 0.42f, 0.38f), 0.35f));
                var brow = MeshKit.Part(root, "Brow", MeshKit.Box(new Vector3(0.055f, 0.013f, 0.016f)), Mat.Cloth, new Vector3(s * 0.046f, 1.685f, 0.118f), new Vector3(0f, 0f, s * -9f));
                ArtLibrary.Tint(brow.GetComponent<Renderer>(), hair * 0.8f);
            }
        }

        /// <summary>A cylinder limb from <paramref name="a"/> to <paramref name="b"/> (local to <paramref name="parent"/>).</summary>
        private static GameObject Limb(Transform parent, string name, Vector3 a, Vector3 b, float radius)
        {
            var d = b - a;
            return MeshKit.Part(parent, name, MeshKit.Cylinder(radius, radius * 0.9f, d.magnitude, 6), Mat.Cloth, a, Quaternion.FromToRotation(Vector3.up, d).eulerAngles);
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
                float hgt = 0.14f + (float)rng.NextDouble() * 0.24f;
                // Thicker wax and a drip at the foot: at 7 cm across the bodies vanished and the flames seemed to float.
                MeshKit.Part(root, "Candle" + i, MeshKit.Cylinder(0.05f, 0.055f, hgt, 6), Mat.Bone, p);
                MeshKit.Part(root, "Drip" + i, MeshKit.Cylinder(0.08f, 0.09f, 0.015f, 6), Mat.Bone, p);
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

            var torch = AddFlame(root, new Vector3(0f, 0.36f, 0.36f), 0.7f, fire ?? new Color(1f, 0.62f, 0.32f), 7f, 10f, shadows);
            MeshKit.MergeParts(root); // the bracket, cup and shaft never move: one mesh per material
            return torch;
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
            MeshKit.Part(root, "Coals", MeshKit.Cylinder(0.2f, 0.44f, 0.07f, 10), Mat.Glow, new Vector3(0f, 1.17f, 0f)); // fills the bowl: lower, it hid under the inner steel, which the flame lit to white
            // Embers, not a lamp: a 0.8 m disc at fire x1.4 bloomed into a flat white plate under the flame. Deep, saturated, dim.
            Color.RGBToHSV(fire, out float ch, out float cs, out _);
            ArtLibrary.SetGlow(root.Find("Coals").GetComponent<Renderer>(), Color.HSVToRGB(ch, Mathf.Max(cs, 0.9f), 1f) * 0.55f);

            var brazier = AddFlame(root, new Vector3(0f, 1.24f, 0f), 1.6f, fire, 12f, 14f, shadows);
            MeshKit.MergeParts(root);
            return brazier;
        }

        /// <summary>A real camp bonfire on the ground: split logs leaned into a cone over a bed of embers and ash,
        /// one big flame and two smaller ones round it (the camp's fire was a tripod brazier standing in an empty stone ring).</summary>
        public static Torch Bonfire(Transform parent, Vector3 floor, Color fire)
        {
            var root = new GameObject("Bonfire").transform;
            root.SetParent(parent, false);
            root.position = floor;
            MeshKit.Part(root, "Ash", MeshKit.Cylinder(1.05f, 1.1f, 0.04f, 14), Mat.Rubble, new Vector3(0f, 0.02f, 0f));
            ArtLibrary.Tint(root.Find("Ash").GetComponent<Renderer>(), new Color(0.32f, 0.3f, 0.29f));
            var bed = MeshKit.Part(root, "Embers", MeshKit.Cylinder(0.55f, 0.7f, 0.08f, 12), Mat.Glow, new Vector3(0f, 0.06f, 0f));
            Color.RGBToHSV(fire, out float ch, out float cs, out _);
            ArtLibrary.SetGlow(bed.GetComponent<Renderer>(), Color.HSVToRGB(ch, Mathf.Max(cs, 0.92f), 1f) * 0.5f);
            bed.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Seven split logs leaned in to a point, charred at the top.
            for (int i = 0; i < 7; i++)
            {
                float yaw = i * (360f / 7f) + (i % 2) * 9f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var foot = dir * 0.72f;
                var top = Vector3.up * 1.05f + dir * 0.06f;
                var mid = (foot + top) * 0.5f;
                var rot = Quaternion.FromToRotation(Vector3.up, (top - foot).normalized) * Quaternion.Euler(0f, yaw, 0f);
                float len = Vector3.Distance(foot, top);
                MeshKit.Part(root, "Log" + i, MeshKit.Cylinder(0.09f, 0.07f + (i % 3) * 0.01f, len, 7), Mat.Wood, foot, rot.eulerAngles); // Cylinder grows up from its base
                MeshKit.Part(root, "Char" + i, MeshKit.Cylinder(0.078f, 0.07f, len * 0.35f, 7), Mat.Dark, Vector3.Lerp(foot, top, 0.65f), rot.eulerAngles);
            }
            // Two logs lying across the bed, glowing where they burn.
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector3(i == 0 ? 0.12f : -0.1f, 0.13f, i == 0 ? 0.08f : -0.12f);
                float yaw = i * 70f + 20f;
                MeshKit.Part(root, "Lying" + i, MeshKit.Cylinder(0.1f, 0.1f, 1.2f, 8), Mat.Dark, p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.6f, 0f, 0f), new Vector3(0f, yaw, 90f));
            }
            var torch = AddFlame(root, new Vector3(0f, 0.12f, 0f), 3.4f, fire, 15f, 18f, true);
            // Smaller licks round the main flame, no lights of their own.
            for (int i = 0; i < 3; i++)
            {
                var at = Quaternion.Euler(0f, i * 120f + 40f, 0f) * new Vector3(0f, 0.1f, 0.34f);
                AddFlame(root, at, 1.6f + i * 0.25f, fire, 0f, 1f, false, light: false); // no Torch: FlameLick flickers them
            }
            foreach (Transform c in root)
                if (c.name == "Flame" && c != torch.Flame) c.gameObject.AddComponent<Art.FlameLick>();
            MeshKit.MergeParts(root);
            return torch;
        }

        public static Torch AddFlame(Transform root, Vector3 at, float size, Color color, float intensity, float range, bool shadows, bool light = true)
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

            Light lamp = null;
            if (light)
            {
                lamp = new GameObject("Light").AddComponent<Light>();
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = at + Vector3.up * 0.25f;
                lamp.type = LightType.Point;
                lamp.color = color;
                lamp.range = range;
                lamp.intensity = intensity;
                lamp.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                lamp.shadowNearPlane = 0.3f;
            }

            if (!light) return null;
            var torch = root.gameObject.AddComponent<Torch>();
            torch.Light = lamp;
            torch.Flame = flame;
            torch.BaseIntensity = intensity;
            return torch;
        }
    }
}
