using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Orbweaver (2026-10-20): a lantern-eyed caster that keeps its distance and conjures ORBS, slow
    /// floating lights that drift toward you and then hang in the air spitting bolts in patterns you
    /// have to read: a turning spiral, rings with a gap to slip through, or aimed bursts. At most two
    /// orbs at once; killing the Orbweaver pops every orb it made. Orbs move on the server clock, so
    /// every peer draws them in the same place without syncing anything but their birth.
    /// </summary>
    public class OrbweaverBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 12f, orbCooldown = 8.5f, orbLife = 9f;
        public const int MaxOrbs = 2;
        public const float BoltSpeed = 8.5f;

        private class Orb { public int Id; public Vector3 From, To; public double Start; public float Life; public byte Pattern; public float NextShot, Angle; }
        private readonly List<Orb> orbs = new List<Orb>();
        private float nextOrb, castAt = -1f;
        private int nextId;
        private byte nextPattern;

        /// <summary>Tests: orbs alive and volleys fired.</summary>
        public int OrbsAlive => orbs.Count;
        public int VolleysFired { get; private set; }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextOrb = Time.time + 2f;
            nextPattern = (byte)Random.Range(0, 3);
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
            if (Time.time < nextOrb || orbs.Count >= MaxOrbs || distanceToTarget > 24f) return false;
            nextOrb = Time.time + orbCooldown;
            castAt = Time.time + 0.9f;
            busyUntil = castAt + 0.3f;
            PlayActionRpc(RigAction.Cast, 0.9f);
            CastTellRpc(transform.position + Vector3.up * 2.2f);
            return true;
        }

        /// <summary>Server: conjure an orb now (tests).</summary>
        public void ServerConjure(Vector3 toward)
        {
            var from = transform.position + Vector3.up * 2.2f + transform.forward * 0.6f;
            // It drifts most of the way to its quarry, then hangs at head height and fires.
            var to = Vector3.Lerp(from, toward + Vector3.up * 1.6f, 0.7f);
            to.y = Mathf.Max(to.y, transform.position.y + 1.4f);
            var orb = new Orb { Id = nextId++, From = from, To = to, Start = NetworkManager.ServerTime.Time, Life = orbLife, Pattern = nextPattern, NextShot = Time.time + 1.6f, Angle = Random.Range(0f, 360f) };
            nextPattern = (byte)((nextPattern + 1) % 3);
            orbs.Add(orb);
            OrbRpc(orb.Id, orb.From, orb.To, orb.Start, orb.Life, orb.Pattern);
        }

        protected override void Update()
        {
            if (HeldByStun()) { TickOrbs(); return; }
            if (IsServer && !IsDead && castAt > 0f && Time.time >= castAt)
            {
                castAt = -1f;
                if (target != null) ServerConjure(target.transform.position);
            }
            if (IsServer) TickOrbs();
            base.Update();
        }

        protected override void OnDied(Health h)
        {
            base.OnDied(h);
            // Its orbs die with it.
            double now = NetworkManager.ServerTime.Time;
            foreach (var o in orbs) PopRpc(o.Id, OrbPosition(o.From, o.To, o.Start, now));
            orbs.Clear();
        }

        public override void ServerStun(float seconds)
        {
            castAt = -1f;
            base.ServerStun(seconds);
        }

        public static Vector3 OrbPosition(Vector3 from, Vector3 to, double start, double now)
        {
            float t = (float)(now - start);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.8f));
            var p = Vector3.Lerp(from, to, k);
            // Once there, it circles slowly and bobs, so you cannot hide behind one spot.
            float s = Mathf.Max(0f, t - 1.8f);
            p += new Vector3(Mathf.Cos(s * 0.9f), 0f, Mathf.Sin(s * 0.9f)) * (1.3f * Mathf.Clamp01(s)) + Vector3.up * Mathf.Sin(t * 2.2f) * 0.18f;
            return p;
        }

        private void TickOrbs()
        {
            if (orbs.Count == 0) return;
            double now = NetworkManager.ServerTime.Time;
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var o = orbs[i];
                if (now - o.Start >= o.Life || IsDead) { orbs.RemoveAt(i); PopRpc(o.Id, OrbPosition(o.From, o.To, o.Start, now)); continue; }
                if (Time.time < o.NextShot) continue;
                var at = OrbPosition(o.From, o.To, o.Start, now);
                float dmg = damage * 0.5f;
                switch (o.Pattern)
                {
                    case 0:
                        // A turning spiral: two arms, a little faster deeper down.
                        o.NextShot = Time.time + 0.16f;
                        o.Angle += 21f;
                        Volley(at, o.Angle, 2, dmg, BoltSpeed);
                        break;
                    case 1:
                    {
                        // Rings of twelve with a three-bolt gap that faces somewhere new each time.
                        o.NextShot = Time.time + 1.05f;
                        o.Angle += 67f;
                        for (int a = 0; a < 12; a++)
                        {
                            if (a < 3) continue;
                            var dir = Quaternion.Euler(0f, o.Angle + a * 30f, 0f) * Vector3.forward;
                            Arrows.ServerFire(at, dir, dmg, Team.Players, NetworkObjectId, BoltSpeed * 0.9f, null, false, 26f);
                        }
                        RingRpc(at, o.Angle, BoltSpeed * 0.9f);
                        VolleysFired++;
                        break;
                    }
                    default:
                    {
                        // Aimed: three quick shots leading the nearest player, then a short breath.
                        o.NextShot = Time.time + 1.2f;
                        Health nearest = null;
                        float best = 30f;
                        foreach (var p in PlayerLife.All)
                            if (p != null && p.Targetable) { float d = Vector3.Distance(p.transform.position, at); if (d < best) { best = d; nearest = p.Health; } }
                        if (nearest == null) break;
                        var dirA = Aim.Lead(nearest, at, BoltSpeed * 1.3f, Mathf.Max(0.5f, LeadSkill)) - at;
                        for (int s = -1; s <= 1; s++)
                        {
                            var d2 = Quaternion.Euler(0f, s * 9f, 0f) * dirA;
                            Arrows.ServerFire(at, d2, dmg, Team.Players, NetworkObjectId, BoltSpeed * 1.3f, null, false, 26f);
                            ShotRpc(at, d2, BoltSpeed * 1.3f);
                        }
                        VolleysFired++;
                        break;
                    }
                }
            }
        }

        private void Volley(Vector3 at, float angle, int arms, float dmg, float speed)
        {
            for (int a = 0; a < arms; a++)
            {
                var dir = Quaternion.Euler(0f, angle + a * 360f / arms, 0f) * Vector3.forward;
                Arrows.ServerFire(at, dir, dmg, Team.Players, NetworkObjectId, speed, null, false, 24f);
            }
            SpiralRpc(at, angle, (byte)arms, speed);
            VolleysFired++;
        }

        // ---- Presentation ----

        private static readonly Color Amber = new Color(1f, 0.62f, 0.2f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.Firebolt, 14f);
            if (!IsServer) Rig?.Play(RigAction.Cast, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CastTellRpc(Vector3 at)
        {
            Vfx.Flash(at, Amber, 3f, 6f, 0.9f);
            Vfx.Embers(at, 12, Amber, 1.5f, 0.05f, 0.8f);
            Sfx.Play(Sound.Summon, at, 0.6f, 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OrbRpc(int id, Vector3 from, Vector3 to, double start, float life, byte pattern) => HazardOrb.Spawn(this, id, from, to, start, life, pattern);

        [Rpc(SendTo.ClientsAndHost)]
        private void PopRpc(int id, Vector3 at)
        {
            HazardOrb.Pop(this, id);
            Vfx.Flash(at, Amber, 3f, 5f, 0.25f);
            Vfx.Embers(at, 10, Amber, 3f, 0.05f, 0.5f);
            Sfx.Play(Sound.Shatter, at, 0.5f, 1.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpiralRpc(Vector3 at, float angle, byte arms, float speed)
        {
            for (int a = 0; a < arms; a++) Arrows.SpawnVisual(at, Quaternion.Euler(0f, angle + a * 360f / arms, 0f) * Vector3.forward, ProjectileKind.Firebolt, speed, 24f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RingRpc(Vector3 at, float angle, float speed)
        {
            for (int a = 3; a < 12; a++) Arrows.SpawnVisual(at, Quaternion.Euler(0f, angle + a * 30f, 0f) * Vector3.forward, ProjectileKind.Firebolt, speed, 26f);
            Sfx.Play(Sound.Fire, at, 0.5f, 1.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShotRpc(Vector3 at, Vector3 dir, float speed) => Arrows.SpawnVisual(at, dir, ProjectileKind.Firebolt, speed, 26f);
    }
}
