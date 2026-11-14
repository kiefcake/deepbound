using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Bone Hoarder: a gilded skeleton with a sack of stolen gold, met once in a while.
    /// It never fights. It runs — and every hit knocks coins out of the sack. Corner it and
    /// kill it for the lot; let it get away and, half a minute after you first spot it, it
    /// sinks into the floor with everything it carries. A chase, not a fight.
    /// </summary>
    public class HoarderBrain : EnemyBrain
    {
        public const float EscapeSeconds = 25f;

        protected override LootTable Loot => LootTable.Chest;

        private float escapeAt = -1f, nextSpill, lastSackHit = -1f;

        /// <summary>Server: the sack was struck (called just before the damage lands).</summary>
        public void ServerSackHit() => lastSackHit = Time.time;
        private bool escaped;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) Health.ServerAnyDamaged += OnAnyDamaged;
        }

        public override void OnNetworkDespawn()
        {
            Health.ServerAnyDamaged -= OnAnyDamaged;
            base.OnNetworkDespawn();
        }

        /// <summary>Server, tests: force the Gilded Skull roll.</summary>
        public static bool DebugAlwaysSkull;

        protected override void OnDied(Health h)
        {
            bool first = !IsDead;
            base.OnDied(h);
            // The Gilded Skull lives in its sack and nowhere else (2026-11-13): a kill shakes it loose some of the time.
            if (IsServer && first && !escaped && (DebugAlwaysSkull || Random.value < Relics.HoarderSkullChance))
                World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.6f, LootKind.Relic, (int)Relic.GildedSkull, settle: false);
        }

        private void OnAnyDamaged(Health hit, float dealt, ulong from)
        {
            if (hit != health || !health.IsAlive || Time.time < nextSpill) return;
            nextSpill = Time.time + 0.35f;
            // A hit on the sack (its weak point) tears it: a bigger spill.
            bool sack = Time.time - lastSackHit < 0.1f;
            World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.6f - transform.forward * 0.4f, LootKind.Gold,
                Mathf.RoundToInt(Random.Range(6, 12) * (sack ? 3 : 1) * (1f + 0.5f * depthForLoot)));
            SpillFxRpc();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            // Spotted: the clock starts.
            if (escapeAt < 0f)
            {
                escapeAt = Time.time + EscapeSeconds;
                SpottedRpc();
            }
            if (Time.time >= escapeAt && !escaped)
            {
                escaped = true;
                NoLoot = true;
                EscapeRpc(transform.position);
                Invoke(nameof(Vanish), 1.2f);
                agent.isStopped = true;
                busyUntil = Time.time + 10f;
                return true;
            }

            // Run: away from whoever is nearest, sliding sideways off walls.
            agent.isStopped = false;
            var away = transform.position - target.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away.Normalize();
            Vector3 best = transform.position;
            float bestScore = float.MinValue;
            for (int i = -3; i <= 3; i++)
            {
                var dir = Quaternion.Euler(0f, i * 35f, 0f) * away;
                var p = transform.position + dir * 6f;
                if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
                float score = Vector3.Distance(hit.position, target.transform.position) - Mathf.Abs(i) * 0.4f;
                if (score > bestScore) { bestScore = score; best = hit.position; }
            }
            if (Time.time >= nextRepath)
            {
                nextRepath = Time.time + 0.3f;
                agent.SetDestination(best);
            }
            return true;
        }

        private float nextRepath;

        private void Vanish()
        {
            if (IsSpawned) NetworkObject.Despawn();
        }

        protected override void Strike() { }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpottedRpc()
        {
            UI.GameHud.Feed("Something heavy jingles off into the dark.", new Color(1f, 0.82f, 0.35f));
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpillFxRpc()
        {
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 0.6f, 1.4f);
            Vfx.Embers(transform.position + Vector3.up * 0.8f, 6, new Color(1f, 0.8f, 0.3f), 3f, 0.05f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EscapeRpc(Vector3 at)
        {
            UI.GameHud.Feed("The Hoarder got away...", new Color(0.75f, 0.7f, 0.6f));
            Sfx.Play(Sound.Whisper, at + Vector3.up, 1f, 0.7f);
            Telegraph.Show(at, 1.2f, 1.2f, null, new Color(0.15f, 0.1f, 0.05f, 0.6f));
            Vfx.Smoke(at, 4, 0.8f, 1.6f);
        }
    }
}
