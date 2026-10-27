using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Tongueless Cantor (2026-10-27, RESEARCH.md's Silence afflicter): a gagged chorister of the Throne
    /// and the Cathedral, ringing a bell with no clapper. It hangs back and casts the HUSH: a pale circle opens
    /// under someone (where they are heading), and whoever is inside when it closes is SILENCED - no Q, E or
    /// ultimate for a few seconds (attacks, blocks, dodges and potions still work). Step out of the circle;
    /// Holy Water or a Cleric's cleanse lifts it; a stun snuffs the cast. Frail: reach it.
    /// </summary>
    public class CantorBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 10f, hushCooldown = 9f, hushWindup = 1.4f;
        public const float HushRadius = 2.8f, SilenceSeconds = 3.5f;
        public static readonly Color HushColor = new Color(0.78f, 0.78f, 1f);
        private float nextHush, hushAt = -1f;
        private Vector3 hushSpot;
        /// <summary>For tests: hushes that closed, and players they silenced.</summary>
        public int Hushes { get; private set; }
        public int Silenced { get; private set; }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextHush = Time.time + 3f;
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.4f;
            var dir = AimDir(origin, 14f);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 14f);
            BoltRpc(origin, dir);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextHush || distanceToTarget > 17f || target == null) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.6f, target)) return false;
            var at = target.transform.position;
            if (target.TryGetComponent<PlayerLife>(out var p))
            {
                var v = p.ServerVelocity;
                v.y = 0f;
                at += v * hushWindup * 0.6f;
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            }
            BeginHush(at, hushWindup);
            return true;
        }

        private void BeginHush(Vector3 at, float windup)
        {
            nextHush = Time.time + hushCooldown;
            hushSpot = at;
            hushAt = Time.time + windup;
            busyUntil = hushAt + 0.3f;
            PlayActionRpc(RigAction.Cast, windup);
            HushWarnRpc(at, HushRadius, windup);
        }

        /// <summary>For tests: hush the floor at <paramref name="at"/> now.</summary>
        public void ServerDebugHush(Vector3 at, float windup) => BeginHush(at, windup);

        protected override void Update()
        {
            if (HeldByStun()) return;
            if (IsServer && !IsDead && hushAt > 0f && Time.time >= hushAt)
            {
                hushAt = -1f;
                Hushes++;
                foreach (var v in CombatQuery.Circle(hushSpot, HushRadius, Team.Players))
                {
                    v.ServerDamage(damage * 0.5f, hushSpot + Vector3.up, NetworkObjectId);
                    if (v.TryGetComponent<PlayerDebuffs>(out var deb) && deb.ServerSilence(SilenceSeconds)) Silenced++;
                }
                HushRpc(hushSpot, HushRadius);
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            if (hushAt > 0f) { hushAt = -1f; CancelHushRpc(); }
            base.ServerStun(seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.ShadowBolt, 14f);
            Sfx.Play(Sound.Whisper, origin, 0.45f, 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HushWarnRpc(Vector3 at, float radius, float seconds)
        {
            Telegraph.Show(at, radius, seconds, this, new Color(HushColor.r, HushColor.g, HushColor.b, 0.22f));
            // A bell with no clapper: you hear the swing, never the note.
            Sfx.Play(Sound.Toll, transform.position + Vector3.up * 1.6f, 0.35f, 1.9f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelHushRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void HushRpc(Vector3 at, float radius)
        {
            Vfx.Ring(at + Vector3.up * 0.05f, radius, HushColor, 0.5f);
            Vfx.Embers(at + Vector3.up * 0.4f, 12, HushColor, 2f, 0.05f, 0.8f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.9f, 0.5f);
        }
    }
}
