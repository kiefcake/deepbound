using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Ghoul: fast, fragile, comes in packs, and closes distance with a pounce. It is the
    /// enemy that punishes standing still at range — and the Mage's natural predator.
    ///
    /// The pounce is short-telegraphed (a crouch and a hiss), crosses a few metres in a
    /// blink, and hits whoever is at the landing spot. Sidestep the crouch and it lands on
    /// nothing, wide open.
    /// </summary>
    public class GhoulBrain : EnemyBrain
    {
        [SerializeField] private float pounceRangeMin = 3.5f, pounceRangeMax = 7.5f;
        [SerializeField] private float pounceWindup = 0.45f, pounceSpeed = 15f, pounceCooldown = 5f;

        private bool pouncing, windingPounce;
        private float pounceAt, pounceTravelled, pounceLength, nextPounce;
        private Vector3 pounceDir;
        private bool pounceHit;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextPounce = Time.time + Random.Range(1f, 3f);
        }

        protected override void Update()
        {
            if (IsServer && !IsDead && agent.enabled)
            {
                if (windingPounce)
                {
                    agent.isStopped = true;
                    if (Time.time >= pounceAt)
                    {
                        windingPounce = false;
                        pouncing = true;
                        pounceTravelled = 0f;
                        pounceHit = false;
                        PlayActionRpc(RigAction.Bash, 0.4f);
                    }
                    return;
                }
                if (pouncing)
                {
                    StepPounce();
                    return;
                }
            }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (Time.time < nextPounce || distanceToTarget < pounceRangeMin || distanceToTarget > pounceRangeMax) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up, target)) return false;

            pounceDir = target.transform.position - transform.position;
            pounceDir.y = 0f;
            pounceLength = pounceDir.magnitude + 0.5f;
            pounceDir.Normalize();
            transform.rotation = Quaternion.LookRotation(pounceDir);

            windingPounce = true;
            pounceAt = Time.time + pounceWindup;
            nextPounce = Time.time + pounceCooldown;
            PlayActionRpc(RigAction.Windup, pounceWindup);
            HissRpc();
            return true;
        }

        private void StepPounce()
        {
            float step = pounceSpeed * Time.deltaTime;
            var before = transform.position;
            agent.Move(pounceDir * step);
            float moved = Vector3.Distance(before, transform.position);
            pounceTravelled += moved;

            if (!pounceHit)
            {
                foreach (var victim in CombatQuery.Circle(transform.position, 1.3f, Team.Players))
                {
                    victim.ServerDamage(damage * 1.2f, transform.position - pounceDir, NetworkObjectId);
                    pounceHit = true;
                    break;
                }
            }

            if (pounceTravelled >= pounceLength || moved < step * 0.3f || pounceHit)
            {
                pouncing = false;
                // A miss leaves it sprawled and open.
                busyUntil = Time.time + (pounceHit ? 0.5f : 1.1f);
            }
        }

        protected override void InterruptWindup(float stagger)
        {
            windingPounce = false;
            base.InterruptWindup(stagger);
        }

        public override void ServerStun(float seconds)
        {
            windingPounce = false;
            pouncing = false;
            base.ServerStun(seconds);
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void HissRpc() => Sfx.Play(Sound.Whisper, transform.position + Vector3.up, 0.9f, 1.5f);
    }
}
