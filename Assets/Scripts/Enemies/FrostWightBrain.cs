using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Frost Wight: a skeleton rimed in ice. Its blows Frostbite you (40% slower), and
    /// every few seconds it gathers a Frost Nova — a ring around it that frostbites and hurts
    /// anyone still inside when it breaks. Stun it to snuff the nova.
    /// </summary>
    public class FrostWightBrain : EnemyBrain
    {
        [SerializeField] private float novaCooldown = 8f, novaWindup = 1.1f, novaRadius = 3.6f;
        private float nextNova, novaAt = -1f;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextNova = Time.time + 4f;
        }

        protected override void Strike()
        {
            base.Strike();
            foreach (var victim in CombatQuery.Arc(transform.position + Vector3.up, transform.forward, attackRange + 0.4f, attackArc, Team.Players))
                if (victim.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerFrostbite(3f);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (Time.time < nextNova || distanceToTarget > novaRadius + 1f) return false;
            nextNova = Time.time + novaCooldown;
            novaAt = Time.time + novaWindup;
            busyUntil = novaAt + 0.4f;
            PlayActionRpc(RigAction.Cast, novaWindup);
            NovaWarnRpc(transform.position, novaRadius, novaWindup);
            return true;
        }

        protected override void Update()
        {
            if (IsServer && !IsDead && novaAt > 0f && Time.time >= novaAt)
            {
                novaAt = -1f;
                foreach (var victim in CombatQuery.Circle(transform.position, novaRadius, Team.Players))
                {
                    victim.ServerDamage(damage * 0.8f, transform.position, NetworkObjectId);
                    if (victim.TryGetComponent<PlayerDebuffs>(out var deb))
                    {
                        deb.ServerFrostbite(4f);
                        // From the Hollow Throne down, the nova freezes your feet as well.
                        if (World.DungeonDirector.Act(depthForLoot) >= 2) deb.ServerRoot(0.8f);
                    }
                }
                NovaRpc(transform.position, novaRadius);
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            if (novaAt > 0f) { novaAt = -1f; CancelNovaRpc(); }
            base.ServerStun(seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NovaWarnRpc(Vector3 at, float radius, float seconds)
        {
            Telegraph.Show(at, radius, seconds, this, Telegraph.Frost);
            Sfx.Play(Sound.Frost, at + Vector3.up, 0.7f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelNovaRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void NovaRpc(Vector3 at, float radius)
        {
            Sfx.Play(Sound.Shatter, at + Vector3.up, 1f, 0.8f);
            Vfx.IceSpikes(at, radius, 18);
            Vfx.Ring(at, radius, new Color(0.6f, 0.9f, 1f), 0.45f);
        }
    }
}
