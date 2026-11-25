using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deepbound.Art
{
    public static class MeshKit
    {
        public const float UvPerMetre = 0.5f;

        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

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

        public static Mesh Lathe(string key, Vector2[] profile, int segments, bool flat = true, float sweepDegrees = 360f)
        {
            key = "lathe_" + key + "_" + segments + "_" + flat + "_" + sweepDegrees;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            bool closed = sweepDegrees >= 359.9f;

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

        public static Mesh Cylinder(float bottomRadius, float topRadius, float height, int segments = 8)
        {
            return Lathe("cyl" + bottomRadius + "_" + topRadius + "_" + height, new[]
            {
                new Vector2(0f, 0f), new Vector2(bottomRadius, 0f),
                new Vector2(topRadius, height), new Vector2(0f, height),
            }, segments);
        }

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

        private static readonly int MergeBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MergeEmissionId = Shader.PropertyToID("_EmissionColor");

        public static Dictionary<Renderer, Renderer> MergeParts(Transform root)
        {
            var map = new Dictionary<Renderer, Renderer>();
            if (root == null) return map;
            var block = new MaterialPropertyBlock();
            var pivots = root.GetComponentsInChildren<Transform>(true);
            var groups = new Dictionary<(Material, Color, Color, ShadowCastingMode), List<MeshRenderer>>();
            var leaves = new List<Transform>();
            foreach (var t in pivots)
            {
                if (t == null) continue;
                groups.Clear();
                leaves.Clear();
                foreach (Transform c in t) leaves.Add(c);
                foreach (var c in leaves)
                {
                    if (c.childCount > 0 || !c.gameObject.activeSelf) continue;
                    if (c.GetComponents<Component>().Length != 3) continue;
                    var mr = c.GetComponent<MeshRenderer>();
                    var mf = c.GetComponent<MeshFilter>();
                    if (mr == null || mf == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
                    mr.GetPropertyBlock(block);
                    var key = (mr.sharedMaterial, block.isEmpty ? Color.clear : block.GetColor(MergeBaseColorId), block.isEmpty ? Color.clear : block.GetColor(MergeEmissionId), mr.shadowCastingMode);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<MeshRenderer>();
                    list.Add(mr);
                }
                foreach (var kv in groups)
                {
                    var list = kv.Value;
                    if (list.Count < 2) continue;
                    var parts = new CombineInstance[list.Count];
                    int verts = 0;
                    var toPivot = t.worldToLocalMatrix;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var mesh = list[i].GetComponent<MeshFilter>().sharedMesh;
                        parts[i] = new CombineInstance { mesh = mesh, transform = toPivot * list[i].transform.localToWorldMatrix };
                        verts += mesh.vertexCount;
                    }
                    var merged = new Mesh { name = t.name + "_merged" };
                    if (verts > 65000) merged.indexFormat = IndexFormat.UInt32;
                    merged.CombineMeshes(parts, true, true);
                    var go = new GameObject("Merged");
                    go.transform.SetParent(t, false);
                    go.layer = list[0].gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = merged;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = list[0].sharedMaterial;
                    r.shadowCastingMode = list[0].shadowCastingMode;
                    r.receiveShadows = list[0].receiveShadows;
                    list[0].GetPropertyBlock(block);
                    if (!block.isEmpty) r.SetPropertyBlock(block);
                    go.AddComponent<MergedMesh>().Mesh = merged;
                    foreach (var old in list)
                    {
                        map[old] = r;
                        Object.DestroyImmediate(old.gameObject);
                    }
                }
            }
            return map;
        }

        public static void MergeRig(HumanoidRig rig)
        {
            if (rig == null) return;
            var map = MergeParts(rig.transform);
            if (rig.GlowRenderers == null) return;
            var glows = new List<Renderer>();
            foreach (var g in rig.GlowRenderers)
            {
                var r = g != null && map.TryGetValue(g, out var m) ? m : g;
                if (r != null && !glows.Contains(r)) glows.Add(r);
            }
            rig.GlowRenderers = glows.ToArray();
        }

        public static Transform Pivot(Transform parent, string name, Vector3 localPos, Vector3 localEuler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(localEuler);
            return t;
        }

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

            public void AddBox(Vector3 center, Vector3 size, string material, Color tint, Quaternion? rotation = null)
            {
                AddBox(center, size, material + "#" + ColorUtility.ToHtmlStringRGB(tint), rotation);
            }

            public bool AddModel(string slug, float height, Vector3 floor, float yaw)
            {
                return ModelKit.Batch(slug, height, Matrix4x4.TRS(floor, Quaternion.Euler(0f, yaw, 0f), Vector3.one), (mesh, sub, mat, m) =>
                {
                    string key = "@" + mat.name;
                    if (!parts.TryGetValue(key, out var list)) parts[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = m });
                });
            }

            public bool AddModel(string slug, float height, Matrix4x4 at)
            {
                return ModelKit.Batch(slug, height, at, (mesh, sub, mat, m) =>
                {
                    string key = "@" + mat.name;
                    if (!parts.TryGetValue(key, out var list)) parts[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = m });
                });
            }

            public void Build(Transform parent, string name, bool collide, System.Func<string, Color> tint = null)
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
                    var renderer = go.AddComponent<MeshRenderer>();
                    int hash = pair.Key.IndexOf('#');
                    string matName = hash >= 0 ? pair.Key.Substring(0, hash) : pair.Key;
                    if (matName.StartsWith("@"))
                    {
                        renderer.sharedMaterial = ModelKit.Baked(matName.Substring(1));
                        if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                        continue;
                    }
                    renderer.sharedMaterial = ArtLibrary.Get(matName);
                    if (hash >= 0 && ColorUtility.TryParseHtmlString(pair.Key.Substring(hash), out var own)) ArtLibrary.Tint(renderer, own);
                    else if (tint != null)
                    {
                        var c = tint(pair.Key);
                        if (c != Color.white) ArtLibrary.Tint(renderer, c);
                    }
                    if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }

                parts.Clear();
            }
        }
    }
}
