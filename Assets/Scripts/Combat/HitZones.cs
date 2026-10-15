using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Combat
{
    public enum HitZone : byte { Body, Head, Weak }

    /// <summary>
    /// Where an aimed hit landed on a creature: its head (from the rig's head pivot) or one
    /// of its glowing <see cref="WeakPoint"/>s, else the body. Used by projectiles (exact,
    /// along the flight path) and by melee (along the swinger's aim).
    /// </summary>
    public static class HitZones
    {
        private struct Cache { public Transform Head; public WeakPoint[] Weak; public bool Boss; }
        private static readonly Dictionary<Health, Cache> cache = new Dictionary<Health, Cache>();

        private static Cache For(Health h)
        {
            if (cache.TryGetValue(h, out var c) && (c.Head != null || c.Weak.Length > 0)) return c;
            var rig = h.GetComponentInChildren<HumanoidRig>();
            c = new Cache
            {
                Head = rig != null && !(h.GetComponent<Enemies.MimicBrain>() != null) ? rig.Head : null,
                Weak = h.GetComponentsInChildren<WeakPoint>(),
                Boss = h.GetComponent<Enemies.BossBrain>() != null,
            };
            cache[h] = c;
            return c;
        }

        /// <summary>The head sphere's centre and radius in world space.</summary>
        private static void HeadSphere(Transform head, out Vector3 center, out float radius)
        {
            float s = Mathf.Max(0.5f, head.lossyScale.y);
            center = head.position + head.up * 0.12f * s;
            radius = Mathf.Max(0.14f, 0.17f * s);
        }

        /// <summary>The zone a straight path from <paramref name="from"/> to <paramref name="to"/> passes through first.</summary>
        public static HitZone Along(Health target, Vector3 from, Vector3 to, out Vector3 point)
        {
            point = target.Center;
            if (target.Team != Team.Enemies) return HitZone.Body;
            var c = For(target);
            const float slack = 0.08f;
            foreach (var w in c.Weak)
            {
                if (w == null || !w.gameObject.activeInHierarchy) continue;
                if (DistanceToSegment(w.transform.position, from, to) <= w.WorldRadius + slack) { point = w.transform.position; return HitZone.Weak; }
            }
            if (c.Head != null)
            {
                HeadSphere(c.Head, out var hc, out float hr);
                if (DistanceToSegment(hc, from, to) <= hr + slack) { point = hc; return HitZone.Head; }
            }
            return HitZone.Body;
        }

        /// <summary>Damage multiplier for a zone. Ranged hits pay more than aimed melee.</summary>
        public static float Multiplier(Health target, HitZone zone, bool ranged)
        {
            if (zone == HitZone.Body) return 1f;
            bool boss = For(target).Boss;
            if (zone == HitZone.Weak) return boss ? (ranged ? 1.6f : 1.35f) : (ranged ? 2f : 1.6f);
            return boss ? (ranged ? 1.25f : 1.1f) : (ranged ? 1.5f : 1.25f);
        }

        /// <summary>Melee: did the swinger's aim, out to <paramref name="reach"/>, pass through a head or weak point?</summary>
        public static HitZone Aimed(Health target, Vector3 eye, Vector3 aim, float reach, out Vector3 point) =>
            Along(target, eye, eye + aim.normalized * (reach + 1f), out point);

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
