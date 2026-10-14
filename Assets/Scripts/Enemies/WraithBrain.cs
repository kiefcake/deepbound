using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Wraith: a drifting spectre that keeps its distance, flings shadow bolts, and
    /// every few seconds lets out a Wail — a ring on the floor under you that hurts if you
    /// are still in it when it lands. Get close and it slips away.
    ///
    /// <para>
    /// <b>Ethereal</b>: most damage passes through it (60% less) unless it is pinned down —
    /// chilled, stunned, marked, or standing in consecrated ground. One player alone can
    /// grind it down; a party that combos makes it solid.
    /// </para>
    /// </summary>
    public class WraithBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 9f;
        [SerializeField] private float wailCooldown = 6.5f, wailWindup = 1.3f, wailRadius = 3f;
        [SerializeField] private float slipCooldown = 7f;

        private float nextWail, nextSlip, wailAt = -1f;
        private Vector3 wailSpot;

        /// <summary>Pinned: chilled, stunned, marked or in Consecrate — takes full damage.</summary>
        public bool Pinned => IsStunned || health.Chilled.Value || health.Marked.Value || ClericKit.InConsecration(transform.position);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextWail = Time.time + 3f;
            if (IsServer) health.IncomingModifiers.Add((dmg, from) => Pinned ? dmg : dmg * 0.4f);
        }

        protected override void Update()
        {
            // Everyone: the spectre hovers and sways.
            if (Rig != null) Rig.transform.localPosition = new Vector3(0f, 0.25f + Mathf.Sin(Time.time * 2.1f + NetworkObjectId) * 0.12f, 0f);

            if (IsServer && !IsDead && wailAt > 0f)
            {
                agent.isStopped = true;
                if (Time.time >= wailAt) ResolveWail();
                return;
            }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;

            // Too close: slip away through the dark.
            if (distanceToTarget < 3.5f && Time.time >= nextSlip)
            {
                nextSlip = Time.time + slipCooldown;
                var away = transform.position - target.transform.position;
                away.y = 0f;
                var to = transform.position + Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * away.normalized * 7f;
                if (NavMesh.SamplePosition(to, out var hit, 3f, NavMesh.AllAreas))
                {
                    var from = transform.position;
                    agent.Warp(hit.position);
                    SlipFxRpc(from, hit.position);
                    return true;
                }
            }

            if (Time.time >= nextWail && distanceToTarget < 16f && CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, target))
            {
                nextWail = Time.time + wailCooldown;
                wailAt = Time.time + wailWindup;
                wailSpot = target.transform.position;
                busyUntil = wailAt + 0.5f;
                PlayActionRpc(RigAction.Cast, wailWindup);
                WailWarnRpc(wailSpot, wailRadius, wailWindup);
                return true;
            }
            return false;
        }

        private void ResolveWail()
        {
            wailAt = -1f;
            foreach (var victim in CombatQuery.Circle(wailSpot, wailRadius, Team.Players))
                victim.ServerDamage(damage * 1.7f, wailSpot + Vector3.up, NetworkObjectId);
            WailFxRpc(wailSpot, wailRadius);
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.7f + transform.forward * 0.4f;
            var dir = target.Center - origin;
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 14f);
            BoltRpc(origin, dir);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 direction)
        {
            Arrows.SpawnVisual(origin, direction, ProjectileKind.ShadowBolt, 14f);
            Sfx.Play(Sound.Whisper, origin, 0.6f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WailWarnRpc(Vector3 at, float radius, float seconds)
        {
            Telegraph.Show(at, radius, seconds, null, new Color(0.45f, 0.7f, 1f, 0.4f));
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 1.8f, 1f, 0.55f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WailFxRpc(Vector3 at, float radius)
        {
            var ghost = new Color(0.55f, 0.75f, 1f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 1f, 0.4f);
            Vfx.Ring(at, radius * 1.2f, ghost, 0.45f);
            Vfx.Embers(at + Vector3.up * 0.3f, 14, ghost, 4f, 0.05f, 0.7f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < radius + 1f) local.AddShake(0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SlipFxRpc(Vector3 from, Vector3 to)
        {
            var ghost = new Color(0.55f, 0.75f, 1f);
            Sfx.Play(Sound.Whisper, from + Vector3.up, 0.8f, 1.8f);
            Vfx.Embers(from + Vector3.up, 12, ghost, 2.5f, 0.05f, 0.6f);
            Vfx.Embers(to + Vector3.up, 12, ghost, 2.5f, 0.05f, 0.6f);
        }
    }
}
