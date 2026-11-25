using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public static class Shapes
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        private static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            m.name = key;
            Cache[key] = m;
            return m;
        }

        public static Mesh Ribbon(IList<Vector3> path, IList<Vector3> side, IList<float> width)
        {
            int n = path.Count;
            var v = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                float u = n > 1 ? i / (float)(n - 1) : 0f;
                var half = side[i].normalized * width[i] * 0.5f;
                v[i * 2] = path[i] - half;
                v[i * 2 + 1] = path[i] + half;
                uv[i * 2] = new Vector2(u, 0f);
                uv[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;
                tris[i * 6 + 0] = a; tris[i * 6 + 1] = a + 2; tris[i * 6 + 2] = a + 1;
                tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = a + 2; tris[i * 6 + 5] = a + 3;
            }
            var m = new Mesh { name = "FxRibbon", vertices = v, uv = uv, triangles = tris };
            m.colors = Whites(v.Length);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Color[] Whites(int n)
        {
            var c = new Color[n];
            for (int i = 0; i < n; i++) c[i] = Color.white;
            return c;
        }

        public static Mesh Crescent(float degrees = 120f, float thickness = 0.22f) => Cached("crescent" + degrees + "_" + thickness, () =>
        {
            const int n = 24;
            var path = new Vector3[n]; var side = new Vector3[n]; var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float a = Mathf.Deg2Rad * Mathf.Lerp(-degrees * 0.5f, degrees * 0.5f, t);
                var out_ = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

                float belly = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.8f) * (0.75f + 0.25f * t);
                path[i] = out_ * (1f - thickness * belly * 0.5f);
                side[i] = out_;
                w[i] = Mathf.Max(0.004f, thickness * belly);
            }
            return Ribbon(path, side, w);
        });

        public static Mesh Hoop(float band = 0.12f, int segments = 40) => Cached("hoop" + band + "_" + segments, () =>
        {
            var path = new Vector3[segments + 1]; var side = new Vector3[segments + 1]; var w = new float[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                path[i] = o; side[i] = o; w[i] = band;
            }
            return Ribbon(path, side, w);
        });

        public static Mesh Wall(int segments = 36) => Cached("wall" + segments, () =>
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(o); uv.Add(new Vector2(i / (float)segments, 0f));
                v.Add(o + Vector3.up); uv.Add(new Vector2(i / (float)segments, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
            var m = new Mesh { vertices = v.ToArray(), uv = uv.ToArray(), triangles = tris.ToArray() };
            m.colors = Whites(v.Count);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        });

        public static Mesh Bar(float length, float width) => Cached("bar" + length + "_" + width, () =>
            Ribbon(new[] { new Vector3(0f, -length * 0.5f, 0f), new Vector3(0f, length * 0.5f, 0f) }, new[] { Vector3.right, Vector3.right }, new[] { width, width }));

        public static Mesh Spindle(float length, float width) => Cached("spindle" + length + "_" + width, () =>
        {
            const int n = 9;
            var path = new Vector3[n]; var side = new Vector3[n]; var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                path[i] = new Vector3(0f, (t - 0.5f) * length, 0f);
                side[i] = Vector3.right;
                w[i] = Mathf.Max(0.003f, width * Mathf.Sin(t * Mathf.PI));
            }
            return Ribbon(path, side, w);
        });

        public static Mesh Outline(string key, Vector2[] points, float width) => Cached("outline_" + key + width, () =>
        {
            var path = new List<Vector3>(); var side = new List<Vector3>(); var w = new List<float>();
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Length];
                var d = (b - a).normalized;
                var n = new Vector3(-d.y, d.x, 0f) * width * 0.5f;

                a -= d * width * 0.4f; b += d * width * 0.4f;
                int o = v.Count;
                v.Add(a - n); v.Add(a + n); v.Add(b - n); v.Add(b + n);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f));
                tris.AddRange(new[] { o, o + 2, o + 1, o + 1, o + 2, o + 3 });
            }
            var m = new Mesh { vertices = v.ToArray(), uv = uv.ToArray(), triangles = tris.ToArray() };
            m.colors = Whites(v.Count);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        });

        public static Mesh Fill(string key, Vector2[] points) => Cached("fill_" + key, () =>
        {
            var c = Vector2.zero;
            foreach (var p in points) c += p;
            c /= points.Length;
            var v = new List<Vector3> { c };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                v.Add(points[i]);

                uv.Add(new Vector2(0.5f, 0.22f));
            }
            for (int i = 0; i < points.Length; i++) tris.AddRange(new[] { 0, 1 + (i + 1) % points.Length, 1 + i });
            var m = new Mesh { vertices = v.ToArray(), uv = uv.ToArray(), triangles = tris.ToArray() };
            m.colors = Whites(v.Count);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        });

        public static readonly Vector2[] Kite =
        {
            new Vector2(-0.45f, 0.48f), new Vector2(0.45f, 0.48f), new Vector2(0.48f, 0.12f),
            new Vector2(0.26f, -0.38f), new Vector2(0f, -0.72f), new Vector2(-0.26f, -0.38f), new Vector2(-0.48f, 0.12f),
        };

        public static readonly Vector2[] Chevron = { new Vector2(-0.3f, 0.2f), new Vector2(0f, -0.15f), new Vector2(0.3f, 0.2f), new Vector2(0f, 0f) };

        public static Mesh Crack(float length, float width, int seed)
        {
            var rnd = new System.Random(seed);
            int n = Mathf.Clamp(Mathf.RoundToInt(length / 0.25f), 4, 16);
            var path = new Vector3[n]; var side = new Vector3[n]; var w = new float[n];
            float x = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                if (i > 0) x += ((float)rnd.NextDouble() - 0.5f) * 0.35f;
                path[i] = new Vector3(x, 0f, t * length);
                side[i] = Vector3.right;
                w[i] = Mathf.Max(0.005f, width * (1f - t) * (0.6f + 0.4f * (float)rnd.NextDouble()));
            }
            return Ribbon(path, side, w);
        }

        public static Mesh Shard => Cached("shard", () =>
        {
            var m = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, -0.3f, 0f), new Vector3(0.5f, -0.4f, 0f), new Vector3(0.05f, 0.6f, 0f) },
                uv = new[] { new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f) },
                triangles = new[] { 0, 2, 1 },
            };
            m.colors = Whites(3);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        });

        public static Vector3[] FlatSides(IList<Vector3> path)
        {
            var s = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                var d = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)];
                d.y = 0f;
                s[i] = d.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, d.normalized) : Vector3.right;
            }
            return s;
        }
    }
}
