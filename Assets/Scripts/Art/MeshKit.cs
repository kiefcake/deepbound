using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deepbound.Art
{
    /// <summary>
    /// Procedural mesh primitives the whole game is built from.
    ///
    /// <para>
    /// Two shapes cover almost everything. <see cref="Box"/> gives walls, floors, crates,
    /// blades and armour plates, with UVs in world units so a 12m wall and a 1m crate show
    /// stone at the same scale instead of one stretched texture. <see cref="Lathe"/> spins a
    /// 2D profile around the Y axis for anything round: helmets, pauldrons, pillars, skulls,
    /// barrels, torch sconces.
    /// </para>
    ///
    /// Meshes are cached by their parameters, so a dungeon with 200 identical crates holds
    /// one crate mesh.
    /// </summary>
    public static class MeshKit
    {
        /// <summary>Texture repeats per metre. The baked textures are authored for ~2m tiles.</summary>
        public const float UvPerMetre = 0.5f;

        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        // ------------------------------------------------------------------
        // Box
        // ------------------------------------------------------------------

        /// <summary>A box centred on the origin with per-face, world-scaled UVs and hard edges.</summary>
        public static Mesh Box(Vector3 size)
        {
            string key = "box" + size.x.ToString("F3") + "_" + size.y.ToString("F3") + "_" + size.z.ToString("F3");
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var verts = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);
            Vector3 h = size * 0.5f;

            void Face(Vector3 n, Vector3 u, Vector3 v, float uLen, float vLen)
            {
                int start = verts.Count;
                Vector3 c = Vector3.Scale(n, h);
                Vector3 hu = u * (uLen * 0.5f), hv = v * (vLen * 0.5f);

                verts.Add(c - hu - hv); verts.Add(c + hu - hv); verts.Add(c + hu + hv); verts.Add(c - hu + hv);
                for (int i = 0; i < 4; i++) normals.Add(n);

                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(uLen * UvPerMetre, 0));
                uvs.Add(new Vector2(uLen * UvPerMetre, vLen * UvPerMetre));
                uvs.Add(new Vector2(0, vLen * UvPerMetre));

                tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
                tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
            }

            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------
        // Lathe
        // ------------------------------------------------------------------

        /// <summary>
        /// Revolves a profile of (radius, height) points around Y. Low segment counts are a
        /// style choice, not a budget one: 6-10 sides reads as hand-made and chunky, which
        /// suits the art direction better than smooth cylinders.
        /// Profiles run bottom to top; that ordering is what makes the faces point outward.
        /// </summary>
        /// <param name="flat">Faceted (hard-edged) shading instead of smooth.</param>
        /// <param name="sweepDegrees">Less than 360 for partial shells, e.g. a pauldron.</param>
        public static Mesh Lathe(string key, Vector2[] profile, int segments, bool flat = true, float sweepDegrees = 360f)
        {
            key = "lathe_" + key + "_" + segments + "_" + flat + "_" + sweepDegrees;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            bool closed = sweepDegrees >= 359.9f;

            // Profile length, so v runs in world units like the boxes.
            var vCoord = new float[profile.Length];
            for (int i = 1; i < profile.Length; i++)
                vCoord[i] = vCoord[i - 1] + Vector2.Distance(profile[i], profile[i - 1]);

            float sweep = sweepDegrees * Mathf.Deg2Rad;
            float maxRadius = 0f;
            foreach (var p in profile) maxRadius = Mathf.Max(maxRadius, p.x);
            float circumference = sweep * Mathf.Max(maxRadius, 0.05f);

            Vector3 Point(int seg, int prof)
            {
                float a = sweep * seg / segments;
                var p = profile[prof];
                return new Vector3(Mathf.Sin(a) * p.x, p.y, Mathf.Cos(a) * p.x);
            }

            if (flat)
            {
                // Unshared vertices per quad give faceted normals once recalculated.
                for (int s = 0; s < segments; s++)
                for (int p = 0; p < profile.Length - 1; p++)
                {
                    int start = verts.Count;
                    verts.Add(Point(s, p)); verts.Add(Point(s + 1, p)); verts.Add(Point(s + 1, p + 1)); verts.Add(Point(s, p + 1));

                    float u0 = (float)s / segments * circumference * UvPerMetre;
                    float u1 = (float)(s + 1) / segments * circumference * UvPerMetre;
                    uvs.Add(new Vector2(u0, vCoord[p] * UvPerMetre)); uvs.Add(new Vector2(u1, vCoord[p] * UvPerMetre));
                    uvs.Add(new Vector2(u1, vCoord[p + 1] * UvPerMetre)); uvs.Add(new Vector2(u0, vCoord[p + 1] * UvPerMetre));

                    tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                    tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
                }
            }
            else
            {
                int rings = segments + 1;
                for (int s = 0; s < rings; s++)
                for (int p = 0; p < profile.Length; p++)
                {
                    verts.Add(Point(s, p));
                    uvs.Add(new Vector2((float)s / segments * circumference * UvPerMetre, vCoord[p] * UvPerMetre));
                }

                for (int s = 0; s < segments; s++)
                for (int p = 0; p < profile.Length - 1; p++)
                {
                    int a = s * profile.Length + p, b = (s + 1) * profile.Length + p;
                    tris.Add(a); tris.Add(b); tris.Add(b + 1);
                    tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
                }
            }

            if (!closed)
            {
                // Partial shells are seen from both sides (the inside of a pauldron), so
                // duplicate the triangles reversed rather than relying on a two-sided shader.
                int count = tris.Count;
                int vCount = verts.Count;
                for (int i = 0; i < vCount; i++) { verts.Add(verts[i]); uvs.Add(uvs[i]); }
                for (int i = 0; i < count; i += 3)
                {
                    tris.Add(tris[i] + vCount); tris.Add(tris[i + 2] + vCount); tris.Add(tris[i + 1] + vCount);
                }
            }

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>A simple n-sided cylinder (or cone, with different radii).</summary>
        public static Mesh Cylinder(float bottomRadius, float topRadius, float height, int segments = 8)
        {
            return Lathe("cyl" + bottomRadius + "_" + topRadius + "_" + height, new[]
            {
                new Vector2(0f, 0f), new Vector2(bottomRadius, 0f),
                new Vector2(topRadius, height), new Vector2(0f, height),
            }, segments);
        }

        /// <summary>A faceted ball, built as a lathe of a semicircle.</summary>
        public static Mesh Ball(float radius, int segments = 8, int rings = 6)
        {
            var profile = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                profile[i] = new Vector2(Mathf.Sin(a) * radius, -Mathf.Cos(a) * radius);
            }
            return Lathe("ball" + radius + "_" + rings, profile, segments);
        }

        // ------------------------------------------------------------------
        // Scene helpers
        // ------------------------------------------------------------------

        /// <summary>Creates a child object rendering <paramref name="mesh"/>. No collider.</summary>
        public static GameObject Part(Transform parent, string name, Mesh mesh, string material,
            Vector3 localPos, Vector3 localEuler = default, Vector3? localScale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ArtLibrary.Get(material);
            return go;
        }

        /// <summary>An empty transform, for animation pivots (shoulders, hips, weapon grips).</summary>
        public static Transform Pivot(Transform parent, string name, Vector3 localPos, Vector3 localEuler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(localEuler);
            return t;
        }

        /// <summary>
        /// Accumulates static geometry per material and emits one combined mesh per material.
        ///
        /// A generated dungeon is thousands of boxes. As separate objects that is thousands
        /// of draw calls and renderers to cull; combined per material it is about ten.
        /// </summary>
        public class Batcher
        {
            private readonly Dictionary<string, List<CombineInstance>> parts = new Dictionary<string, List<CombineInstance>>();

            public void Add(Mesh mesh, string material, Matrix4x4 transform)
            {
                if (!parts.TryGetValue(material, out var list)) parts[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, transform = transform });
            }

            public void AddBox(Vector3 center, Vector3 size, string material, Quaternion? rotation = null)
            {
                Add(Box(size), material, Matrix4x4.TRS(center, rotation ?? Quaternion.identity, Vector3.one));
            }

            /// <summary>Builds the combined meshes under <paramref name="parent"/>.</summary>
            /// <param name="collide">Add MeshColliders, so the geometry is walkable and navmesh-bakeable.</param>
            public void Build(Transform parent, string name, bool collide)
            {
                foreach (var pair in parts)
                {
                    var mesh = new Mesh { name = name + "_" + pair.Key, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();

                    var go = new GameObject(name + " (" + pair.Key + ")");
                    go.transform.SetParent(parent, false);
                    go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = ArtLibrary.Get(pair.Key);
                    if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }

                parts.Clear();
            }
        }
    }
}
