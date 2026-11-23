using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// THE PROVING PATH (2026-11-23, the user: "make a tutorial level, going over game mechanics"): a fixed floor
    /// (<see cref="DungeonLayout.Tutorial"/>) reached from the title screen. Each room holds one lesson; its north gate
    /// rises once the lesson is met, and the next lesson begins when someone walks into the next room. The lesson line
    /// is drawn by <see cref="UI.TutorialPanel"/> from <see cref="TutStep"/> and <see cref="TutDone"/>.
    /// Blows land at 60%, a wiped party gets up at the room's entrance, nothing is recorded, and the party walks into
    /// camp at the end exactly as it left it.
    /// </summary>
    public partial class DungeonDirector
    {
        public enum Lesson : byte { Move, Attack, Dodge, Abilities, Telegraphs, Shields, Potions, Loot, Ultimate, Guardian }

        /// <summary>The title screen asked for the Proving Path: the host starts it once the class is chosen.</summary>
        public static bool TutorialPending;
        public const float TutorialTaken = 0.6f, TutorialBossHealth = 0.5f, TutorialEndDelay = 4f;
        public const int TutorialDummyHits = 8, TutorialDodges = 2;

        /// <summary>The lesson under way (a <see cref="Lesson"/>), and whether it has been met.</summary>
        public readonly NetworkVariable<byte> TutStep = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> TutDone = new NetworkVariable<bool>();

        /// <summary>Any peer: the party is on the Proving Path.</summary>
        public static bool InTutorial => Instance != null && !InLobby && Instance.Floor.Value.x == DungeonLayout.TutorialSeed;

        private readonly List<Portcullis> tutGates = new List<Portcullis>();
        private readonly List<EnemyBrain> tutFoes = new List<EnemyBrain>();
        private Chest tutChest;
        private int tutHits, tutDodges;
        private float tutMoved, tutSprint;
        private bool tutJumped, tutSprinted;
        private double tutEndAt = -1;
        private float tutLobbyAt = -1f;
        private bool tutPickerSeen;
        private readonly Dictionary<PlayerLife, Vector3> tutLast = new Dictionary<PlayerLife, Vector3>();

        /// <summary>Server, for tests: lesson counters.</summary>
        public int TutorialHits => tutHits;
        public int TutorialDodgeCount => tutDodges;
        public Chest TutorialChest => tutChest;
        public int TutorialFoesAlive { get { int n = 0; foreach (var f in tutFoes) if (f != null && !f.IsDead && f.IsSpawned) n++; return n; } }

        /// <summary>Server, from the lobby tick: the title screen asked for the Proving Path. Waits for the class picker to close.</summary>
        private void TickTutorialPending()
        {
            if (!TutorialPending || RunInProgress || departAt > 0f) return;
            if (tutLobbyAt < 0f) tutLobbyAt = Time.time;
            if (UI.CharacterSelect.Open) { tutPickerSeen = true; return; }
            if (NetworkPlayer.Local == null) return;
            if (!tutPickerSeen && Time.time - tutLobbyAt < 6f) return;
            ServerStartTutorial();
        }

        public void ServerStartTutorial()
        {
            if (!IsServer || Phase.Value != RunPhase.Lobby || RunInProgress) return;
            TutorialPending = false;
            tutLobbyAt = -1f;
            tutPickerSeen = false;
            StartFloor(0, tutorial: true);

            // Only the guardian stays: every lesson brings its own.
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                var o = spawned[i];
                if (o == null) { spawned.RemoveAt(i); continue; }
                bool foe = o.TryGetComponent<EnemyBrain>(out var e) && !(e is BossBrain);
                if (!foe && !o.TryGetComponent<Interactable>(out _) && !o.TryGetComponent<SpikeTrap>(out _)) continue;
                if (o.IsSpawned) o.Despawn();
                spawned.RemoveAt(i);
            }
            foreach (var t in FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None)) if (t.NetworkObject.IsSpawned) t.NetworkObject.Despawn();
            foreach (var t in FindObjectsByType<Interactable>(FindObjectsSortMode.None)) if (t.NetworkObject.IsSpawned) t.NetworkObject.Despawn();
            foreach (var e in new List<EnemyBrain>(EnemyBrain.All)) if (e != null && !(e is BossBrain) && e.IsSpawned) e.NetworkObject.Despawn();

            ClearFloorEvent();
            Health.FloorPlayerDamageScale = TutorialTaken;
            TallyArriveAfter.Value = 1e6f;
            if (boss != null)
            {
                boss.NoLoot = true;
                boss.Health.ServerSetMax(boss.Health.Max.Value * TutorialBossHealth, refill: true);
            }

            // A closed gate on the north door of every room before the arena.
            tutGates.Clear();
            for (int i = 0; i < DungeonLayout.TutorialRooms - 1 && portcullisPrefab != null; i++)
            {
                var room = Layout.Rooms[i];
                var g = Spawn(portcullisPrefab, room.Center + Vector3.forward * (room.Size.y / 2f + 0.4f), Quaternion.identity);
                g.GetComponent<Portcullis>().Closed.Value = true;
                spawned.Add(g);
                tutGates.Add(g.GetComponent<Portcullis>());
            }
            tutFoes.Clear();
            tutChest = null;
            tutHits = tutDodges = 0;
            tutMoved = tutSprint = 0f;
            tutJumped = tutSprinted = false;
            tutEndAt = -1;
            tutLast.Clear();
            TutStep.Value = (byte)Lesson.Move;
            TutDone.Value = false;
            TutorialCardRpc();
            Debug.Log("[Director] The Proving Path");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TutorialCardRpc() =>
            UI.GameHud.ShowTitleCard("THE PROVING PATH", "Learn the descent before you make it", new Color(0.85f, 0.78f, 0.6f));

        /// <summary>Host, from the pause menu: back to camp.</summary>
        public void ServerLeaveTutorial()
        {
            if (!IsServer || !InTutorial) return;
            EndTutorial();
        }

        private void EndTutorial()
        {
            tutEndAt = -1;
            Health.FloorPlayerDamageScale = 1f;
            EnterLobby(keepRun: true);
        }

        /// <summary>Tests: from wherever the run is, to camp and onto the Proving Path.</summary>
        public void ServerDebugTutorial()
        {
            if (!IsServer) return;
            NextDepth.Value = 0;
            EnterLobby();
            ServerStartTutorial();
        }

        public IReadOnlyList<Portcullis> TutorialGates => tutGates;

        /// <summary>Server, a player dodged (counted for the dodge lesson).</summary>
        public void TutorialOnDodge() { if (InTutorial && TutStep.Value == (byte)Lesson.Dodge && !TutDone.Value) tutDodges++; }

        /// <summary>Server, each frame while exploring: true while the Proving Path runs (the normal floor rules are skipped).</summary>
        private bool TickTutorial(double now)
        {
            if (!InTutorial) return false;
            if (tutEndAt >= 0)
            {
                if (now >= tutEndAt) EndTutorial();
                return true;
            }

            var lesson = (Lesson)TutStep.Value;
            // A wiped party gets up at the entrance of the room it fell in.
            if (PartyWiped())
            {
                var room = Layout.Rooms[Mathf.Min((int)lesson, Layout.Rooms.Count - 1)];
                var at = room.Center + Vector3.back * (room.Size.y / 2f - 2f);
                foreach (var p in PlayerLife.All)
                {
                    if (p == null) continue;
                    p.ServerRevive(1f);
                    if (p.TryGetComponent<NetworkPlayer>(out var np)) np.ShoveRpc(at + Vector3.up * 0.1f);
                }
                ServerFeedAll("Back on your feet. Try again.", new Color(0.85f, 0.78f, 0.6f));
                return true;
            }

            if (!TutDone.Value)
            {
                if (LessonMet(lesson))
                {
                    TutDone.Value = true;
                    if (lesson == Lesson.Guardian)
                    {
                        tutEndAt = now + TutorialEndDelay;
                        TutorialOverRpc();
                        foreach (var p in PlayerLife.All) if (p != null && p.Downed.Value) p.ServerRevive(0.5f);
                        ServerEndTheSummons();
                    }
                    else
                    {
                        if ((int)lesson < tutGates.Count && tutGates[(int)lesson] != null) tutGates[(int)lesson].Closed.Value = false;
                        LessonMetRpc();
                    }
                }
                return true;
            }

            // Met: the next lesson begins when someone walks into the next room.
            int next = (int)lesson + 1;
            if (next >= Layout.Rooms.Count) return true;
            var nextRoom = Layout.Rooms[next];
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value || !nextRoom.Contains(p.transform.position, -1.5f)) continue;
                BeginLesson((Lesson)next);
                break;
            }
            return true;
        }

        private void BeginLesson(Lesson lesson)
        {
            TutStep.Value = (byte)lesson;
            TutDone.Value = false;
            tutFoes.Clear();
            var room = Layout.Rooms[(int)lesson];
            var c = room.Center;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            switch (lesson)
            {
                case Lesson.Attack:
                    tutHits = 0;
                    if (dummyPrefab == null) break;
                    for (int i = 0; i < 3; i++)
                    {
                        var d = Spawn(dummyPrefab, c + new Vector3((i - 1) * 3.5f, 0f, 3f), Quaternion.Euler(0f, 180f, 0f));
                        spawned.Add(d);
                        d.GetComponent<Health>().DamagedServer += (_, __) => tutHits++;
                    }
                    break;
                case Lesson.Dodge:
                    tutDodges = 0;
                    Foe(skeletonPrefab, c + Vector3.forward * 4f, players);
                    break;
                case Lesson.Abilities:
                    for (int i = 0; i < 3; i++) Foe(skeletonPrefab, c + new Vector3((i - 1) * 2.5f, 0f, 4f), players);
                    Foe(archerPrefab, c + Vector3.forward * 7f, players);
                    break;
                case Lesson.Telegraphs:
                    Foe(brutePrefab, c + Vector3.forward * 5f, players);
                    break;
                case Lesson.Shields:
                    Foe(shieldbearerPrefab, c + Vector3.forward * 4f, players);
                    Foe(skeletonPrefab, c + new Vector3(3f, 0f, 5f), players);
                    break;
                case Lesson.Potions:
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<Flasks>(out var pouch)) pouch.ServerAdd(Flasks.Kind.Health, 2);
                    Foe(plaguebearerPrefab != null ? plaguebearerPrefab : ghoulPrefab, c + Vector3.forward * 4f, players);
                    break;
                case Lesson.Loot:
                    if (chestPrefab == null) break;
                    var chest = Spawn(chestPrefab, c + Vector3.forward * 3f, Quaternion.Euler(0f, 180f, 0f));
                    tutChest = chest.GetComponent<Chest>();
                    tutChest.SetLootPrefab(lootPrefab);
                    spawned.Add(chest);
                    break;
                case Lesson.Ultimate:
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<Ultimates>(out var ult)) ult.Charge.Value = Ultimates.ChargeMax;
                    for (int i = 0; i < 5; i++) Foe(skeletonPrefab, c + Vector3.forward * 5f + Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward * 1.8f, players);
                    break;
            }
            LessonBeganRpc();
        }

        private void Foe(NetworkObject prefab, Vector3 at, int players)
        {
            if (prefab == null) return;
            if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
            var obj = Spawn(prefab, at, Quaternion.Euler(0f, 180f, 0f));
            var brain = obj.GetComponent<EnemyBrain>();
            brain.ServerScale(players, 0);
            brain.NoLoot = true;
            if (brain is NecromancerBrain necro) necro.SetMinionPrefab(skeletonPrefab);
            spawned.Add(obj);
            tutFoes.Add(brain);
        }

        private bool LessonMet(Lesson lesson)
        {
            switch (lesson)
            {
                case Lesson.Move: return WatchMovement();
                case Lesson.Attack: return tutHits >= TutorialDummyHits;
                case Lesson.Dodge: return tutDodges >= TutorialDodges && TutorialFoesAlive == 0;
                case Lesson.Loot: return tutChest != null && tutChest.Opened.Value;
                case Lesson.Guardian: return bossSeen && (boss == null || boss.IsDead);
                default: return TutorialFoesAlive == 0;
            }
        }

        /// <summary>Server: has anyone walked, sprinted and jumped yet?</summary>
        private bool WatchMovement()
        {
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var at = p.transform.position;
                if (tutLast.TryGetValue(p, out var was))
                {
                    var step = at - was; step.y = 0f;
                    float d = step.magnitude;
                    if (d < 2f) tutMoved += d;
                    float speed = Time.deltaTime > 0f ? d / Time.deltaTime : 0f;
                    tutSprint = speed > 5.8f && speed < 11f ? tutSprint + Time.deltaTime : Mathf.Max(0f, tutSprint - Time.deltaTime * 0.5f);
                    if (tutSprint > 0.6f) tutSprinted = true;
                }
                if (at.y > Layout.StartRoom.Center.y + 0.45f) tutJumped = true;
                tutLast[p] = at;
            }
            return tutMoved > 6f && tutSprinted && tutJumped;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LessonMetRpc()
        {
            UI.TutorialPanel.Met();
            Art.Sfx.Play2D(Art.Sound.Cleared, 0.55f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LessonBeganRpc() => UI.TutorialPanel.Began();

        [Rpc(SendTo.ClientsAndHost)]
        private void TutorialOverRpc() =>
            UI.GameHud.ShowTitleCard("THE PATH IS PROVEN", "Back to camp", new Color(0.85f, 0.78f, 0.6f));

        /// <summary>Tests: meet the lesson under way at once (gates rise as in play).</summary>
        public void ServerDebugMeetLesson()
        {
            switch ((Lesson)TutStep.Value)
            {
                case Lesson.Move: tutMoved = 10f; tutSprinted = tutJumped = true; break;
                case Lesson.Attack: tutHits = TutorialDummyHits; break;
                case Lesson.Dodge: tutDodges = TutorialDodges; goto default;
                case Lesson.Loot: if (tutChest != null) tutChest.Opened.Value = true; break;
                case Lesson.Guardian: if (boss != null) boss.Health.ServerDamage(1e6f, boss.transform.position, ulong.MaxValue); break;
                default: foreach (var f in tutFoes) if (f != null && f.IsSpawned) f.GetComponent<Health>().ServerDamage(f.GetComponent<Health>().Max.Value * 10f, f.transform.position, ulong.MaxValue); break;
            }
        }
    }
}
