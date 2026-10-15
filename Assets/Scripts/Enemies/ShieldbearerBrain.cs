using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Shieldbearer: a skeleton behind a tower shield. From the front almost nothing gets
    /// through (85% blocked) — flank it, or stun it and the shield drops. It advances
    /// steadily, turns slowly, and answers anyone in its face with a shield shove that
    /// knocks them back. A puzzle for the party: one holds its attention, another goes round.
    /// </summary>
    public class ShieldbearerBrain : EnemyBrain
    {
        [SerializeField] private float frontalTaken = 0.15f, frontArc = 70f;
        private float lastClang;

        /// <summary>Server clock time a kick or a bash knocked the shield aside until.</summary>
        public readonly NetworkVariable<double> GuardDownUntil = new NetworkVariable<double>();

        /// <summary>The shield is knocked aside right now (anyone).</summary>
        public bool GuardDown => NetworkManager != null && NetworkManager.IsListening && NetworkManager.ServerTime.Time < GuardDownUntil.Value;

        /// <summary>Is the shield up (anyone): not while stunned, nor while knocked aside.</summary>
        public bool ShieldUp => (Status.Value & 1) == 0 && !GuardDown && !IsDead;

        /// <summary>Server: a kick or a bash knocks the shield wide for a few seconds.</summary>
        public void ServerBreakGuard(float seconds)
        {
            if (IsDead) return;
            bool wasUp = !GuardDown;
            GuardDownUntil.Value = System.Math.Max(GuardDownUntil.Value, NetworkManager.ServerTime.Time + seconds);
            if (wasUp) GuardBrokenRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GuardBrokenRpc()
        {
            var at = transform.position + transform.forward * 0.5f + Vector3.up * 1.2f;
            Sfx.Play(Sound.Block, at, 1f, 0.6f);
            Sfx.Play(Sound.Bash, at, 0.8f, 0.7f);
            Vfx.Embers(at, 12, new Color(1f, 0.85f, 0.55f), 4f, 0.05f, 0.5f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.9f, "SHIELD BROKEN", new Color(1f, 0.85f, 0.55f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) health.IncomingModifiers.Add(Shield);
        }

        private float Shield(float damage, Vector3 from)
        {
            if (IsStunned || GuardDown) return damage;
            var to = from - transform.position;
            to.y = 0f;
            // Blasts centred on it (a meteor landing on its head) go round the shield.
            if (to.magnitude < 0.8f) return damage;
            if (Vector3.Angle(transform.forward, to) > frontArc) return damage;
            if (Time.time - lastClang > 0.25f)
            {
                lastClang = Time.time;
                ClangRpc();
            }
            return damage * frontalTaken;
        }

        protected override void Update()
        {
            if (Rig != null) Rig.Blocking = ShieldUp;
            base.Update();
        }

        protected override void Strike()
        {
            if (target == null) return;
            PlayActionRpc(RigAction.Bash, 0.35f);
            foreach (var victim in CombatQuery.Arc(transform.position + Vector3.up, transform.forward, attackRange + 0.4f, 90f, Team.Players))
            {
                victim.ServerDamage(damage, transform.position, NetworkObjectId);
                var push = victim.transform.position - transform.position;
                push.y = 0f;
                if (victim.TryGetComponent<Player.KnockbackReceiver>(out var k)) k.KnockbackRpc(push.normalized * 8f + Vector3.up * 2f);
            }
            ShoveFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ClangRpc()
        {
            var at = transform.position + transform.forward * 0.6f + Vector3.up * 1.2f;
            Sfx.Play(Sound.Block, at, 0.8f, 0.85f);
            Vfx.Embers(at, 4, new Color(1f, 0.85f, 0.55f), 3f, 0.04f, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShoveFxRpc() => Sfx.Play(Sound.Bash, transform.position + transform.forward + Vector3.up, 0.9f, 0.8f);
    }
}
