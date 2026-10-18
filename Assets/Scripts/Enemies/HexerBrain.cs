using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Hexer: a gaunt cultist that hangs back and lays a Curse on whoever it can see —
    /// 30% less damage dealt, half the healing received — for a long while. The curse ends
    /// the moment the Hexer dies, so it is always the first target. It slips away from
    /// anyone who closes in.
    /// </summary>
    public class HexerBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 11f, curseCooldown = 9f, curseSeconds = 12f, curseWindup = 1f;
        private float nextCurse, curseAt = -1f;
        private Health curseTarget;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextCurse = Time.time + 2.5f;
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.4f;
            var dir = AimDir(origin, 15f);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 15f);
            BoltRpc(origin, dir);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextCurse || distanceToTarget > 18f) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.6f, target)) return false;
            nextCurse = Time.time + curseCooldown;
            curseTarget = target;
            curseAt = Time.time + curseWindup;
            busyUntil = curseAt + 0.3f;
            PlayActionRpc(RigAction.Cast, curseWindup);
            CurseWarnRpc(target.NetworkObjectId);
            return true;
        }

        protected override void Update()
        {
            if (IsServer && !IsDead && curseAt > 0f && Time.time >= curseAt)
            {
                curseAt = -1f;
                if (curseTarget != null && curseTarget.IsAlive && CombatQuery.ClearLine(transform.position + Vector3.up * 1.6f, curseTarget) &&
                    curseTarget.TryGetComponent<PlayerDebuffs>(out var deb))
                {
                    deb.ServerCurse(curseSeconds, NetworkObjectId);
                    CurseRpc(transform.position + Vector3.up * 1.6f, curseTarget.Center, curseTarget.NetworkObjectId);
                }
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            curseAt = -1f;
            base.ServerStun(seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.ShadowBolt, 15f);
            Sfx.Play(Sound.Whisper, origin, 0.5f, 1.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CurseWarnRpc(ulong who)
        {
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 1.6f, 1f, 0.5f);
            if (NetworkPlayer.Local != null && NetworkPlayer.Local.NetworkObjectId == who)
                UI.GameHud.Feed("A Hexer is cursing you - break its line of sight!", new Color(0.75f, 0.45f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CurseRpc(Vector3 from, Vector3 to, ulong who)
        {
            var c = new Color(0.6f, 0.25f, 0.9f);
            for (int i = 0; i <= 12; i++) Vfx.Speck(Vector3.Lerp(from, to, i / 12f) + Random.insideUnitSphere * 0.08f, c * 2f);
            Vfx.Flash(to, c, 4f, 4f, 0.3f);
            Sfx.Play(Sound.Summon, to, 0.7f, 1.4f);
            UI.GameHud.ShowCombo(to + Vector3.up * 0.8f, "CURSED", c);
        }
    }
}
