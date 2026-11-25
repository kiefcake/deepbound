using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>
    /// Server tools for Ysolde's ability options (2026-11-20 rework): delayed blasts, bleeds, pulls, lanes, and the ally
    /// protections some options lay down (Bastion, Sanctuary, Thorned Aegis, Sheltering Rain). Every kit uses these so the
    /// many small choices behave the same way.
    /// </summary>
    public static class AbilityKit
    {
        public static float BodyRadius(Health h) => h != null && h.TryGetComponent<CapsuleCollider>(out var cap) ? cap.radius * h.transform.lossyScale.x : 0.4f;

        public static bool IsBoss(Health h) => h != null && h.GetComponent<Enemies.BossBrain>() != null;

        /// <summary>Server: run <paramref name="act"/> after <paramref name="seconds"/> (on <paramref name="host"/>, which may be gone by then).</summary>
        public static void Later(MonoBehaviour host, float seconds, Action act)
        {
            if (host != null && host.isActiveAndEnabled) host.StartCoroutine(After(host, seconds, act));
        }

        private static IEnumerator After(MonoBehaviour host, float seconds, Action act)
        {
            yield return new WaitForSeconds(seconds);
            if (host != null) act();
        }

        public static void Stun(Health h, float seconds)
        {
            if (h != null && h.IsAlive && h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(seconds);
        }

        public static void Push(Health h, Vector3 velocity)
        {
            if (h != null && h.IsAlive && h.TryGetComponent<ICrowdControl>(out var cc)) { velocity.y = 0f; cc.ServerKnockback(velocity); }
        }

        /// <summary>Knock <paramref name="h"/> toward <paramref name="to"/>, hard enough to arrive roughly there.</summary>
        public static void PullTo(Health h, Vector3 to, float strength = 1f)
        {
            if (h == null) return;
            var d = to - h.transform.position; d.y = 0f;
            if (d.magnitude < 0.6f) return;
            Push(h, d.normalized * Mathf.Min(16f, 3f + d.magnitude * 1.6f) * strength);
        }

        /// <summary>A bleed (or any damage over time): <paramref name="dps"/> every half second for <paramref name="seconds"/>.</summary>
        public static void Bleed(MonoBehaviour host, Health h, float dps, float seconds, ulong by, string tag, Action<float> onTick = null)
        {
            if (host != null && h != null && host.isActiveAndEnabled) host.StartCoroutine(Bleeding(h, dps, seconds, by, tag, onTick));
        }

        private static IEnumerator Bleeding(Health h, float dps, float seconds, ulong by, string tag, Action<float> onTick)
        {
            float end = Time.time + seconds;
            while (Time.time < end && h != null && h.IsAlive)
            {
                yield return new WaitForSeconds(0.5f);
                if (h == null || !h.IsAlive) yield break;
                float dealt = h.Tag(tag).Ticking().ServerDamage(dps * 0.5f, h.transform.position, by);
                onTick?.Invoke(dealt);
            }
        }

        /// <summary>Server: <paramref name="act"/> every <paramref name="every"/> s for <paramref name="seconds"/> (a mend over time, a pulse).</summary>
        public static void Repeat(MonoBehaviour host, float seconds, float every, Action act)
        {
            if (host != null && host.isActiveAndEnabled) host.StartCoroutine(Repeating(host, seconds, every, act));
        }

        private static IEnumerator Repeating(MonoBehaviour host, float seconds, float every, Action act)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return new WaitForSeconds(every);
                if (host == null) yield break;
                act();
            }
        }

        /// <summary>Server: hurt everything in a circle. Returns what was hit (alive or not).</summary>
        public static List<Health> Blast(Vector3 at, float radius, float damage, ulong by, string tag, Action<Health> each = null)
        {
            var hit = CombatQuery.Circle(at, radius, Team.Enemies).ToList();
            foreach (var h in hit)
            {
                if (h == null || !h.IsAlive) continue;
                if (damage > 0f) h.Tag(tag).ServerDamage(damage, at, by);
                each?.Invoke(h);
            }
            return hit;
        }

        /// <summary>Server: everything within <paramref name="width"/> of the segment, nearest first.</summary>
        public static List<Health> Lane(Vector3 from, Vector3 dir, float length, float width)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return new List<Health>();
            dir.Normalize();
            var found = new List<Health>();
            foreach (var h in CombatQuery.Circle(from + dir * (length * 0.5f), length * 0.5f + width + 1.5f, Team.Enemies))
            {
                var p = h.transform.position - from; p.y = 0f;
                float along = Vector3.Dot(p, dir);
                if (along < -0.5f || along > length + 0.5f) continue;
                if ((p - dir * along).magnitude <= width + BodyRadius(h)) found.Add(h);
            }
            found.Sort((a, b) => (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            return found;
        }

        /// <summary>Nearest living enemy to <paramref name="at"/> within <paramref name="range"/>, skipping <paramref name="skip"/>.</summary>
        public static Health Nearest(Vector3 at, float range, ICollection<Health> skip = null)
        {
            Health best = null;
            float bd = range;
            foreach (var h in CombatQuery.Circle(at, range, Team.Enemies))
            {
                if (h == null || !h.IsAlive || (skip != null && skip.Contains(h)) || h.Team != Team.Enemies) continue;
                float d = Vector3.Distance(h.transform.position, at);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        // ---------------------------------------------------------------- ally protections

        private struct Ward
        {
            public Vector3 At; public float Radius, Until, Scale; public Health Follow; public string Why;
        }
        private static readonly List<Ward> damageZones = new List<Ward>();
        private static readonly Dictionary<Health, float> thornedUntil = new Dictionary<Health, float>();
        private static readonly Dictionary<Health, (float until, ulong by)> sanctuary = new Dictionary<Health, (float, ulong)>();

        /// <summary>Server: allies inside take x<paramref name="scale"/> until <paramref name="seconds"/> pass (Bastion follows its Knight).</summary>
        public static void ServerDamageZone(Vector3 at, float radius, float seconds, float scale, Health follow = null, string why = "")
        {
            damageZones.RemoveAll(z => Time.time > z.Until);
            damageZones.Add(new Ward { At = at, Radius = radius, Until = Time.time + seconds, Scale = scale, Follow = follow, Why = why });
        }

        /// <summary>Server: whatever strikes <paramref name="h"/> takes a third back, for a while (Thorned Aegis).</summary>
        public static void ServerThorn(Health h, float seconds) { if (h != null) thornedUntil[h] = Time.time + seconds; }

        /// <summary>Server: the next blow that would down <paramref name="h"/> leaves 1 health (Sanctuary), once.</summary>
        public static void ServerSanctuary(Health h, float seconds, ulong by) { if (h != null && !sanctuary.ContainsKey(h)) sanctuary[h] = (Time.time + seconds, by); }

        /// <summary>Server: forget who has had this cast's Sanctuary (each cast grants it once).</summary>
        public static void ServerClearSanctuary(ulong by)
        {
            foreach (var k in sanctuary.Where(kv => kv.Value.by == by).Select(kv => kv.Key).ToList()) sanctuary.Remove(k);
        }

        /// <summary>Server: one incoming modifier on every delver for all of the above (registered by <see cref="PlayerLife"/>).</summary>
        public static float AllyIncoming(PlayerLife who, float dmg, Vector3 from)
        {
            if (dmg <= 0f || who == null) return dmg;
            var me = who.Health;
            var at = who.transform.position;
            for (int i = damageZones.Count - 1; i >= 0; i--)
            {
                var z = damageZones[i];
                if (Time.time > z.Until) { damageZones.RemoveAt(i); continue; }
                var c = z.Follow != null ? z.Follow.transform.position : z.At;
                if (z.Follow == me) continue;
                if ((at - c).sqrMagnitude <= z.Radius * z.Radius) { dmg *= z.Scale; break; }
            }
            if (thornedUntil.TryGetValue(me, out var thornEnd))
            {
                if (Time.time > thornEnd || me.Shield.Value <= 0f) thornedUntil.Remove(me);
                else Thorn(me, dmg / 3f);
            }
            if (sanctuary.TryGetValue(me, out var s) && Time.time <= s.until && dmg >= me.Current.Value && me.Current.Value > 1f)
            {
                sanctuary[me] = (-1f, s.by);
                if (me.TryGetComponent<PlayerProgress>(out var pp)) pp.AbilityTextRpc(at + Vector3.up * 2.2f, "SANCTUARY", PlayerProgress.FxKind.Holy);
                return Mathf.Max(0f, me.Current.Value - 1f);
            }
            return dmg;
        }

        /// <summary>Server: hurt whoever last hit <paramref name="me"/> by <paramref name="damage"/> (thorns).</summary>
        public static void Thorn(Health me, float damage)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (damage <= 0f || nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(me.LastHitFrom, out var obj)) return;
            if (!obj.TryGetComponent<Health>(out var foe) || foe.Team != Team.Enemies || !foe.IsAlive) return;
            foe.Tag("Thorns").ServerDamage(damage, me.transform.position, me.NetworkObjectId);
        }
    }
}
