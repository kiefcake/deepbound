using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public static class ModelKit
    {
        private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();
        private static Dictionary<string, Material> mats;

        public static bool Has(string slug) => Prefab(slug) != null;

        private static GameObject Prefab(string slug)
        {
            if (cache.TryGetValue(slug, out var p)) return p;
            p = Resources.Load<GameObject>("Models/" + slug + "/" + slug);
            cache[slug] = p;
            if (p != null) bounds[slug] = Measure(p);
            return p;
        }

        private static Bounds Measure(GameObject p)
        {
            var b = new Bounds();
            bool first = true;
            var root = Matrix4x4.Translate(-p.transform.position);
            foreach (var mf in p.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = root * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    var w = m.MultiplyPoint3x4(c);
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                }
            }
            return b;
        }

        private static Material MaterialFor(string slug, string sourceName)
        {
            if (mats == null)
            {
                mats = new Dictionary<string, Material>();
                foreach (var m in Resources.LoadAll<Material>("Materials/Models")) mats[m.name.ToLowerInvariant()] = m;
            }
            string n = (sourceName ?? "").Replace(" (Instance)", "").ToLowerInvariant();
            if (mats.TryGetValue(n, out var hit)) return hit;
            Material best = null;
            int bestLen = 0;
            foreach (var kv in mats)
            {
                bool match = n.Length > 0 && (n.Contains(kv.Key) || kv.Key.Contains(n));
                if (match && kv.Key.Length > bestLen) { best = kv.Value; bestLen = kv.Key.Length; }
            }
            if (best != null) return best;
            string s = slug.ToLowerInvariant();
            foreach (var kv in mats)
                if (kv.Key.StartsWith(s) && !kv.Key.EndsWith("_glass")) return kv.Value;
            return null;
        }

        public static bool Size(string slug, float height, out Vector3 size)
        {
            size = Vector3.zero;
            if (Prefab(slug) == null) return false;
            var b = bounds[slug];
            size = b.size * (b.size.y > 0.001f ? height / b.size.y : 1f);
            return true;
        }

        public static Transform Place(string slug, Transform parent, Vector3 localPos, float yaw, float height, bool shadows = true)
        {
            var p = Prefab(slug);
            if (p == null) return null;
            var b = bounds[slug];
            float scale = b.size.y > 0.001f ? height / b.size.y : 1f;
            var holder = new GameObject("Model_" + slug).transform;
            holder.SetParent(parent, false);
            holder.localPosition = localPos;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var go = Object.Instantiate(p, holder, false);
            go.transform.localRotation = p.transform.localRotation;
            go.transform.localScale = p.transform.localScale * scale;
            go.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z) * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    var m = MaterialFor(slug, shared[i] != null ? shared[i].name : null);
                    if (m != null) shared[i] = m;
                }
                r.sharedMaterials = shared;
            }
            return holder;
        }

        public static bool Batch(string slug, float height, Matrix4x4 at, System.Action<Mesh, int, Material, Matrix4x4> add)
        {
            var p = Prefab(slug);
            if (p == null) return false;
            var b = bounds[slug];
            float s = b.size.y > 0.001f ? height / b.size.y : 1f;
            var norm = at * Matrix4x4.Translate(-new Vector3(b.center.x, b.min.y, b.center.z) * s) * Matrix4x4.Scale(Vector3.one * s) * Matrix4x4.Translate(-p.transform.position);
            foreach (var mf in p.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var r = mf.GetComponent<Renderer>();
                var shared = r != null ? r.sharedMaterials : new Material[0];
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                {
                    var src = i < shared.Length && shared[i] != null ? shared[i].name : null;
                    var m = MaterialFor(slug, src);
                    if (m != null) add(mf.sharedMesh, i, m, norm * mf.transform.localToWorldMatrix);
                }
            }
            return true;
        }

        public static Material Baked(string name)
        {
            MaterialFor("", name);
            return mats.TryGetValue(name.ToLowerInvariant(), out var m) ? m : null;
        }

        public static Vector2 Footprint(string slug, float height)
        {
            if (Prefab(slug) == null) return Vector2.zero;
            var b = bounds[slug];
            float s = b.size.y > 0.001f ? height / b.size.y : 1f;
            return new Vector2(b.size.x * s, b.size.z * s);
        }
    }
}
