using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Combat
{
    public enum HitZone : byte { Body, Head, Weak }

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

            radius = Mathf.Clamp((ext.x + ext.y + ext.z) / 3f * 1.08f, 0.1f, 0.9f);
        }

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

        private static void HeadSphere(Cache c, out Vector3 center, out float radius)
        {
            var sc = c.Head.lossyScale;
            center = c.Head.TransformPoint(c.HeadLocal);
            radius = Mathf.Max(0.12f, c.HeadRadius * (Mathf.Abs(sc.x) + Mathf.Abs(sc.y) + Mathf.Abs(sc.z)) / 3f);
        }

        public static HitZone Along(Health target, Vector3 from, Vector3 to, out Vector3 point)
        {
            point = target.Center;
            if (target.Team != Team.Enemies) return HitZone.Body;
            var c = For(target);

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

        public const float WeakCooldown = 1.4f;
        private static readonly Dictionary<Health, float> weakReadyAt = new Dictionary<Health, float>();
        private static Health pendingBurst;
        private static int pendingFrame = -1;

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
