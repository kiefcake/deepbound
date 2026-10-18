using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Necromancer: the priority target. It hangs at the back of a room, flinging shadow
    /// bolts, and does two things that make every other enemy worse:
    ///
    /// <list type="bullet">
    /// <item><b>Bone Mend</b> — a green circle fills around it; when it completes, every
    ///   undead inside heals a third of its health. Interrupt it (a bash, or any big hit
    ///   during the cast) or the fight resets.</item>
    /// <item><b>Raise</b> — every so often it pulls two fresh skeletons out of the floor.</item>
    /// </list>
    /// Fragile. The lesson is "kill the healer first", which is a Mage job — or a Knight
    /// willing to walk past everything else.
    /// </summary>
    public class NecromancerBrain : EnemyBrain
    {
        [SerializeField] private NetworkObject minionPrefab;
        [SerializeField] private float preferredRange = 11f;
        [SerializeField] private float mendCooldown = 8f, mendWindup = 1.6f, mendRadius = 8f, mendFraction = 0.33f;
        [SerializeField] private float raiseCooldown = 15f;

        private float nextMend, nextRaise, mendAt = -1f;

        public void SetMinionPrefab(NetworkObject prefab) => minionPrefab = prefab;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextMend = Time.time + 4f;
            nextRaise = Time.time + 6f;
        }

        protected override void Update()
        {
            if (IsServer && !IsDead && mendAt > 0f)
            {
                agent.isStopped = true;
                if (Time.time >= mendAt) ResolveMend();
                return;
            }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;

            if (Time.time >= nextMend && AnyHurtAllyNearby())
            {
                mendAt = Time.time + mendWindup;
                nextMend = Time.time + mendCooldown;
                busyUntil = mendAt + 0.4f;
                PlayActionRpc(RigAction.Cast, mendWindup);
                MendTelegraphRpc(transform.position, mendRadius, mendWindup);
                return true;
            }

            if (Time.time >= nextRaise && minionPrefab != null)
            {
                nextRaise = Time.time + raiseCooldown;
                busyUntil = Time.time + 1.4f;
                PlayActionRpc(RigAction.Cast, 1.4f);
                for (int i = 0; i < 2; i++)
                {
                    var pos = transform.position + Quaternion.Euler(0f, i * 180f + 45f, 0f) * Vector3.forward * 2.5f;
                    if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas)) pos = hit.position;
                    var m = Instantiate(minionPrefab, pos, transform.rotation);
                    m.Spawn(destroyWithScene: true);
                    var brain = m.GetComponent<EnemyBrain>();
                    brain.NoLoot = true;
                    brain.ServerScale(scaledPlayersBase, 0);
                }
                RaiseFxRpc();
                return true;
            }

            // Too close: back off toward preferred range.
            if (distanceToTarget < 5f)
            {
                var away = transform.position - target.transform.position;
                away.y = 0f;
                agent.isStopped = false;
                agent.SetDestination(transform.position + away.normalized * 5f);
                return true;
            }
            return false;
        }

        /// <summary>Its "melee" is a shadow bolt: attackRange is long, the strike fires a projectile.</summary>
        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.5f;
            var dir = AimDir(origin, 18f);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 18f);
            BoltRpc(origin, dir);
        }

        private bool AnyHurtAllyNearby()
        {
            foreach (var h in CombatQuery.Circle(transform.position, mendRadius, Team.Enemies))
            {
                if (h != health && h.Team == Team.Enemies && h.Fraction < 0.75f) return true;
            }
            return false;
        }

        private void ResolveMend()
        {
            mendAt = -1f;
            foreach (var h in CombatQuery.Circle(transform.position, mendRadius, Team.Enemies))
            {
                if (h.Team != Team.Enemies || h.GetComponent<TrainingDummy>() != null) continue;
                h.ServerHeal(h.Max.Value * mendFraction);
            }
            MendFxRpc();
        }

        protected override void InterruptWindup(float stagger)
        {
            CancelMend();
            base.InterruptWindup(stagger);
        }

        public override void ServerStun(float seconds)
        {
            CancelMend();
            base.ServerStun(seconds);
        }

        private void CancelMend()
        {
            if (mendAt < 0f) return;
            mendAt = -1f;
            CancelTelegraphRpc();
        }

        // ------------------------------------------------------------------

        [Rpc(SendTo.ClientsAndHost)]
        private void MendTelegraphRpc(Vector3 at, float radius, float duration)
        {
            Telegraph.Show(at, radius, duration, this, new Color(0.3f, 1f, 0.45f, 0.3f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void MendFxRpc() => Sfx.Play(Sound.Revive, transform.position + Vector3.up, 0.9f, 0.7f);

        [Rpc(SendTo.ClientsAndHost)]
        private void RaiseFxRpc() => Sfx.Play(Sound.Summon, transform.position + Vector3.up, 0.8f, 1.3f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.ShadowBolt, 18f);
            Sfx.Play(Sound.Whisper, origin, 0.7f);
        }
    }
}
