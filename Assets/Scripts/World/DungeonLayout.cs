using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.World
{
    public enum RoomKind { Start, Combat, Side, Boss }

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
        public const float CellSize = 30f;
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

        /// <summary>Player spawn points around the start room centre, one per slot.</summary>
        public Vector3 PlayerSpawnFor(ulong clientId)
        {
            float angle = clientId * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            return PlayerSpawn + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f + Vector3.up * 0.1f;
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
                case RoomKind.Boss: size = new Vector2(26f, 26f); height = 11f; break;
                case RoomKind.Side: size = new Vector2(Even(rng, 10, 14), Even(rng, 10, 14)); height = 4.5f; break;
                default: size = new Vector2(Even(rng, 13, 22), Even(rng, 13, 22)); height = 5f + rng.Next(3); break;
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

        private static void PlaceEnemySpawns(DungeonLayout layout, System.Random rng, int depth)
        {
            foreach (var room in layout.Rooms)
            {
                if (room.Kind == RoomKind.Start || room.Kind == RoomKind.Boss) continue;

                int count = room.Kind == RoomKind.Side ? 2 : 3 + rng.Next(2) + Mathf.Min(depth, 3);
                for (int i = 0; i < count; i++)
                {
                    float x = (float)(rng.NextDouble() - 0.5) * (room.Size.x - 4f);
                    float z = (float)(rng.NextDouble() - 0.5) * (room.Size.y - 4f);
                    room.EnemySpawns.Add(room.Center + new Vector3(x, 0.1f, z));
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
