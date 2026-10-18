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

        // After each shot the archer sidesteps to a new spot, slowly, so it is never a
        // stationary turret: you have to track it, and its next arrow comes from a new angle.
        private float repositionUntil;
        private Vector3 repositionTo;

        protected override float SpeedScale => Time.time < repositionUntil ? 0.5f : 1f;

        private void PlanReposition()
        {
            if (target == null) return;
            var toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            var side = Vector3.Cross(Vector3.up, toTarget.normalized) * (Random.value < 0.5f ? -1f : 1f);
            // Sideways, and a little toward or away to settle back near the preferred range.
            var step = side * Random.Range(2.5f, 4.5f) + toTarget.normalized * Mathf.Clamp(dist - preferredRange, -2f, 2f);
            var want = transform.position + step;
            if (UnityEngine.AI.NavMesh.SamplePosition(want, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas) &&
                UnityEngine.AI.NavMesh.Raycast(transform.position, hit.position, out _, UnityEngine.AI.NavMesh.AllAreas) == false)
            {
                repositionTo = hit.position;
                repositionUntil = Time.time + Random.Range(1.6f, 2.6f);
            }
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            // Mid-sidestep: keep walking (and keep facing the target) instead of shooting.
            if (Time.time < repositionUntil)
            {
                if ((transform.position - repositionTo).sqrMagnitude < 0.5f) repositionUntil = 0f;
                else
                {
                    agent.isStopped = false;
                    agent.stoppingDistance = 0.2f;
                    agent.SetDestination(repositionTo);
                    if (target != null)
                    {
                        var look = target.transform.position - transform.position;
                        look.y = 0f;
                        if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 6f);
                    }
                    return true;
                }
            }

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
            var dir = AimDir(origin, Arrows.Speed);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId);
            FireRpc(origin, dir);
            PlanReposition();
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
