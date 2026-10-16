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

            // The crack itself: a ragged bright gap in the ceiling, with fallen stone beneath.
            var crack = MeshKit.Part(root, "Crack", MeshKit.Box(new Vector3(radius * 1.2f, 0.05f, radius * 0.7f)), Mat.Glow, Vector3.up * (height - 0.02f), new Vector3(0f, 25f, 0f));
            ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), color * 1.4f);
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

                // Split around a doorway on this wall.
                var segments = new System.Collections.Generic.List<(float a, float b)>();
                if (room.Doors[side])
                {
                    float gap = DungeonLayout.CorridorWidth / 2f + 0.9f;
                    segments.Add((-wallLen / 2f, -gap));
                    segments.Add((gap, wallLen / 2f));
                }
                else segments.Add((-wallLen / 2f, wallLen / 2f));

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
