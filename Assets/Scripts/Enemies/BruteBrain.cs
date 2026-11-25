using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.Enemies
{
    public class BruteBrain : EnemyBrain
    {
        [SerializeField] private float poundRadius = 2.8f;

        protected override bool Heavy => true;

        protected override void BeginAttack()
        {
            base.BeginAttack();

            var at = transform.position + transform.forward * 1.2f;

            TelegraphRpc(at, poundRadius, Mathf.Max(0.1f, strikeAt - Time.time));
        }

        protected override void Strike()
        {
            PlayActionRpc(RigAction.Slam, 0.3f);
            var at = transform.position + transform.forward * 1.2f;
            foreach (var victim in CombatQuery.Circle(at, poundRadius, Team.Players))
            {
                victim.ServerDamage(damage, at, NetworkObjectId);
                if (victim.TryGetComponent<Player.PlayerDebuffs>(out var daze)) daze.ServerDaze(1.6f);
                var push = victim.transform.position - at;
                push.y = 0f;
                if (victim.TryGetComponent<Player.KnockbackReceiver>(out var k)) k.KnockbackRpc(push.normalized * 9f + Vector3.up * 3f);
            }
            PoundFxRpc(at);
        }

        protected override void InterruptWindup(float stagger)
        {
            CancelTelegraphRpc();
            base.InterruptWindup(stagger);
        }

        public override void ServerStun(float seconds)
        {
            if (WindingUp) CancelTelegraphRpc();
            base.ServerStun(Mathf.Min(seconds, 0.8f));
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void TelegraphRpc(Vector3 at, float radius, float duration)
        {
            Telegraph.Show(at, radius, duration, this);
            Rig?.Play(RigAction.Slam, duration + 0.3f);
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void PoundFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 0.9f, 1.15f);
            var local = Player.NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 9f) local.ViewModelBlockImpact();
        }
    }
}
