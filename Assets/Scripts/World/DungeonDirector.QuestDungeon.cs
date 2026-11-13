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

        public static string CargoTask(FolkId who) => "Find " + QuestCargo.NameOf(who) + " and carry it into the guardian's arena before the gates fall (it is safe there)";
        public static string GatherTask(FolkId who, int goal) => "Find " + goal + " " + (who == FolkId.Aldric ? "saint's knucklebones (gold glow, a whisper)" : who == FolkId.Brann ? "fallen standards of Brann's squad" : "humming rune shards") + " in different rooms and bring them out on the grave lift (go down and you drop them)";

        /// <summary>Server: finds held by each player (NetworkObjectId -> givers), counted when the lift rises.</summary>
        private readonly Dictionary<ulong, List<FolkId>> heldFinds = new Dictionary<ulong, List<FolkId>>();
        private float nextHeldCheck;

        /// <summary>Server: a find picked up - held by that player until the lift, dropped where they fall.</summary>
        public void ServerTrinketTaken(FolkId giver, Progression.PlayerProgress by)
        {
            if (by == null) return;
            if (!heldFinds.TryGetValue(by.NetworkObjectId, out var list)) heldFinds[by.NetworkObjectId] = list = new List<FolkId>();
            list.Add(giver);
            SyncHeld();
            HeldFindRpc(UI.Dialogue.NameOf(giver), RpcTarget.Single(by.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void HeldFindRpc(string giver, RpcParams p = default) =>
            UI.GameHud.Feed("You carry " + giver + "'s find - it counts once you ride the grave lift out. Go down and you drop it.", new Color(1f, 0.85f, 0.5f));

        private void SyncHeld()
        {
            for (int i = 0; i < Quests.Count; i++)
            {
                var q = Quests[i];
                if (q.Contract != Contract.Gather) continue;
                int n = 0;
                foreach (var kv in heldFinds) foreach (var g in kv.Value) if (g == q.Who) n++;
                if (q.Held != n) { q.Held = (byte)n; Quests[i] = q; }
            }
        }

        /// <summary>Server, from Update: a player who goes down (or leaves) drops what they hold where they are.</summary>
        private void TickHeldFinds()
        {
            if (heldFinds.Count == 0 || Time.time < nextHeldCheck) return;
            nextHeldCheck = Time.time + 0.25f;
            List<ulong> drop = null;
            foreach (var kv in heldFinds)
            {
                if (kv.Value.Count == 0) continue;
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(kv.Key, out var obj);
                var life = obj != null ? obj.GetComponent<Player.PlayerLife>() : null;
                if (life == null || life.Downed.Value) (drop ??= new List<ulong>()).Add(kv.Key);
            }
            if (drop == null) return;
            foreach (var id in drop)
            {
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj);
                var at = obj != null ? obj.transform.position : (Layout != null ? Layout.WaystonePosition : Vector3.zero);
                int k = 0;
                foreach (var giver in heldFinds[id])
                {
                    if (questTrinketPrefab == null) break;
                    var spot = OnNav(at + Quaternion.Euler(0f, k++ * 120f, 0f) * Vector3.forward * 0.7f);
                    var t = Instantiate(questTrinketPrefab, spot, Quaternion.identity);
                    t.GetComponent<QuestTrinket>().Giver.Value = (byte)giver;
                    t.Spawn(destroyWithScene: true);
                }
                if (heldFinds[id].Count > 0) FindsDroppedRpc(obj != null ? obj.name : "Someone", heldFinds[id].Count);
                heldFinds.Remove(id);
            }
            SyncHeld();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FindsDroppedRpc(string who, int n) => UI.GameHud.Feed(who + " went down and dropped " + n + " quest find" + (n > 1 ? "s" : "") + " - pick " + (n > 1 ? "them" : "it") + " up again.", new Color(1f, 0.6f, 0.4f));

        /// <summary>
        /// Server, when the arena seals: cargo inside the arena is DELIVERED there and then - set safe by the gate - and cargo
        /// outside is left behind (2026-11-13, the user: carrying it through the boss unharmed was "basically impossible on a
        /// single player run").
        /// </summary>
        private void QuestCargoAtSeal()
        {
            var room = Layout != null ? Layout.BossRoom : null;
            if (room == null) return;
            foreach (var c in FindObjectsByType<QuestCargo>(FindObjectsSortMode.None))
            {
                if (c == null || !c.IsSpawned) continue;
                if (room.Contains(c.transform.position, 0.5f))
                {
                    CargoSafeRpc(QuestCargo.NameOf(c.Who), c.transform.position);
                    Advance(Contract.Cargo, 1, c.Who);
                    c.NetworkObject.Despawn();
                    continue;
                }
                LeftBehindRpc(QuestCargo.NameOf(c.Who));
                ServerCargoRuined(c.Who);
                c.NetworkObject.Despawn();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CargoSafeRpc(string what, Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Relic, at + Vector3.up, 0.7f, 1.1f);
            Art.Vfx.Ring(at + Vector3.up * 0.05f, 1.2f, new Color(1f, 0.85f, 0.45f), 0.5f);
            UI.GameHud.Feed("The gates fall - " + what + " is safe inside. Delivered.", new Color(0.6f, 1f, 0.6f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeftBehindRpc(string what) => UI.GameHud.Feed("The gates fall shut - " + what + " is left behind in the dark.", new Color(0.9f, 0.45f, 0.35f));

        /// <summary>Server, as the grave lift rises: cargo carried out (or set in the cage) and finds held count; the rest fails.</summary>
        public void QuestSettleOnAscent()
        {
            var cargo = FindObjectsByType<QuestCargo>(FindObjectsSortMode.None);
            for (int i = 0; i < Quests.Count; i++)
            {
                var q = Quests[i];
                if (q.Status != ContractStatus.Active) continue;
                if (q.Contract == Contract.Cargo)
                {
                    var c = System.Array.Find(cargo, x => x != null && x.IsSpawned && x.Who == q.Who);
                    var flat = c != null ? c.transform.position - ExitPosition : Vector3.one * 99f; flat.y = 0f;
                    bool out_ = c != null && (c.Carried || flat.magnitude <= ExitRadius + 1f);
                    if (out_) Advance(Contract.Cargo, 1, q.Who);
                    else FailQuest(i);
                }
                else if (q.Contract == Contract.Gather)
                {
                    int held = 0;
                    foreach (var kv in heldFinds) foreach (var g in kv.Value) if (g == q.Who) held++;
                    if (held > 0) Advance(Contract.Gather, held, q.Who);
                    q = Quests[i];
                    if (q.Status == ContractStatus.Active) FailQuest(i);
                }
            }
            heldFinds.Clear();
            SyncHeld();
            foreach (var c in cargo) if (c != null && c.IsSpawned) c.NetworkObject.Despawn();
        }
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
            heldFinds.Clear();
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
            int ok = 0;
            if (placed)
            {
                // The host carries (the first in the list can be a client out fighting, who may drop it).
                var taker = (Player.NetworkPlayer.Local != null ? Player.NetworkPlayer.Local.GetComponent<Progression.PlayerProgress>() : null)
                    ?? System.Linq.Enumerable.FirstOrDefault(Player.PlayerLife.All, p => p != null)?.GetComponent<Progression.PlayerProgress>();
                cargo.ServerDebugCarry(taker);
                foreach (var t in finds) if (taker != null) t.ServerTake(taker);
                yield return new WaitForSeconds(0.3f);
                // Held, not yet counted: no waystone delivery, and the finds wait for the lift.
                bool waiting = Quests[baseCount].Status == ContractStatus.Active && Quests[baseCount + 1].Progress == 0 && Quests[baseCount + 1].Held == finds.Length;
                why += ", held " + Quests[baseCount + 1].Held + " waiting " + waiting;
                // A champion can get up once (the Unburied): strike until the mark is really gone (2026-11-13, a flaky run).
                for (int strike = 0; strike < 4 && marks.ContainsKey(markId); strike++)
                {
                    if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(markId, out var markObj) && markObj.GetComponent<Health>().IsAlive)
                        markObj.GetComponent<Health>().ServerDamage(1e6f, markObj.transform.position);
                    yield return new WaitForSeconds(0.8f);
                }
                QuestSettleOnAscent();
                yield return null;
                for (int i = baseCount; i < Quests.Count; i++) if (Quests[i].Status == ContractStatus.Fulfilled) ok++;
                why += " (cargo " + Quests[baseCount].Status + ", finds " + Quests[baseCount + 1].Status + " " + Quests[baseCount + 1].Progress + "/" + Quests[baseCount + 1].Goal + ", mark " + Quests[baseCount + 2].Status + ")";
                if (!waiting) ok = -1;
                // Cargo outside the arena when it seals is left behind.
                Quests.Add(new QuestSlot { Kind = (byte)Contract.Cargo, Giver = (byte)FolkId.Marta, State = (byte)ContractStatus.Active, Goal = 1 });
                SpawnQuestObjects();
                yield return null;
                QuestCargoAtSeal();
                yield return null;
                bool leftBehind = Quests[Quests.Count - 1].Status == ContractStatus.Failed;
                why += ", left behind at the seal " + leftBehind;
                if (!leftBehind) ok = -1;
            }
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
