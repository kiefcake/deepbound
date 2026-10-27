using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Boss practice (2026-10-27): from Corwin's carriage, before a run, the host can take the party straight to the
    /// arena of any guardian already beaten on this machine (both of an act's guardians unlock with its master).
    /// The floor is emptied of everything but its guardian; the party arrives at the arena's edge; blows land at
    /// 60% and the guardian has 60% of its health. Nothing drops, nothing is recorded, and win or wipe the party
    /// walks back into camp exactly as it left it.
    /// </summary>
    public partial class DungeonDirector
    {
        public readonly NetworkVariable<bool> Practice = new NetworkVariable<bool>();
        public const float PracticeTaken = 0.6f, PracticeHealth = 0.6f, PracticeEndDelay = 3.5f;
        private double practiceEndsAt = -1;

        /// <summary>Is a practice fight going on (any peer)?</summary>
        public static bool InPractice => Instance != null && Instance.Practice.Value && !InLobby;

        /// <summary>Can this machine's host offer practice against the guardian of <paramref name="depth"/>?</summary>
        public static bool PracticeUnlocked(int depth) => (Progression.Cosmetics.ActsBeaten & (1 << Mathf.Clamp(depth / 2, 0, 30))) != 0;

        /// <summary>Host to server: go and fight the guardian of floor <paramref name="depth"/> (camp, between runs only).</summary>
        [Rpc(SendTo.Server)]
        public void PracticeRpc(byte depth) => ServerStartPractice(depth);

        public void ServerStartPractice(int depth)
        {
            if (!IsServer || Phase.Value != RunPhase.Lobby || RunInProgress || depth < 0 || depth > FinalDepth) return;
            Practice.Value = true;
            practiceEndsAt = -1;
            StartFloor(depth);
            // Only the guardian (and whatever it calls) stays.
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                var o = spawned[i];
                if (o == null || !o.TryGetComponent<EnemyBrain>(out var e) || e is BossBrain) continue;
                if (o.IsSpawned) o.Despawn();
                spawned.RemoveAt(i);
            }
            ClearFloorEvent();
            Combat.Health.FloorPlayerDamageScale = PracticeTaken;
            TallyArriveAfter.Value = 1e6f;
            if (boss != null)
            {
                boss.NoLoot = true;
                var bh = boss.GetComponent<Combat.Health>();
                bh.ServerSetMax(bh.Max.Value * PracticeHealth, refill: true);
            }
            PracticeCardRpc(boss != null ? boss.DisplayName : "the guardian");
            Debug.Log("[Director] Practice against floor " + (depth + 1));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PracticeCardRpc(string who) =>
            UI.GameHud.ShowTitleCard("PRACTICE: " + who.ToUpperInvariant(), "An echo of the fight. Nothing is lost, nothing is won", new Color(0.7f, 0.8f, 0.95f));

        /// <summary>Server, each frame while exploring: a practice ends when the guardian falls or the party does.</summary>
        private bool TickPractice(double now)
        {
            if (!Practice.Value) return false;
            if (practiceEndsAt < 0)
            {
                bool won = bossSeen && (boss == null || boss.IsDead);
                if (won || PartyWiped())
                {
                    practiceEndsAt = now + PracticeEndDelay;
                    PracticeOverRpc(won);
                }
                return true;
            }
            if (now >= practiceEndsAt)
            {
                practiceEndsAt = -1;
                // keepRun: the party walks back into camp exactly as it left it.
                EnterLobby(keepRun: true);
            }
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PracticeOverRpc(bool won) =>
            UI.GameHud.ShowTitleCard(won ? "THE ECHO FALLS" : "THE ECHO FADES", "Back to camp", new Color(0.7f, 0.8f, 0.95f));

        private static Vector3 PracticeAway()
        {
            var away = Layout.BossRoom.Center - Layout.BossSpawn;
            away.y = 0f;
            return away.sqrMagnitude < 0.01f ? Vector3.back : away.normalized;
        }

        /// <summary>Spawns face away from this point, so: behind the party, and they face the guardian.</summary>
        private static Vector3 PracticeLookTarget => Layout.BossRoom.Center + PracticeAway() * 16f;

        /// <summary>Where the party arrives for a practice: the arena's edge, facing in.</summary>
        private static Vector3 PracticeSpawn(ulong clientId)
        {
            var room = Layout.BossRoom;
            var away = PracticeAway();
            var side = Vector3.Cross(Vector3.up, away);
            var at = room.Center + away * 8f + side * (((int)(clientId % 4)) - 1.5f) * 1.6f;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            return at + Vector3.up * 0.1f;
        }
    }
}
