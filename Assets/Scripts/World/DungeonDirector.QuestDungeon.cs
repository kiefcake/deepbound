using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// The dungeon half of the camp folk's quests (2026-11-08): when a floor starts, every live quest that needs
    /// something put on the floor gets it - CARGO far from the start (<see cref="QuestCargo"/>, carried to the waystone),
    /// GATHER finds in separate rooms (<see cref="QuestTrinket"/>), and MARKS: a named champion for the giver, with a
    /// grudge of its own (<see cref="MarkName"/>). Kills of marks, cargo delivered/ruined and finds report here.
    /// </summary>
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject questCargoPrefab, questTrinketPrefab;
        /// <summary>Server: living marks by NetworkObjectId -> giver.</summary>
        private readonly Dictionary<ulong, FolkId> marks = new Dictionary<ulong, FolkId>();

        public static string MarkName(FolkId who) => who switch
        {
            FolkId.Silas => "Gilt-Tooth Morrow",
            FolkId.Hobb => "The Ironeater",
            FolkId.Aldric => "The Unshriven",
            FolkId.Vey => "The Moth-Mother",
            FolkId.Corwin => "The Highwayman",
            FolkId.Brann => "Corporal Vane, Deserter",
            FolkId.Ysolde => "The Glyph-Eater",
            _ => "The Cellar King",
        };

        private static string MarkKind(FolkId who) => who switch
        {
            FolkId.Silas => "skeleton",
            FolkId.Hobb => "brute",
            FolkId.Aldric => "necromancer",
            FolkId.Vey => "wraith",
            FolkId.Corwin => "archer",
            FolkId.Brann => "shieldbearer",
            FolkId.Ysolde => "hexer",
            _ => "ghoul",
        };

        public static string CargoTask(FolkId who) => "Find " + QuestCargo.NameOf(who) + " and carry it to the start room's waystone";
        public static string GatherTask(FolkId who, int goal) => "Find " + goal + " " + (who == FolkId.Aldric ? "saint's knucklebones (gold glow, a whisper)" : who == FolkId.Brann ? "fallen standards of Brann's squad" : "humming rune shards") + " in different rooms";
        public static string MarkTask(FolkId who) => "Hunt " + MarkName(who) + " (a named " + MarkKind(who) + ") somewhere on the floor";

        /// <summary>Rooms worth hiding things in, farthest from the start first.</summary>
        private List<DungeonLayout.Room> FarRooms(DungeonLayout layout)
        {
            var start = layout.Rooms.Find(r => r.Kind == RoomKind.Start);
            var list = layout.Rooms.FindAll(r => (r.Kind == RoomKind.Side || r.Kind == RoomKind.Combat) && !IsAmbushRoom(r) && r.Feature != RoomFeature.Vault);
            var from = start != null ? start.Center : Vector3.zero;
            list.Sort((a, b) => (b.Center - from).sqrMagnitude.CompareTo((a.Center - from).sqrMagnitude));
            return list;
        }

        private static Vector3 OnNav(Vector3 p) => NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas) ? hit.position : p;

        /// <summary>Server, at the floor's start (from BeginContract): put each live quest's things on the floor.</summary>
        private void SpawnQuestObjects()
        {
            marks.Clear();
            var layout = Layout;
            if (layout == null) return;
            var rooms = FarRooms(layout);
            if (rooms.Count == 0) return;
            var rng = new System.Random(layout.Seed ^ 0x51ed);
            int far = 0;
            for (int i = 0; i < Quests.Count; i++)
            {
                var q = Quests[i];
                if (q.Status != ContractStatus.Active) continue;
                switch (q.Contract)
                {
                    case Contract.Cargo when questCargoPrefab != null:
                    {
                        // In one of the three farthest rooms, against a wall.
                        var room = rooms[Mathf.Min(far++ % 3, rooms.Count - 1)];
                        var at = OnNav(room.Kind == RoomKind.Side ? room.Center + new Vector3(0.6f, 0f, 0.6f) : WallSpot(room, rng));
                        var obj = Instantiate(questCargoPrefab, at, Quaternion.Euler(0f, rng.Next(360), 0f));
                        obj.GetComponent<QuestCargo>().Giver.Value = q.Giver;
                        obj.Spawn(destroyWithScene: true);
                        break;
                    }
                    case Contract.Gather when questTrinketPrefab != null:
                    {
                        // One per room, spread through the floor (not all in the far end).
                        var pool = new List<DungeonLayout.Room>(rooms);
                        for (int k = 0; k < q.Goal && pool.Count > 0; k++)
                        {
                            var room = pool[rng.Next(pool.Count)];
                            pool.Remove(room);
                            var at = OnNav(WallSpot(room, rng));
                            var obj = Instantiate(questTrinketPrefab, at, Quaternion.Euler(0f, rng.Next(360), 0f));
                            obj.GetComponent<QuestTrinket>().Giver.Value = q.Giver;
                            obj.Spawn(destroyWithScene: true);
                        }
                        // A floor with fewer rooms than finds asks for fewer.
                        if (q.Goal > rooms.Count) { q.Goal = rooms.Count; Quests[i] = q; }
                        break;
                    }
                    case Contract.Mark:
                    {
                        var combat = rooms.FindAll(r => r.Kind == RoomKind.Combat);
                        var room = combat.Count > 0 ? combat[Mathf.Min(far++ % 2, combat.Count - 1)] : rooms[0];
                        var brain = ServerDebugSpawn(MarkKind(q.Who), OnNav(room.Center), true);
                        if (brain == null) break;
                        brain.ServerMakeChampion();
                        brain.Title.Value = MarkName(q.Who);
                        var h = brain.GetComponent<Health>();
                        h.ServerSetMax(h.Max.Value * 1.3f, refill: true);
                        marks[brain.NetworkObjectId] = q.Who;
                        break;
                    }
                }
            }
            if (marks.Count > 0) MarkNoticeRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MarkNoticeRpc() => UI.GameHud.Feed("A mark walks this floor: hunt it for the quest that named it.", new Color(1f, 0.6f, 0.35f));

        /// <summary>Server: a death on the floor might be a mark.</summary>
        private void QuestMarkKilled(Health target)
        {
            if (target == null || !marks.TryGetValue(target.NetworkObjectId, out var who)) return;
            marks.Remove(target.NetworkObjectId);
            Advance(Contract.Mark, 1, who);
        }

        /// <summary>Server, tests: Hobb's cargo, Aldric's finds and Brann's mark go on the floor; each is then done.</summary>
        public System.Collections.IEnumerator ServerDebugQuestDungeon(System.Action<bool, string> done)
        {
            int baseCount = Quests.Count;
            Quests.Add(new QuestSlot { Kind = (byte)Contract.Cargo, Giver = (byte)FolkId.Hobb, State = (byte)ContractStatus.Active, Goal = 1 });
            Quests.Add(new QuestSlot { Kind = (byte)Contract.Gather, Giver = (byte)FolkId.Aldric, State = (byte)ContractStatus.Active, Goal = 3 });
            Quests.Add(new QuestSlot { Kind = (byte)Contract.Mark, Giver = (byte)FolkId.Brann, State = (byte)ContractStatus.Active, Goal = 1 });
            SpawnQuestObjects();
            yield return null;
            var cargo = System.Array.Find(FindObjectsByType<QuestCargo>(FindObjectsSortMode.None), c => c.Who == FolkId.Hobb);
            var finds = System.Array.FindAll(FindObjectsByType<QuestTrinket>(FindObjectsSortMode.None), t => t.Who == FolkId.Aldric);
            ulong markId = 0; foreach (var kv in marks) if (kv.Value == FolkId.Brann) markId = kv.Key;
            string why = "cargo " + (cargo != null) + ", finds " + finds.Length + ", mark " + (markId != 0);
            bool placed = cargo != null && finds.Length >= 1 && markId != 0;
            if (placed)
            {
                ServerCargoDelivered(FolkId.Hobb);
                var taker = System.Linq.Enumerable.FirstOrDefault(Player.PlayerLife.All, p => p != null)?.GetComponent<Progression.PlayerProgress>();
                foreach (var t in finds) if (taker != null) t.ServerTake(taker);
                yield return new WaitForSeconds(0.3f);
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(markId, out var markObj)) markObj.GetComponent<Health>().ServerDamage(1e6f, markObj.transform.position);
                yield return new WaitForSeconds(0.3f);
            }
            int ok = 0;
            for (int i = baseCount; i < Quests.Count; i++) if (Quests[i].Status == ContractStatus.Fulfilled) ok++;
            why += ", fulfilled " + ok + "/3";
            while (Quests.Count > baseCount) Quests.RemoveAt(Quests.Count - 1);
            if (cargo != null && cargo.IsSpawned) cargo.NetworkObject.Despawn();
            done(placed && ok == 3, why);
        }

        public void ServerCargoDelivered(FolkId who) => Advance(Contract.Cargo, 1, who);
        public void ServerTrinketFound(FolkId who) => Advance(Contract.Gather, 1, who);

        public void ServerCargoRuined(FolkId who)
        {
            for (int i = 0; i < Quests.Count; i++)
                if (Quests[i].Status == ContractStatus.Active && Quests[i].Contract == Contract.Cargo && Quests[i].Who == who) FailQuest(i);
        }

        /// <summary>Server: the strongbox burst open a little - a few coins spill (the party can pick them up).</summary>
        public void ServerCargoSpill(Vector3 at) => ServerDropLoot(at + Vector3.up * 0.4f, LootKind.Gold, Difficulty.Gold(8));
    }
}
