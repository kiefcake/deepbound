using System.Collections.Generic;
using System.Linq;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject[] lairPrefabs;
        public const float LairChance = 0.6f;

        public const float LairWakeRadius = 7.5f;

        public static bool ForceLair;

        private class LairState
        {
            public DungeonLayout.Room Room;
            public readonly List<Portcullis> Gates = new List<Portcullis>();
            public LairBrain Boss;
            public int State;
            public bool Opened;
            public float EmptySince = -1f;
            public float OutSince = -1f, NextDiag;

            public int PutBack;
        }
        private LairState lair;

        public DungeonLayout.Room LairRoom => lair?.Room;
        public LairBrain LairBoss => lair?.Boss;
        public bool LairCleared => lair != null && lair.State == 2;
        private bool IsLairRoom(DungeonLayout.Room room) => lair != null && lair.Room == room;

        private void SetupLair(DungeonLayout layout)
        {
            lair = null;
            int act = Act(layout.Depth);
            if (lairPrefabs == null || act > 3 || act >= lairPrefabs.Length || lairPrefabs[act] == null || portcullisPrefab == null) return;
            if (InTutorial || (!ForceLair && (TestMode || Practice.Value || Random.value > LairChance))) return;
            var room = PickLairRoom(layout);
            if (room == null) return;
            lair = new LairState { Room = room };
            lair.Gates.AddRange(SpawnGates(room));

            var at = room.Center;
            float off = Mathf.Min(room.Size.x, room.Size.y) * 0.26f;
            foreach (var o in new[] { new Vector3(0f, 0f, off), new Vector3(off, 0f, 0f), new Vector3(0f, 0f, -off), new Vector3(-off, 0f, 0f), Vector3.zero })
                if (UnityEngine.AI.NavMesh.SamplePosition(room.Center + o, out var h, 1.5f, UnityEngine.AI.NavMesh.AllAreas) && Mathf.Abs(h.position.y - room.Center.y) < 0.3f) { at = h.position; break; }
            var obj = Spawn(lairPrefabs[act], at, Quaternion.LookRotation(layout.Rooms[0].Center - room.Center));
            var boss = obj.GetComponent<LairBrain>();
            if (boss == null) { obj.Despawn(); lair = null; return; }
            boss.RoomCenter = room.Center;
            boss.RoomSize = room.Size;
            boss.Dormant = true;

            boss.SetMinionPrefab(boss is TwiceCrownedBrain || boss is MirrorNunBrain ? lairPrefabs[act]
                : boss is LanternFisherBrain && lampreyPrefab != null ? lampreyPrefab : skeletonPrefab);
            boss.SetGuardPrefab(shieldbearerPrefab);
            boss.ServerScaleBoss(1, layout.Depth);
            lair.Boss = boss;
            Debug.Log("[Director] lair: " + boss.DisplayName + " in room " + room.Id + " (" + room.Kind + " " + room.Feature + ")");
        }

        private DungeonLayout.Room PickLairRoom(DungeonLayout layout)
        {
            var rng = new System.Random(layout.Seed ^ 0x1A1C);
            var list = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && r.Id >= 2 && !r.Special && !r.Peaceful
                && (r.Feature == RoomFeature.None || r.Feature == RoomFeature.GreatHall) && r.Decks.Count == 0
                && Mathf.Min(r.Size.x, r.Size.y) >= 12f
                && !(layout.PeddlerSpot.HasValue && r.Contains(layout.PeddlerSpot.Value, 0.5f)));
            return list.Count == 0 ? null : list[rng.Next(list.Count)];
        }

        private void TickLair()
        {
            if (lair == null || lair.State == 2) return;
            var room = lair.Room;
            var boss = lair.Boss;
            if (lair.State == 0)
            {
                int inside = InAmbushTrigger(room);
                if (inside == 0 || boss == null) return;

                bool roused = false;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var d = p.transform.position - boss.transform.position; d.y = 0f;
                    if (d.magnitude <= LairWakeRadius) roused = true;
                }
                if (!roused) return;
                lair.State = 1;
                ShoveUnderGates(lair.Gates, room);
                SetGates(lair.Gates, true);
                boss.ServerEngage(inside, Layout.Depth);
                BossIntroRpc(boss.DisplayName.ToUpperInvariant(), boss.Epithet, boss.Hint);
                Debug.Log("[Director] lair sealed on " + inside + ": " + boss.DisplayName + " | room " + room.Center + " size " + room.Size + ", keeper " + boss.transform.position
                    + ", gates " + string.Join(" ", lair.Gates.Where(g => g != null).Select(g => g.transform.position.ToString()))
                    + ", players " + string.Join(" ", PlayerLife.All.Where(p => p != null).Select(p => p.transform.position.ToString())));
                return;
            }
            if (boss == null || boss.IsDead)
            {
                lair.State = 2;
                SetGates(lair.Gates, false);
                if (chestPrefab != null)
                {
                    var chest = Spawn(chestPrefab, DropSpots.ClearForChest(room.Center, Quaternion.Euler(0f, 180f, 0f), room), Quaternion.Euler(0f, 180f, 0f));
                    chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                }
                TitleCardRpc("THE LAIR FALLS", "Its hoard is yours");
                return;
            }

            if (!room.Contains(boss.transform.position, -0.5f))
            {
                if (lair.OutSince < 0f) lair.OutSince = Time.time;
                else if (Time.time - lair.OutSince > 0.8f)
                {
                    var inward = room.Center - boss.transform.position; inward.y = 0f;
                    boss.ServerDebugPlace(boss.transform.position + inward.normalized * Mathf.Min(inward.magnitude, 6f));
                    lair.OutSince = -1f;
                    lair.PutBack++;
                    Debug.Log("[Director] lair keeper was outside its lair: put back in");
                }
            }
            else lair.OutSince = -1f;

            if (Time.time >= lair.NextDiag)
            {
                lair.NextDiag = Time.time + 20f;
                Debug.Log("[Director] lair: keeper " + boss.transform.position + " in " + room.Contains(boss.transform.position, -0.5f) + ", players " + string.Join(" ", PlayerLife.All.Where(p => p != null).Select(p => p.transform.position + (room.Contains(p.transform.position, -0.1f) ? " in" : " out"))) + ", opened " + lair.Opened);
            }
            if (AnyoneWellInside(room)) lair.EmptySince = -1f;
            else if (lair.EmptySince < 0f) lair.EmptySince = Time.time;
            if (!lair.Opened && lair.EmptySince >= 0f && Time.time - lair.EmptySince > 2f)
            {
                lair.Opened = true;
                SetGates(lair.Gates, false);
                TitleCardRpc("THE GATES LIFT", "Go to them");
            }
        }

        private static bool AnyoneWellInside(DungeonLayout.Room room)
        {
            foreach (var p in PlayerLife.All)
                if (p != null && !p.Downed.Value && p.Health != null && p.Health.IsAlive && room.Contains(p.transform.position, -0.1f)) return true;
            return false;
        }

        public int LairPutBack => lair?.PutBack ?? 0;

        public LairBrain ServerDebugLair() => lair?.Boss;

        public LairBrain ServerDebugSpawnLair(int act, Vector3 at, Vector2 roomSize)
        {
            if (lairPrefabs == null || act < 0 || act >= lairPrefabs.Length || lairPrefabs[act] == null) return null;
            var obj = Spawn(lairPrefabs[act], at, Quaternion.identity);
            var boss = obj.GetComponent<LairBrain>();
            if (boss == null) { obj.Despawn(); Debug.LogError("[Director] lair prefab " + act + " has no LairBrain"); return null; }
            boss.RoomCenter = at;
            boss.RoomSize = roomSize;
            boss.SetMinionPrefab(boss is TwiceCrownedBrain || boss is MirrorNunBrain ? lairPrefabs[act] : skeletonPrefab);
            boss.SetGuardPrefab(shieldbearerPrefab);
            boss.ServerEngage(1, Mathf.Max(0, Floor.Value.y));
            return boss;
        }
    }
}
