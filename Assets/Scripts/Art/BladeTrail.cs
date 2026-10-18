using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A first-person blade trail: while armed, it samples the blade's tip and a point down its edge
    /// every frame (in the view model's space, so it stays with the hands), and builds a ribbon
    /// between the recent samples - full width at the blade, tapering and thinning toward the tail -
    /// plus a bright hairline along the path of the tip. It reads as the blade's swept arc, not a
    /// shape pasted in front of the camera.
    /// </summary>
    public class BladeTrail
    {
        private readonly Transform space, tip, edge;
        private readonly Mesh ribbon, line;
        private readonly Renderer ribbonR, lineR;
        private readonly Color color;
        private readonly List<(Vector3 tip, Vector3 edge, float at)> samples = new List<(Vector3, Vector3, float)>();
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<int> tris = new List<int>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private float armedUntil = -1f;
        private const float Life = 0.085f;

        public BladeTrail(Transform space, Transform tip, Transform edge, Color color)
        {
            this.space = space;
            this.tip = tip;
            this.edge = edge;
            this.color = color;
            ribbon = new Mesh { name = "bladeTrail" };
            line = new Mesh { name = "bladeTrailEdge" };
            ribbon.MarkDynamic();
            line.MarkDynamic();
            ribbonR = Make("BladeTrail", ribbon, Mat.Smoke);
            lineR = Make("BladeTrailEdge", line, Mat.Glow);
        }

        private Renderer Make(string name, Mesh mesh, string mat)
        {
            var go = new GameObject(name);
            go.layer = space.gameObject.layer;
            go.transform.SetParent(space, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ArtLibrary.Get(mat);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            return r;
        }

        /// <summary>Start drawing the arc for <paramref name="seconds"/> (the cut itself).</summary>
        public void Arm(float seconds)
        {
            armedUntil = Time.time + seconds;
            samples.Clear();
        }

        public void Update()
        {
            float now = Time.time;
            if (now < armedUntil)
                samples.Add((space.InverseTransformPoint(tip.position), space.InverseTransformPoint(edge.position), now));
            samples.RemoveAll(s => now - s.at > Life);
            bool show = samples.Count >= 2;
            ribbonR.enabled = lineR.enabled = show;
            if (!show) return;

            // The ribbon: newest sample full blade width, the oldest a sliver near the tip.
            verts.Clear();
            tris.Clear();
            int n = samples.Count;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);           // 0 oldest .. 1 newest
                var s = samples[i];
                verts.Add(s.tip);
                verts.Add(Vector3.Lerp(s.tip, s.edge, Mathf.Lerp(0.08f, 0.75f, k * k * k)));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                tris.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 }); // both sides
            }
            ribbon.Clear();
            ribbon.SetVertices(verts);
            ribbon.SetTriangles(tris, 0);
            ribbon.RecalculateNormals();
            ribbon.RecalculateBounds();

            // The hairline: a thin strip just inside the tip's path.
            verts.Clear();
            tris.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                var inward = (s.edge - s.tip).normalized;
                verts.Add(s.tip);
                verts.Add(s.tip + inward * 0.007f);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3, a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
            line.Clear();
            line.SetVertices(verts);
            line.SetTriangles(tris, 0);
            line.RecalculateNormals();
            line.RecalculateBounds();

            // Fade with the age of the newest sample: bright while cutting, gone a moment after.
            float age = now - samples[n - 1].at;
            float a2 = Mathf.Clamp01(1f - age / Life);
            ribbonR.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 0.2f * a2));
            ribbonR.SetPropertyBlock(block);
            ArtLibrary.SetGlow(lineR, color * (0.75f * a2));
        }
    }
}
