using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Turns a <see cref="DungeonLayout"/> into geometry, lights and props.
    ///
    /// Runs identically on every peer (the layout is deterministic, and so is this — all
    /// randomness comes from each room's <c>DecorSeed</c>). Only the server additionally
    /// bakes a navmesh over the result, since only the server runs enemy AI.
    /// </summary>
    public static class DungeonBuilder
    {
        private const float WallThickness = 0.8f;
        private const float FloorThickness = 0.4f;

        /// <summary>The theme of the floor being built. Set at the top of <see cref="Build"/>.</summary>
        private static DungeonTheme theme = DungeonTheme.ByDepth[0];

        public static GameObject Build(DungeonLayout layout)
        {
            theme = DungeonTheme.ForDepth(layout.Depth);
            var root = new GameObject("Dungeon (seed " + layout.Seed + ", depth " + layout.Depth + ")");
            var structure = new MeshKit.Batcher();
            var dressing = new MeshKit.Batcher();
            var lights = new GameObject("Lights").transform;
            lights.SetParent(root.transform, false);

            foreach (var room in layout.Rooms) BuildRoom(room, structure, dressing, lights);
            Props.Merchant(lights, layout.MerchantPosition, layout.StartRoom.Center, dressing);
            foreach (var link in layout.Links) BuildCorridor(layout.Rooms[link.A], layout.Rooms[link.B], link.Dir, structure, dressing, lights);

            System.Func<string, Color> tint = mat => mat == Mat.StoneWall ? theme.StoneTint : mat == Mat.Flagstone ? theme.FloorTint : Color.white;
            structure.Build(root.transform, "Structure", collide: true, tint);
            dressing.Build(root.transform, "Dressing", collide: true, tint);

            if (theme.WaterDepth > 0f) BuildWater(layout, root.transform);

            ApplyAtmosphere(theme);
            return root;
        }

        /// <summary>
        /// Standing water: one sheet across the whole floor's footprint at the water line.
        /// Walls hide it outside the rooms, so it needs no per-room fitting. No collider —
        /// you wade through it.
        /// </summary>
        private static void BuildWater(DungeonLayout layout, Transform parent)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var room in layout.Rooms)
            {
                min = Vector2.Min(min, new Vector2(room.Center.x - room.Size.x, room.Center.z - room.Size.y));
                max = Vector2.Max(max, new Vector2(room.Center.x + room.Size.x, room.Center.z + room.Size.y));
            }

            var size = max - min;
            var center = new Vector3((min.x + max.x) / 2f, theme.WaterDepth, (min.y + max.y) / 2f);
            var water = MeshKit.Part(parent, "Water", MeshKit.Box(new Vector3(size.x, 0.01f, size.y)), Mat.Water, center);
            var r = water.GetComponent<Renderer>();
            ArtLibrary.Tint(r, theme.WaterColor);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Near-black ambient and dense fog: the dark is what makes torchlight mean
        /// something. Shadows read as cool blue-grey (see the post-processing split tone),
        /// firelight as warm — that contrast does most of the mood work.
        /// </summary>
        public static void ApplyAtmosphere(DungeonTheme t = null)
        {
            t ??= DungeonTheme.ByDepth[0];
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = t.AmbientSky;
            RenderSettings.ambientEquatorColor = t.AmbientEquator;
            RenderSettings.ambientGroundColor = t.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = t.Fog;
            RenderSettings.fogDensity = t.FogDensity;

            // There is no sky underground. Unity's default reflections come from a bright
            // procedural sky, which makes wet floors look like ice and steel look like
            // chrome; keep only a trace of it so torch highlights still glint.
            RenderSettings.reflectionIntensity = 0.12f;

            // The camera clears to the fog colour, so the far darkness matches the murk.
            foreach (var cam in Camera.allCameras) cam.backgroundColor = t.Fog;
        }

        // ------------------------------------------------------------------
        // Rooms
        // ------------------------------------------------------------------

        private static void BuildRoom(DungeonLayout.Room room, MeshKit.Batcher structure, MeshKit.Batcher dressing, Transform lights)
        {
            var c = room.Center;
            float w = room.Size.x, d = room.Size.y, h = room.Height;
            string floorMat = Mat.Flagstone;

            structure.AddBox(c + Vector3.down * FloorThickness / 2f, new Vector3(w + WallThickness * 2f, FloorThickness, d + WallThickness * 2f), floorMat);
            structure.AddBox(c + Vector3.up * (h + FloorThickness / 2f), new Vector3(w + WallThickness * 2f, FloorThickness, d + WallThickness * 2f), Mat.StoneWall);

            for (int side = 0; side < 4; side++) BuildWall(room, side, structure);

            var rng = new System.Random(room.DecorSeed);

            // Ceiling beams across the shorter span, for scale and a sense of weight.
            bool beamsAlongX = w < d;
            float span = beamsAlongX ? d : w;
            for (float t = -span / 2f + 3f; t < span / 2f - 1f; t += 4f)
            {
                var pos = c + Vector3.up * (h - 0.3f) + (beamsAlongX ? new Vector3(0f, 0f, t) : new Vector3(t, 0f, 0f));
                dressing.AddBox(pos, beamsAlongX ? new Vector3(w, 0.5f, 0.4f) : new Vector3(0.4f, 0.5f, d), Mat.Wood);
            }

            PlaceWallTorches(room, lights, rng);

            switch (room.Kind)
            {
                case RoomKind.Boss: DressBossRoom(room, dressing, lights); break;
                case RoomKind.Start: DressStartRoom(room, dressing, lights); break;
                default: DressRoom(room, dressing, lights, rng); break;
            }
        }

        private static void BuildWall(DungeonLayout.Room room, int side, MeshKit.Batcher b)
        {
            var c = room.Center;
            float h = room.Height;
            bool alongX = side == 0 || side == 2;                     // N/S walls run along X
            float length = (alongX ? room.Size.x : room.Size.y) + WallThickness * 2f;
            var dir = DungeonLayout.Dir[side];
            float offset = (alongX ? room.Size.y : room.Size.x) / 2f + WallThickness / 2f;
            var wallCenter = c + new Vector3(dir.x, 0f, dir.y) * offset;

            Vector3 Size(float len, float height) => alongX ? new Vector3(len, height, WallThickness) : new Vector3(WallThickness, height, len);
            Vector3 Along(float t) => alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t);

            if (!room.Doors[side])
            {
                b.AddBox(wallCenter + Vector3.up * h / 2f, Size(length, h), Mat.StoneWall);
            }
            else
            {
                float door = DungeonLayout.CorridorWidth;
                float seg = (length - door) / 2f;
                b.AddBox(wallCenter + Along(-(door + seg) / 2f) + Vector3.up * h / 2f, Size(seg, h), Mat.StoneWall);
                b.AddBox(wallCenter + Along((door + seg) / 2f) + Vector3.up * h / 2f, Size(seg, h), Mat.StoneWall);
                float lintel = h - DungeonLayout.CorridorHeight;
                b.AddBox(wallCenter + Vector3.up * (DungeonLayout.CorridorHeight + lintel / 2f) + Along(0f), Size(door, lintel), Mat.StoneWall);

                // Door frame: two jambs and a header in heavy timber.
                var inward = -new Vector3(dir.x, 0f, dir.y) * (WallThickness / 2f + 0.1f);
                b.AddBox(wallCenter + inward + Along(-door / 2f - 0.15f) + Vector3.up * DungeonLayout.CorridorHeight / 2f, Size(0.3f, DungeonLayout.CorridorHeight) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.Wood);
                b.AddBox(wallCenter + inward + Along(door / 2f + 0.15f) + Vector3.up * DungeonLayout.CorridorHeight / 2f, Size(0.3f, DungeonLayout.CorridorHeight) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.Wood);
                b.AddBox(wallCenter + inward + Vector3.up * (DungeonLayout.CorridorHeight + 0.15f), Size(door + 0.9f, 0.3f) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.Wood);
            }

            // Plinth along the base of the wall, so walls meet the floor with a line
            // rather than a seam.
            var plinthIn = -new Vector3(dir.x, 0f, dir.y) * (WallThickness / 2f + 0.1f);
            if (!room.Doors[side])
            {
                b.AddBox(wallCenter + plinthIn + Vector3.up * 0.15f, Size(length - WallThickness * 2f, 0.3f) + (alongX ? new Vector3(0, 0, -0.6f) : new Vector3(-0.6f, 0, 0)), Mat.StoneWall);
            }
        }

        private static void PlaceWallTorches(DungeonLayout.Room room, Transform lights, System.Random rng)
        {
            float spacing = room.Kind == RoomKind.Boss ? 6.5f : 7f;
            int shadowBudget = room.Kind == RoomKind.Boss ? 0 : 2; // boss room uses braziers for shadows
            float y = Mathf.Min(2.6f, room.Height - 1f);

            for (int side = 0; side < 4; side++)
            {
                bool alongX = side == 0 || side == 2;
                float len = alongX ? room.Size.x : room.Size.y;
                var dir = DungeonLayout.Dir[side];
                var outward = -new Vector3(dir.x, 0f, dir.y);
                float offset = (alongX ? room.Size.y : room.Size.x) / 2f;

                int count = Mathf.Max(1, Mathf.FloorToInt(len / spacing));
                for (int i = 0; i < count; i++)
                {
                    float t = -len / 2f + (i + 0.5f) * len / count;
                    if (room.Doors[side] && Mathf.Abs(t) < DungeonLayout.CorridorWidth) t += t >= 0f ? DungeonLayout.CorridorWidth : -DungeonLayout.CorridorWidth;
                    if (Mathf.Abs(t) > len / 2f - 0.8f) continue;

                    var p = room.Center + new Vector3(dir.x, 0f, dir.y) * offset + (alongX ? new Vector3(t, y, 0f) : new Vector3(0f, y, t));
                    Props.WallTorch(lights, p, outward, shadowBudget-- > 0, theme.TorchColor);
                }
            }
        }

        private static void DressRoom(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            float hx = room.Size.x / 2f, hz = room.Size.y / 2f;

            // Large rooms get pillars: line-of-sight breaks for kiting and hiding.
            if (room.Size.x >= 16f && room.Size.y >= 16f)
            {
                float px = hx * 0.5f, pz = hz * 0.5f;
                Props.Pillar(b, c + new Vector3(px, 0f, pz), room.Height);
                Props.Pillar(b, c + new Vector3(-px, 0f, pz), room.Height);
                Props.Pillar(b, c + new Vector3(px, 0f, -pz), room.Height);
                Props.Pillar(b, c + new Vector3(-px, 0f, -pz), room.Height);
            }

            // Clutter in the corners, away from doors and the middle where fights happen.
            for (int corner = 0; corner < 4; corner++)
            {
                if (rng.NextDouble() < 0.35) continue;
                float sx = (corner & 1) == 0 ? -1f : 1f, sz = (corner & 2) == 0 ? -1f : 1f;
                var p = c + new Vector3(sx * (hx - 1.3f), 0f, sz * (hz - 1.3f));

                switch (rng.Next(4))
                {
                    case 0:
                        Props.Crate(b, p, 1.1f, rng.Next(-20, 20));
                        Props.Crate(b, p + new Vector3(-sx * 1.2f, 0f, 0.2f), 0.9f, rng.Next(-30, 30));
                        Props.Crate(b, p + Vector3.up * 1.1f, 0.7f, rng.Next(0, 90));
                        break;
                    case 1:
                        Props.Barrel(b, p);
                        Props.Barrel(b, p + new Vector3(-sx * 0.9f, 0f, 0f));
                        Props.Barrel(b, p + new Vector3(0f, 0f, -sz * 0.9f));
                        break;
                    case 2:
                        Props.Rubble(b, p, rng);
                        break;
                    default:
                        Props.BonePile(b, p, rng);
                        break;
                }
            }

            if (rng.NextDouble() < 0.5) Props.BonePile(b, c + new Vector3((float)rng.NextDouble() * 4f - 2f, 0f, (float)rng.NextDouble() * 4f - 2f), rng);
            if (room.Kind == RoomKind.Combat && rng.NextDouble() < 0.4) Props.Brazier(lights, c, theme.BrazierColor, true);
        }

        private static void DressStartRoom(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights)
        {
            // A campfire-like brazier in the middle: the one safe, warm place on the floor.
            Props.Brazier(lights, room.Center + new Vector3(0f, 0f, 0f), new Color(1f, 0.6f, 0.3f), true);
            Props.Crate(b, room.Center + new Vector3(-4.5f, 0f, -4.5f), 1f, 10f);
            Props.Barrel(b, room.Center + new Vector3(4.5f, 0f, -4.5f));
        }

        private static void DressBossRoom(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights)
        {
            var c = room.Center;
            // Ring of pillars — cover from the slam, and a circle to fight around.
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f + Mathf.PI / 8f;
                Props.Pillar(b, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 9.5f, room.Height);
            }

            // Throne at the far wall from whichever door the party enters by.
            int entry = 0;
            for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
            var back = -new Vector3(DungeonLayout.Dir[entry].x, 0f, DungeonLayout.Dir[entry].y);
            Props.Throne(b, c + back * (room.Size.y / 2f - 2.5f), Quaternion.LookRotation(-back));

            var cold = new Color(0.35f, 0.65f, 1f);
            Props.Brazier(lights, c + new Vector3(5f, 0f, 5f), cold, true);
            Props.Brazier(lights, c + new Vector3(-5f, 0f, 5f), cold, true);
            Props.Brazier(lights, c + new Vector3(5f, 0f, -5f), cold, false);
            Props.Brazier(lights, c + new Vector3(-5f, 0f, -5f), cold, false);
        }

        // ------------------------------------------------------------------
        // Corridors
        // ------------------------------------------------------------------

        private static void BuildCorridor(DungeonLayout.Room a, DungeonLayout.Room bRoom, int dir, MeshKit.Batcher b, MeshKit.Batcher dressing, Transform lights)
        {
            var d = DungeonLayout.Dir[dir];
            var axis = new Vector3(d.x, 0f, d.y);
            bool alongX = d.x != 0;

            float aEdge = (alongX ? a.Size.x : a.Size.y) / 2f + WallThickness;
            float bEdge = (alongX ? bRoom.Size.x : bRoom.Size.y) / 2f + WallThickness;
            var start = a.Center + axis * aEdge;
            var end = bRoom.Center - axis * bEdge;
            float length = Vector3.Distance(start, end);
            var mid = (start + end) / 2f;

            float w = DungeonLayout.CorridorWidth, h = DungeonLayout.CorridorHeight;
            var side = new Vector3(-axis.z, 0f, axis.x);

            Vector3 Size(float along, float height, float across) => alongX ? new Vector3(along, height, across) : new Vector3(across, height, along);

            b.AddBox(mid + Vector3.down * FloorThickness / 2f, Size(length, FloorThickness, w + WallThickness * 2f), Mat.Flagstone);
            b.AddBox(mid + Vector3.up * (h + FloorThickness / 2f), Size(length, FloorThickness, w + WallThickness * 2f), Mat.StoneWall);
            b.AddBox(mid + side * (w / 2f + WallThickness / 2f) + Vector3.up * h / 2f, Size(length, h, WallThickness), Mat.StoneWall);
            b.AddBox(mid - side * (w / 2f + WallThickness / 2f) + Vector3.up * h / 2f, Size(length, h, WallThickness), Mat.StoneWall);

            // Mine-shaft timber sets: posts and a cap beam every few metres.
            int sets = Mathf.Max(1, Mathf.FloorToInt(length / 3.5f));
            for (int i = 0; i < sets; i++)
            {
                var p = start + axis * ((i + 0.5f) * length / sets);
                dressing.AddBox(p + side * (w / 2f - 0.15f) + Vector3.up * h / 2f, new Vector3(0.28f, h, 0.28f), Mat.Wood);
                dressing.AddBox(p - side * (w / 2f - 0.15f) + Vector3.up * h / 2f, new Vector3(0.28f, h, 0.28f), Mat.Wood);
                dressing.AddBox(p + Vector3.up * (h - 0.18f), Size(0.3f, 0.36f, w), Mat.Wood);
            }

            // One torch per ~9m, alternating walls.
            int torches = Mathf.Max(1, Mathf.FloorToInt(length / 9f));
            for (int i = 0; i < torches; i++)
            {
                float s = (i % 2 == 0) ? 1f : -1f;
                var p = start + axis * ((i + 0.5f) * length / torches + 1.7f) + side * s * (w / 2f) + Vector3.up * 2.3f;
                Props.WallTorch(lights, p, -side * s, false, theme.TorchColor);
            }
        }
    }
}
