using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.World
{
    public enum RoomKind { Start, Combat, Side, Boss }

    /// <summary>
    /// What makes a room more than four walls. Assigned per floor from the seed, so every
    /// peer agrees. Combat rooms can be a Great Hall, a Chasm or a Crypt; side rooms can be
    /// a Vault or a Fountain.
    /// </summary>
    public enum RoomFeature { None, GreatHall, Chasm, Crypt, Vault, Fountain }

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
        public const float CellSize = 40f;
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

        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<Link> Links = new List<Link>();
        public int Seed { get; private set; }
        public int Depth { get; private set; }

        public Room StartRoom => Rooms[0];
        public Room BossRoom { get; private set; }

        public Vector3 PlayerSpawn => StartRoom.Center;
        public Vector3 BossSpawn => BossRoom.Center + new Vector3(0f, 0f, BossRoom.Size.y * 0.2f);

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
                default: size = new Vector2(Even(rng, 16, 28), Even(rng, 16, 28)); height = 5.5f + rng.Next(3); break;
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
                for (int n = 0; n < count && pool.Count > 0; n++)
                {
                    var room = pool[rng.Next(pool.Count)];
                    pool.Remove(room);
                    var sides = new List<int>();
                    for (int s = 0; s < 4; s++)
                    {
                        float len = (s == 0 || s == 2) ? room.Size.x : room.Size.y;
                        if (!room.Doors[s] && len >= 10f) sides.Add(s);
                    }
                    if (sides.Count == 0) continue;
                    int side = sides[rng.Next(sides.Count)];
                    float wallLen = (side == 0 || side == 2) ? room.Size.x : room.Size.y;
                    float along = ((float)rng.NextDouble() * 2f - 1f) * (wallLen / 2f - 3f);
                    room.Holes.Add(new WallHole { Side = side, Along = along, Breach = breach });
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
                hall.Size = new Vector2(30f, 30f);
                hall.Height = 9f;
            }

            var chasm = Take();
            if (chasm != null)
            {
                chasm.Feature = RoomFeature.Chasm;
                chasm.Size = new Vector2(Mathf.Max(chasm.Size.x, 24f), Mathf.Max(chasm.Size.y, 24f));
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
                    // Never inside the pit.
                    if (room.Feature == RoomFeature.Chasm && room.Pit.Contains(new Vector2(p.x, p.z), true)) continue;
                    room.EnemySpawns.Add(p);
                    i++;
                }
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
