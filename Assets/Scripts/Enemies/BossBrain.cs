using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// What every boss shares: the boss-bar identity, party scaling, guaranteed loot,
    /// immunity to knockback, summoning, and the telegraph and impact effects. Each floor's
    /// boss subclasses this with its own mechanics.
    ///
    /// Design rule for every boss: each mechanic has one visible tell and one answer, and
    /// the answers are different skills — so a fight tests the whole kit and the whole
    /// party, not just damage output.
    /// </summary>
    public abstract class BossBrain : EnemyBrain
    {
        public static BossBrain Active { get; private set; }

        [Header("Boss")]
        [SerializeField] protected NetworkObject minionPrefab;

        public abstract string DisplayName { get; }
        public Health Health => health;

        protected int scaledPlayers = 1, scaledDepth;
        protected float castUntil;

        public void SetMinionPrefab(NetworkObject prefab) => minionPrefab = prefab;

        protected override LootTable Loot => LootTable.SkeletonKing;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Active = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Active == this) Active = null;
            base.OnNetworkDespawn();
        }

        public void ServerScaleBoss(int players, int depth)
        {
            scaledPlayers = players;
            scaledDepth = depth;
            ServerScale(players, depth);
        }

        protected bool Casting => Time.time < castUntil;

        protected override bool IsBoss => true;
        protected override bool Heavy => true;

        protected override void Update()
        {
            if (IsServer && !IsDead && Casting)
            {
                agent.isStopped = true;
                return;
            }
            base.Update();
        }

        /// <summary>A cast-time summon of the minion prefab in a ring around the boss.</summary>
        protected void Summon(int count, float radius, float castTime = 1.6f)
        {
            castUntil = Time.time + castTime;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, castTime);
            SummonFxRpc();

            if (minionPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                var minion = Instantiate(minionPrefab, pos, Quaternion.LookRotation(transform.position - pos));
                minion.Spawn(destroyWithScene: true);
                var brain = minion.GetComponent<EnemyBrain>();
                brain.NoLoot = true;
                brain.ServerScale(scaledPlayers, scaledDepth);
            }
        }

        /// <summary>Server: shove a player. Movement is owner-authoritative, so this asks their client to do it.</summary>
        protected static void KnockPlayer(Health victim, Vector3 velocity)
        {
            if (victim.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(velocity);
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <param name="keyed">Keyed telegraphs can be cancelled (a slam interrupted by a bash).</param>
        [Rpc(SendTo.ClientsAndHost)]
        protected void CircleTelegraphRpc(Vector3 center, float radius, float duration, bool keyed)
        {
            Telegraph.Show(center, radius, duration, keyed ? this : null);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void LineTelegraphRpc(Vector3 start, Vector3 direction, float length, float width, float duration)
        {
            Telegraph.ShowLine(start, direction, length, width, duration, this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        protected void ImpactFxRpc(Vector3 at, float shakeRange)
        {
            Sfx.Play(Sound.Slam, at, 1f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < shakeRange) local.ViewModelBlockImpact();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SummonFxRpc() => Sfx.Play(Sound.Summon, transform.position + Vector3.up * 2f, 1f);
    }
}
