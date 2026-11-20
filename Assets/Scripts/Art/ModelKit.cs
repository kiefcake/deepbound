using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Downloaded CC0 models (2026-11-20, the user: "replace all poorly code generated assets with ones downloaded if available
    /// in the same look"). Poly Haven meshes live at Resources/Models/&lt;slug&gt;/&lt;slug&gt;.fbx; ArtBaker bakes a URP material per
    /// texture set into Resources/Materials/Models, swapped on here by material name. <see cref="Place"/> returns null when a
    /// model is missing, so every caller keeps its procedural build as the fallback. No colliders: callers keep their own.
    /// </summary>
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
                // The longest baked stem the source name contains (or that contains it) wins: "kite_shield_body" over "kite_shield".
                bool match = n.Length > 0 && (n.Contains(kv.Key) || kv.Key.Contains(n));
                if (match && kv.Key.Length > bestLen) { best = kv.Value; bestLen = kv.Key.Length; }
            }
            if (best != null) return best;
            string s = slug.ToLowerInvariant();
            foreach (var kv in mats)
                if (kv.Key.StartsWith(s) && !kv.Key.EndsWith("_glass")) return kv.Value;
            return null;
        }

        /// <summary>
        /// <paramref name="slug"/> under <paramref name="parent"/>, its base centred on local <paramref name="localPos"/>, turned
        /// <paramref name="yaw"/>, scaled to stand <paramref name="height"/> m tall. Null if the model is not in the build.
        /// </summary>
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

        /// <summary>
        /// Static dressing: every sub-mesh of <paramref name="slug"/>, sized to <paramref name="height"/> and placed by
        /// <paramref name="at"/> (its base centre), handed to <paramref name="add"/> with its baked material, for
        /// <see cref="MeshKit.Batcher.AddModel"/>. False if the model is missing.
        /// </summary>
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

        /// <summary>A baked model material by name (the batcher's "@name" keys).</summary>
        public static Material Baked(string name)
        {
            MaterialFor("", name);
            return mats.TryGetValue(name.ToLowerInvariant(), out var m) ? m : null;
        }

        /// <summary>The model's footprint (x, z) when it stands <paramref name="height"/> m tall; zero if missing.</summary>
        public static Vector2 Footprint(string slug, float height)
        {
            if (Prefab(slug) == null) return Vector2.zero;
            var b = bounds[slug];
            float s = b.size.y > 0.001f ? height / b.size.y : 1f;
            return new Vector2(b.size.x * s, b.size.z * s);
        }
    }
}
