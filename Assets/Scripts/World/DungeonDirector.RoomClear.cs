using System.Collections.Generic;
using Deepbound.Enemies;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Room clears (2026-10-20, Binding of Isaac's beat): when the last of a room's own dead falls, a
    /// chime and a CLEARED stamp for whoever is near, and sometimes a little something left in the middle
    /// of the room (gold, a potion, embers). Counted by the room each enemy was raised in, so one chasing
    /// you down a corridor still counts for its room. Patrols, gargoyles and later summons are not counted.
    /// </summary>
    public partial class DungeonDirector
    {
        public const float RoomClearRewardChance = 0.45f;

        private readonly Dictionary<EnemyBrain, int> spawnRoom = new Dictionary<EnemyBrain, int>();
        private readonly HashSet<int> roomsCleared = new HashSet<int>();
        private float nextClearCheck;

        /// <summary>Server, for tests: rooms cleared this floor.</summary>
        public int RoomsClearedCount => roomsCleared.Count;

        /// <summary>Server, for tests: remove the dead raised in one room not yet cleared (no loot); its id, or -1.</summary>
        public int ServerDebugClearOneRoom()
        {
            int pick = -1;
            foreach (var kv in spawnRoom) if (!roomsCleared.Contains(kv.Value) && kv.Key != null && !kv.Key.IsDead) { pick = kv.Value; break; }
            if (pick < 0) return -1;
            foreach (var kv in spawnRoom)
                if (kv.Value == pick && kv.Key != null && kv.Key.IsSpawned) kv.Key.NetworkObject.Despawn();
            return pick;
        }

        public bool RoomIsCleared(int id) => roomsCleared.Contains(id);

        private void ResetRoomClears()
        {
            spawnRoom.Clear();
            roomsCleared.Clear();
        }

        private void NoteSpawnRoom(EnemyBrain brain, DungeonLayout.Room room)
        {
            if (brain != null && room != null && room.Kind == RoomKind.Combat) spawnRoom[brain] = room.Id;
        }

        private void TickRoomClears()
        {
            if (Time.time < nextClearCheck || spawnRoom.Count == 0 || Layout == null) return;
            nextClearCheck = Time.time + 0.5f;
            var alive = new HashSet<int>();
            var counted = new HashSet<int>();
            foreach (var kv in spawnRoom)
            {
                counted.Add(kv.Value);
                if (kv.Key != null && !kv.Key.IsDead && kv.Key.IsSpawned) alive.Add(kv.Value);
            }
            foreach (int id in counted)
            {
                if (alive.Contains(id) || roomsCleared.Contains(id)) continue;
                roomsCleared.Add(id);
                ServerRoomCleared(Layout.Rooms[id]);
            }
        }

        private void ServerRoomCleared(DungeonLayout.Room room)
        {
            var at = room.Center + Vector3.up * room.RaisedAt(room.Center);
            int reward = -1;
            if (Random.value < EventClearChance)
            {
                reward = Random.Range(0, 3);
                int tier = Mathf.RoundToInt(Tier(Floor.Value.y));
                if (reward == 0) for (int i = 0; i < 3; i++) ServerDropLoot(at + Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 0.7f, LootKind.Gold, 6 + 3 * tier);
                else if (reward == 1) ServerDropLoot(at, LootKind.PotionHP, 1);
                else ServerDropEmbers(at, 3 + tier);
            }
            RoomClearedRpc(at, reward >= 0);
            QuestOnRoomClear();
            // The Chorister's Bell: a room won is a breath taken.
            foreach (var p in Player.PlayerLife.All)
                if (p != null && !p.Downed.Value && p.TryGetComponent<Progression.PlayerProgress>(out var prog) && prog.HasRelic(Relic.ChoristersBell)
                    && Vector3.Distance(p.transform.position, at) < 40f)
                    p.Health.ServerHeal(p.Health.Max.Value * 0.12f * prog.RelicPower(Relic.ChoristersBell));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RoomClearedRpc(Vector3 at, bool reward)
        {
            var me = Player.NetworkPlayer.Local;
            if (me == null || Vector3.Distance(me.transform.position, at) > 34f) return;
            UI.GameHud.RoomCleared(reward);
            Art.Sfx.Play2D(Art.Sound.Cleared, 0.55f);
            if (reward) Art.Sfx.Play2D(Art.Sound.Coins, 0.4f);
            if (reward) Art.Vfx.Ring(at + Vector3.up * 0.05f, 1.6f, new Color(1f, 0.85f, 0.45f), 0.6f);
        }
    }
}
