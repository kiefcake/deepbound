using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Bell-ringer (2026-11-13, the user's silence foe): a tall dead sexton of the Black Cathedral with a cracked
    /// bronze bell yoked on its back and a clapper for a club. It fights up close; every so often it heaves and RINGS
    /// THE BELL: a pale ring rolls out along the floor, and whoever it passes under is SILENCED (no Q, E or ultimate) and
    /// knocked back a little. The ring is low: JUMP it, as you jump a guardian's shockwave. A stun stops the swing.
    /// </summary>
    public class BellringerBrain : EnemyBrain
    {
        [SerializeField] private float tollCooldown = 11f, tollWindup = 1.2f;
        public const float TollSpeed = 8.5f, TollReach = 13f, TollSilence = 3f;
        private float nextToll, tollAt = -1f;
        private class Ring { public Vector3 Origin; public float Start; public readonly HashSet<ulong> Hit = new HashSet<ulong>(); }
        private readonly List<Ring> rings = new List<Ring>();
        /// <summary>For tests: tolls rung, and players they silenced.</summary>
        public int Tolls { get; private set; }
        public int Silenced { get; private set; }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextToll = Time.time + 4f;
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (Time.time < nextToll || distanceToTarget > 10f || target == null) return false;
            BeginToll(tollWindup);
            return true;
        }

        private void BeginToll(float windup)
        {
            nextToll = Time.time + tollCooldown;
            tollAt = Time.time + windup;
            busyUntil = tollAt + 0.5f;
            agent.isStopped = true;
            PlayActionRpc(RigAction.Slam, windup + 0.2f);
            TollWarnRpc(transform.position, windup);
        }

        /// <summary>For tests: ring the bell now.</summary>
        public void ServerDebugToll(float windup) => BeginToll(windup);

        protected override void Update()
        {
            if (IsServer && !IsDead) TickRings();
            if (HeldByStun()) return;
            if (IsServer && !IsDead && tollAt > 0f && Time.time >= tollAt)
            {
                tollAt = -1f;
                Tolls++;
                rings.Add(new Ring { Origin = transform.position, Start = Time.time });
                TollRpc(transform.position);
            }
            base.Update();
        }

        private void TickRings()
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var w = rings[i];
                float r = (Time.time - w.Start) * TollSpeed;
                if (r > TollReach + 1f) { rings.RemoveAt(i); continue; }
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable || w.Hit.Contains(p.NetworkObjectId)) continue;
                    var d = p.transform.position - w.Origin;
                    if (d.y > 0.45f) continue; // airborne: it passes under
                    d.y = 0f;
                    if (Mathf.Abs(d.magnitude - r) > 0.8f) continue;
                    w.Hit.Add(p.NetworkObjectId);
                    p.Health.Tag("Toll").ServerDamage(damage * 0.4f, w.Origin + Vector3.up, NetworkObjectId);
                    if (p.TryGetComponent<PlayerDebuffs>(out var deb) && deb.ServerSilence(TollSilence)) Silenced++;
                    if (p.TryGetComponent<KnockbackReceiver>(out var kb)) kb.KnockbackRpc(d.normalized * 4f + Vector3.up * 1.5f);
                }
            }
        }

        public override void ServerStun(float seconds)
        {
            if (tollAt > 0f) { tollAt = -1f; CancelRpc(); }
            base.ServerStun(seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TollWarnRpc(Vector3 at, float seconds)
        {
            Telegraph.Show(at, 2.4f, seconds, this, new Color(CantorBrain.HushColor.r, CantorBrain.HushColor.g, CantorBrain.HushColor.b, 0.26f));
            Sfx.Play(Sound.Chain, at + Vector3.up * 2f, 0.6f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void TollRpc(Vector3 origin)
        {
            ShockwaveRing.Spawn(origin, TollSpeed, TollReach, CantorBrain.HushColor);
            Sfx.Play(Sound.Toll, origin + Vector3.up * 2f, 1f, 0.75f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, origin) < 12f) local.AddShake(0.2f);
        }
    }
}
