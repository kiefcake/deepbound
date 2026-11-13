using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The newer room mechanics' geometry: galleries with balconies, sunken courts, flooded
    /// cisterns, pendulum halls and ritual chambers — plus shafts of light from the
    /// ceiling, which every larger room gets.
    /// </summary>
    public static partial class DungeonBuilder
    {
        // ------------------------------------------------------------------
        // Shared pieces
        // ------------------------------------------------------------------

        /// <summary>
        /// A ramp as one rotated slab: from <paramref name="low"/> (bottom edge centre) to
        /// <paramref name="high"/> (top edge centre), <paramref name="width"/> across. Thin
        /// stone treads on top make it read as stairs without bumping the collider.
        /// </summary>
        private static void Ramp(Vector3 low, Vector3 high, float width, MeshKit.Batcher b, MeshKit.Batcher dressing)
        {
            var run = high - low;
            float length = run.magnitude;
            var rot = Quaternion.LookRotation(run.normalized, Vector3.up);
            const float thick = 0.3f;
            var up = rot * Vector3.up;
            b.AddBox((low + high) / 2f - up * thick / 2f, new Vector3(width, thick, length), Mat.StoneWall, rot);
            int treads = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(high.y - low.y) / 0.3f));
            for (int i = 0; i < treads; i++)
            {
                var p = Vector3.Lerp(low, high, (i + 0.5f) / treads);
                dressing.AddBox(p + up * 0.01f, new Vector3(width * 0.98f, 0.03f, 0.12f), Mat.Flagstone, rot);
            }
            // Side walls on the ramp's flanks, so it reads as a stair cut into stone.
            var side = rot * Vector3.right;
            foreach (float s in new[] { -1f, 1f })
                dressing.AddBox((low + high) / 2f + side * s * (width / 2f + 0.12f) + up * 0.2f, new Vector3(0.22f, 0.5f, length), Mat.StoneWall, rot);
        }

        /// <summary>
        /// A floor with a lowered rectangle in it (a sunken court or a cistern basin): the
        /// ring around it at floor level, the low floor, its walls, and a ramp down the
        /// middle of each edge.
        /// </summary>
        private static void BuildLowFloor(DungeonLayout.Room room, float depth, float rampLength, MeshKit.Batcher b, MeshKit.Batcher dressing, string lowMat)
        {
            var c = room.Center;
            var low = room.Low;
            float fullW = room.Size.x + WallThickness * 2f, fullD = room.Size.y + WallThickness * 2f;
            float minX = c.x - fullW / 2f, maxX = c.x + fullW / 2f, minZ = c.z - fullD / 2f, maxZ = c.z + fullD / 2f;
            float y = -FloorThickness / 2f;

            void Slab(float x0, float x1, float z0, float z1, float top, string mat)
            {
                if (x1 - x0 < 0.05f || z1 - z0 < 0.05f) return;
                b.AddBox(new Vector3((x0 + x1) / 2f, top - FloorThickness / 2f, (z0 + z1) / 2f), new Vector3(x1 - x0, FloorThickness, z1 - z0), mat);
            }

            Slab(minX, maxX, minZ, low.yMin, 0f, Mat.Flagstone);
            Slab(minX, maxX, low.yMax, maxZ, 0f, Mat.Flagstone);
            Slab(minX, low.xMin, low.yMin, low.yMax, 0f, Mat.Flagstone);
            Slab(low.xMax, maxX, low.yMin, low.yMax, 0f, Mat.Flagstone);
            Slab(low.xMin, low.xMax, low.yMin, low.yMax, -depth, lowMat);

            // The drop's walls.
            b.AddBox(new Vector3(low.center.x, -depth / 2f, low.yMin + 0.2f), new Vector3(low.width, depth, 0.4f), Mat.StoneWall);
            b.AddBox(new Vector3(low.center.x, -depth / 2f, low.yMax - 0.2f), new Vector3(low.width, depth, 0.4f), Mat.StoneWall);
            b.AddBox(new Vector3(low.xMin + 0.2f, -depth / 2f, low.center.y), new Vector3(0.4f, depth, low.height), Mat.StoneWall);
            b.AddBox(new Vector3(low.xMax - 0.2f, -depth / 2f, low.center.y), new Vector3(0.4f, depth, low.height), Mat.StoneWall);
            // A kerb along the lip.
            dressing.AddBox(new Vector3(low.center.x, 0.06f, low.yMin + 0.2f), new Vector3(low.width, 0.12f, 0.5f), Mat.StoneWall);
            dressing.AddBox(new Vector3(low.center.x, 0.06f, low.yMax - 0.2f), new Vector3(low.width, 0.12f, 0.5f), Mat.StoneWall);
            dressing.AddBox(new Vector3(low.xMin + 0.2f, 0.06f, low.center.y), new Vector3(0.5f, 0.12f, low.height), Mat.StoneWall);
            dressing.AddBox(new Vector3(low.xMax - 0.2f, 0.06f, low.center.y), new Vector3(0.5f, 0.12f, low.height), Mat.StoneWall);

            // Ramps down from the middle of each edge.
            foreach (var (edge, inward) in new[]
            {
                (new Vector3(low.center.x, 0f, low.yMax - 0.4f), Vector3.back),
                (new Vector3(low.center.x, 0f, low.yMin + 0.4f), Vector3.forward),
                (new Vector3(low.xMax - 0.4f, 0f, low.center.y), Vector3.left),
                (new Vector3(low.xMin + 0.4f, 0f, low.center.y), Vector3.right),
            })
            {
                Ramp(edge + inward * rampLength + Vector3.down * depth, edge, 3f, b, dressing);
            }
        }

        /// <summary>
        /// A shaft of light from a crack in the ceiling: a faint haze column, a pool of light
        /// on the floor, and motes turning in it. Moonlight on most floors; the cathedral's
        /// comes through red glass.
        /// </summary>
        private static void LightShaft(Transform lights, Vector3 floor, float height, float radius, Color color, MeshKit.Batcher b)
        {
            var root = new GameObject("LightShaft").transform;
            root.SetParent(lights, false);
            root.position = floor;

            // Haze: one unit cone scaled to the shaft (meshes are cached by key), very faint —
            // the fog and the pool of light on the floor do the rest.
            // Three nested layers, so the haze thickens toward the middle and has no hard rim.
            foreach (float k in new[] { 1f, 0.72f, 0.45f })
            {
                var haze = MeshKit.Part(root, "Haze", MeshKit.Lathe("shaftUnit", new[]
                {
                    new Vector2(1f, 0f), new Vector2(0.92f, 0.5f), new Vector2(0.6f, 1f),
                }, 16, false), Mat.Smoke, Vector3.zero, Vector3.zero, new Vector3(radius * k, height, radius * k)).GetComponent<Renderer>();
                ArtLibrary.Tint(haze, new Color(color.r, color.g, color.b, 0.011f));
                haze.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // The opening: an iron-barred oculus in the ceiling, the light beyond kept dim enough that bloom does not
            // burn it to a white slab (it did until 2026-10-31), with fallen stone beneath.
            float gw = radius * 1.2f, gd = radius * 0.8f, top = height - 0.02f;
            var crack = MeshKit.Part(root, "Crack", MeshKit.Box(new Vector3(gw, 0.04f, gd)), Mat.Glow, Vector3.up * (top + 0.03f), new Vector3(0f, 25f, 0f));
            ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), color * 0.32f);
            var grate = new GameObject("Grate").transform;
            grate.SetParent(root, false);
            grate.localPosition = Vector3.up * top;
            grate.localRotation = Quaternion.Euler(0f, 25f, 0f);
            const float bar = 0.07f;
            // A frame, then a lattice: three bars one way, two the other.
            MeshKit.Part(grate, "Frame", MeshKit.Box(new Vector3(gw + 0.2f, 0.09f, 0.12f)), Mat.Dark, new Vector3(0f, 0f, gd * 0.5f + 0.04f));
            MeshKit.Part(grate, "Frame", MeshKit.Box(new Vector3(gw + 0.2f, 0.09f, 0.12f)), Mat.Dark, new Vector3(0f, 0f, -gd * 0.5f - 0.04f));
            MeshKit.Part(grate, "Frame", MeshKit.Box(new Vector3(0.12f, 0.09f, gd + 0.2f)), Mat.Dark, new Vector3(gw * 0.5f + 0.04f, 0f, 0f));
            MeshKit.Part(grate, "Frame", MeshKit.Box(new Vector3(0.12f, 0.09f, gd + 0.2f)), Mat.Dark, new Vector3(-gw * 0.5f - 0.04f, 0f, 0f));
            for (int i = 1; i <= 3; i++) MeshKit.Part(grate, "Bar", MeshKit.Box(new Vector3(bar, 0.06f, gd)), Mat.Dark, new Vector3(-gw * 0.5f + gw * i / 4f, -0.01f, 0f));
            for (int i = 1; i <= 2; i++) MeshKit.Part(grate, "Bar", MeshKit.Box(new Vector3(gw, 0.06f, bar)), Mat.Dark, new Vector3(0f, -0.02f, -gd * 0.5f + gd * i / 3f));
            foreach (var r in grate.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            MeshKit.MergeParts(grate);
            b.AddBox(floor + new Vector3(radius * 0.5f, 0.12f, -radius * 0.3f), new Vector3(0.5f, 0.24f, 0.4f), Mat.Rubble, Quaternion.Euler(0f, 30f, 8f));
            b.AddBox(floor + new Vector3(-radius * 0.4f, 0.08f, radius * 0.45f), new Vector3(0.35f, 0.16f, 0.3f), Mat.Rubble, Quaternion.Euler(0f, -20f, -6f));

            var spot = new GameObject("ShaftLight").AddComponent<Light>();
            spot.transform.SetParent(root, false);
            spot.transform.localPosition = Vector3.up * (height - 0.1f);
            spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            spot.type = LightType.Spot;
            spot.spotAngle = Mathf.Clamp(2f * Mathf.Atan(radius * 1.4f / height) * Mathf.Rad2Deg, 15f, 70f);
            spot.innerSpotAngle = spot.spotAngle * 0.5f;
            spot.range = height + 3f;
            spot.intensity = 6f;
            spot.color = color;
            spot.shadows = LightShadows.None;

            root.gameObject.AddComponent<ShaftMotes>().Init(radius, height, color);
        }

        /// <summary>One or two shafts in a room, kept off the centre so they light the edges of a fight.</summary>
        private static void PlaceLightShafts(DungeonLayout.Room room, Transform lights, MeshKit.Batcher b, System.Random rng)
        {
            if (room.Size.x < 14f || room.Size.y < 14f) return;
            int count = room.Kind == RoomKind.Boss ? 2 : room.Size.x * room.Size.y > 500f ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                float sx = (i == 0 ? 1f : -1f) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                var p = room.Center + new Vector3(sx * room.Size.x * (0.18f + (float)rng.NextDouble() * 0.12f), 0f, (float)(rng.NextDouble() - 0.5) * room.Size.y * 0.5f);
                if (room.Feature == RoomFeature.Chasm && room.Pit.Contains(new Vector2(p.x, p.z))) continue;
                float floorY = DungeonLayout.InLowArea(room, p) ? -(room.Feature == RoomFeature.SunkenCourt ? DungeonLayout.SunkenDepth : DungeonLayout.CisternDepth) : 0f;
                LightShaft(lights, new Vector3(p.x, floorY, p.z), room.Height - floorY, 1.3f + (float)rng.NextDouble() * 0.7f, theme.ShaftColor, b);
            }
        }

        // ------------------------------------------------------------------
        // Gallery: balconies with stairs
        // ------------------------------------------------------------------

        private static void DressGallery(DungeonLayout.Room room, MeshKit.Batcher b, MeshKit.Batcher dressing, Transform lights, System.Random rng)
        {
            var c = room.Center;
            float h = DungeonLayout.BalconyHeight, deep = DungeonLayout.BalconyDepth;
            const float rampLen = 6.5f, rampW = 2.2f, landing = 2.2f;
            foreach (int side in room.Balconies)
            {
                var d = DungeonLayout.Dir[side];
                var outward = new Vector3(d.x, 0f, d.y);
                bool alongX = side == 0 || side == 2;
                var along = alongX ? Vector3.right : Vector3.forward;
                float wallLen = alongX ? room.Size.x : room.Size.y;
                float half = (alongX ? room.Size.y : room.Size.x) / 2f;
                var wallLine = c + outward * half;

                // Split around a doorway on this wall, and around a hollow wall too: the stairs and posts were
                // built across a breach's mouth and sealed its tunnel (2026-10-22, the navmesh check).
                var gaps = new System.Collections.Generic.List<(float c, float half)>();
                if (room.Doors[side]) gaps.Add((0f, DungeonLayout.CorridorWidth / 2f + 0.9f));
                var hole = room.HoleOn(side);
                if (hole != null) gaps.Add((hole.Along, WallHole.Width / 2f + 0.9f));
                gaps.Sort((p, q) => p.c.CompareTo(q.c));
                var segments = new System.Collections.Generic.List<(float a, float b)>();
                float cursor = -wallLen / 2f;
                foreach (var (gc, gh) in gaps)
                {
                    if (gc - gh > cursor) segments.Add((cursor, gc - gh));
                    cursor = Mathf.Max(cursor, gc + gh);
                }
                if (cursor < wallLen / 2f) segments.Add((cursor, wallLen / 2f));

                foreach (var (s0, s1) in segments)
                {
                    float len = s1 - s0;
                    if (len < 4f) continue;
                    float mid = (s0 + s1) / 2f;
                    // The walkway: timber deck on stone corbels.
                    var deckCenter = wallLine - outward * (deep / 2f) + along * mid;
                    b.AddBox(deckCenter + Vector3.up * (h - 0.15f), alongX ? new Vector3(len, 0.3f, deep) : new Vector3(deep, 0.3f, len), Mat.Wood);
                    dressing.AddBox(deckCenter + Vector3.up * (h - 0.42f) - outward * (deep / 2f - 0.15f), alongX ? new Vector3(len, 0.24f, 0.3f) : new Vector3(0.3f, 0.24f, len), Mat.StoneWall);
                    // Posts under the front edge every ~4.5 m: cover underneath, and the gallery's rhythm.
                    int posts = Mathf.Max(2, Mathf.RoundToInt(len / 4.5f));
                    for (int i = 0; i <= posts; i++)
                    {
                        var p = wallLine - outward * (deep - 0.35f) + along * Mathf.Lerp(s0 + 0.4f, s1 - 0.4f, i / (float)posts);
                        b.AddBox(p + Vector3.up * (h - 0.3f) / 2f, new Vector3(0.4f, h - 0.3f, 0.4f), Mat.Wood);
                    }

                    // Stairs at one end (the end away from the room's corner nearest a door), with a landing.
                    bool stairsAtEnd = len >= rampLen + landing + 2f;
                    float stairTop = s1 - landing;
                    if (stairsAtEnd)
                    {
                        var strip = wallLine - outward * (deep + rampW / 2f);
                        var lowEnd = strip + along * (stairTop - rampLen);
                        var highEnd = strip + along * stairTop + Vector3.up * h;
                        Ramp(lowEnd, highEnd, rampW, b, dressing);
                        b.AddBox(strip + along * (stairTop + landing / 2f) + Vector3.up * (h - 0.15f), alongX ? new Vector3(landing, 0.3f, rampW) : new Vector3(rampW, 0.3f, landing), Mat.Wood);
                        b.AddBox(strip + along * (stairTop + landing / 2f) + Vector3.up * (h - 0.3f) / 2f, new Vector3(0.4f, h - 0.3f, 0.4f), Mat.Wood);
                    }

                    // Railing along the front edge (open where the landing meets it).
                    float railEnd = stairsAtEnd ? stairTop : s1;
                    var railLine = wallLine - outward * (deep - 0.08f);
                    if (railEnd - s0 > 0.5f)
                    {
                        dressing.AddBox(railLine + along * ((s0 + railEnd) / 2f) + Vector3.up * (h + 0.5f), alongX ? new Vector3(railEnd - s0, 0.08f, 0.12f) : new Vector3(0.12f, 0.08f, railEnd - s0), Mat.Wood);
                        for (float t = s0 + 0.3f; t < railEnd; t += 1.1f)
                            dressing.AddBox(railLine + along * t + Vector3.up * (h + 0.25f), new Vector3(0.08f, 0.5f, 0.08f), Mat.Wood);
                    }
                    // Candles along the gallery: warm pools up high.
                    for (float t = s0 + 3f; t < s1 - 2f; t += 7f)
                        Props.Candles(lights, wallLine - outward * 0.7f + along * t + Vector3.up * h, theme.TorchColor);
                }
            }
            // The floor below: a long table and benches down the middle.
            bool tableAlongX = room.Size.x >= room.Size.y;
            var tableSize = tableAlongX ? new Vector3(room.Size.x * 0.35f, 0.12f, 1.4f) : new Vector3(1.4f, 0.12f, room.Size.y * 0.35f);
            dressing.AddBox(c + Vector3.up * 0.85f, tableSize, Mat.Wood);
            foreach (float s in new[] { -0.4f, 0.4f })
            {
                var leg = tableAlongX ? new Vector3(s * room.Size.x * 0.4f, 0.4f, 0f) : new Vector3(0f, 0.4f, s * room.Size.y * 0.4f);
                dressing.AddBox(c + leg, tableAlongX ? new Vector3(0.2f, 0.8f, 1.1f) : new Vector3(1.1f, 0.8f, 0.2f), Mat.Wood);
            }
            Props.Chandelier(lights, b, c + Vector3.up * (room.Height - 2.5f), 2.5f, theme.TorchColor);
        }

        // ------------------------------------------------------------------
        // Sunken court
        // ------------------------------------------------------------------

        private static void DressSunken(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var low = room.Low;
            float y = -DungeonLayout.SunkenDepth;
            var mid = new Vector3(low.center.x, y, low.center.y);
            // An obelisk in the middle of the pit: cover, and something to circle.
            b.AddBox(mid + Vector3.up * 0.3f, new Vector3(2.4f, 0.6f, 2.4f), Mat.StoneWall);
            b.Add(MeshKit.Lathe("obelisk", new[] { new Vector2(0.7f, 0f), new Vector2(0.55f, 3.2f), new Vector2(0.3f, 4f), new Vector2(0f, 4.4f) }, 4),
                Mat.StoneWall, Matrix4x4.TRS(mid + Vector3.up * 0.6f, Quaternion.Euler(0f, 45f, 0f), Vector3.one));
            var rune = MeshKit.Part(lights, "ObeliskRune", MeshKit.Box(new Vector3(0.08f, 1.6f, 0.08f)), Mat.Glow, mid + new Vector3(0f, 2.4f, 0.5f));
            ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), theme.BrazierColor * 1.2f);
            // Braziers at the pit's corners light the fight from below.
            foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, -1f) })
                Props.Brazier(lights, new Vector3(low.center.x + sx * (low.width / 2f - 1.6f), y, low.center.y + sz * (low.height / 2f - 1.6f)), theme.BrazierColor, true);
            for (int i = 0; i < 4; i++)
                Props.BonePile(b, new Vector3(Mathf.Lerp(low.xMin + 2f, low.xMax - 2f, (float)rng.NextDouble()), y, Mathf.Lerp(low.yMin + 2f, low.yMax - 2f, (float)rng.NextDouble())), rng);
            // Stone balusters round the lip, between the ramps: you can still drop in between them.
            for (float t = 0.12f; t < 1f; t += 0.12f)
            {
                if (Mathf.Abs(t - 0.5f) < 0.1f) continue;
                foreach (var p in new[]
                {
                    new Vector3(Mathf.Lerp(low.xMin, low.xMax, t), 0f, low.yMin + 0.2f), new Vector3(Mathf.Lerp(low.xMin, low.xMax, t), 0f, low.yMax - 0.2f),
                    new Vector3(low.xMin + 0.2f, 0f, Mathf.Lerp(low.yMin, low.yMax, t)), new Vector3(low.xMax - 0.2f, 0f, Mathf.Lerp(low.yMin, low.yMax, t)),
                })
                    b.Add(MeshKit.Lathe("baluster", new[] { new Vector2(0.14f, 0f), new Vector2(0.09f, 0.3f), new Vector2(0.13f, 0.6f), new Vector2(0.1f, 0.85f), new Vector2(0.16f, 0.9f) }, 6), Mat.StoneWall, Matrix4x4.Translate(p));
            }
            DressRoomCorners(room, b, rng);
        }

        // ------------------------------------------------------------------
        // Cistern: flooded basin, stepping stones
        // ------------------------------------------------------------------

        private static void DressCistern(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, Transform root, System.Random rng)
        {
            var low = room.Low;
            float depth = DungeonLayout.CisternDepth;
            foreach (var spot in room.FeatureSpots)
            {
                float s = 1.8f + (float)rng.NextDouble() * 0.8f;
                b.AddBox(new Vector3(spot.x, (-depth + 0.15f) / 2f, spot.z), new Vector3(s, depth + 0.15f, s), Mat.StoneWall, Quaternion.Euler(0f, rng.Next(0, 45), 0f));
            }
            // Broken columns standing in the water.
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(Mathf.Lerp(low.xMin + 2f, low.xMax - 2f, (float)rng.NextDouble()), -depth, Mathf.Lerp(low.yMin + 2f, low.yMax - 2f, (float)rng.NextDouble()));
                b.Add(MeshKit.Cylinder(0.5f, 0.5f, 1.6f + (float)rng.NextDouble() * 2.5f, 8), Mat.StoneWall, Matrix4x4.Translate(p));
            }
            // Water, a little below the rim.
            var water = MeshKit.Part(root, "CisternWater", MeshKit.Box(new Vector3(low.width, 0.01f, low.height)), Mat.Water, new Vector3(low.center.x, -0.08f, low.center.y));
            water.AddComponent<WaterFlow>();
            ArtLibrary.Tint(water.GetComponent<Renderer>(), new Color(0.1f, 0.22f, 0.24f, 0.85f));
            water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // A teal glow from under the water, and drains spilling from the walls.
            foreach (var (sx, sz) in new[] { (0.3f, 0.3f), (-0.3f, -0.3f) })
            {
                var l = new GameObject("CisternGlow").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.position = new Vector3(low.center.x + sx * low.width, -0.3f, low.center.y + sz * low.height);
                l.type = LightType.Point;
                l.color = new Color(0.25f, 0.8f, 0.75f);
                l.range = 9f;
                l.intensity = 2.2f;
                l.shadows = LightShadows.None;
            }
            for (int side = 0; side < 4; side++)
            {
                if (room.Doors[side] || room.HoleOn(side) != null) continue;
                var d = DungeonLayout.Dir[side];
                var outward = new Vector3(d.x, 0f, d.y);
                float half = (side == 0 || side == 2 ? room.Size.y : room.Size.x) / 2f;
                var spout = room.Center + outward * (half - 0.3f) + Vector3.up * 2.4f;
                b.Add(MeshKit.Cylinder(0.28f, 0.28f, 0.6f, 8), Mat.StoneWall, Matrix4x4.TRS(spout, Quaternion.LookRotation(Vector3.up, -outward) * Quaternion.Euler(90f, 0f, 0f), Vector3.one));
                var fall = MeshKit.Part(root, "Spill", MeshKit.Box(new Vector3(0.35f, 2.4f, 0.12f)), Mat.Water, spout - outward * 0.9f + Vector3.down * 1.2f, Quaternion.LookRotation(outward).eulerAngles);
                fall.AddComponent<WaterFlow>().Falling = true;
                ArtLibrary.Tint(fall.GetComponent<Renderer>(), new Color(0.35f, 0.55f, 0.6f, 0.6f));
                fall.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            DressRoomCorners(room, b, rng);
        }

        // ------------------------------------------------------------------
        // Pendulum hall
        // ------------------------------------------------------------------

        private static void DressPendulums(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, Transform root)
        {
            bool alongX = DungeonLayout.PendulumsAlongX(room);
            for (int i = 0; i < room.FeatureSpots.Count; i++)
            {
                var spot = room.FeatureSpots[i];
                // A heavy beam across the room carries each blade's pivot.
                var beamSize = alongX ? new Vector3(0.6f, 0.6f, room.Size.y) : new Vector3(room.Size.x, 0.6f, 0.6f);
                b.AddBox(new Vector3(spot.x, room.Height - 0.5f, spot.z), beamSize, Mat.Wood);
                Pendulum.Build(root, room, i);
                // Red warning lamps on the beam's ends.
                var across = alongX ? Vector3.forward : Vector3.right;
                float half = (alongX ? room.Size.y : room.Size.x) / 2f - 0.6f;
                foreach (float s in new[] { -1f, 1f })
                    Props.WallTorch(lights, new Vector3(spot.x, 0f, spot.z) + across * s * half + Vector3.up * 2.4f, -across * s, false, new Color(1f, 0.35f, 0.2f));
            }
            // Scored floor: the blades have cut grooves where they pass.
            foreach (var spot in room.FeatureSpots)
            {
                var size = alongX ? new Vector3(0.25f, 0.02f, room.Size.y * 0.55f) : new Vector3(room.Size.x * 0.55f, 0.02f, 0.25f);
                b.AddBox(new Vector3(spot.x, 0.01f, spot.z), size, Mat.Dark);
            }
        }

        // ------------------------------------------------------------------
        // Library: shelf aisles that break line of sight
        // ------------------------------------------------------------------

        private static readonly Color[] BookColours =
        {
            new Color(0.66f, 0.17f, 0.12f), new Color(0.22f, 0.4f, 0.22f), new Color(0.22f, 0.27f, 0.55f),
            new Color(0.58f, 0.38f, 0.2f), new Color(0.2f, 0.17f, 0.16f), new Color(0.72f, 0.6f, 0.38f),
        };

        private static void DressLibrary(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            float shelfH = Mathf.Min(room.Height - 2.5f, 5.2f);
            foreach (var r in DungeonLayout.LibraryShelves(room))
            {
                var c = new Vector3(r.center.x, 0f, r.center.y);
                var size = new Vector3(r.width, shelfH, r.height);
                bool longX = r.width > r.height;
                // The case: back board, sides, shelves; then rows of books, a ladder now and then.
                b.AddBox(c + Vector3.up * shelfH / 2f, new Vector3(size.x * (longX ? 1f : 0.35f), shelfH, size.z * (longX ? 0.35f : 1f)), Mat.Wood);
                for (float y = 0.4f; y < shelfH - 0.3f; y += 0.85f)
                {
                    b.AddBox(c + Vector3.up * y, new Vector3(size.x + (longX ? 0f : 0.05f), 0.06f, size.z + (longX ? 0.05f : 0f)), Mat.Wood);
                    foreach (float face in new[] { -1f, 1f })
                    {
                        var off = longX ? new Vector3(0f, 0f, face * 0.22f) : new Vector3(face * 0.22f, 0f, 0f);
                        float run = longX ? size.x - 0.2f : size.z - 0.2f;
                        // Rows of old bindings (2026-10-31: they were pale cream slabs): thin spines in oxblood, forest,
                        // navy, umber and black, of varied height, a gap now and then, and the odd one leaning.
                        float u = -run / 2f;
                        while (u < run / 2f - 0.25f)
                        {
                            if (rng.NextDouble() < 0.07) { u += 0.25f + (float)rng.NextDouble() * 0.3f; continue; }
                            float bw = 0.09f + (float)rng.NextDouble() * 0.1f, bh = 0.46f + (float)rng.NextDouble() * 0.28f;
                            bool lean = rng.NextDouble() < 0.05;
                            var at = c + off + Vector3.up * (y + 0.03f + bh / 2f) + (longX ? new Vector3(u + bw / 2f, 0f, 0f) : new Vector3(0f, 0f, u + bw / 2f));
                            var tilt = lean ? Quaternion.Euler(longX ? 0f : 14f, 0f, longX ? -14f : 0f) : Quaternion.identity;
                            b.AddBox(at, longX ? new Vector3(bw, bh, 0.22f) : new Vector3(0.22f, bh, bw), Mat.Leather, BookColours[rng.Next(BookColours.Length)], tilt);
                            u += bw + 0.01f + (lean ? 0.08f : 0f);
                        }
                    }
                }
                foreach (float end in new[] { -0.5f, 0.5f })
                    b.AddBox(c + Vector3.up * shelfH / 2f + (longX ? new Vector3(end * size.x, 0f, 0f) : new Vector3(0f, 0f, end * size.z)), longX ? new Vector3(0.1f, shelfH + 0.1f, 0.75f) : new Vector3(0.75f, shelfH + 0.1f, 0.1f), Mat.Wood);
                if (rng.NextDouble() < 0.35)
                {
                    var face = longX ? new Vector3(0f, 0f, 0.55f) : new Vector3(0.55f, 0f, 0f);
                    b.AddBox(c + face + Vector3.up * shelfH * 0.45f, new Vector3(0.5f, shelfH * 0.9f, 0.05f), Mat.Wood, Quaternion.Euler(longX ? -12f : 0f, longX ? 0f : 90f, longX ? 0f : 12f));
                }
            }
            // Reading desks in the central aisle, scattered pages, and candles hung in the air.
            bool ax = room.Size.x >= room.Size.y;
            float len = ax ? room.Size.x : room.Size.y;
            for (float t = -len / 2f + 6f; t <= len / 2f - 6f; t += 7f)
            {
                if (Mathf.Abs(t) < DungeonLayout.CorridorWidth / 2f + 1.8f) continue;
                var at = room.Center + (ax ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
                b.AddBox(at + Vector3.up * 0.55f, new Vector3(1.1f, 0.08f, 0.7f), Mat.Wood, Quaternion.Euler(0f, ax ? 0f : 90f, 0f));
                b.AddBox(at + Vector3.up * 0.27f, new Vector3(0.12f, 0.55f, 0.12f), Mat.Wood);
                b.AddBox(at + Vector3.up * 0.62f, new Vector3(0.4f, 0.05f, 0.3f), Mat.Bone, Quaternion.Euler(0f, (float)rng.NextDouble() * 40f - 20f, 0f));
                Props.Candles(lights, at + Vector3.up * 0.6f + (ax ? new Vector3(0.38f, 0f, 0.2f) : new Vector3(0.2f, 0f, 0.38f)), theme.TorchColor);
            }
            for (int i = 0; i < 14; i++)
            {
                var p = room.Center + new Vector3(((float)rng.NextDouble() - 0.5f) * (room.Size.x - 4f), 0.012f, ((float)rng.NextDouble() - 0.5f) * (room.Size.y - 4f));
                b.AddBox(p, new Vector3(0.22f, 0.01f, 0.3f), Mat.Bone, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            }
            for (int i = 0; i < 6; i++)
            {
                var p = room.Center + new Vector3(((float)rng.NextDouble() - 0.5f) * (room.Size.x - 6f), room.Height * 0.62f + (float)rng.NextDouble(), ((float)rng.NextDouble() - 0.5f) * (room.Size.y - 6f));
                Props.AddFlame(lights, p, 0.6f, theme.TorchColor, i % 2 == 0 ? 0.9f : 0f, 6f, false);
            }
        }

        // ------------------------------------------------------------------
        // Pyre hall: a great fire on a beat
        // ------------------------------------------------------------------

        private static void DressPyre(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            PyreFire.Build(lights, room);
            // Scorched flagstones in a wide ring, soot up the walls, and gibbets of the burned.
            b.AddBox(room.Center + Vector3.up * 0.011f, new Vector3(PyreFire.EruptRadius * 1.6f, 0.01f, PyreFire.EruptRadius * 1.6f), Mat.Dark, Quaternion.Euler(0f, 45f, 0f));
            b.AddBox(room.Center + Vector3.up * 0.012f, new Vector3(PyreFire.EruptRadius * 1.6f, 0.01f, PyreFire.EruptRadius * 1.6f), Mat.Dark);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var at = room.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (Mathf.Min(room.Size.x, room.Size.y) / 2f - 2.2f);
                b.AddBox(at + Vector3.up * 1.8f, new Vector3(0.18f, 3.6f, 0.18f), Mat.Wood);
                b.AddBox(at + new Vector3(0f, 3.5f, 0f) + (room.Center - at).normalized * 0.4f, new Vector3(0.9f, 0.14f, 0.14f), Mat.Wood, Quaternion.LookRotation(room.Center - at) * Quaternion.Euler(0f, 90f, 0f));
                b.AddBox(at + new Vector3(0f, 2.6f, 0f) + (room.Center - at).normalized * 0.8f, new Vector3(0.5f, 1.1f, 0.5f), Mat.Steel);
                Props.Brazier(lights, at + (room.Center - at).normalized * 1.6f, theme.TorchColor, false);
            }
        }

        // ------------------------------------------------------------------
        // Gauntlet: a wave of spikes down the hall
        // ------------------------------------------------------------------

        private static void DressGauntlet(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            SpikeGauntlet.Build(lights, room);
            // Bloodied grates at each end, and chains hanging over the rows.
            bool ax = SpikeGauntlet.AlongX(room);
            float wid = ax ? room.Size.y : room.Size.x;
            for (int i = 0; i < SpikeGauntlet.Rows(room); i += 2)
            {
                var c = SpikeGauntlet.RowCenter(room, i);
                foreach (float s in new[] { -0.3f, 0.3f })
                {
                    var at = c + (ax ? new Vector3(0f, 0f, s * wid) : new Vector3(s * wid, 0f, 0f));
                    b.AddBox(at + Vector3.up * (room.Height - 1.4f), new Vector3(0.05f, 2.8f, 0.05f), Mat.Steel);
                    b.AddBox(at + Vector3.up * (room.Height - 2.9f), new Vector3(0.22f, 0.22f, 0.22f), Mat.Steel, Quaternion.Euler(45f, 45f, 0f));
                }
            }
            for (int i = 0; i < 8; i++)
            {
                var p = room.Center + new Vector3(((float)rng.NextDouble() - 0.5f) * (room.Size.x - 3f), 0.06f, ((float)rng.NextDouble() - 0.5f) * (room.Size.y - 3f));
                b.AddBox(p, new Vector3(0.6f + (float)rng.NextDouble() * 0.6f, 0.012f, 0.5f), Mat.Dark, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            }
        }

        // ------------------------------------------------------------------
        // Elevation (2026-10-20): a terrace, a ziggurat
        // ------------------------------------------------------------------

        private static void DressTerrace(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            foreach (var (rr, top) in room.Raised)
            {
                // A parapet along the open edges, broken where the ramps arrive; braziers at its ends.
                var c = new Vector3(rr.center.x, top, rr.center.y);
                bool longX = rr.width >= rr.height;
                float len = longX ? rr.width : rr.height;
                var frontDir = (room.Center - new Vector3(rr.center.x, 0f, rr.center.y));
                frontDir = longX ? new Vector3(0f, 0f, Mathf.Sign(frontDir.z)) : new Vector3(Mathf.Sign(frontDir.x), 0f, 0f);
                var front = c + frontDir * ((longX ? rr.height : rr.width) / 2f - 0.2f);
                var along = longX ? Vector3.right : Vector3.forward;
                for (float t = -len / 2f + 0.6f; t < len / 2f - 0.6f; t += 1.2f)
                {
                    var p = front + along * t;
                    bool atRamp = room.Ramps.Exists(rp => Vector3.Distance(new Vector3(rp.high.x, 0f, rp.high.z), new Vector3(p.x, 0f, p.z)) < 1.8f);
                    if (atRamp) continue;
                    b.AddBox(p + Vector3.up * 0.45f, longX ? new Vector3(1.1f, 0.9f, 0.4f) : new Vector3(0.4f, 0.9f, 1.1f), Mat.StoneWall);
                    if (rng.NextDouble() < 0.35) b.AddBox(p + Vector3.up * 1.0f, longX ? new Vector3(0.5f, 0.2f, 0.45f) : new Vector3(0.45f, 0.2f, 0.5f), Mat.StoneWall);
                }
                foreach (float s in new[] { -1f, 1f }) Props.Brazier(lights, front - frontDir * 0.9f + along * (s * (len / 2f - 1.2f)), theme.TorchColor, false);
                // Banners on the wall above the terrace.
                for (float t = -len / 2f + 3f; t < len / 2f - 2f; t += 6f)
                    b.AddBox(c - frontDir * ((longX ? rr.height : rr.width) / 2f - 0.15f) + along * t + Vector3.up * 2.6f, longX ? new Vector3(1.1f, 2.4f, 0.05f) : new Vector3(0.05f, 2.4f, 1.1f), Mat.Cloth);
            }
            DressRoom(room, b, lights, rng);
        }

        private static void DressZiggurat(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            // A great brazier on the summit, bones heaped on the tiers, and four standing torches round the base.
            if (room.FeatureSpots.Count > 0) Props.Brazier(lights, room.FeatureSpots[0], theme.TorchColor, true);
            for (int i = 0; i < room.Raised.Count; i++)
            {
                var (rr, top) = room.Raised[i];
                for (int k = 0; k < 4; k++)
                {
                    float a = (k * 90f + 45f) * Mathf.Deg2Rad;
                    var p = new Vector3(rr.center.x, top, rr.center.y) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (rr.width * 0.5f - 0.6f) * 1.41f * 0.7f;
                    if (rng.NextDouble() < 0.5) b.AddBox(p + Vector3.up * 0.12f, new Vector3(0.5f, 0.24f, 0.3f), Mat.Bone, Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f));
                }
            }
            var baseR = room.Raised.Count > 0 ? room.Raised[0].r : new Rect(room.Center.x, room.Center.z, 1f, 1f);
            foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
                Props.Brazier(lights, new Vector3(baseR.center.x + sx * (baseR.width / 2f + 1.6f), 0f, baseR.center.y + sz * (baseR.height / 2f + 1.6f)), theme.TorchColor, false);
        }

        // ------------------------------------------------------------------
        // Special side rooms: carpet, candles and a mood for each
        // ------------------------------------------------------------------

        /// <summary>The Panoply: an empty armour stand on a dais at the back, lit cold from above, racks either side.</summary>
        private static void DressPanoply(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights)
        {
            if (room.FeatureSpots.Count == 0) return;
            var s = room.FeatureSpots[0];
            var face = room.Center - s; face.y = 0f;
            var rot = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : Vector3.forward);
            var right = rot * Vector3.right;
            b.AddBox(s + Vector3.up * 0.15f, new Vector3(2.4f, 0.3f, 2.4f), Mat.StoneWall, rot);
            b.AddBox(s + Vector3.up * 0.32f, new Vector3(2f, 0.04f, 2f), Mat.Cloth, new Color(0.25f, 0.12f, 0.3f), rot);
            // The stand: post, shoulder bar, a hollow breastplate and a helm on the peg.
            b.AddBox(s + Vector3.up * 1.2f, new Vector3(0.1f, 1.8f, 0.1f), Mat.Wood, rot);
            b.AddBox(s + Vector3.up * 1.85f, new Vector3(0.9f, 0.08f, 0.08f), Mat.Wood, rot);
            b.AddBox(s + Vector3.up * 1.55f + rot * Vector3.forward * 0.08f, new Vector3(0.62f, 0.62f, 0.22f), Mat.Steel, new Color(0.55f, 0.55f, 0.6f), rot);
            foreach (float k in new[] { -1f, 1f })
                b.AddBox(s + Vector3.up * 1.8f + right * k * 0.38f, new Vector3(0.26f, 0.12f, 0.3f), Mat.Steel, new Color(0.5f, 0.5f, 0.55f), rot * Quaternion.Euler(0f, 0f, k * -18f));
            b.Add(MeshKit.Ball(0.17f, 10, 8), Mat.Steel, Matrix4x4.TRS(s + Vector3.up * 2.12f, rot, new Vector3(1f, 1.15f, 1.1f)));
            // Weapon racks either side of the dais.
            foreach (float k in new[] { -1f, 1f })
                for (int w = 0; w < 3; w++)
                    b.AddBox(s + right * k * (1.8f + w * 0.3f) + Vector3.up * 1f, new Vector3(0.06f, 2f, 0.06f), Mat.Steel, rot * Quaternion.Euler(0f, 0f, k * 8f));
            // A cold shaft of light on the stand.
            var l = new GameObject("PanoplyLight").AddComponent<Light>();
            l.transform.SetParent(lights, false);
            l.transform.position = s + Vector3.up * 4f;
            l.type = LightType.Point;
            l.color = new Color(0.78f, 0.74f, 1f);
            l.range = 6.5f;
            l.intensity = 2.2f;
            l.shadows = LightShadows.None;
        }

        private static void DressSpecial(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            var mood = room.Feature == RoomFeature.Treasury ? new Color(1f, 0.85f, 0.5f)
                : room.Feature == RoomFeature.Mirror ? new Color(0.6f, 0.35f, 1f)
                : room.Feature == RoomFeature.Oath ? new Color(0.55f, 0.75f, 1f)
                : room.Feature == RoomFeature.Statues ? new Color(1f, 0.8f, 0.45f)
                : room.Feature == RoomFeature.Chimes ? new Color(0.6f, 0.85f, 1f)
                : room.Feature == RoomFeature.Mirrors ? new Color(1f, 0.9f, 0.65f)
                : room.Feature == RoomFeature.Scales ? new Color(1f, 0.72f, 0.42f)
                : room.Feature == RoomFeature.Draught ? new Color(0.75f, 0.55f, 1f)
                : room.Feature == RoomFeature.Proving ? new Color(1f, 0.6f, 0.3f)
                : room.Feature == RoomFeature.Armoury ? new Color(1f, 0.7f, 0.4f)
                : room.Feature == RoomFeature.Panoply ? new Color(0.75f, 0.7f, 1f) : theme.TorchColor;
            // A long carpet from the door to the feature.
            int entry = 0;
            for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
            bool alongX = entry == 1 || entry == 3;
            // (The puzzle rooms keep their floor bare: the sigil and the plates are the floor.)
            if (room.Feature != RoomFeature.Statues && room.Feature != RoomFeature.Chimes && room.Feature != RoomFeature.Mirrors && room.Feature != RoomFeature.Scales)
            b.AddBox(c + Vector3.up * 0.012f, alongX ? new Vector3(room.Size.x - 2f, 0.02f, 2.2f) : new Vector3(2.2f, 0.02f, room.Size.y - 2f), Mat.Cloth);
            Props.Candelabra(lights, b, c + new Vector3(2.8f, 0f, 2.8f), mood);
            Props.Candelabra(lights, b, c + new Vector3(-2.8f, 0f, -2.8f), mood);
            if (room.Feature == RoomFeature.Armoury)
            {
                // Racks of old weapons along the walls.
                for (int k = 0; k < 6; k++)
                {
                    float t = -room.Size.x / 2f + 2f + k * (room.Size.x - 4f) / 5f;
                    b.AddBox(new Vector3(c.x + t, 1f, c.z + room.Size.y / 2f - 0.3f), new Vector3(0.08f, 2f, 0.1f), Mat.Steel, Quaternion.Euler(0f, 0f, (k % 2 == 0 ? 6f : -6f)));
                }
            }
            if (room.Feature == RoomFeature.Treasury)
                for (int k = 0; k < 10; k++)
                    b.AddBox(c + new Vector3(((float)rng.NextDouble() - 0.5f) * (room.Size.x - 3f), 0.03f, ((float)rng.NextDouble() - 0.5f) * (room.Size.y - 3f)), new Vector3(0.12f, 0.03f, 0.12f), Mat.Gold);
        }

        // ------------------------------------------------------------------
        // Ritual chamber
        // ------------------------------------------------------------------

        private static void DressRitual(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            var violet = new Color(0.7f, 0.3f, 1f);
            float r = Mathf.Min(room.Size.x, room.Size.y) * 0.3f;
            // The circle: two glowing rings and spokes to each brazier.
            foreach (float k in new[] { 1f, 0.55f })
            {
                var ring = MeshKit.Part(lights, "RitualRing", MeshKit.Lathe("ritualRing", new[] { new Vector2(0.97f, 0f), new Vector2(1f, 0.01f), new Vector2(0.97f, 0.02f) }, 48, false),
                    Mat.Glow, c + Vector3.up * 0.02f, Vector3.zero, new Vector3(r * k + 1f, 1f, r * k + 1f));
                ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), violet * 0.9f);
                ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var spot in room.FeatureSpots)
            {
                var to = spot - c;
                var spoke = MeshKit.Part(lights, "RitualSpoke", MeshKit.Box(new Vector3(0.08f, 0.01f, to.magnitude)), Mat.Glow, c + to / 2f + Vector3.up * 0.02f, Quaternion.LookRotation(to).eulerAngles);
                ArtLibrary.SetGlow(spoke.GetComponent<Renderer>(), violet * 0.7f);
                Props.Candles(lights, spot + Quaternion.Euler(0f, 90f, 0f) * to.normalized * 1.4f, new Color(0.8f, 0.5f, 1f));
            }
            // A black altar at the centre with a skull on it.
            b.AddBox(c + Vector3.up * 0.5f, new Vector3(1.8f, 1f, 1.1f), Mat.Dark);
            b.Add(MeshKit.Ball(0.18f, 8, 6), Mat.Bone, Matrix4x4.Translate(c + Vector3.up * 1.15f));
            var glow = new GameObject("RitualGlow").AddComponent<Light>();
            glow.transform.SetParent(lights, false);
            glow.transform.position = c + Vector3.up * 1.6f;
            glow.type = LightType.Point;
            glow.color = violet;
            glow.range = r * 2.2f;
            glow.intensity = 3f;
            glow.shadows = LightShadows.Soft;
            DressRoomCorners(room, b, rng);
        }

        /// <summary>The same corner clutter ordinary rooms get, for feature rooms that want it.</summary>
        private static void DressRoomCorners(DungeonLayout.Room room, MeshKit.Batcher b, System.Random rng)
        {
            var c = room.Center;
            float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
            for (int corner = 0; corner < 4; corner++)
            {
                if (rng.NextDouble() < 0.45) continue;
                float sx = (corner & 1) == 0 ? -1f : 1f, sz = (corner & 2) == 0 ? -1f : 1f;
                var p = c + new Vector3(sx * (hx - 1.3f), 0f, sz * (hz - 1.3f));
                if (rng.Next(2) == 0) { Props.Barrel(b, p); Props.Barrel(b, p + new Vector3(-sx * 0.9f, 0f, 0f)); }
                else Props.Rubble(b, p, rng);
            }
        }
    }
}
