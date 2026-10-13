using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Skeleton King: first boss.
    ///
    /// <para>
    /// The fight is built on three readable mechanics, each answered by a different skill:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Royal Cleave</b> — slow, very wide melee swing. Answer: block it, or step out.</item>
    /// <item><b>Grave Slam</b> — a red circle fills on the floor around him; when it is full,
    ///   everything inside takes heavy damage and is thrown back. Answer: get out of the
    ///   circle, or put a pillar between you. Shield Bash during the wind-up cancels it.</item>
    /// <item><b>Raise the Dead</b> — at 66% and 33% health he calls skeletons out of the
    ///   floor. Answer: the tank War Cries to hold them while the party burns the King.</item>
    /// </list>
    /// Below 25% he enrages: every wind-up is 30% shorter.
    /// </summary>
    public class BossBrain : EnemyBrain
    {
        public static BossBrain Active { get; private set; }

        [Header("Boss")]
        [SerializeField] private float slamRadius = 6.5f;
        [SerializeField] private float slamWindup = 1.5f;
        [SerializeField] private float slamDamage = 48f;
        [SerializeField] private float slamCooldown = 9f;
        [SerializeField] private float cleaveWindup = 0.85f;
        [SerializeField] private NetworkObject minionPrefab;

        public string DisplayName => "Ossric, the Hollow King";
        public Health Health => health;

        private float nextSlam;
        private bool slamming;
        private float slamAt;
        private bool summoned66, summoned33;
        private float castUntil;
        private int scaledPlayers = 1, scaledDepth;

        public void SetMinionPrefab(NetworkObject prefab) => minionPrefab = prefab;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Active = this;
            Rig.StrideLength = 2.4f;
            nextSlam = Time.time + 6f;
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

        private bool Enraged => health.Fraction < 0.25f;
        private float WindupScale => Enraged ? 0.7f : 1f;

        protected override void Update()
        {
            if (IsServer && !IsDead && slamming)
            {
                agent.isStopped = true;
                if (Time.time >= slamAt) ResolveSlam();
                return;
            }

            if (IsServer && !IsDead && Time.time < castUntil)
            {
                agent.isStopped = true;
                return;
            }

            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (!summoned66 && health.Fraction < 0.66f) { summoned66 = true; Summon(); return true; }
            if (!summoned33 && health.Fraction < 0.33f) { summoned33 = true; Summon(); return true; }

            if (Time.time >= nextSlam && distanceToTarget < slamRadius + 2f)
            {
                BeginSlam();
                return true;
            }
            return false;
        }

        protected override void BeginAttack()
        {
            // Royal Cleave replaces the basic swing: wider, slower, harder.
            windup = cleaveWindup * WindupScale;
            attackArc = 170f;
            base.BeginAttack();
        }

        private void BeginSlam()
        {
            slamming = true;
            slamAt = Time.time + slamWindup * WindupScale;
            nextSlam = Time.time + slamCooldown;
            busyUntil = slamAt + 1.2f;
            PlayActionRpc(RigAction.Slam, slamWindup * WindupScale + 0.3f);
            TelegraphRpc(transform.position, slamRadius, slamWindup * WindupScale);
        }

        private void ResolveSlam()
        {
            slamming = false;
            foreach (var victim in CombatQuery.Circle(transform.position, slamRadius, Team.Players))
            {
                // Pillars are cover: a slam does not go through stone.
                if (!CombatQuery.ClearLine(transform.position + Vector3.up * 0.8f, victim)) continue;

                victim.ServerDamage(slamDamage, transform.position, NetworkObjectId);
                var push = victim.transform.position - transform.position;
                push.y = 0f;
                if (victim.TryGetComponent<NetworkPlayer>(out var p)) p.GetComponent<KnockbackReceiver>()?.KnockbackRpc(push.normalized * 14f + Vector3.up * 5f);
            }
            SlamFxRpc(transform.position);
        }

        public override void ServerStun(float seconds)
        {
            // Heavy: a bash only staggers him briefly — but it does cancel the slam.
            if (slamming)
            {
                slamming = false;
                CancelTelegraphRpc();
            }
            base.ServerStun(Mathf.Min(seconds, 0.5f));
        }

        public override void ServerKnockback(Vector3 velocity) { }

        private void Summon()
        {
            castUntil = Time.time + 1.6f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, 1.6f);
            SummonFxRpc();

            if (minionPrefab == null) return;
            int count = 2 + scaledPlayers;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f;
                var minion = Instantiate(minionPrefab, pos, Quaternion.LookRotation(transform.position - pos));
                minion.Spawn(destroyWithScene: true);
                minion.GetComponent<EnemyBrain>().ServerScale(scaledPlayers, scaledDepth);
            }
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        [Rpc(SendTo.ClientsAndHost)]
        private void TelegraphRpc(Vector3 center, float radius, float duration)
        {
            Telegraph.Show(center, radius, duration, this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void SlamFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 18f) local.ViewModelBlockImpact();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SummonFxRpc() => Sfx.Play(Sound.Summon, transform.position + Vector3.up * 2f, 1f);
    }
}
