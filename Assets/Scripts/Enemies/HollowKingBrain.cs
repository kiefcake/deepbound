using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Ossric, the Hollow King: the final boss, on floor 3.
    ///
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
    public class HollowKingBrain : BossBrain
    {
        [SerializeField] private float slamRadius = 6.5f;
        [SerializeField] private float slamWindup = 1.5f;
        [SerializeField] private float slamDamage = 48f;
        [SerializeField] private float slamCooldown = 9f;
        [SerializeField] private float cleaveWindup = 0.85f;

        public override string DisplayName => "Ossric, the Hollow King";

        private float nextSlam;
        private bool slamming;
        private float slamAt;
        private bool summoned66, summoned33;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Rig.StrideLength = 2.4f;
            nextSlam = Time.time + 6f;
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
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (!summoned66 && health.Fraction < 0.66f) { summoned66 = true; Summon(2 + scaledPlayers, 4f); return true; }
            if (!summoned33 && health.Fraction < 0.33f) { summoned33 = true; Summon(2 + scaledPlayers, 4f); return true; }

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
            CircleTelegraphRpc(transform.position, slamRadius, slamWindup * WindupScale, true);
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
                KnockPlayer(victim, push.normalized * 14f + Vector3.up * 5f);
            }
            ImpactFxRpc(transform.position, 18f);
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
    }
}
