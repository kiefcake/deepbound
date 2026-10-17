using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Dressing that belongs to one floor and no other, set along the walls of its ordinary rooms
    /// (never in front of a doorway): the Ossuary's shelves of skulls, the Drowned Vaults' sunken
    /// strongboxes and weed-hung chains, the Hollow Throne's toppled statues and gilded urns, the
    /// Black Cathedral's pews and censers, and the Abyss's violet crystal. The rest of the kit is
    /// shared; this is what makes each floor look like somewhere.
    /// </summary>
    public static partial class DungeonBuilder
    {
        /// <summary>Where theme pieces went on the last built floor (screenshots look at one).</summary>
        public static readonly System.Collections.Generic.List<(Vector3 at, Vector3 inward)> ThemeSpots = new System.Collections.Generic.List<(Vector3, Vector3)>();

        private static void ThemeDressing(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng, int count)
        {
            int depth = DepthOf(theme);
            for (int i = 0; i < count; i++)
            {
                if (!ThemeWallSpot(room, rng, 0.9f, out var at, out var inward)) continue;
                var face = Quaternion.LookRotation(inward);
                ThemeSpots.Add((at, inward));
                switch (depth)
                {
                    case 0: if (rng.NextDouble() < 0.6) SkullShelf(b, at, face, rng); else BoneHeap(b, at, rng); break;
                    case 1: if (rng.NextDouble() < 0.5) SunkenStrongbox(b, lights, at, face, rng); else WeedChain(b, lights, at + inward * 0.4f, room, rng); break;
                    case 2: if (rng.NextDouble() < 0.45) ToppledStatue(b, at + inward * 0.3f, face, rng); else if (rng.NextDouble() < 0.5) BrokenColumn(b, at, rng); else GildedUrn(b, at, rng); break;
                    case 3: if (rng.NextDouble() < 0.55) Pews(b, at + inward * 0.4f, face); else PrayerStand(b, lights, at, face, room); break;
                    default: CrystalCluster(lights, at, rng); break;
                }
            }
        }

        private static int DepthOf(DungeonTheme t)
        {
            for (int i = 0; i < DungeonTheme.ByDepth.Length; i++) if (DungeonTheme.ByDepth[i] == t) return i;
            return 0;
        }

        /// <summary>A spot against a wall, clear of doorways and wall holes; <paramref name="inward"/> faces into the room.</summary>
        private static bool ThemeWallSpot(DungeonLayout.Room room, System.Random rng, float inset, out Vector3 at, out Vector3 inward)
        {
            for (int tries = 0; tries < 10; tries++)
            {
                int side = rng.Next(4);
                var d = DungeonLayout.Dir[side];
                var outward = new Vector3(d.x, 0f, d.y);
                float half = (side == 0 || side == 2 ? room.Size.y : room.Size.x) / 2f;
                float len = side == 0 || side == 2 ? room.Size.x : room.Size.y;
                float t = (float)(rng.NextDouble() - 0.5) * (len - 4f);
                if (room.Doors[side] && Mathf.Abs(t) < DungeonLayout.CorridorWidth / 2f + 1.6f) continue;
                var hole = room.HoleOn(side);
                if (hole != null && Mathf.Abs(t - hole.Along) < 2.5f) continue;
                var tangent = new Vector3(-outward.z, 0f, outward.x);
                at = room.Center + outward * (half - inset) + tangent * t;
                inward = -outward;
                return true;
            }
            at = room.Center; inward = Vector3.forward;
            return false;
        }

        // ---------------- The Ossuary ----------------
        private static void SkullShelf(MeshKit.Batcher b, Vector3 at, Quaternion face, System.Random rng)
        {
            var back = face * Vector3.back;
            var along = face * Vector3.right;
            var skull = MeshKit.Ball(0.11f, 8, 6);
            for (int row = 0; row < 3; row++)
            {
                float y = 0.55f + row * 0.62f;
                var shelf = at + back * 0.55f + Vector3.up * y;
                b.AddBox(shelf, new Vector3(1.8f, 0.08f, 0.42f), Mat.StoneWall, face);
                for (int k = 0; k < 6; k++)
                {
                    if (rng.NextDouble() < 0.15) continue; // a gap where one fell
                    var p = shelf + along * (-0.75f + k * 0.3f) + Vector3.up * 0.13f + back * 0.02f;
                    b.Add(skull, Mat.Bone, Matrix4x4.TRS(p, face * Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 40f - 20f, 0f), new Vector3(0.95f, 1f, 1.05f)));
                }
                // Long bones stacked under each row, like cordwood.
                for (int k = 0; k < 4; k++)
                    b.AddBox(shelf + Vector3.up * -0.12f + along * (-0.6f + k * 0.4f) + back * 0.05f, new Vector3(0.36f, 0.05f, 0.05f), Mat.Bone, face * Quaternion.Euler(0f, (float)rng.NextDouble() * 10f - 5f, 0f));
            }
        }

        private static void BoneHeap(MeshKit.Batcher b, Vector3 at, System.Random rng)
        {
            var skull = MeshKit.Ball(0.11f, 8, 6);
            for (int i = 0; i < 9; i++)
            {
                var p = at + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.9f + Vector3.up * (0.04f + (float)rng.NextDouble() * 0.12f);
                var r = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
                if (i % 3 == 0) b.Add(skull, Mat.Bone, Matrix4x4.TRS(p + Vector3.up * 0.05f, r, Vector3.one));
                else b.AddBox(p, new Vector3(0.38f, 0.05f, 0.05f), Mat.Bone, r);
            }
        }

        // ---------------- The Drowned Vaults ----------------
        private static void SunkenStrongbox(MeshKit.Batcher b, Transform lights, Vector3 at, Quaternion face, System.Random rng)
        {
            var tilt = face * Quaternion.Euler((float)rng.NextDouble() * 8f, (float)rng.NextDouble() * 30f - 15f, 6f);
            b.AddBox(at + Vector3.up * 0.22f, new Vector3(0.8f, 0.45f, 0.55f), Mat.Steel, tilt);
            // The lid, burst open and fallen against it.
            b.AddBox(at + Vector3.up * 0.5f + face * Vector3.back * 0.35f, new Vector3(0.82f, 0.06f, 0.5f), Mat.Steel, tilt * Quaternion.Euler(-70f, 0f, 0f));
            var coin = MeshKit.Cylinder(0.045f, 0.045f, 0.012f, 8);
            for (int i = 0; i < 10; i++)
            {
                var p = at + face * new Vector3((float)rng.NextDouble() * 1.4f - 0.7f, 0f, 0.3f + (float)rng.NextDouble() * 0.8f) + Vector3.up * 0.01f;
                b.Add(coin, Mat.Gold, Matrix4x4.TRS(p, Quaternion.Euler((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 360f, 0f), Vector3.one));
            }
            // Weed draped over it.
            var weed = MeshKit.Part(lights, "Weed", MeshKit.Box(new Vector3(0.7f, 0.02f, 0.25f)), Mat.Cloth, at + Vector3.up * 0.46f, tilt.eulerAngles);
            ArtLibrary.Tint(weed.GetComponent<Renderer>(), new Color(0.12f, 0.22f, 0.12f));
        }

        private static void WeedChain(MeshKit.Batcher b, Transform lights, Vector3 at, DungeonLayout.Room room, System.Random rng)
        {
            float top = room.Center.y + room.Height - 0.3f;
            float bottom = room.Center.y + 0.6f + (float)rng.NextDouble() * 1.4f;
            for (float y = top; y > bottom; y -= 0.16f)
                b.AddBox(new Vector3(at.x, y, at.z), new Vector3(0.05f, 0.14f, 0.025f), Mat.Steel, Quaternion.Euler(0f, ((int)(y / 0.16f) % 2) * 90f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float y = Mathf.Lerp(bottom, top, 0.15f + i * 0.3f);
                var strand = MeshKit.Part(lights, "Weed", MeshKit.Box(new Vector3(0.05f, 0.6f, 0.012f)), Mat.Cloth, new Vector3(at.x + 0.03f, y - 0.3f, at.z), new Vector3(0f, i * 60f, 8f));
                ArtLibrary.Tint(strand.GetComponent<Renderer>(), new Color(0.13f, 0.26f, 0.14f));
                strand.AddComponent<BannerSway>();
            }
        }

        // ---------------- The Hollow Throne ----------------
        private static void ToppledStatue(MeshKit.Batcher b, Vector3 at, Quaternion face, System.Random rng)
        {
            // A courtier in marble, fallen on its side, its head rolled a little way off.
            var lying = face * Quaternion.Euler(0f, 90f, 90f);
            b.Add(MeshKit.Lathe("statueBody", new[] { new Vector2(0.3f, 0f), new Vector2(0.26f, 0.6f), new Vector2(0.22f, 1.2f), new Vector2(0.25f, 1.45f), new Vector2(0.1f, 1.6f) }, 10),
                Mat.Marble, Matrix4x4.TRS(at + Vector3.up * 0.27f + face * Vector3.right * -0.8f, lying, Vector3.one));
            b.Add(MeshKit.Ball(0.17f, 10, 8), Mat.Marble, Matrix4x4.TRS(at + Vector3.up * 0.17f + face * Vector3.right * 1.3f + face * Vector3.forward * 0.4f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 40f), Vector3.one));
            b.AddBox(at + face * Vector3.back * 0.3f + Vector3.up * 0.2f + face * Vector3.right * -1.5f, new Vector3(0.7f, 0.4f, 0.7f), Mat.Marble, face);
        }

        private static void BrokenColumn(MeshKit.Batcher b, Vector3 at, System.Random rng)
        {
            float h = 1.2f + (float)rng.NextDouble() * 1.4f;
            b.Add(MeshKit.Cylinder(0.42f, 0.38f, h, 10), Mat.StoneWall, Matrix4x4.TRS(at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one));
            b.AddBox(at + Vector3.up * 0.1f, new Vector3(1.1f, 0.2f, 1.1f), Mat.StoneWall);
            // Fallen drums of the shaft.
            for (int i = 0; i < 2; i++)
                b.Add(MeshKit.Cylinder(0.38f, 0.38f, 0.6f, 10), Mat.StoneWall,
                    Matrix4x4.TRS(at + new Vector3((float)rng.NextDouble() * 2f - 1f, 0.38f, (float)rng.NextDouble() * 2f - 1f) * 1.2f, Quaternion.Euler(90f, (float)rng.NextDouble() * 360f, 0f), Vector3.one));
        }

        private static void GildedUrn(MeshKit.Batcher b, Vector3 at, System.Random rng)
        {
            b.AddBox(at + Vector3.up * 0.25f, new Vector3(0.6f, 0.5f, 0.6f), Mat.StoneWall);
            b.Add(MeshKit.Lathe("gildedUrn", new[] { new Vector2(0.12f, 0f), new Vector2(0.22f, 0.12f), new Vector2(0.26f, 0.35f), new Vector2(0.14f, 0.6f), new Vector2(0.18f, 0.7f), new Vector2(0f, 0.72f) }, 12),
                Mat.Gold, Matrix4x4.TRS(at + Vector3.up * 0.5f, Quaternion.identity, Vector3.one));
        }

        // ---------------- The Black Cathedral ----------------
        private static void Pews(MeshKit.Batcher b, Vector3 at, Quaternion face)
        {
            var along = face * Vector3.right;
            var fwd = face * Vector3.forward;
            for (int row = 0; row < 2; row++)
            {
                var c = at + fwd * (row * 1.1f);
                b.AddBox(c + Vector3.up * 0.45f, new Vector3(2.4f, 0.08f, 0.45f), Mat.Wood, face);
                b.AddBox(c + Vector3.up * 0.85f - fwd * 0.22f, new Vector3(2.4f, 0.75f, 0.06f), Mat.Wood, face);
                foreach (float s in new[] { -1.1f, 1.1f })
                    b.AddBox(c + along * s + Vector3.up * 0.42f, new Vector3(0.08f, 0.84f, 0.5f), Mat.Wood, face);
            }
        }

        private static void PrayerStand(MeshKit.Batcher b, Transform lights, Vector3 at, Quaternion face, DungeonLayout.Room room)
        {
            b.AddBox(at + Vector3.up * 0.55f, new Vector3(0.5f, 1.1f, 0.4f), Mat.Wood, face);
            b.AddBox(at + Vector3.up * 1.13f, new Vector3(0.62f, 0.05f, 0.48f), Mat.Wood, face * Quaternion.Euler(-18f, 0f, 0f));
            Props.Candles(lights, at + face * Vector3.forward * 0.6f, theme.TorchColor);
            // A censer on a chain above, swaying.
            var hang = MeshKit.Pivot(lights, "Censer", at + face * Vector3.forward * 0.5f + Vector3.up * Mathf.Min(room.Height - 0.3f, 4.2f));
            MeshKit.Part(hang, "Chain", MeshKit.Box(new Vector3(0.015f, 1.6f, 0.015f)), Mat.Gold, Vector3.down * 0.8f);
            MeshKit.Part(hang, "Bowl", MeshKit.Ball(0.12f, 8, 6), Mat.Gold, Vector3.down * 1.65f, default, new Vector3(1f, 0.8f, 1f));
            var ember = MeshKit.Part(hang, "Ember", MeshKit.Ball(0.05f, 6, 4), Mat.Glow, Vector3.down * 1.58f);
            ArtLibrary.SetGlow(ember.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.2f) * 1.3f);
            hang.gameObject.AddComponent<BannerSway>();
        }

        // ---------------- The Abyss ----------------
        private static void CrystalCluster(Transform lights, Vector3 at, System.Random rng)
        {
            var mesh = MeshKit.Lathe("abyssCrystal", new[] { new Vector2(0f, 0f), new Vector2(0.12f, 0.15f), new Vector2(0.1f, 0.8f), new Vector2(0f, 1.1f) }, 6);
            var root = MeshKit.Pivot(lights, "Crystals", at);
            int n = 3 + rng.Next(0, 4);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.8f;
                var shard = MeshKit.Part(root, "Shard", mesh, Mat.Glow, p, new Vector3((float)rng.NextDouble() * 50f - 25f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 50f - 25f),
                    Vector3.one * (0.5f + (float)rng.NextDouble() * 1.1f));
                ArtLibrary.SetGlow(shard.GetComponent<Renderer>(), new Color(0.55f, 0.25f, 1f) * (0.7f + (float)rng.NextDouble() * 0.6f));
                shard.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
