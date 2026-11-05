using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Choir Acolyte (2026-10-20, the Black Cathedral's own dead): a pale chorister with a censer that
    /// hangs back behind the others and sings. Every so often it stands still and raises a HYMN (a golden
    /// circle fills under it for <see cref="HymnSeconds"/>); if the hymn finishes, every one of the dead in
    /// the circle is SHIELDED for a quarter of a quarter of its health. Stun it, or hit it hard
    /// (<see cref="BreakFraction"/> of its health) while it sings, and the hymn breaks. It throws a feeble
    /// spark when it has nothing to sing for.
    /// </summary>
    public class ChoirAcolyteBrain : EnemyBrain
    {
        public const float HymnSeconds = 2.6f, HymnRadius = 9f, HymnCooldown = 11f, WardFraction = 0.25f, WardSeconds = 14f, BreakFraction = 0.12f;
        [SerializeField] private float preferredRange = 10f;

        private float nextHymn, hymnEnds = -1f, hymnHealth;

        /// <summary>Server, for tests.</summary>
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

        /// <summary>Server, for tests: let it sing again at once.</summary>
        public void ServerDebugResetHymn() { nextHymn = 0f; hymnEnds = -1f; }

        /// <summary>Server: start singing (tests call it directly).</summary>
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
                // A hard blow breaks the song.
                if (hymnHealth - health.Current.Value >= health.Max.Value * BreakFraction) { ServerBreakHymn(); return; }
                if (Time.time >= hymnEnds)
                {
                    hymnEnds = -1f;
                    HymnsSung++;
                    var warded = new System.Collections.Generic.List<Vector3>();
                    foreach (var e in All)
                    {
                        // Never itself: breaking the singer has to stay possible.
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
            Sfx.Play(Sound.Toll, at + Vector3.up * 1.6f, 0.7f, 0.7f); // not the revive chime: this is the enemy's hymn
            Sfx.Play(Sound.Whisper, at + Vector3.up * 1.6f, 0.8f, 1.3f);
            UI.GameHud.ShowCombo(at + Vector3.up * 2.3f, "HYMN - BREAK IT", Gilt);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnBrokenRpc(Vector3 at)
        {
            Telegraph.Cancel(this);
            Sfx.Play(Sound.Shatter, at, 0.7f, 0.8f);
            UI.GameHud.ShowCombo(at, "HYMN BROKEN", new Color(0.9f, 0.9f, 0.9f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnSungRpc(Vector3[] warded)
        {
            Sfx.Play(Sound.Relic, transform.position + Vector3.up * 1.6f, 0.8f, 0.7f);
            foreach (var p in warded)
            {
                Vfx.Ring(p - Vector3.up * 0.9f, 0.9f, Gilt, 0.5f);
                UI.GameHud.ShowCombo(p + Vector3.up * 1.2f, "SHIELDED", Gilt);
            }
        }
    }
}
