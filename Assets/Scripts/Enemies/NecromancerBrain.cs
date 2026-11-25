using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    public class NecromancerBrain : EnemyBrain
    {
        [SerializeField] private NetworkObject minionPrefab;
        [SerializeField] private float preferredRange = 11f;
        public const float BossMendFraction = 0.03f, ChampionMendFraction = 0.12f;
        [SerializeField] private float mendCooldown = 8f, mendWindup = 1.6f, mendRadius = 8f, mendFraction = 0.33f;
        [SerializeField] private float raiseCooldown = 15f;
        private bool riftOpened;

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
            if (HeldByStun()) return;
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

                if (!riftOpened)
                {
                    riftOpened = true;
                    var face = target != null ? target.transform.position - transform.position : transform.forward;
                    face.y = 0f;
                    if (face.sqrMagnitude < 0.01f) face = transform.forward;
                    var side = Vector3.Cross(Vector3.up, face.normalized) * (Random.value < 0.5f ? -3f : 3f);
                    var rift = World.DungeonDirector.ServerOpenRift(transform.position + side - face.normalized * 1.5f, Quaternion.LookRotation(face), minionPrefab);
                    if (rift != null) { RaiseFxRpc(); return true; }
                }
                for (int i = 0; i < 2; i++)
                {
                    var pos = World.DropSpots.Stand(transform.position, transform.position + Quaternion.Euler(0f, i * 180f + 45f, 0f) * Vector3.forward * 2.5f, 0.3f, 0.8f);
                    var m = World.DungeonDirector.ServerSpawnEntering(minionPrefab, pos, transform.rotation, 0f, 0f);
                    var brain = m.GetComponent<EnemyBrain>();
                    brain.NoLoot = true;
                    brain.ServerScale(scaledPlayersBase, 0);
                }
                RaiseFxRpc();
                return true;
            }

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
            var healed = new System.Collections.Generic.List<Vector3>();
            foreach (var h in CombatQuery.Circle(transform.position, mendRadius, Team.Enemies))
            {
                if (h.Team != Team.Enemies || h.GetComponent<TrainingDummy>() != null) continue;

                var hb = h.GetComponent<EnemyBrain>();
                float frac = hb is BossBrain ? BossMendFraction : hb != null && hb.IsChampion ? ChampionMendFraction : mendFraction;
                h.ServerHeal(h.Max.Value * frac);
                if (h != health) healed.Add(h.transform.position);
            }
            MendFxRpc(healed.ToArray());
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

        [Rpc(SendTo.ClientsAndHost)]
        private void MendTelegraphRpc(Vector3 at, float radius, float duration)
        {
            Telegraph.Show(at, radius, duration, this, new Color(0.3f, 1f, 0.45f, 0.3f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void MendFxRpc(Vector3[] healed)
        {
            Sfx.Play(Sound.Shade, transform.position + Vector3.up, 0.9f, 0.7f);
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.5f, 0.8f);
            var green = new Color(0.45f, 1f, 0.5f);
            Vfx.Ring(transform.position, 1.2f, green, 0.6f);
            foreach (var at in healed)
            {
                Vfx.Ring(at, 1.1f, green, 0.6f);
                Vfx.Embers(at + Vector3.up, 6, green, 2.5f, 0.05f, 0.8f);
            }
        }

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
