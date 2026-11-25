using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject puzzleMirrorPrefab, scaleWeightPrefab, statDraughtPrefab;

        private StatDraught SpawnDraught(DungeonLayout.Room room, Vector3 at)
        {
            if (room != null) at.y = room.Center.y + room.RaisedAt(at);
            var obj = Instantiate(statDraughtPrefab, at, Quaternion.identity);
            var dr = obj.GetComponent<StatDraught>();
            dr.Prepare((Core.StatType)Random.Range(0, 8));
            obj.Spawn(destroyWithScene: true);
            return dr;
        }

        public StatDraught ServerDebugDraught(Vector3 at) => statDraughtPrefab == null ? null : SpawnDraught(null, at);
        [SerializeField] private NetworkObject puzzleRoomPrefab, puzzleStatuePrefab, wellFishingPrefab, knifeBoardPrefab, bellFramePrefab, armWrestlePrefab;
        private int debugPuzzles;

        public int PuzzlesSolved { get; private set; }

        private PuzzleRoom SpawnPuzzle(PuzzleRoom.Kind kind, Vector3 centre, Vector3 coffer, int roomId)
        {
            if (puzzleRoomPrefab == null) return null;
            var obj = Instantiate(puzzleRoomPrefab, Vfx.Floor(coffer), Quaternion.identity);
            obj.Spawn(destroyWithScene: true);
            spawned.Add(obj);
            var p = obj.GetComponent<PuzzleRoom>();
            int seed = Floor.Value.x ^ (roomId * 7919 + 17);
            p.ServerSetup(kind, centre, roomId, Floor.Value.y, seed);
            if (kind == PuzzleRoom.Kind.Watchers && puzzleStatuePrefab != null)
            {
                var rng = new System.Random(seed);
                for (int i = 0; i < 4; i++)
                {
                    var s = Instantiate(puzzleStatuePrefab, Vfx.Floor(p.Centre.Value + PuzzleRoom.Axis(i) * PuzzleRoom.StatueRing), Quaternion.identity);
                    var st = s.GetComponent<PuzzleStatue>();
                    byte target = (byte)((i + 2) % 4);
                    st.Group.Value = roomId;
                    st.Target.Value = target;
                    st.Facing.Value = (byte)((target + 1 + rng.Next(3)) % 4);
                    s.Spawn(destroyWithScene: true);
                    spawned.Add(s);
                }
            }
            if (kind == PuzzleRoom.Kind.Mirrors && puzzleMirrorPrefab != null) SpawnMirrors(p, roomId, seed);
            if (kind == PuzzleRoom.Kind.Scales && scaleWeightPrefab != null) SpawnScales(p, roomId, seed);
            return p;
        }

        private void SpawnMirrors(PuzzleRoom p, int roomId, int seed)
        {
            var rng = new System.Random(seed ^ 0x5eed);
            int r0 = 0, r2 = 0;
            var cells = new List<(int x, int r)>();
            for (int tries = 0; tries < 300; tries++)
            {
                r0 = rng.Next(-2, 3);
                int x1 = rng.Next(-2, 1), r1 = rng.Next(-2, 3), x2 = rng.Next(x1 + 1, 3);
                r2 = rng.Next(-2, 3);
                if (r1 == r0 || r2 == r1) continue;
                cells.Clear();
                cells.Add((x1, r0)); cells.Add((x1, r1)); cells.Add((x2, r1)); cells.Add((x2, r2));
                if (Solutions(r0, r2, cells).Count > 0) break;
                cells.Clear();
            }
            if (cells.Count == 0) return;
            var sols = Solutions(r0, r2, cells);
            int sol = sols[0];

            int start = sol;
            for (int tries = 0; tries < 64; tries++)
            {
                int c = rng.Next(16);
                if (sols.Contains(c) || Bits(c ^ sol) < 2) continue;
                start = c;
                break;
            }
            p.SourceRow.Value = (sbyte)r0;
            p.CrystalRow.Value = (sbyte)r2;
            var slash = (p.AxisA + p.AxisB).normalized;
            var back = (p.AxisA - p.AxisB).normalized;
            for (int i = 0; i < cells.Count; i++)
            {
                var obj = Instantiate(puzzleMirrorPrefab, Vfx.Floor(p.GridPos(cells[i].x, cells[i].r)), Quaternion.identity);
                var m = obj.GetComponent<PuzzleMirror>();
                m.Group.Value = roomId;
                m.GridX.Value = (sbyte)cells[i].x;
                m.GridR.Value = (sbyte)cells[i].r;
                m.State.Value = (byte)((start >> i) & 1);
                m.SolutionState = (byte)((sol >> i) & 1);
                m.DiagSlash.Value = slash;
                m.DiagBack.Value = back;
                obj.Spawn(destroyWithScene: true);
                spawned.Add(obj);
            }
        }

        private void SpawnScales(PuzzleRoom p, int roomId, int seed)
        {
            var rng = new System.Random(seed ^ 0x5ca1e);
            int pick = Act(Floor.Value.y) >= 2 ? 3 : 2;
            var ws = new List<int>();
            while (ws.Count < PuzzleRoom.ScaleWeights) { int w = rng.Next(1, 8); if (!ws.Contains(w)) ws.Add(w); }
            int target = 0;
            var order = new List<int> { 0, 1, 2, 3, 4 };
            for (int tries = 0; tries < 200 && target == 0; tries++)
            {
                for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                int sum = 0;
                for (int i = 0; i < pick; i++) sum += ws[order[i]];
                if (!ws.Contains(sum) && sum <= 16) target = sum;
            }
            if (target == 0) target = ws[0] + ws[1];
            p.Target.Value = (byte)target;
            var b = p.AxisB;
            for (int i = 0; i < ws.Count; i++)
            {
                var dir = Quaternion.AngleAxis(110f + i * 35f, Vector3.up) * b;
                var want = p.Centre.Value + dir * (i % 2 == 0 ? 3.4f : 2.6f);
                var at = Vfx.Floor(DropSpots.Settle(p.Centre.Value, want));
                var obj = Instantiate(scaleWeightPrefab, at, Quaternion.Euler(0f, rng.Next(360), 0f));
                var sw = obj.GetComponent<ScaleWeight>();
                sw.Weight.Value = (byte)ws[i];
                sw.Group.Value = roomId;
                obj.Spawn(destroyWithScene: true);
                spawned.Add(obj);
            }
        }

        private static int Bits(int m) { int c = 0; while (m != 0) { c += m & 1; m >>= 1; } return c; }

        private static List<int> Solutions(int sourceRow, int crystalRow, List<(int x, int r)> cells)
        {
            var found = new List<int>();
            var map = new Dictionary<int, byte>();
            for (int c = 0; c < (1 << cells.Count); c++)
            {
                map.Clear();
                for (int i = 0; i < cells.Count; i++) map[PuzzleRoom.Key(cells[i].x, cells[i].r)] = (byte)((c >> i) & 1);
                if (map.Count == cells.Count && PuzzleRoom.TraceGrid(sourceRow, crystalRow, map, null)) found.Add(c);
            }
            return found;
        }

        public void ServerPuzzleSolved(PuzzleRoom puzzle)
        {
            var at = puzzle.transform.position;
            var room = Layout != null && puzzle.RoomId.Value >= 0 && puzzle.RoomId.Value < Layout.Rooms.Count ? Layout.Rooms[puzzle.RoomId.Value] : null;
            var face = puzzle.Centre.Value - at;
            face.y = 0f;
            var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
            if (chestPrefab != null)
            {
                var chest = Spawn(chestPrefab, room != null ? DropSpots.ClearForChest(at, rot, room) : at, rot);
                chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                spawned.Add(chest);
            }
            var front = at + rot * Vector3.forward * 1.6f;
            ServerDropLoot(front, LootKind.Sigil, 1);
            for (int i = 0; i < 4; i++) ServerDropLoot(front + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 0.9f, LootKind.Gold, 15 + 8 * (int)Tier(Floor.Value.y));
            PuzzlesSolved++;
        }

        public PuzzleRoom ServerDebugPuzzle(PuzzleRoom.Kind kind, Vector3 centre, Vector3 coffer) => SpawnPuzzle(kind, centre, coffer, 9000 + debugPuzzles++);
    }
}
