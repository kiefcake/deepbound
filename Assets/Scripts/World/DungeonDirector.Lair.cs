using System.Collections.Generic;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// LAIRS (2026-11-14): a sealed side lair on an act's floors (acts 0-3, <see cref="LairChance"/>) with that act's mini boss
    /// asleep in it (<see cref="LairBrain"/>: Mother Marrow, the Lantern Fisher, the Twice-Crowned, Mother Veyle). Like an
    /// ambush, whoever walks in is shut in, and it wakes scaled to how many came - solo or the whole party. If all of them
    /// go down the gates lift so the rest can come. Dead, it leaves a hoard (its guaranteed drop and a chest) and a deed.
    /// The lair room is kept clear of everything else on the floor (<see cref="IsAmbushRoom"/> counts it).
    /// </summary>
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject[] lairPrefabs;
        public const float LairChance = 0.6f;
        /// <summary>How close someone must come to the sleeping keeper to wake it.</summary>
        public const float LairWakeRadius = 7.5f;
        /// <summary>Tests: a lair on every floor of acts 0-3, of this act's kind (or -1 for the floor's own).</summary>
        public static bool ForceLair;

        private class LairState
        {
            public DungeonLayout.Room Room;
            public readonly List<Portcullis> Gates = new List<Portcullis>();
            public LairBrain Boss;
            public int State; // 0 asleep, 1 fought, 2 done
            public bool Opened;
        }
        private LairState lair;

        public DungeonLayout.Room LairRoom => lair?.Room;
        public LairBrain LairBoss => lair?.Boss;
        public bool LairCleared => lair != null && lair.State == 2;
        private bool IsLairRoom(DungeonLayout.Room room) => lair != null && lair.Room == room;

        /// <summary>Server, right after the navmesh: pick the lair and put its keeper to sleep in it.</summary>
        private void SetupLair(DungeonLayout layout)
        {
            lair = null;
            int act = Act(layout.Depth);
            if (lairPrefabs == null || act > 3 || act >= lairPrefabs.Length || lairPrefabs[act] == null || portcullisPrefab == null) return;
            if (!ForceLair && (TestMode || Practice.Value || Random.value > LairChance)) return;
            var room = PickLairRoom(layout);
            if (room == null) return;
            lair = new LairState { Room = room };
            lair.Gates.AddRange(SpawnGates(room));
            // On open floor, off the room's middle: a lair room can hold a centrepiece, and a keeper asleep on top of it
            // was out of reach and could not come down (2026-11-14 bot run: Mother Marrow at 100% for 11 minutes).
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
            // Its helpers: Mother Marrow's knitters, the Fisher's ink-spawn; the twin kings and the nun echo themselves.
            boss.SetMinionPrefab(boss is TwiceCrownedBrain || boss is MirrorNunBrain ? lairPrefabs[act]
                : boss is LanternFisherBrain && lampreyPrefab != null ? lampreyPrefab : skeletonPrefab);
            boss.SetGuardPrefab(shieldbearerPrefab);
            boss.ServerScaleBoss(1, layout.Depth);
            lair.Boss = boss;
            Debug.Log("[Director] lair: " + boss.DisplayName + " in room " + room.Id + " (" + room.Kind + " " + room.Feature + ")");
        }

        /// <summary>A plain combat room away from the start, by the seed; never the boss room or one already spoken for.</summary>
        private DungeonLayout.Room PickLairRoom(DungeonLayout layout)
        {
            var rng = new System.Random(layout.Seed ^ 0x1A1C);
            var list = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && r.Id >= 2 && !r.Special && !r.Peaceful
                && (r.Feature == RoomFeature.None || r.Feature == RoomFeature.GreatHall || r.Feature == RoomFeature.Crypt)
                && Mathf.Min(r.Size.x, r.Size.y) >= 12f
                && !(layout.PeddlerSpot.HasValue && r.Contains(layout.PeddlerSpot.Value, 0.5f)));
            return list.Count == 0 ? null : list[rng.Next(list.Count)];
        }

        /// <summary>Server: the lair while exploring.</summary>
        private void TickLair()
        {
            if (lair == null || lair.State == 2) return;
            var room = lair.Room;
            var boss = lair.Boss;
            if (lair.State == 0)
            {
                int inside = InAmbushTrigger(room);
                if (inside == 0 || boss == null) return;
                // It sleeps until someone comes close (2026-11-14: a lair on the way through sealed on a bot just crossing the
                // hall): skirt round it, or walk up and wake it.
                bool roused = false;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var d = p.transform.position - boss.transform.position; d.y = 0f;
                    if (d.magnitude <= LairWakeRadius) roused = true;
                }
                if (!roused) return;
                lair.State = 1;
                SetGates(lair.Gates, true);
                boss.ServerEngage(inside, Layout.Depth);
                BossIntroRpc(boss.DisplayName.ToUpperInvariant(), boss.Epithet, boss.Hint);
                Debug.Log("[Director] lair sealed on " + inside + ": " + boss.DisplayName);
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
            // Everyone shut in is down: lift the gates for the rest of the party; it fights on.
            // "Inside" means a metre past the walls: someone who slipped out under the falling gate and stands in the doorway is
            // not shut in with it (2026-11-14 bot run: the keeper fought on behind the bars for six minutes).
            if (!lair.Opened && !AnyoneWellInside(room))
            {
                lair.Opened = true;
                SetGates(lair.Gates, false);
                TitleCardRpc("THE GATES LIFT", "Go to them");
            }
        }

        private static bool AnyoneWellInside(DungeonLayout.Room room)
        {
            foreach (var p in PlayerLife.All)
                if (p != null && !p.Downed.Value && p.Health != null && p.Health.IsAlive && room.Contains(p.transform.position, -1f)) return true;
            return false;
        }

        /// <summary>Tests: the lair's keeper, wherever it sleeps (or null).</summary>
        public LairBrain ServerDebugLair() => lair?.Boss;

        /// <summary>Tests: spawn a lair keeper of <paramref name="act"/> at <paramref name="at"/>, awake, with no room.</summary>
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
