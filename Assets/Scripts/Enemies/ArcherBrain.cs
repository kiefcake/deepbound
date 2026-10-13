using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Skeleton archer: the first ranged enemy, and the reason a room is not just a melee
    /// scrum. It hangs back, keeps line of sight, and backs off if you close in — so the
    /// party has to decide who dives the back line while someone holds the front.
    ///
    /// The draw is a long, visible telegraph (bow raised, arm pulled back, eyes flaring);
    /// the arrow is aimed where you were, not where you are going, so moving dodges it.
    /// </summary>
    public class ArcherBrain : EnemyBrain
    {
        [Header("Archer")]
        [SerializeField] private float preferredRange = 10f;
        [SerializeField] private float panicRange = 5f;

        private float nextRetreat;

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            // Too close: back away rather than shoot point-blank.
            if (distanceToTarget < panicRange && Time.time >= nextRetreat)
            {
                nextRetreat = Time.time + 0.4f;
                var away = transform.position - target.transform.position;
                away.y = 0f;
                agent.isStopped = false;
                agent.SetDestination(transform.position + away.normalized * (preferredRange - distanceToTarget));
                return true;
            }

            // No line of sight: walk closer instead of firing into a wall.
            if (distanceToTarget <= attackRange && !CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, target))
            {
                agent.isStopped = false;
                agent.stoppingDistance = 1f;
                if (Time.time >= repathAt)
                {
                    repathAt = Time.time + 0.3f;
                    agent.SetDestination(target.transform.position);
                }
                return true;
            }

            agent.stoppingDistance = preferredRange;
            return false;
        }

        protected override void BeginAttack()
        {
            base.BeginAttack();
            // Replace the melee wind-up animation with the bow draw.
            PlayActionRpc(RigAction.Draw, windup);
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.45f + transform.forward * 0.4f;
            var dir = target.Center - origin;
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId);
            FireRpc(origin, dir);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FireRpc(Vector3 origin, Vector3 direction)
        {
            Arrows.SpawnVisual(origin, direction);
            Sfx.Play(Sound.Bow, origin, 0.8f);
            Rig?.Play(RigAction.None, 0.1f);
        }
    }
}
