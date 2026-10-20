using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.World
{
    public enum RoomKind { Start, Combat, Side, Boss }

    /// <summary>
    /// What makes a room more than four walls. Assigned per floor from the seed, so every
    /// peer agrees. Every combat room gets one: a Great Hall, a Chasm, a Crypt, a Gallery
    /// (balconies to climb), a Sunken Court (a pit arena), a Cistern (flooded, with dry
    /// stepping stones), a Pendulum hall (swinging blades that cut anyone) or a Ritual
    /// chamber (braziers empower the room's dead until smashed). Side rooms can be a
    /// Vault or a Fountain.
    /// </summary>
    public enum RoomFeature { None, GreatHall, Chasm, Crypt, Vault, Fountain, Gallery, SunkenCourt, Cistern, Pendulums, Ritual, Library, Pyre, Gauntlet }

    /// <summary>A hollow section of wall with a niche behind it.</summary>
    public class WallHole
    {
        public const float Width = 2.4f, Height = 2.8f, NicheDepth = 3.6f;
        public int Side;
        /// <summary>Offset along the wall from the room's centre line.</summary>
        public float Along;
        /// <summary>True: enemies burst out of it. False: a secret cache, broken by players.</summary>
        public bool Breach;
    }

    /// <summary>
    /// The abstract shape of one dungeon floor: which rooms exist, where, how big, and how
    /// they connect. No geometry — <see cref="DungeonBuilder"/> turns this into meshes.
    ///
    /// <para>
    /// Generated from a seed with <see cref="System.Random"/> and nothing else, so every
    /// peer that receives the same seed and depth produces the identical layout. That is
    /// the whole networking story for level geometry: one int is synced instead of
    /// thousands of wall transforms, and late joiners rebuild the level locally.
    /// </para>
    ///
    /// Rooms sit on a coarse grid (one room per cell) with straight corridors between grid
    /// neighbours. The main path is a self-avoiding walk from the start room to the boss
    /// room; side rooms hang off it. Ported in spirit from the old prototype's
    /// DungeonLayout room graph.
    /// </summary>
    public class DungeonLayout
    {
        /// <summary>Distance between room centres. Bounds the largest room plus a corridor.</summary>
        /// <remarks>Raised from 30m so rooms can be bigger: a 30m Great Hall still leaves a corridor.</remarks>
        public const float CellSize = 46f;
        public const float CorridorWidth = 3.2f;
        public const float CorridorHeight = 3.8f;

        public class Room
        {
            public int Id;
            public RoomKind Kind;
            public Vector2Int Cell;
            public Vector3 Center;
            /// <summary>Interior floor size in metres (x, z).</summary>
            public Vector2 Size;
            public float Height;
            /// <summary>Door on each side, indexed by <see cref="Dir"/>: N, E, S, W.</summary>
            public readonly bool[] Doors = new bool[4];
            public readonly List<Vector3> EnemySpawns = new List<Vector3>();
            /// <summary>Per-room seed for decoration, so props are stable but varied.</summary>
            public int DecorSeed;

            public RoomFeature Feature;
            /// <summary>Feature positions: sarcophagi in a crypt, levers for a vault (in the room next door), the fountain.</summary>
            public readonly List<Vector3> FeatureSpots = new List<Vector3>();
            /// <summary>For a chasm: the pit, as a rect in world XZ.</summary>
            public Rect Pit;
            /// <summary>Gallery: the walls with a balcony (side indices). Sunken court / cistern: the low area, as a rect in world XZ.</summary>
            public readonly List<int> Balconies = new List<int>();
            public Rect Low;
            /// <summary>For a vault: the room its levers are in (the one its door opens onto).</summary>
            public Room VaultHost;

            /// <summary>Hollow wall sections: secret caches to smash open, or breaches enemies burst out of.</summary>
            public readonly List<WallHole> Holes = new List<WallHole>();

            public WallHole HoleOn(int side) => Holes.Find(h => h.Side == side);

            /// <summary>The hole's centre on the room's inner wall face, at floor level.</summary>
            public Vector3 HoleFace(WallHole h)
            {
                var d = Dir[h.Side];
                bool alongX = h.Side == 0 || h.Side == 2;
                float half = (alongX ? Size.y : Size.x) / 2f;
                return Center + new Vector3(d.x, 0f, d.y) * half + (alongX ? new Vector3(h.Along, 0f, 0f) : new Vector3(0f, 0f, h.Along));
            }

            /// <summary>Unit vector from the room out through the hole.</summary>
            public static Vector3 HoleOutward(WallHole h) => new Vector3(Dir[h.Side].x, 0f, Dir[h.Side].y);

            public Rect Bounds => new Rect(Center.x - Size.x / 2f, Center.z - Size.y / 2f, Size.x, Size.y);

            public bool Contains(Vector3 p, float margin = 0f)
            {
                return Mathf.Abs(p.x - Center.x) <= Size.x / 2f + margin && Mathf.Abs(p.z - Center.z) <= Size.y / 2f + margin;
            }
        }

        public struct Link
        {
            public int A, B;
            public int Dir; // from A to B
        }

        public static readonly Vector2Int[] Dir = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        /// <summary>A pair of fire vents across a corridor. <see cref="Phase"/> offsets its beat.</summary>
        public struct FlameTrap { public Vector3 At, Across; public int Phase; }
        public readonly List<FlameTrap> FlameTraps = new List<FlameTrap>();

        /// <summary>From floor two: sometimes a wandering peddler sets up in a side room (null if not).</summary>
        public Vector3? PeddlerSpot;
        public Vector3 PeddlerFacing;

        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<Link> Links = new List<Link>();
        public int Seed { get; private set; }
        public int Depth { get; private set; }

        public Room StartRoom => Rooms[0];
        public Room BossRoom { get; private set; }

        public Vector3 PlayerSpawn => StartRoom.Center;
        public Vector3 BossSpawn => BossRoom.Center + new Vector3(0f, 0f, BossRoom.Size.y * 0.2f);

        /// <summary>The start room's waystone (the Shrine of Return): the first wall with no door.</summary>
        public Vector3 WaystonePosition
        {
            get
            {
                var c = StartRoom.Center;
                float hx = StartRoom.Size.x / 2f, hz = StartRoom.Size.y / 2f;
                for (int side = 0; side < 4; side++)
                {
                    if (StartRoom.Doors[side]) continue;
                    var d = Dir[side];
                    return c + new Vector3(d.x, 0f, d.y) * ((side == 0 || side == 2 ? hz : hx) - 1.2f);
                }
                return c + Vector3.forward * 4f;
            }
        }

        /// <summary>Where the merchant stands: against the start room's back wall, facing the centre.</summary>
        public Vector3 MerchantPosition
        {
            get
            {
                // Put him on a side of the start room with no door, so he never blocks the way out.
                for (int side = 0; side < 4; side++)
                {
                    if (StartRoom.Doors[side]) continue;
                    var d = Dir[side];
                    return StartRoom.Center + new Vector3(d.x, 0f, d.y) * (StartRoom.Size.y / 2f - 1.6f);
                }
                return StartRoom.Center + Vector3.forward * 4f;
            }
        }

        /// <summary>Player spawn points around the start room centre, one per slot.</summary>
        public Vector3 PlayerSpawnFor(ulong clientId)
        {
            float angle = clientId * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            return PlayerSpawn + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f + Vector3.up * 0.1f;
        }

        /// <summary>The midpoint of a corridor, on the floor.</summary>
        public Vector3 CorridorMidpoint(Link link)
        {
            var a = Rooms[link.A];
            var b = Rooms[link.B];
            var d = Dir[link.Dir];
            bool alongX = d.x != 0;
            var axis = new Vector3(d.x, 0f, d.y);
            var start = a.Center + axis * ((alongX ? a.Size.x : a.Size.y) / 2f + 0.8f);
            var end = b.Center - axis * ((alongX ? b.Size.x : b.Size.y) / 2f + 0.8f);
            return (start + end) / 2f;
        }

        public Room RoomAt(Vector3 position, float margin = 0.5f)
        {
            foreach (var room in Rooms)
            {
                if (room.Contains(position, margin)) return room;
            }
            return null;
        }

        /// <param name="depth">0 for the first floor. Deeper floors have longer paths and more enemies.</param>
        public static DungeonLayout Generate(int seed, int depth)
        {
            // A self-avoiding walk can box itself in. Retrying with a derived seed keeps
            // the result deterministic — every peer retries identically.
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var layout = TryGenerate(seed + attempt * 7919, depth);
                if (layout != null)
                {
                    layout.Seed = seed;
                    return layout;
                }
            }

            Debug.LogError("[Dungeon] Could not generate a layout for seed " + seed);
            return TryGenerate(0, 0);
        }

        private static DungeonLayout TryGenerate(int seed, int depth)
        {
            var rng = new System.Random(seed);
            var layout = new DungeonLayout { Depth = depth };
            var occupied = new Dictionary<Vector2Int, Room>();

            int mainLength = 5 + Mathf.Min(depth, 3);
            var cell = Vector2Int.zero;
            int lastDir = 0;

            Room previous = null;
            for (int i = 0; i <= mainLength; i++)
            {
                RoomKind kind = i == 0 ? RoomKind.Start : i == mainLength ? RoomKind.Boss : RoomKind.Combat;
                var room = AddRoom(layout, occupied, cell, kind, rng);

                if (previous != null) Connect(layout, previous, room, lastDir);
                previous = room;

                if (i == mainLength) break;

                // Prefer to keep heading roughly the same way — long winding paths feel
                // like a descent; tight spirals feel like a maze.
                var options = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    if (occupied.ContainsKey(cell + Dir[d])) continue;
                    if (FreeNeighbours(occupied, cell + Dir[d]) == 0 && i < mainLength - 1) continue;
                    options.Add(d);
                    if (d == lastDir) { options.Add(d); options.Add(d); }
                }
                if (options.Count == 0) return null;

                lastDir = options[rng.Next(options.Count)];
                cell += Dir[lastDir];
            }

            layout.BossRoom = previous;

            // Side rooms: branch off main-path rooms (not start or boss) into free cells.
            int sideCount = 2 + (depth > 1 ? 1 : 0);
            var mainRooms = new List<Room>(layout.Rooms);
            for (int s = 0, tries = 0; s < sideCount && tries < 50; tries++)
            {
                var host = mainRooms[1 + rng.Next(mainRooms.Count - 2)];
                int d = rng.Next(4);
                var target = host.Cell + Dir[d];
                if (occupied.ContainsKey(target)) continue;
                // Never adjacent to the boss room: the boss should be reached, not stumbled into.
                if (IsNeighbour(target, layout.BossRoom.Cell)) continue;

                var side = AddRoom(layout, occupied, target, RoomKind.Side, rng);
                Connect(layout, host, side, d);
                s++;
            }

            AssignFeatures(layout, rng);
            PlaceEnemySpawns(layout, rng, depth);
            PlaceFlameTraps(layout, rng);
            PlacePeddler(layout, rng);
            return layout;
        }

        private static Room AddRoom(DungeonLayout layout, Dictionary<Vector2Int, Room> occupied, Vector2Int cell, RoomKind kind, System.Random rng)
        {
            Vector2 size;
            float height;
            switch (kind)
            {
                case RoomKind.Start: size = new Vector2(12f, 12f); height = 5f; break;
                case RoomKind.Boss: size = new Vector2(30f, 30f); height = 12f; break;
                case RoomKind.Side: size = new Vector2(Even(rng, 12, 16), Even(rng, 12, 16)); height = 5f; break;
                default: size = new Vector2(Even(rng, 18, 32), Even(rng, 18, 32)); height = 6f + rng.Next(3); break;
            }

            var room = new Room
            {
                Id = layout.Rooms.Count,
                Kind = kind,
                Cell = cell,
                Center = new Vector3(cell.x * CellSize, 0f, cell.y * CellSize),
                Size = size,
                Height = height,
                DecorSeed = rng.Next(),
            };

            layout.Rooms.Add(room);
            occupied[cell] = room;
            return room;
        }

        private static void Connect(DungeonLayout layout, Room a, Room b, int dir)
        {
            a.Doors[dir] = true;
            b.Doors[(dir + 2) % 4] = true;
            layout.Links.Add(new Link { A = a.Id, B = b.Id, Dir = dir });
        }

        private static void AssignFeatures(DungeonLayout layout, System.Random rng)
        {
            AssignOtherFeatures(layout, rng);
            AssignHoles(layout, rng);
        }

        /// <summary>
        /// Secret caches (one or two a floor) and wall breaches (one, two from floor two on),
        /// each on a side of a room with no door. Rooms sit in 40 m cells, so there is always
        /// room for a niche behind the wall.
        /// </summary>
        private static void AssignHoles(DungeonLayout layout, System.Random rng)
        {
            bool Eligible(Room r, bool breach) =>
                r.Kind != RoomKind.Start && r.Kind != RoomKind.Boss && (!breach || r.Kind == RoomKind.Combat) &&
                r.Feature != RoomFeature.Chasm && r.Feature != RoomFeature.Vault && r.Feature != RoomFeature.Fountain && r.Holes.Count == 0;

            void Place(bool breach, int count)
            {
                var pool = layout.Rooms.FindAll(r => Eligible(r, breach));
                for (int n = 0; n < count && pool.Count > 0;)
                {
                    var room = pool[rng.Next(pool.Count)];
                    pool.Remove(room);
                    // Doorless walls first; a long wall with a doorway can take one off to the side.
                    float doorClear = CorridorWidth / 2f + WallHole.Width / 2f + 2.4f;
                    var sides = new List<int>();
                    for (int s = 0; s < 4; s++)
                    {
                        float len = (s == 0 || s == 2) ? room.Size.x : room.Size.y;
                        if (!room.Doors[s] && len >= 10f && !room.Balconies.Contains(s)) sides.Add(s);
                    }
                    if (sides.Count == 0)
                        for (int s = 0; s < 4; s++)
                        {
                            float len = (s == 0 || s == 2) ? room.Size.x : room.Size.y;
                            if (room.Doors[s] && len / 2f - 2.5f > doorClear && !room.Balconies.Contains(s)) sides.Add(s);
                        }
                    if (sides.Count == 0) continue;
                    int side = sides[rng.Next(sides.Count)];
                    float wallLen = (side == 0 || side == 2) ? room.Size.x : room.Size.y;
                    float along = room.Doors[side]
                        ? (rng.Next(2) == 0 ? -1f : 1f) * Mathf.Lerp(doorClear, wallLen / 2f - 2.5f, (float)rng.NextDouble())
                        : ((float)rng.NextDouble() * 2f - 1f) * (wallLen / 2f - 3f);
                    room.Holes.Add(new WallHole { Side = side, Along = along, Breach = breach });
                    n++;
                }
            }

            Place(false, 1 + (rng.NextDouble() < 0.5 ? 1 : 0));
            Place(true, layout.Depth >= 1 ? 2 : 1);
        }

        private static void AssignOtherFeatures(DungeonLayout layout, System.Random rng)
        {
            var combat = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat);
            // Shuffle so which room gets which feature varies by seed.
            for (int i = combat.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (combat[i], combat[j]) = (combat[j], combat[i]);
            }

            int next = 0;
            Room Take() => next < combat.Count ? combat[next++] : null;

            var hall = Take();
            if (hall != null)
            {
                hall.Feature = RoomFeature.GreatHall;
                hall.Size = new Vector2(34f, 34f);
                hall.Height = 9f;
            }

            var chasm = Take();
            if (chasm != null)
            {
                chasm.Feature = RoomFeature.Chasm;
                chasm.Size = new Vector2(Mathf.Max(chasm.Size.x, 26f), Mathf.Max(chasm.Size.y, 26f));
                chasm.Height = 8f;
                // The pit runs across the middle; bridges cross it on both axes.
                float pw = chasm.Size.x * 0.55f, pd = chasm.Size.y * 0.5f;
                chasm.Pit = new Rect(chasm.Center.x - pw / 2f, chasm.Center.z - pd / 2f, pw, pd);
            }

            var crypt = Take();
            if (crypt != null)
            {
                crypt.Feature = RoomFeature.Crypt;
                // Sarcophagi along the walls, clear of the doorways.
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side == 0 || side == 2;
                    float len = alongX ? crypt.Size.x : crypt.Size.y;
                    var d = Dir[side];
                    float off = (alongX ? crypt.Size.y : crypt.Size.x) / 2f - 1.6f;
                    for (float t = -len / 2f + 3f; t <= len / 2f - 3f; t += 5f)
                    {
                        if (crypt.Doors[side] && Mathf.Abs(t) < CorridorWidth + 1f) continue;
                        crypt.FeatureSpots.Add(crypt.Center + new Vector3(d.x, 0f, d.y) * off + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t)));
                    }
                }
            }

            // Every other combat room gets one of the newer mechanics, shuffled, no repeats
            // until all have been used.
            var extras = new List<RoomFeature> { RoomFeature.Gallery, RoomFeature.SunkenCourt, RoomFeature.Cistern, RoomFeature.Pendulums, RoomFeature.Ritual, RoomFeature.Library, RoomFeature.Pyre, RoomFeature.Gauntlet };
            for (int i = extras.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (extras[i], extras[j]) = (extras[j], extras[i]);
            }
            for (int k = 0; ; k++)
            {
                var room = Take();
                if (room == null) break;
                ShapeFeature(room, extras[k % extras.Count], rng);
            }

            var sides = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side);
            Room HostOf(Room side)
            {
                foreach (var link in layout.Links)
                {
                    if (link.B == side.Id) return layout.Rooms[link.A];
                    if (link.A == side.Id) return layout.Rooms[link.B];
                }
                return null;
            }
            // The vault's levers sit in the room its door opens onto — never the boss arena,
            // where pulling them would seal you in with the boss.
            var vault = sides.Find(r => HostOf(r) != null && HostOf(r).Kind != RoomKind.Boss);
            if (vault != null)
            {
                vault.Feature = RoomFeature.Vault;
                vault.VaultHost = HostOf(vault);
                // Three levers spread around the host room's walls.
                var host = vault.VaultHost;
                if (host != null)
                {
                    for (int side = 0, placed = 0; side < 4 && placed < 3; side++)
                    {
                        // Mid-wall, or off to one side of a doorway.
                        var d = Dir[side];
                        bool alongX = side == 0 || side == 2;
                        float off = (alongX ? host.Size.y : host.Size.x) / 2f - 0.6f;
                        float t = (alongX ? host.Size.x : host.Size.y) * (host.Doors[side] ? 0.32f : 0f);
                        vault.FeatureSpots.Add(host.Center + new Vector3(d.x, 0f, d.y) * off + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t)));
                        placed++;
                    }
                }
            }
            var fountain = sides.Find(r => r != vault);
            if (fountain != null)
            {
                fountain.Feature = RoomFeature.Fountain;
                fountain.FeatureSpots.Add(fountain.Center);
            }
        }

        public const float BalconyHeight = 2.8f, BalconyDepth = 3.2f, SunkenDepth = 1.8f, CisternDepth = 0.6f;

        /// <summary>Give a combat room one of the newer mechanics, and the size and shape it needs.</summary>
        private static void ShapeFeature(Room room, RoomFeature feature, System.Random rng)
        {
            room.Feature = feature;
            switch (feature)
            {
                case RoomFeature.Gallery:
                    // Balconies along the two long walls; tall enough to stand under them.
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 22f), Mathf.Max(room.Size.y, 22f));
                    room.Height = 9f;
                    bool longX = room.Size.x >= room.Size.y;
                    room.Balconies.Add(longX ? 0 : 1);
                    room.Balconies.Add(longX ? 2 : 3);
                    break;
                case RoomFeature.SunkenCourt:
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 24f), Mathf.Max(room.Size.y, 24f));
                    room.Height = 8f;
                    room.Low = new Rect(room.Center.x - room.Size.x * 0.3f, room.Center.z - room.Size.y * 0.3f, room.Size.x * 0.6f, room.Size.y * 0.6f);
                    break;
                case RoomFeature.Cistern:
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 22f), Mathf.Max(room.Size.y, 22f));
                    room.Height = 7f;
                    room.Low = new Rect(room.Center.x - room.Size.x / 2f + 2.5f, room.Center.z - room.Size.y / 2f + 2.5f, room.Size.x - 5f, room.Size.y - 5f);
                    // Stepping stones: dry ground in a loose grid across the water.
                    for (float x = -room.Size.x / 2f + 6f; x <= room.Size.x / 2f - 6f; x += 5.5f)
                        for (float z = -room.Size.y / 2f + 6f; z <= room.Size.y / 2f - 6f; z += 5.5f)
                            if (rng.NextDouble() < 0.6)
                                room.FeatureSpots.Add(room.Center + new Vector3(x + (float)(rng.NextDouble() - 0.5) * 1.5f, 0f, z + (float)(rng.NextDouble() - 0.5) * 1.5f));
                    break;
                case RoomFeature.Pendulums:
                    // Blades swing across the room's long axis, spaced along it.
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 20f), Mathf.Max(room.Size.y, 20f));
                    room.Height = 9f;
                    bool alongX = room.Size.x >= room.Size.y;
                    float len = alongX ? room.Size.x : room.Size.y;
                    int n = Mathf.Clamp(Mathf.FloorToInt(len / 7f), 2, 4);
                    for (int i = 0; i < n; i++)
                    {
                        float t = -len / 2f + len * (i + 0.5f) / n;
                        room.FeatureSpots.Add(room.Center + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t)));
                    }
                    break;
                case RoomFeature.Library:
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 24f), Mathf.Max(room.Size.y, 24f));
                    room.Height = 9f;
                    break;
                case RoomFeature.Pyre:
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 24f), Mathf.Max(room.Size.y, 24f));
                    room.Height = 11f;
                    room.FeatureSpots.Add(room.Center);
                    break;
                case RoomFeature.Gauntlet:
                    // Long and narrow-ish: the wave runs down the long axis.
                    if (room.Size.x >= room.Size.y) room.Size = new Vector2(Mathf.Max(room.Size.x, 26f), Mathf.Clamp(room.Size.y, 14f, 20f));
                    else room.Size = new Vector2(Mathf.Clamp(room.Size.x, 14f, 20f), Mathf.Max(room.Size.y, 26f));
                    room.Height = 7f;
                    break;
                case RoomFeature.Ritual:
                    room.Size = new Vector2(Mathf.Max(room.Size.x, 20f), Mathf.Max(room.Size.y, 20f));
                    room.Height = 8f;
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (i * 120f + 90f) * Mathf.Deg2Rad;
                        room.FeatureSpots.Add(room.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Min(room.Size.x, room.Size.y) * 0.3f);
                    }
                    break;
            }
        }

        /// <summary>
        /// A library's shelves (in plan, x/z): rows across the long axis with a central aisle, a
        /// gap at each wall, and the cross aisle through the side doors left open.
        /// </summary>
        public static List<Rect> LibraryShelves(Room room)
        {
            var list = new List<Rect>();
            if (room.Feature != RoomFeature.Library) return list;
            bool alongX = room.Size.x >= room.Size.y;
            float len = alongX ? room.Size.x : room.Size.y, wid = alongX ? room.Size.y : room.Size.x;
            for (float t = -len / 2f + 4f; t <= len / 2f - 4f; t += 3.6f)
            {
                if (Mathf.Abs(t) < CorridorWidth / 2f + 1.8f) continue;
                foreach (int s in new[] { -1, 1 })
                {
                    float a0 = s < 0 ? -wid / 2f + 1.8f : 2.2f, a1 = s < 0 ? -2.2f : wid / 2f - 1.8f;
                    if (a1 - a0 < 1.5f) continue;
                    list.Add(alongX
                        ? new Rect(room.Center.x + t - 0.35f, room.Center.z + a0, 0.7f, a1 - a0)
                        : new Rect(room.Center.x + a0, room.Center.z + t - 0.35f, a1 - a0, 0.7f));
                }
            }
            return list;
        }

        /// <summary>Inside a sunken court's pit or a cistern's basin (in plan)?</summary>
        public static bool InLowArea(Room room, Vector3 p) =>
            (room.Feature == RoomFeature.SunkenCourt || room.Feature == RoomFeature.Cistern) && room.Low.Contains(new Vector2(p.x, p.z));

        /// <summary>The pendulums swing across the long axis: true if that axis is X.</summary>
        public static bool PendulumsAlongX(Room room) => room.Size.x >= room.Size.y;

        /// <summary>Is this spot in the flooded part of a cistern (and not up on a stepping stone)?</summary>
        public static bool InCisternWater(Room room, Vector3 p) =>
            room.Feature == RoomFeature.Cistern && room.Low.Contains(new Vector2(p.x, p.z)) && p.y < -0.15f;

        private static void PlaceEnemySpawns(DungeonLayout layout, System.Random rng, int depth)
        {
            foreach (var room in layout.Rooms)
            {
                if (room.Kind == RoomKind.Start || room.Kind == RoomKind.Boss) continue;
                // Crypt dead come out of the sarcophagi; vaults and fountains are rewards.
                if (room.Feature == RoomFeature.Crypt || room.Feature == RoomFeature.Vault || room.Feature == RoomFeature.Fountain) continue;

                int count = room.Kind == RoomKind.Side ? 2 : 3 + rng.Next(2) + Mathf.Min(depth, 3);
                if (room.Feature == RoomFeature.GreatHall) count += 3;
                for (int i = 0, tries = 0; i < count && tries < 60; tries++)
                {
                    float x = (float)(rng.NextDouble() - 0.5) * (room.Size.x - 4f);
                    float z = (float)(rng.NextDouble() - 0.5) * (room.Size.y - 4f);
                    var p = room.Center + new Vector3(x, 0.1f, z);
                    // Never inside the pit, the pyre, or a bookshelf.
                    if (room.Feature == RoomFeature.Chasm && room.Pit.Contains(new Vector2(p.x, p.z), true)) continue;
                    if (room.Feature == RoomFeature.Pyre && Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(room.Center.x, 0f, room.Center.z)) < PyreFire.CoreRadius + 1.2f) continue;
                    if (room.Feature == RoomFeature.Library && LibraryShelves(room).Exists(r => { var g = r; g.xMin -= 0.6f; g.yMin -= 0.6f; g.xMax += 0.6f; g.yMax += 0.6f; return g.Contains(new Vector2(p.x, p.z)); })) continue;
                    if (room.Feature == RoomFeature.SunkenCourt && room.Low.Contains(new Vector2(p.x, p.z))) p.y = -SunkenDepth + 0.1f;
                    if (room.Feature == RoomFeature.Cistern && room.Low.Contains(new Vector2(p.x, p.z))) p.y = -CisternDepth + 0.1f;
                    room.EnemySpawns.Add(p);
                    i++;
                }
                // A gallery posts some of its dead up on the balconies.
                foreach (int side in room.Balconies)
                {
                    var d = Dir[side];
                    bool alongX = side == 0 || side == 2;
                    float off = (alongX ? room.Size.y : room.Size.x) / 2f - BalconyDepth / 2f;
                    float t = (float)(rng.NextDouble() - 0.5) * (alongX ? room.Size.x : room.Size.y) * 0.4f;
                    room.EnemySpawns.Add(room.Center + new Vector3(d.x, 0f, d.y) * off + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t)) + Vector3.up * (BalconyHeight + 0.1f));
                }
            }
        }

        private static void PlacePeddler(DungeonLayout layout, System.Random rng)
        {
            if (layout.Depth < 1 || rng.NextDouble() > 0.6) return;
            var pool = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side && r.Feature != RoomFeature.Vault);
            if (pool.Count == 0) return;
            var room = pool[rng.Next(pool.Count)];
            // Against a doorless side wall (the chest has the back wall), facing the middle.
            int entry = 0;
            for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
            int back = (entry + 2) % 4;
            for (int side = 0; side < 4; side++)
            {
                if (room.Doors[side] || room.HoleOn(side) != null || side == back) continue;
                var d = Dir[side];
                var outward = new Vector3(d.x, 0f, d.y);
                float half = (side == 0 || side == 2 ? room.Size.y : room.Size.x) / 2f;
                var spot = room.Center + outward * (half - 1.4f) + new Vector3(-outward.z, 0f, outward.x) * 2.5f;
                layout.PeddlerSpot = spot;
                layout.PeddlerFacing = -outward;
                return;
            }
        }

        /// <summary>Long corridors sometimes carry a flame trap, a third of the way along (clear of spike traps at the middle).</summary>
        private static void PlaceFlameTraps(DungeonLayout layout, System.Random rng)
        {
            int n = 0;
            foreach (var link in layout.Links)
            {
                var a = layout.Rooms[link.A];
                var b = layout.Rooms[link.B];
                var d = Dir[link.Dir];
                bool alongX = d.x != 0;
                var axis = new Vector3(d.x, 0f, d.y);
                var start = a.Center + axis * ((alongX ? a.Size.x : a.Size.y) / 2f + 0.8f);
                var end = b.Center - axis * ((alongX ? b.Size.x : b.Size.y) / 2f + 0.8f);
                float len = Vector3.Distance(start, end);
                if (len < 9f || rng.NextDouble() > 0.45) continue;
                layout.FlameTraps.Add(new FlameTrap
                {
                    At = Vector3.Lerp(start, end, rng.NextDouble() < 0.5 ? 0.3f : 0.7f),
                    Across = new Vector3(-axis.z, 0f, axis.x),
                    Phase = n++,
                });
            }
        }

        private static int FreeNeighbours(Dictionary<Vector2Int, Room> occupied, Vector2Int cell)
        {
            int free = 0;
            foreach (var d in Dir)
            {
                if (!occupied.ContainsKey(cell + d)) free++;
            }
            return free;
        }

        private static bool IsNeighbour(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

        private static float Even(System.Random rng, int min, int max) => (min + rng.Next(max - min + 1)) / 2 * 2;
    }
}
