using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Ink Lamprey (2026-11-13, the user: "some kind of blindness effect"): a drowned thing from the cisterns with a
    /// round mouth of hooked teeth where a face should be, wrapped in black weed. It spits a gout of ink in an arc at
    /// where you are going (a dark circle shows where it lands): caught under it you are BLINDED - ink closes over your
    /// eyes to a small clear hole, the map and the dead's health bars are gone - and the ink pools there a while,
    /// blinding anyone who walks through. Sidestep the circle, keep out of the pools, Holy Water washes your eyes.
    /// Up close it bites.
    /// </summary>
    public class InkLampreyBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 8f, spitCooldown = 8f, spitWindup = 1.0f;
        public const float InkRadius = 2.3f, BlindSeconds = 4f, PoolSeconds = 6f, PoolBlind = 1.2f;
        public static readonly Color InkColor = new Color(0.12f, 0.08f, 0.2f);
        private float nextSpit, spitAt = -1f, nextPoolTick;
        private Vector3 spitSpot;
        private struct Pool { public Vector3 At; public float Until; }
        private readonly List<Pool> pools = new List<Pool>();
        /// <summary>For tests: gouts that landed, and players they blinded.</summary>
        public int Spits { get; private set; }
        public int Blinded { get; private set; }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextSpit = Time.time + 2.5f;
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = Mathf.Min(preferredRange, attackRange * 0.9f);
            if (Time.time < nextSpit || distanceToTarget > 15f || distanceToTarget < 3f || target == null) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, target)) return false;
            var at = target.transform.position;
            if (target.TryGetComponent<PlayerLife>(out var p))
            {
                var v = p.ServerVelocity;
                v.y = 0f;
                at += v * spitWindup * 0.55f;
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            }
            BeginSpit(at, spitWindup);
            return true;
        }

        private void BeginSpit(Vector3 at, float windup)
        {
            nextSpit = Time.time + spitCooldown;
            spitSpot = at;
            spitAt = Time.time + windup;
            busyUntil = spitAt + 0.4f;
            var look = at - transform.position; look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
            PlayActionRpc(RigAction.WindHeavy, windup);
            SpitWarnRpc(at, windup);
        }

        /// <summary>For tests: spit at <paramref name="at"/> now.</summary>
        public void ServerDebugSpit(Vector3 at, float windup) => BeginSpit(at, windup);

        protected override void Update()
        {
            if (IsServer && !IsDead) TickPools();
            if (HeldByStun()) return;
            if (IsServer && !IsDead && spitAt > 0f && Time.time >= spitAt)
            {
                spitAt = -1f;
                Spits++;
                foreach (var v in CombatQuery.Circle(spitSpot, InkRadius, Team.Players))
                {
                    v.Tag("Ink").ServerDamage(damage * 0.6f, spitSpot + Vector3.up, NetworkObjectId);
                    if (v.TryGetComponent<PlayerDebuffs>(out var deb) && deb.ServerBlind(BlindSeconds)) Blinded++;
                }
                pools.Add(new Pool { At = spitSpot, Until = Time.time + PoolSeconds });
                SplashRpc(transform.position + Vector3.up * 1.6f, spitSpot);
            }
            base.Update();
        }

        private void TickPools()
        {
            if (pools.Count == 0 || Time.time < nextPoolTick) return;
            nextPoolTick = Time.time + 0.25f;
            for (int i = pools.Count - 1; i >= 0; i--)
            {
                if (Time.time > pools[i].Until) { pools.RemoveAt(i); continue; }
                foreach (var v in CombatQuery.Circle(pools[i].At, InkRadius * 0.85f, Team.Players))
                    if (v.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerBlind(PoolBlind);
            }
        }

        public override void ServerStun(float seconds)
        {
            if (spitAt > 0f) { spitAt = -1f; CancelRpc(); }
            base.ServerStun(seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpitWarnRpc(Vector3 at, float seconds)
        {
            Telegraph.Show(at, InkRadius, seconds, this, new Color(0.35f, 0.2f, 0.55f, 0.3f));
            Sfx.Play(Sound.Slime, transform.position + Vector3.up * 1.5f, 0.7f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void SplashRpc(Vector3 from, Vector3 at)
        {
            var d = at - from;
            float flat = new Vector2(d.x, d.z).magnitude;
            Arrows.SpawnVisual(from, (d + Vector3.up * flat * 0.35f).normalized, ProjectileKind.Spit, 18f, flat + 1f, 6f);
            var floor = Vfx.Floor(at);
            FxSprite.Decal(FxSprite.AnyScorch, floor + Vector3.up * 0.02f, InkRadius * 1.6f, InkRadius * 1.75f, PoolSeconds + 1f, new Color(0.03f, 0.02f, 0.06f, 0.92f));
            for (int i = 0; i < 6; i++)
                FxSprite.Billboard(FxSprite.AnySmoke, floor + new Vector3(Random.Range(-1f, 1f), 0.4f, Random.Range(-1f, 1f)), Vector3.up * 0.5f, 1.6f, 0.8f, 2f, new Color(0.05f, 0.03f, 0.08f, 0.55f));
            Sfx.Play(Sound.Splash, floor + Vector3.up, 0.9f, 0.6f);
            Sfx.Play(Sound.Slime, floor + Vector3.up, 0.6f, 0.8f);
        }
    }
}
