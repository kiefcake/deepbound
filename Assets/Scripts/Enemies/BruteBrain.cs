using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Bone Brute: a slow, massive skeleton with a stone maul — a mini-boss in a normal
    /// room. It cannot be shoved, and its attack is a ground pound with a filling circle
    /// telegraph: leave the circle, or Shield Bash the wind-up to cancel it.
    ///
    /// It exists to anchor a fight: while it lumbers toward the tank, everything else in
    /// the room is where the damage dealers should be looking.
    /// </summary>
    public class BruteBrain : EnemyBrain
    {
        [SerializeField] private float poundRadius = 2.8f;

        protected override bool Heavy => true;

        protected override void BeginAttack()
        {
            base.BeginAttack();
            // The pound is centred a little in front of it, where the maul lands.
            var at = transform.position + transform.forward * 1.2f;
            TelegraphRpc(at, poundRadius, windup);
        }

        protected override void Strike()
        {
            PlayActionRpc(RigAction.Slam, 0.3f);
            var at = transform.position + transform.forward * 1.2f;
            foreach (var victim in CombatQuery.Circle(at, poundRadius, Team.Players))
            {
                victim.ServerDamage(damage, at, NetworkObjectId);
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
