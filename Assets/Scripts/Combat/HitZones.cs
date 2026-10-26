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
        private struct Cache { public Transform Head; public WeakPoint[] Weak; public bool Boss; public Vector3 HeadLocal; public float HeadRadius; }
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
            if (c.Head != null) MeasureHead(c.Head, out c.HeadLocal, out c.HeadRadius);
            cache[h] = c;
            return c;
        }

        /// <summary>
        /// The head as built (2026-10-21, "fix all headshot hitboxes"): one skeleton-sized sphere used to
        /// stand in for every creature, so an eye that IS its head, a gargoyle's snout or a boss's big skull
        /// were all off. Measure the parts hung straight on the head pivot (skull, face, helm, hat; not a
        /// chanter or a held thing on its own pivot) and fit a sphere to them, in the head's own space.
        /// </summary>
        private static void MeasureHead(Transform head, out Vector3 local, out float radius)
        {
            bool any = false;
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
            foreach (var mf in head.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var parent = mf.transform.parent;
                if (parent != head && (parent == null || parent.parent != head || !parent.name.Contains("Jaw"))) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = head.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    any = true;
                }
            }
            if (!any) { local = new Vector3(0f, 0.12f, 0f); radius = 0.17f; return; }
            local = (min + max) * 0.5f;
            var ext = (max - min) * 0.5f;
            // A sphere a touch rounder than the average half-size: a hat brim widens it a little, as it should.
            radius = Mathf.Clamp((ext.x + ext.y + ext.z) / 3f * 1.08f, 0.1f, 0.9f);
        }

        /// <summary>Does a path pass through the head or a weak point (a hit there counts even if it misses the body capsule)?</summary>
        public static bool Touches(Health target, Vector3 from, Vector3 to, float pad)
        {
            if (target.Team != Team.Enemies) return false;
            var c = For(target);
            foreach (var w in c.Weak)
                if (w != null && w.gameObject.activeInHierarchy && DistanceToSegment(w.transform.position, from, to) <= w.WorldRadius + pad) return true;
            if (c.Head == null) return false;
            HeadSphere(c, out var hc, out float hr);
            return DistanceToSegment(hc, from, to) <= hr + pad;
        }

        /// <summary>The head sphere's centre and radius in world space.</summary>
        private static void HeadSphere(Cache c, out Vector3 center, out float radius)
        {
            var sc = c.Head.lossyScale;
            center = c.Head.TransformPoint(c.HeadLocal);
            radius = Mathf.Max(0.12f, c.HeadRadius * (Mathf.Abs(sc.x) + Mathf.Abs(sc.y) + Mathf.Abs(sc.z)) / 3f);
        }

        /// <summary>The zone a straight path from <paramref name="from"/> to <paramref name="to"/> passes through first.</summary>
        public static HitZone Along(Health target, Vector3 from, Vector3 to, out Vector3 point)
        {
            point = target.Center;
            if (target.Team != Team.Enemies) return HitZone.Body;
            var c = For(target);
            // The shot's own radius plus a little for the server seeing the head a beat away from where you saw it.
            const float slack = 0.12f;
            foreach (var w in c.Weak)
            {
                if (w == null || !w.gameObject.activeInHierarchy) continue;
                if (DistanceToSegment(w.transform.position, from, to) <= w.WorldRadius + slack) { point = w.transform.position; return HitZone.Weak; }
            }
            if (c.Head != null)
            {
                HeadSphere(c, out var hc, out float hr);
                if (DistanceToSegment(hc, from, to) <= hr + slack) { point = hc; return HitZone.Head; }
            }
            return HitZone.Body;
        }

        /// <summary>Damage multiplier for a zone. Ranged hits pay more than aimed melee.</summary>
        /// <summary>
        /// A weak point bursts for its full bonus at most this often per creature (2026-10-24, the user: "more damage to
        /// some, however you can only hit them so often"); hits in between count as a headshot.
        /// </summary>
        public const float WeakCooldown = 1.4f;
        private static readonly Dictionary<Health, float> weakReadyAt = new Dictionary<Health, float>();
        private static Health pendingBurst;
        private static int pendingFrame = -1;

        /// <summary>Server: the weak point that burst this frame (consumed by <see cref="Arrows.ReportZone"/>).</summary>
        public static Health TakeBurst()
        {
            if (pendingFrame != Time.frameCount) return null;
            var h = pendingBurst;
            pendingBurst = null;
            pendingFrame = -1;
            return h;
        }

        public static float Multiplier(Health target, HitZone zone, bool ranged)
        {
            if (zone == HitZone.Body) return 1f;
            bool boss = For(target).Boss;
            // Bosses: every head and weak point hit fills the break meter (2026-10-26).
            var bossBrain = boss ? target.GetComponent<Enemies.BossBrain>() : null;
            if (zone == HitZone.Weak)
            {
                if (weakReadyAt.TryGetValue(target, out float ready) && Time.time < ready)
                {
                    bossBrain?.ServerAddStagger(Enemies.BossBrain.StaggerWeak);
                    return boss ? (ranged ? 1.25f : 1.1f) : (ranged ? 1.5f : 1.25f);
                }
                bossBrain?.ServerAddStagger(Enemies.BossBrain.StaggerWeakBurst);
                weakReadyAt[target] = Time.time + WeakCooldown;
                pendingBurst = target;
                pendingFrame = Time.frameCount;
                return boss ? (ranged ? 1.75f : 1.5f) : (ranged ? 2.25f : 1.8f);
            }
            bossBrain?.ServerAddStagger(Enemies.BossBrain.StaggerHead);
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
