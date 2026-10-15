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
    public static partial class DungeonBuilder
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
            foreach (var link in layout.Links) BuildCorridor(layout.Rooms[link.A], layout.Rooms[link.B], link.Dir, structure, dressing, lights);
            foreach (var trap in layout.FlameTraps) FlameJet.Build(lights, trap, dressing);
            if (layout.PeddlerSpot.HasValue)
            {
                // The peddler: the camp merchant's travelling cousin, with a lantern and his pack.
                var at = layout.PeddlerSpot.Value;
                Props.Merchant(lights, at, at + layout.PeddlerFacing * 3f, dressing);
                Props.Crate(dressing, at - layout.PeddlerFacing * 0.6f + new Vector3(layout.PeddlerFacing.z, 0f, -layout.PeddlerFacing.x) * 1.2f, 0.9f, 20f);
                Props.Candles(lights, at + layout.PeddlerFacing * 1.2f, new Color(1f, 0.75f, 0.45f));
            }

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

            if (room.Feature == RoomFeature.Chasm) BuildChasmFloor(room, structure, dressing, lights);
            else if (room.Feature == RoomFeature.SunkenCourt) BuildLowFloor(room, DungeonLayout.SunkenDepth, 4.6f, structure, dressing, Mat.Flagstone);
            else if (room.Feature == RoomFeature.Cistern) BuildLowFloor(room, DungeonLayout.CisternDepth, 1.8f, structure, dressing, Mat.Rubble);
            else structure.AddBox(c + Vector3.down * FloorThickness / 2f, new Vector3(w + WallThickness * 2f, FloorThickness, d + WallThickness * 2f), floorMat);
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
            if (room.Kind != RoomKind.Start) PlaceLightShafts(room, lights, dressing, rng);

            switch (room.Feature)
            {
                case RoomFeature.Gallery: DressGallery(room, structure, dressing, lights, rng); return;
                case RoomFeature.SunkenCourt: DressSunken(room, dressing, lights, rng); return;
                case RoomFeature.Cistern: DressCistern(room, dressing, lights, lights, rng); return;
                case RoomFeature.Pendulums: DressPendulums(room, dressing, lights, lights); return;
                case RoomFeature.Ritual: DressRitual(room, dressing, lights, rng); return;
                case RoomFeature.GreatHall: DressGreatHall(room, dressing, lights); return;
                case RoomFeature.Chasm: return; // the pit is the dressing
                case RoomFeature.Crypt: DressCrypt(room, dressing, lights, rng); return;
                case RoomFeature.Vault: DressVault(room, dressing, lights, rng); return;
                case RoomFeature.Fountain: return; // the fountain is a networked object
            }

            switch (room.Kind)
            {
                case RoomKind.Boss: DressBossRoom(room, dressing, lights); break;
                case RoomKind.Start: DressStartRoom(room, dressing, lights); break;
                default: DressRoom(room, dressing, lights, rng); break;
            }
        }

        /// <summary>Timber door frame and the pointed stone arch over a doorway.</summary>
        private static void DoorTrim(Vector3 wallCenter, Vector2 dir, bool alongX, MeshKit.Batcher b)
        {
            float door = DungeonLayout.CorridorWidth;
            Vector3 Size(float len, float height) => alongX ? new Vector3(len, height, WallThickness) : new Vector3(WallThickness, height, len);
            Vector3 Along(float t) => alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t);
            // Door frame: two jambs and a header in heavy timber.
            var inward = -new Vector3(dir.x, 0f, dir.y) * (WallThickness / 2f + 0.1f);
            b.AddBox(wallCenter + inward + Along(-door / 2f - 0.15f) + Vector3.up * DungeonLayout.CorridorHeight / 2f, Size(0.3f, DungeonLayout.CorridorHeight) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.Wood);
            b.AddBox(wallCenter + inward + Along(door / 2f + 0.15f) + Vector3.up * DungeonLayout.CorridorHeight / 2f, Size(0.3f, DungeonLayout.CorridorHeight) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.Wood);
            b.AddBox(wallCenter + inward + Vector3.up * (DungeonLayout.CorridorHeight + 0.15f), Size(door + 0.9f, 0.3f) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.Wood);
            // A pointed gothic arch of dressed stone over the lintel, with a keystone.
            var normal = new Vector3(dir.x, 0f, dir.y);
            float archY = DungeonLayout.CorridorHeight + 0.3f;
            float leg = door * 0.62f;
            foreach (float sgn in new[] { -1f, 1f })
            {
                var rot = Quaternion.AngleAxis(sgn * (alongX ? -32f : 32f), normal);
                b.AddBox(wallCenter + inward * 1.15f + Along(sgn * door * 0.25f) + Vector3.up * (archY + leg * 0.27f),
                    Size(leg, 0.32f) + (alongX ? new Vector3(0, 0, -0.55f) : new Vector3(-0.55f, 0, 0)), Mat.StoneWall, rot);
            }
            b.AddBox(wallCenter + inward * 1.2f + Vector3.up * (archY + leg * 0.55f), Size(0.32f, 0.42f) + (alongX ? new Vector3(0, 0, -0.5f) : new Vector3(-0.5f, 0, 0)), Mat.StoneWall);
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

            var hole = room.HoleOn(side);
            if (hole != null && room.Doors[side])
            {
                // A doorway and a hollow on the same wall: three solid stretches around two gaps.
                float door = DungeonLayout.CorridorWidth, hw = WallHole.Width, a = hole.Along;
                var gaps = new[] { (c: 0f, w: door, top: DungeonLayout.CorridorHeight), (c: a, w: hw, top: WallHole.Height) };
                System.Array.Sort(gaps, (p, q) => p.c.CompareTo(q.c));
                float cursor = -length / 2f;
                foreach (var g in gaps)
                {
                    float left = g.c - g.w / 2f;
                    if (left - cursor > 0.01f) b.AddBox(wallCenter + Along((cursor + left) / 2f) + Vector3.up * h / 2f, Size(left - cursor, h), Mat.StoneWall);
                    b.AddBox(wallCenter + Along(g.c) + Vector3.up * (g.top + (h - g.top) / 2f), Size(g.w, h - g.top), Mat.StoneWall);
                    cursor = g.c + g.w / 2f;
                }
                if (length / 2f - cursor > 0.01f) b.AddBox(wallCenter + Along((cursor + length / 2f) / 2f) + Vector3.up * h / 2f, Size(length / 2f - cursor, h), Mat.StoneWall);
                BuildNiche(wallCenter + Along(a), new Vector3(dir.x, 0f, dir.y), alongX, hole.Breach, b);
                DoorTrim(wallCenter, dir, alongX, b);
            }
            else if (hole != null && !room.Doors[side])
            {
                // Wall split around the hole, a lintel over it, and the niche beyond.
                float hw = WallHole.Width, a = hole.Along;
                float leftLen = (a - hw / 2f) + length / 2f, rightLen = length / 2f - (a + hw / 2f);
                b.AddBox(wallCenter + Along(-length / 2f + leftLen / 2f) + Vector3.up * h / 2f, Size(leftLen, h), Mat.StoneWall);
                b.AddBox(wallCenter + Along(length / 2f - rightLen / 2f) + Vector3.up * h / 2f, Size(rightLen, h), Mat.StoneWall);
                b.AddBox(wallCenter + Along(a) + Vector3.up * (WallHole.Height + (h - WallHole.Height) / 2f), Size(hw, h - WallHole.Height), Mat.StoneWall);
                BuildNiche(wallCenter + Along(a), new Vector3(dir.x, 0f, dir.y), alongX, hole.Breach, b);
            }
            else if (!room.Doors[side])
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

                DoorTrim(wallCenter, dir, alongX, b);
            }

            // Plinth along the base of the wall, so walls meet the floor with a line
            // rather than a seam.
            var plinthIn = -new Vector3(dir.x, 0f, dir.y) * (WallThickness / 2f + 0.1f);
            if (!room.Doors[side] && hole == null)
            {
                b.AddBox(wallCenter + plinthIn + Vector3.up * 0.15f, Size(length - WallThickness * 2f, 0.3f) + (alongX ? new Vector3(0, 0, -0.6f) : new Vector3(-0.6f, 0, 0)), Mat.StoneWall);
            }
        }

        /// <summary>
        /// The space behind a hollow wall: floor, ceiling, back and sides. A secret cache is
        /// a little shrine-alcove; a breach is a rough tunnel the dead dug to reach you.
        /// </summary>
        private static void BuildNiche(Vector3 wallMid, Vector3 outward, bool alongX, bool breach, MeshKit.Batcher b)
        {
            float w = WallHole.Width + 1.2f, depth = WallHole.NicheDepth, hh = WallHole.Height + 0.2f;
            var start = wallMid + outward * (WallThickness / 2f);
            var mid = start + outward * (depth / 2f);
            Vector3 S(float across, float height, float deep) => alongX ? new Vector3(across, height, deep) : new Vector3(deep, height, across);
            var across = alongX ? Vector3.right : Vector3.forward;

            b.AddBox(mid + Vector3.down * FloorThickness / 2f, S(w, FloorThickness, depth), breach ? Mat.Rubble : Mat.Flagstone);
            b.AddBox(mid + Vector3.up * (hh + 0.2f), S(w, 0.4f, depth), Mat.StoneWall);
            b.AddBox(start + outward * (depth + 0.4f) + Vector3.up * hh / 2f, S(w + 0.8f, hh + 0.4f, 0.8f), Mat.StoneWall);
            b.AddBox(mid + across * (w / 2f + 0.4f) + Vector3.up * hh / 2f, S(0.8f, hh + 0.4f, depth), Mat.StoneWall);
            b.AddBox(mid - across * (w / 2f + 0.4f) + Vector3.up * hh / 2f, S(0.8f, hh + 0.4f, depth), Mat.StoneWall);

            if (breach)
            {
                // Spoil heaps and gouges: something dug through here.
                b.AddBox(mid + outward * 0.9f + across * 0.9f + Vector3.up * 0.25f, S(0.9f, 0.5f, 1.1f), Mat.Rubble, Quaternion.Euler(0f, 23f, 8f));
                b.AddBox(mid - outward * 0.2f - across * 1.1f + Vector3.up * 0.2f, S(0.7f, 0.4f, 0.8f), Mat.Rubble, Quaternion.Euler(0f, -31f, -6f));
            }
            else
            {
                // A little plinth at the back for whatever was hidden here.
                b.AddBox(start + outward * (depth - 0.5f) + Vector3.up * 0.3f, S(1.6f, 0.6f, 0.6f), Mat.StoneWall);
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
                    var holeHere = room.HoleOn(side);
                    if (holeHere != null && Mathf.Abs(t - holeHere.Along) < WallHole.Width) continue;
                    if (Mathf.Abs(t) > len / 2f - 0.8f) continue;

                    // On a gallery wall the torches hang above the balcony.
                    float ty = room.Balconies.Contains(side) ? DungeonLayout.BalconyHeight + 1.7f : y;
                    var p = room.Center + new Vector3(dir.x, 0f, dir.y) * offset + (alongX ? new Vector3(t, ty, 0f) : new Vector3(0f, ty, t));
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
            GothicDressing(room, b, lights, rng);
            if (room.Kind == RoomKind.Combat && rng.NextDouble() < 0.4) Props.Brazier(lights, c, theme.BrazierColor, true);
        }

        // ------------------------------------------------------------------
        // Feature rooms
        // ------------------------------------------------------------------

        /// <summary>
        /// A chasm: the floor is a ring around a pit, crossed by two narrow bridges. Far
        /// below, a dim red glow — you can see how far it is to fall.
        /// </summary>
        private static void BuildChasmFloor(DungeonLayout.Room room, MeshKit.Batcher b, MeshKit.Batcher dressing, Transform lights)
        {
            var c = room.Center;
            var pit = room.Pit;
            float fullW = room.Size.x + WallThickness * 2f, fullD = room.Size.y + WallThickness * 2f;
            float minX = c.x - fullW / 2f, maxX = c.x + fullW / 2f, minZ = c.z - fullD / 2f, maxZ = c.z + fullD / 2f;
            float y = -FloorThickness / 2f;

            void Slab(float x0, float x1, float z0, float z1)
            {
                if (x1 - x0 < 0.05f || z1 - z0 < 0.05f) return;
                b.AddBox(new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), new Vector3(x1 - x0, FloorThickness, z1 - z0), Mat.Flagstone);
            }

            // Ring around the pit.
            Slab(minX, maxX, minZ, pit.yMin);
            Slab(minX, maxX, pit.yMax, maxZ);
            Slab(minX, pit.xMin, pit.yMin, pit.yMax);
            Slab(pit.xMax, maxX, pit.yMin, pit.yMax);

            // Two bridges: one along X, one along Z, with low stone rails.
            const float bridge = 2.2f;
            Slab(pit.xMin, pit.xMax, c.z - bridge / 2f, c.z + bridge / 2f);
            Slab(c.x - bridge / 2f, c.x + bridge / 2f, pit.yMin, pit.yMax);
            foreach (float s in new[] { -1f, 1f })
            {
                dressing.AddBox(new Vector3(c.x - pit.width / 4f - bridge / 4f, 0.25f, c.z + s * (bridge / 2f + 0.1f)), new Vector3(pit.width / 2f - bridge / 2f, 0.5f, 0.2f), Mat.StoneWall);
                dressing.AddBox(new Vector3(c.x + pit.width / 4f + bridge / 4f, 0.25f, c.z + s * (bridge / 2f + 0.1f)), new Vector3(pit.width / 2f - bridge / 2f, 0.5f, 0.2f), Mat.StoneWall);
                dressing.AddBox(new Vector3(c.x + s * (bridge / 2f + 0.1f), 0.25f, c.z - pit.height / 4f - bridge / 4f), new Vector3(0.2f, 0.5f, pit.height / 2f - bridge / 2f), Mat.StoneWall);
                dressing.AddBox(new Vector3(c.x + s * (bridge / 2f + 0.1f), 0.25f, c.z + pit.height / 4f + bridge / 4f), new Vector3(0.2f, 0.5f, pit.height / 2f - bridge / 2f), Mat.StoneWall);
            }

            // Pit walls going down, and the glowing bottom.
            const float depth = 10f;
            b.AddBox(new Vector3(pit.center.x, -depth / 2f, pit.yMin - 0.3f), new Vector3(pit.width, depth, 0.6f), Mat.Rubble);
            b.AddBox(new Vector3(pit.center.x, -depth / 2f, pit.yMax + 0.3f), new Vector3(pit.width, depth, 0.6f), Mat.Rubble);
            b.AddBox(new Vector3(pit.xMin - 0.3f, -depth / 2f, pit.center.y), new Vector3(0.6f, depth, pit.height), Mat.Rubble);
            b.AddBox(new Vector3(pit.xMax + 0.3f, -depth / 2f, pit.center.y), new Vector3(0.6f, depth, pit.height), Mat.Rubble);
            b.AddBox(new Vector3(pit.center.x, -depth - 0.2f, pit.center.y), new Vector3(pit.width, 0.4f, pit.height), Mat.Rubble);

            // Kerb stones mark the lip, broken where the bridges cross.
            void Kerb(Vector3 from, Vector3 to)
            {
                var mid = (from + to) / 2f;
                var size = new Vector3(Mathf.Abs(to.x - from.x) + 0.35f, 0.14f, Mathf.Abs(to.z - from.z) + 0.35f);
                if (size.x < 0.6f && size.z < 0.6f) return;
                dressing.AddBox(mid + Vector3.up * 0.07f, size, Mat.StoneWall);
            }
            float gap = bridge / 2f + 0.3f;
            foreach (float z in new[] { pit.yMin, pit.yMax })
            {
                Kerb(new Vector3(pit.xMin, 0f, z), new Vector3(c.x - gap, 0f, z));
                Kerb(new Vector3(c.x + gap, 0f, z), new Vector3(pit.xMax, 0f, z));
            }
            foreach (float x in new[] { pit.xMin, pit.xMax })
            {
                Kerb(new Vector3(x, 0f, pit.yMin), new Vector3(x, 0f, c.z - gap));
                Kerb(new Vector3(x, 0f, c.z + gap), new Vector3(x, 0f, pit.yMax));
            }

            // Embers glowing in the cracks far below: the only way to judge the drop.
            var rng = new System.Random(room.Id * 7919 + 17);
            for (int i = 0; i < 18; i++)
            {
                float ex = Mathf.Lerp(pit.xMin + 0.6f, pit.xMax - 0.6f, (float)rng.NextDouble());
                float ez = Mathf.Lerp(pit.yMin + 0.6f, pit.yMax - 0.6f, (float)rng.NextDouble());
                float len = 0.6f + (float)rng.NextDouble() * 1.8f;
                bool alongX = rng.Next(2) == 0;
                dressing.AddBox(new Vector3(ex, -depth + 0.02f, ez), alongX ? new Vector3(len, 0.06f, 0.18f) : new Vector3(0.18f, 0.06f, len), Mat.Flame);
            }

            foreach (var (height, range, intensity) in new[] { (-depth + 1.5f, 14f, 5f), (-depth * 0.45f, 9f, 1.4f) })
            {
                var glow = new GameObject("AbyssGlow").AddComponent<Light>();
                glow.transform.SetParent(lights, false);
                glow.transform.position = new Vector3(pit.center.x, height, pit.center.y);
                glow.type = LightType.Point;
                glow.color = new Color(0.95f, 0.3f, 0.1f);
                glow.range = range;
                glow.intensity = intensity;
            }
        }

        /// <summary>A great hall: a colonnade down both sides, chandeliers, and banners in the floor's colours.</summary>
        /// <summary>
        /// The gothic layer for any room: gargoyles flanking a doorless wall, iron candelabras,
        /// and cobwebs in the high corners.
        /// </summary>
        private static void GothicDressing(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
            // A pair of gargoyles watching from a wall with no door (and no hollow in it).
            for (int side = 0; side < 4; side++)
            {
                if (room.Doors[side] || room.HoleOn(side) != null || room.Size.x < 12f || rng.NextDouble() < 0.5) continue;
                var d = DungeonLayout.Dir[side];
                var wall = c + new Vector3(d.x, 0f, d.y) * ((side == 0 || side == 2 ? hz : hx) - 0.9f);
                var along = side == 0 || side == 2 ? Vector3.right : Vector3.forward;
                var face = Quaternion.LookRotation(-new Vector3(d.x, 0f, d.y));
                Props.Gargoyle(b, wall + along * 2.2f, face);
                Props.Gargoyle(b, wall - along * 2.2f, face);
                Props.Candelabra(lights, b, wall + along * 0.0f - new Vector3(d.x, 0f, d.y) * 0.3f, theme.TorchColor);
                break;
            }
            // Cobwebs in two or three of the high corners.
            for (int corner = 0; corner < 4; corner++)
            {
                if (rng.NextDouble() < 0.4) continue;
                float sx = (corner & 1) == 0 ? -1f : 1f, sz = (corner & 2) == 0 ? -1f : 1f;
                var p = c + new Vector3(sx * (hx - 0.05f), room.Height - 0.1f, sz * (hz - 0.05f));
                Props.Cobweb(lights, p, new Vector3(-sx, 0f, 0f), new Vector3(0f, 0f, -sz), 1.4f + (float)rng.NextDouble());
            }
        }

        private static void DressGreatHall(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights)
        {
            var c = room.Center;
            // Stained-glass lancets along both long walls, between the banners.
            int seed = room.DecorSeed;
            for (int side = 1; side < 4; side += 2)
            {
                var d = DungeonLayout.Dir[side];
                for (float z = -room.Size.y / 2f + 8f; z < room.Size.y / 2f - 6f; z += 6f)
                {
                    if (room.Doors[side] && Mathf.Abs(z) < DungeonLayout.CorridorWidth + 2f) continue;
                    if (room.HoleOn(side) != null && Mathf.Abs(z - room.HoleOn(side).Along) < 3f) continue;
                    var face = c + new Vector3(d.x * (room.Size.x / 2f), 0f, z);
                    Props.StainedGlass(lights, b, face, -new Vector3(d.x, 0f, 0f), room.Height * 0.55f, seed++);
                }
            }
            Props.Candelabra(lights, b, c + new Vector3(-2.5f, 0f, 0f), theme.TorchColor);
            Props.Candelabra(lights, b, c + new Vector3(2.5f, 0f, 0f), theme.TorchColor);
            for (int i = 0; i < 4; i++)
            {
                float z = -room.Size.y / 2f + room.Size.y * (i + 0.5f) / 4f;
                Props.Pillar(b, c + new Vector3(-room.Size.x * 0.28f, 0f, z), room.Height);
                Props.Pillar(b, c + new Vector3(room.Size.x * 0.28f, 0f, z), room.Height);
            }
            for (int i = 0; i < 2; i++)
            {
                var at = c + new Vector3(0f, room.Height - 2.2f, (i == 0 ? -1f : 1f) * room.Size.y * 0.22f);
                Props.Chandelier(lights, b, at, room.Height - at.y, theme.TorchColor);
            }
            var bannerColor = theme.BrazierColor * 0.6f;
            for (int side = 1; side < 4; side += 2)
            {
                var d = DungeonLayout.Dir[side];
                for (float z = -room.Size.y / 2f + 5f; z < room.Size.y / 2f - 4f; z += 6f)
                {
                    if (room.Doors[side] && Mathf.Abs(z) < DungeonLayout.CorridorWidth + 1f) continue;
                    var p = c + new Vector3(d.x * (room.Size.x / 2f - 0.15f), room.Height - 3.2f, z);
                    Props.Banner(lights, p, -new Vector3(d.x, 0f, 0f), bannerColor);
                }
            }
        }

        /// <summary>A crypt: candles, bones and a stone altar. The sarcophagi themselves are networked.</summary>
        private static void DressCrypt(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            b.AddBox(c + Vector3.up * 0.45f, new Vector3(2.4f, 0.9f, 1.2f), Mat.StoneWall);
            for (int i = 0; i < 6; i++)
            {
                var p = c + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * (room.Size.x - 6f);
                Props.BonePile(b, p, rng);
            }
            Props.Candles(lights, c + Vector3.up * 0.9f, new Color(0.9f, 0.75f, 0.5f));
            Props.Candles(lights, c + new Vector3(-room.Size.x * 0.3f, 0f, room.Size.y * 0.3f), new Color(0.9f, 0.75f, 0.5f));
            Props.Candles(lights, c + new Vector3(room.Size.x * 0.3f, 0f, -room.Size.y * 0.3f), new Color(0.9f, 0.75f, 0.5f));
        }

        /// <summary>A treasure vault: heaped gold and a pedestal. The chests inside are networked.</summary>
        private static void DressVault(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            for (int i = 0; i < 7; i++)
            {
                var p = c + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * (room.Size.x - 4f);
                float s = 0.5f + (float)rng.NextDouble() * 0.6f;
                b.Add(MeshKit.Lathe("goldHeap", new[] { new Vector2(0f, 0f), new Vector2(0.6f, 0f), new Vector2(0.3f, 0.3f), new Vector2(0f, 0.38f) }, 9),
                    Mat.Gold, Matrix4x4.TRS(p, Quaternion.Euler(0f, rng.Next(360), 0f), Vector3.one * s));
            }
            var glow = new GameObject("GoldGlow").AddComponent<Light>();
            glow.transform.SetParent(lights, false);
            glow.transform.position = c + Vector3.up * 2.5f;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.8f, 0.4f);
            glow.range = 10f;
            glow.intensity = 3f;
        }

        private static void DressStartRoom(DungeonLayout.Room room, MeshKit.Batcher b, Transform lights)
        {
            // A campfire-like brazier in the middle: the one safe, warm place on the floor.
            Props.Brazier(lights, room.Center + new Vector3(0f, 0f, 0f), new Color(1f, 0.6f, 0.3f), true);
            Props.Crate(b, room.Center + new Vector3(-4.5f, 0f, -4.5f), 1f, 10f);
            Props.Barrel(b, room.Center + new Vector3(4.5f, 0f, -4.5f));

            // The expedition's landing: bedrolls round the fire, packs, a supply stack,
            // and a waystone glowing where the carriage set you down.
            var c = room.Center;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 20f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var roll = MeshKit.Part(lights, "Bedroll" + i, MeshKit.Box(new Vector3(0.8f, 0.12f, 1.9f)), Mat.Cloth, c + dir * 4f + Vector3.up * 0.06f,
                    Quaternion.LookRotation(dir).eulerAngles);
                ArtLibrary.Tint(roll.GetComponent<Renderer>(), i % 2 == 0 ? new Color(0.4f, 0.12f, 0.1f) : new Color(0.25f, 0.3f, 0.22f));
                b.Add(MeshKit.Cylinder(0.14f, 0.14f, 0.8f, 8), Mat.Cloth, Matrix4x4.TRS(c + dir * 4.85f + Vector3.up * 0.14f, Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, 90f) * Quaternion.Euler(0f, 0f, 0f), Vector3.one));
                b.AddBox(c + dir * 3.1f + Quaternion.Euler(0f, 90f, 0f) * dir * 0.9f + Vector3.up * 0.25f, new Vector3(0.45f, 0.5f, 0.3f), Mat.Leather, Quaternion.LookRotation(dir));
            }
            // Supply stack in a corner.
            float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
            Props.Crate(b, c + new Vector3(hx - 1.4f, 0f, hz - 1.4f), 1.1f, 15f);
            Props.Crate(b, c + new Vector3(hx - 2.6f, 0f, hz - 1.3f), 0.9f, -10f);
            Props.Crate(b, c + new Vector3(hx - 1.5f, 1.1f, hz - 1.4f), 0.7f, 40f);
            Props.Barrel(b, c + new Vector3(-hx + 1.3f, 0f, hz - 1.3f));
            Props.Barrel(b, c + new Vector3(-hx + 2.2f, 0f, hz - 1.4f));
            // The waystone: on a wall with no door, a tall carved stone with a glowing rune.
            for (int side = 0; side < 4; side++)
            {
                if (room.Doors[side]) continue;
                var d = DungeonLayout.Dir[side];
                var outward = new Vector3(d.x, 0f, d.y);
                var at = c + outward * ((side == 0 || side == 2 ? hz : hx) - 1.2f);
                b.AddBox(at + Vector3.up * 0.15f, new Vector3(1.6f, 0.3f, 1.6f), Mat.StoneWall);
                b.Add(MeshKit.Lathe("waystone", new[] { new Vector2(0.45f, 0f), new Vector2(0.4f, 1.6f), new Vector2(0.28f, 2.4f), new Vector2(0f, 2.7f) }, 6),
                    Mat.StoneWall, Matrix4x4.TRS(at + Vector3.up * 0.3f, Quaternion.LookRotation(-outward), new Vector3(1f, 1f, 0.55f)));
                var rune = MeshKit.Part(lights, "WaystoneRune", MeshKit.Box(new Vector3(0.12f, 0.7f, 0.04f)), Mat.Glow, at - outward * 0.25f + Vector3.up * 1.5f, Quaternion.LookRotation(-outward).eulerAngles);
                ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), new Color(0.5f, 0.8f, 1f) * 1.5f);
                var bar = MeshKit.Part(lights, "WaystoneRuneBar", MeshKit.Box(new Vector3(0.4f, 0.1f, 0.04f)), Mat.Glow, at - outward * 0.25f + Vector3.up * 1.65f, Quaternion.LookRotation(-outward).eulerAngles);
                ArtLibrary.SetGlow(bar.GetComponent<Renderer>(), new Color(0.5f, 0.8f, 1f) * 1.5f);
                var l = new GameObject("WaystoneLight").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.position = at - outward * 1f + Vector3.up * 1.6f;
                l.type = LightType.Point;
                l.color = new Color(0.5f, 0.75f, 1f);
                l.range = 5f;
                l.intensity = 1.5f;
                l.shadows = LightShadows.None;
                Props.Banner(lights, at + outward * 1.05f + Vector3.up * 3f + Quaternion.Euler(0f, 90f, 0f) * outward * 1.6f, -outward, theme.BrazierColor * 0.6f);
                Props.Banner(lights, at + outward * 1.05f + Vector3.up * 3f - Quaternion.Euler(0f, 90f, 0f) * outward * 1.6f, -outward, theme.BrazierColor * 0.6f);
                break;
            }
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

            // Clutter, deterministic per corridor: bones and rubble at the foot of the walls,
            // chains hanging from the cap beams, cobwebs in the corners, a puddle or two.
            var crng = new System.Random(a.Id * 7919 + bRoom.Id * 104729 + 11);
            for (float t = 2f; t < length - 2f; t += 2.5f + (float)crng.NextDouble() * 3f)
            {
                float s = crng.Next(2) == 0 ? 1f : -1f;
                var foot = start + axis * t + side * s * (w / 2f - 0.45f);
                switch (crng.Next(6))
                {
                    case 0: Props.BonePile(dressing, foot, crng); break;
                    case 1: Props.Rubble(dressing, foot, crng); break;
                    case 2:
                        // A chain hanging from the beam, swaying slightly out of true.
                        float len = 0.8f + (float)crng.NextDouble() * 1.4f;
                        dressing.AddBox(start + axis * t + side * s * 0.6f + Vector3.up * (h - 0.4f - len / 2f), new Vector3(0.04f, len, 0.04f), Mat.Steel,
                            Quaternion.Euler((float)crng.NextDouble() * 8f - 4f, 0f, (float)crng.NextDouble() * 8f - 4f));
                        break;
                    case 3:
                        Props.Cobweb(lights, start + axis * t + side * s * (w / 2f - 0.05f) + Vector3.up * (h - 0.1f), -side * s, axis, 0.9f + (float)crng.NextDouble() * 0.6f);
                        break;
                    case 4:
                        if (theme.WaterDepth <= 0f)
                        {
                            var puddle = MeshKit.Part(lights, "Puddle", MeshKit.Cylinder(0.6f + (float)crng.NextDouble() * 0.5f, 0.6f, 0.01f, 10), Mat.Water,
                                start + axis * t + side * s * 0.4f + Vector3.up * 0.005f, Vector3.zero, new Vector3(1f, 1f, 0.6f + (float)crng.NextDouble() * 0.5f));
                            ArtLibrary.Tint(puddle.GetComponent<Renderer>(), new Color(0.06f, 0.07f, 0.08f, 0.8f));
                            puddle.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                        break;
                }
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
