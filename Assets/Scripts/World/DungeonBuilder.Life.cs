using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// More of each act (2026-11-07, the user: room dressing, dungeon life and lighting moods, all five acts evenly).
    /// Per room, seeded: one act piece (Ossuary gibbet cages, Vaults collapsed shelving, Throne torn banners, Cathedral
    /// votive racks, Abyss tendrils), a cobweb with its spider in a high corner, cistern fish, moths round a light,
    /// and a MOOD: warm (a candle cluster and a low gold light), or a broken lantern that stutters.
    /// </summary>
    public static partial class DungeonBuilder
    {
        private static void ActExtras(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng, int act)
        {
            if (room.Kind == RoomKind.Boss) return;
            if (ThemeWallSpot(room, rng, 0.9f, out var at, out var inward))
            {
                switch (act)
                {
                    case 0: Gibbet(b, lights, at + inward * 2.2f, room, rng); break;
                    // ThemeWallSpot stands 0.9 m in from the wall: these sit back against it.
                    case 1: FallenShelf(b, at - inward * 0.4f, Quaternion.LookRotation(inward), rng); break;
                    case 2: TornBanner(lights, at - inward * 0.78f + Vector3.up * (room.Height - 1.2f), inward, rng); break;
                    case 3: VotiveRack(b, lights, at - inward * 0.5f, Quaternion.LookRotation(inward)); break;
                    default: Tendrils(b, lights, at - inward * 0.6f, inward, rng); break;
                }
            }
            // A web in a high corner, and its spider (the Ossuary and the Throne; one room in two).
            if ((act == 0 || act == 2) && rng.NextDouble() < 0.5)
            {
                float sx = rng.NextDouble() < 0.5 ? -1f : 1f, sz = rng.NextDouble() < 0.5 ? -1f : 1f;
                var corner = room.Center + new Vector3(sx * (room.Size.x / 2f - 0.05f), Mathf.Min(room.Height, 5f) - 0.1f, sz * (room.Size.y / 2f - 0.05f));
                var a = new Vector3(-sx, 0f, 0f);
                var bb = new Vector3(0f, 0f, -sz);
                Props.Cobweb(lights, corner, a, bb, 1.4f);
                var spot = corner + (a + bb) * 0.35f + Vector3.down * 0.25f;
                LittleLife.Make(lights, LittleLife.Kind.Spider, spot, Color.black).AxisA = (a - bb).normalized;
            }
            // Fish in a cistern.
            if (room.Feature == RoomFeature.Cistern)
                for (int i = 0; i < 5; i++)
                {
                    var p = room.Center + new Vector3(((float)rng.NextDouble() - 0.5f) * room.Size.x * 0.4f, -0.45f, ((float)rng.NextDouble() - 0.5f) * room.Size.y * 0.4f);
                    var f = LittleLife.Make(lights, LittleLife.Kind.Fish, p, new Color(0.45f, 0.48f, 0.4f));
                    f.Radius = 1.2f + (float)rng.NextDouble() * 1.4f;
                }
            // MOOD, one room in three: warm candlelight, or a broken lantern that stutters.
            double mood = rng.NextDouble();
            if (mood < 0.2 && room.Kind != RoomKind.Start)
            {
                var c = room.Center + new Vector3(((float)rng.NextDouble() - 0.5f) * room.Size.x * 0.5f, 0f, ((float)rng.NextDouble() - 0.5f) * room.Size.y * 0.5f);
                Props.Candles(lights, c, new Color(1f, 0.7f, 0.35f));
                if (act >= 3) for (int i = 0; i < 3; i++) LittleLife.Make(lights, LittleLife.Kind.Moth, c + Vector3.up * 0.8f, act == 4 ? new Color(0.7f, 0.4f, 1f) * 1.4f : new Color(1f, 0.92f, 0.75f) * 1.2f);
            }
            else if (mood < 0.33)
            {
                var hang = room.Center + new Vector3(((float)rng.NextDouble() - 0.5f) * room.Size.x * 0.4f, room.Height - 1.4f, ((float)rng.NextDouble() - 0.5f) * room.Size.y * 0.4f);
                b.AddBox(hang + Vector3.up * 0.7f, new Vector3(0.03f, 1.4f, 0.03f), Mat.Steel);
                b.AddBox(hang + Vector3.up * 0.14f, new Vector3(0.24f, 0.05f, 0.24f), Mat.Steel);
                var pane = MeshKit.Part(lights, "BrokenLantern", MeshKit.Box(new Vector3(0.17f, 0.24f, 0.17f)), Mat.Glow, hang);
                ArtLibrary.SetGlow(pane.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 0.7f);
                var l = new GameObject("BrokenLanternLight").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.position = hang + Vector3.down * 0.3f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.65f, 0.35f);
                l.range = 9f;
                l.intensity = 2.4f;
                l.shadows = LightShadows.None;
                var st = l.gameObject.AddComponent<StutterLight>();
                st.Pane = pane.GetComponent<Renderer>();
            }
            // The Abyss: violet moths round the crystal anyway.
            if (act == 4 && ThemeSpots.Count > 0 && rng.NextDouble() < 0.6)
            {
                var (cAt, cIn) = ThemeSpots[ThemeSpots.Count - 1];
                // Only this room's crystal (the list runs for the whole floor).
                bool here = Mathf.Abs(cAt.x - room.Center.x) <= room.Size.x / 2f && Mathf.Abs(cAt.z - room.Center.z) <= room.Size.y / 2f;
                if (here) for (int i = 0; i < 2; i++) LittleLife.Make(lights, LittleLife.Kind.Moth, cAt + cIn * 0.8f + Vector3.up * 1.2f, new Color(0.7f, 0.4f, 1f) * 1.4f);
            }
        }

        /// <summary>The Ossuary: an iron gibbet cage on a chain, bones inside.</summary>
        private static void Gibbet(MeshKit.Batcher b, Transform lights, Vector3 floor, DungeonLayout.Room room, System.Random rng)
        {
            float top = room.Height - 0.2f, cageBottom = 2.4f;
            var c = floor + Vector3.up * cageBottom;
            b.AddBox(new Vector3(c.x, floor.y + (top + cageBottom + 1.4f) / 2f, c.z), new Vector3(0.05f, top - cageBottom - 1.4f, 0.05f), Mat.Steel);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                b.AddBox(c + new Vector3(Mathf.Cos(a) * 0.42f, 0.7f, Mathf.Sin(a) * 0.42f), new Vector3(0.04f, 1.4f, 0.04f), Mat.Steel);
            }
            b.Add(MeshKit.Cylinder(0.46f, 0.46f, 0.05f, 10), Mat.Steel, Matrix4x4.Translate(c));
            b.Add(MeshKit.Cylinder(0.46f, 0.2f, 0.12f, 10), Mat.Steel, Matrix4x4.Translate(c + Vector3.up * 1.4f));
            b.Add(MeshKit.Ball(0.13f, 8, 6), Mat.Bone, Matrix4x4.Translate(c + new Vector3(0.1f, 0.18f, 0.05f)));
            for (int i = 0; i < 4; i++)
                b.AddBox(c + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.5f, 0.06f, ((float)rng.NextDouble() - 0.5f) * 0.5f), new Vector3(0.36f, 0.05f, 0.05f), Mat.Bone, Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f));
        }

        /// <summary>The Vaults: a shelving rack fallen against the wall, its crates burst on the floor.</summary>
        private static void FallenShelf(MeshKit.Batcher b, Vector3 at, Quaternion face, System.Random rng)
        {
            var tilt = face * Quaternion.Euler(-28f, 0f, 0f);
            foreach (float s in new[] { -0.9f, 0.9f }) b.AddBox(at + face * new Vector3(s, 1.1f, 0f), new Vector3(0.1f, 2.4f, 0.1f), Mat.Wood, tilt);
            for (int k = 0; k < 4; k++) b.AddBox(at + face * new Vector3(0f, 0.4f + k * 0.55f, -0.25f + k * 0.13f), new Vector3(1.9f, 0.05f, 0.45f), Mat.Wood, tilt);
            for (int i = 0; i < 3; i++)
            {
                var p = at + face * new Vector3(((float)rng.NextDouble() - 0.5f) * 2f, 0.2f, 0.6f + (float)rng.NextDouble() * 0.8f);
                b.AddBox(p, Vector3.one * (0.35f + (float)rng.NextDouble() * 0.15f), Mat.Wood, new Color(0.3f, 0.26f, 0.2f), Quaternion.Euler((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 90f, (float)rng.NextDouble() * 25f));
            }
        }

        /// <summary>The Throne: a torn, faded banner hanging high on a wall.</summary>
        private static void TornBanner(Transform lights, Vector3 at, Vector3 outward, System.Random rng)
        {
            var faded = rng.NextDouble() < 0.5 ? new Color(0.3f, 0.1f, 0.1f) : new Color(0.14f, 0.13f, 0.24f);
            Props.Banner(lights, at, outward, faded);
        }

        /// <summary>The Cathedral: a tiered votive rack of candles, with its own small warm light.</summary>
        private static void VotiveRack(MeshKit.Batcher b, Transform lights, Vector3 at, Quaternion face)
        {
            for (int k = 0; k < 3; k++) b.AddBox(at + face * new Vector3(0f, 0.35f + k * 0.28f, -k * 0.18f), new Vector3(1.4f, 0.06f, 0.3f), Mat.Wood, face);
            b.AddBox(at + face * new Vector3(0f, 0.35f, 0f), new Vector3(1.4f, 0.7f, 0.08f), Mat.Wood, face);
            for (int k = 0; k < 3; k++) Props.Candles(lights, at + face * new Vector3(0f, 0.38f + k * 0.28f, -k * 0.18f), new Color(1f, 0.75f, 0.4f));
        }

        /// <summary>The Abyss: black tendrils coming up through the floor by the wall, glowing at the tips.</summary>
        private static void Tendrils(MeshKit.Batcher b, Transform lights, Vector3 at, Vector3 inward, System.Random rng)
        {
            for (int i = 0; i < 4; i++)
            {
                var root = at + inward * (0.3f + (float)rng.NextDouble() * 0.8f) + Vector3.Cross(Vector3.up, inward) * ((float)rng.NextDouble() - 0.5f) * 1.6f;
                var p = root; var dir = Vector3.up;
                for (int s = 0; s < 5; s++)
                {
                    dir = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 40f, 0f, ((float)rng.NextDouble() - 0.5f) * 40f) * dir;
                    float w = 0.12f * (1f - s * 0.17f);
                    b.AddBox(p + dir * 0.25f, new Vector3(w, 0.5f, w), Mat.Dark, Quaternion.FromToRotation(Vector3.up, dir));
                    p += dir * 0.48f;
                }
                var tip = MeshKit.Part(lights, "TendrilTip", MeshKit.Ball(0.05f, 6, 4), Mat.Glow, p);
                ArtLibrary.SetGlow(tip.GetComponent<Renderer>(), new Color(0.7f, 0.35f, 1f) * 1.2f);
            }
        }
    }
}
