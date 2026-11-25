using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public class ChoirAcolyteBrain : EnemyBrain
    {
        public const float HymnSeconds = 2.6f, HymnRadius = 9f, HymnCooldown = 11f, WardFraction = 0.25f, WardSeconds = 14f, BreakFraction = 0.12f;
        [SerializeField] private float preferredRange = 10f;

        private float nextHymn, hymnEnds = -1f, hymnHealth;

        public bool Singing => hymnEnds > 0f;
        public int HymnsSung { get; private set; }
        public int HymnsBroken { get; private set; }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextHymn = Time.time + 3f;
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.5f + transform.forward * 0.4f;
            var dir = AimDir(origin, 14f);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 14f);
            SparkRpc(origin, dir);
        }

        private int AlliesInChoir()
        {
            int n = 0;
            foreach (var e in All)
                if (e != null && e != this && !e.IsDead && !(e is BossBrain) && (e.transform.position - transform.position).sqrMagnitude <= HymnRadius * HymnRadius) n++;
            return n;
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextHymn || Singing || AlliesInChoir() == 0) return false;
            ServerBeginHymn();
            return true;
        }

        public void ServerDebugResetHymn() { nextHymn = 0f; hymnEnds = -1f; }

        public void ServerBeginHymn()
        {
            if (!IsServer || IsDead || Singing) return;
            nextHymn = Time.time + HymnCooldown;
            hymnEnds = Time.time + HymnSeconds;
            hymnHealth = health.Current.Value;
            busyUntil = hymnEnds + 0.2f;
            PlayActionRpc(RigAction.Cast, HymnSeconds);
            HymnBeginRpc(transform.position);
        }

        private void ServerBreakHymn()
        {
            if (!Singing) return;
            hymnEnds = -1f;
            HymnsBroken++;
            HymnBrokenRpc(transform.position + Vector3.up * 2f);
        }

        protected override void Update()
        {
            if (IsServer && Singing && (IsStunned || IsDead)) ServerBreakHymn();
            if (HeldByStun()) return;
            if (IsServer && !IsDead && Singing)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;

                if (hymnHealth - health.Current.Value >= health.Max.Value * BreakFraction) { ServerBreakHymn(); return; }
                if (Time.time >= hymnEnds)
                {
                    hymnEnds = -1f;
                    HymnsSung++;
                    var warded = new System.Collections.Generic.List<Vector3>();
                    foreach (var e in All)
                    {
                        if (e == null || e == this || e.IsDead || e is BossBrain || (e.transform.position - transform.position).sqrMagnitude > HymnRadius * HymnRadius) continue;
                        var h = e.GetComponent<Health>();
                        h.ServerShield(h.Max.Value * WardFraction, WardSeconds);
                        warded.Add(h.Center);
                    }
                    HymnSungRpc(warded.ToArray());
                }
                return;
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            ServerBreakHymn();
            base.ServerStun(seconds);
        }

        private static readonly Color Gilt = new Color(1f, 0.82f, 0.4f);

        [Rpc(SendTo.ClientsAndHost)]
        private void SparkRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.ShadowBolt, 14f);
            Sfx.Play(Sound.Frost, origin, 0.4f, 1.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnBeginRpc(Vector3 at)
        {
            Telegraph.Show(at, HymnRadius, HymnSeconds, this, Gilt);
            Sfx.Play(Sound.Toll, at + Vector3.up * 1.6f, 0.7f, 0.7f);
            Sfx.Play(Sound.Whisper, at + Vector3.up * 1.6f, 0.8f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnBrokenRpc(Vector3 at)
        {
            Telegraph.Cancel(this);
            Sfx.Play(Sound.Shatter, at, 0.7f, 0.8f);
            global::Deepbound.Art.Vfx.Embers(at, 16, new Color(1f, 0.92f, 0.7f), 5f, 0.06f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnSungRpc(Vector3[] warded)
        {
            Sfx.Play(Sound.Relic, transform.position + Vector3.up * 1.6f, 0.8f, 0.7f);
            foreach (var p in warded)
            {
                Vfx.Ring(p - Vector3.up * 0.9f, 0.9f, Gilt, 0.5f);
                global::Deepbound.Art.Vfx.Embers(p + Vector3.up * 0.4f, 6, Gilt, 1.5f, 0.05f, 0.8f);
            }
        }
    }
}
