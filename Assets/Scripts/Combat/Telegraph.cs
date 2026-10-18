using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// A ground warning for an area attack. One rule for every shape - "when it is full, it hits" -
    /// drawn so the timing reads at a glance: a dim zone, a fill growing from the centre (or the near
    /// end, or the apex), a bright sweep line riding the fill's edge, a rim that pulses faster as the
    /// hit nears and blinks white in the last moments, and a flash on the ground when it lands.
    /// Shapes: circles (slams, novas, pools), lanes (charges, beams) and cones (cleaves).
    /// Colour says what it does: red hurts, blue freezes, green poisons, violet maddens.
    /// </summary>
    public class Telegraph : MonoBehaviour
    {
        private static readonly Dictionary<Object, Telegraph> BySource = new Dictionary<Object, Telegraph>();

        private enum Shape { Circle, Line, Cone }

        private Transform fill, sweep;
        private Renderer rim, zone;
        private Renderer[] rims;
        private float start, duration, radius;
        private Object source;
        private Shape shape;
        private float lineLength, lineWidth;
        private Color color;

        /// <summary>Friendly area effects (a Mage's Meteor) are blue, so players do not dodge their own team's spells.</summary>
        public static readonly Color Friendly = new Color(0.25f, 0.6f, 1f, 0.4f);
        public static readonly Color Harm = new Color(1f, 0.15f, 0.05f, 0.4f);
        public static readonly Color Frost = new Color(0.45f, 0.8f, 1f, 0.4f);
        public static readonly Color Poison = new Color(0.45f, 0.95f, 0.2f, 0.4f);
        public static readonly Color Madness = new Color(0.8f, 0.3f, 1f, 0.4f);

        private static Mesh disc, ring, strip, coneFan, coneEdge;
        private static Mesh Disc => disc ??= MeshKit.Cylinder(1f, 1f, 0.01f, 48);
        private static Mesh Ring => ring ??= MeshKit.Lathe("telegraphRing", new[] { new Vector2(0.95f, 0f), new Vector2(1f, 0.02f), new Vector2(0.95f, 0.03f) }, 48, false);
        private static Mesh Strip => strip ??= MeshKit.Box(new Vector3(1f, 0.01f, 1f));

        /// <param name="source">Owner for cancelling. Null = an independent telegraph (several can exist at once).</param>
        public static void Show(Vector3 center, float radius, float duration, Object source, Color? tint = null)
        {
            Cancel(source);
            var t = Make("Telegraph", center, Quaternion.identity, duration, source, tint ?? Harm);
            t.shape = Shape.Circle;
            t.radius = radius;
            t.zone = MeshKit.Part(t.transform, "Zone", Disc, Mat.Telegraph, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius)).GetComponent<Renderer>();
            // The rim, plus faint inner rings so a big circle still reads as a circle from inside it.
            t.rim = MeshKit.Part(t.transform, "Edge", Ring, Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius)).GetComponent<Renderer>();
            var inner = MeshKit.Part(t.transform, "Inner", Ring, Mat.Glow, Vector3.up * 0.002f, Vector3.zero, new Vector3(radius * 0.5f, 1f, radius * 0.5f)).GetComponent<Renderer>();
            ArtLibrary.SetGlow(inner, (Color)t.Rgb * 0.6f);
            t.fill = MeshKit.Part(t.transform, "Fill", Disc, Mat.Telegraph, Vector3.up * 0.01f, Vector3.zero, Vector3.zero).transform;
            t.sweep = MeshKit.Part(t.transform, "Sweep", Ring, Mat.Glow, Vector3.up * 0.015f, Vector3.zero, Vector3.zero).transform;
            t.Finish();
        }

        /// <summary>
        /// A lane warning for a charge or a beam: a strip from <paramref name="origin"/> along
        /// <paramref name="direction"/>, filling from the near end. Same rule: full = hit.
        /// </summary>
        public static void ShowLine(Vector3 origin, Vector3 direction, float length, float width, float duration, Object source, Color? tint = null)
        {
            Cancel(source);
            direction.y = 0f;
            direction.Normalize();
            var t = Make("LineTelegraph", origin, Quaternion.LookRotation(direction), duration, source, tint ?? Harm);
            t.shape = Shape.Line;
            t.lineLength = length;
            t.lineWidth = width;
            t.zone = MeshKit.Part(t.transform, "Zone", Strip, Mat.Telegraph, new Vector3(0f, 0f, length / 2f), Vector3.zero, new Vector3(width, 1f, length)).GetComponent<Renderer>();
            var l = MeshKit.Part(t.transform, "EdgeL", Strip, Mat.Glow, new Vector3(-width / 2f, 0.005f, length / 2f), Vector3.zero, new Vector3(0.09f, 1f, length)).GetComponent<Renderer>();
            var r = MeshKit.Part(t.transform, "EdgeR", Strip, Mat.Glow, new Vector3(width / 2f, 0.005f, length / 2f), Vector3.zero, new Vector3(0.09f, 1f, length)).GetComponent<Renderer>();
            var end = MeshKit.Part(t.transform, "EdgeEnd", Strip, Mat.Glow, new Vector3(0f, 0.005f, length), Vector3.zero, new Vector3(width, 1f, 0.09f)).GetComponent<Renderer>();
            t.rims = new[] { l, r, end };
            // Chevrons down the lane point the way it will come.
            for (float z = 1.2f; z < length - 0.5f; z += 2.2f)
                foreach (float s in new[] { -1f, 1f })
                {
                    var c = MeshKit.Part(t.transform, "Chevron", Strip, Mat.Glow, new Vector3(s * width * 0.18f, 0.004f, z), new Vector3(0f, s * -35f, 0f), new Vector3(0.06f, 1f, width * 0.42f)).GetComponent<Renderer>();
                    ArtLibrary.SetGlow(c, (Color)t.Rgb * 0.7f);
                }
            t.fill = MeshKit.Part(t.transform, "Fill", Strip, Mat.Telegraph, new Vector3(0f, 0.01f, 0f), Vector3.zero, Vector3.zero).transform;
            t.sweep = MeshKit.Part(t.transform, "Sweep", Strip, Mat.Glow, new Vector3(0f, 0.015f, 0f), Vector3.zero, new Vector3(width, 1f, 0.12f)).transform;
            t.Finish();
        }

        /// <summary>A cleave warning: a wedge of <paramref name="angle"/> degrees out to <paramref name="radius"/>, filling from the apex.</summary>
        public static void ShowCone(Vector3 apex, Vector3 direction, float angle, float radius, float duration, Object source, Color? tint = null)
        {
            Cancel(source);
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            var t = Make("ConeTelegraph", apex, Quaternion.LookRotation(direction.normalized), duration, source, tint ?? Harm);
            t.shape = Shape.Cone;
            t.radius = radius;
            var fan = Fan(angle);
            t.zone = MeshKit.Part(t.transform, "Zone", fan, Mat.Telegraph, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius)).GetComponent<Renderer>();
            t.rim = MeshKit.Part(t.transform, "Edge", Arc(angle), Mat.Glow, Vector3.up * 0.005f, Vector3.zero, new Vector3(radius, 1f, radius)).GetComponent<Renderer>();
            var sides = new List<Renderer> { t.rim };
            foreach (float s in new[] { -1f, 1f })
            {
                var side = MeshKit.Part(t.transform, "Side", Strip, Mat.Glow, Quaternion.Euler(0f, s * angle / 2f, 0f) * new Vector3(0f, 0.005f, radius / 2f),
                    new Vector3(0f, s * angle / 2f, 0f), new Vector3(0.08f, 1f, radius)).GetComponent<Renderer>();
                sides.Add(side);
            }
            t.rims = sides.ToArray();
            t.rim = null;
            t.fill = MeshKit.Part(t.transform, "Fill", fan, Mat.Telegraph, Vector3.up * 0.01f, Vector3.zero, Vector3.zero).transform;
            t.sweep = MeshKit.Part(t.transform, "Sweep", Arc(angle), Mat.Glow, Vector3.up * 0.015f, Vector3.zero, Vector3.zero).transform;
            t.Finish();
        }

        private Color Rgb => new Color(color.r, color.g, color.b);

        private static Telegraph Make(string name, Vector3 at, Quaternion rot, float duration, Object source, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.03f, rot);
            var t = go.AddComponent<Telegraph>();
            t.start = Time.time;
            t.duration = Mathf.Max(0.05f, duration);
            t.source = source;
            t.color = tint;
            return t;
        }

        private void Finish()
        {
            if (zone != null) ArtLibrary.Tint(zone, new Color(color.r, color.g, color.b, color.a * 0.55f));
            ArtLibrary.Tint(fill.GetComponent<Renderer>(), new Color(color.r, color.g, color.b, Mathf.Min(0.75f, color.a * 1.4f)));
            ArtLibrary.SetGlow(sweep.GetComponent<Renderer>(), Rgb * 3.2f);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (source != null) BySource[source] = this;
            Update();
        }

        public static void Cancel(Object source)
        {
            if (source != null && BySource.TryGetValue(source, out var existing) && existing != null) Destroy(existing.gameObject);
            if (source != null) BySource.Remove(source);
        }

        private void Update()
        {
            float k = Mathf.Clamp01((Time.time - start) / duration);
            // Fill eases in, so the last stretch visibly races to the edge.
            float f = Mathf.Lerp(k, k * k, 0.35f);
            switch (shape)
            {
                case Shape.Line:
                    fill.localScale = new Vector3(lineWidth, 1f, lineLength * f);
                    fill.localPosition = new Vector3(0f, 0.01f, lineLength * f / 2f);
                    sweep.localPosition = new Vector3(0f, 0.015f, lineLength * f);
                    break;
                default:
                    fill.localScale = new Vector3(radius * f, 1f, radius * f);
                    sweep.localScale = new Vector3(radius * f, 1f, radius * f);
                    break;
            }
            // The rim beats faster as the hit nears, then blinks white.
            float rate = Mathf.Lerp(3f, 16f, k);
            float pulse = 0.6f + 0.4f * Mathf.Sin((Time.time - start) * rate);
            var c = k > 0.82f && Mathf.Repeat((Time.time - start) * 14f, 1f) < 0.5f ? Color.Lerp(Rgb, Color.white, 0.75f) : Rgb;
            var glow = c * (2.2f + 1.6f * pulse);
            if (rim != null) ArtLibrary.SetGlow(rim, glow);
            if (rims != null) foreach (var r in rims) ArtLibrary.SetGlow(r, glow);

            if (k >= 1f)
            {
                if (source != null) BySource.Remove(source);
                Land();
                Destroy(gameObject);
                enabled = false;
            }
        }

        /// <summary>The moment it hits: a bright pulse on the ground in the shape that was warned.</summary>
        private void Land()
        {
            var p = transform.position;
            switch (shape)
            {
                case Shape.Circle:
                    Vfx.Ring(p, radius, Rgb, 0.3f);
                    Vfx.Flash(p + Vector3.up * 0.3f, Rgb, 3f, Mathf.Min(12f, radius * 1.8f), 0.18f);
                    break;
                case Shape.Line:
                    for (float z = lineLength * 0.25f; z <= lineLength; z += Mathf.Max(3f, lineLength * 0.5f)) Vfx.Flash(p + transform.forward * z + Vector3.up * 0.3f, Rgb, 2.5f, 4f, 0.15f);
                    break;
                case Shape.Cone:
                    Vfx.Flash(p + transform.forward * radius * 0.5f + Vector3.up * 0.3f, Rgb, 2.5f, radius * 1.4f, 0.15f);
                    break;
            }
        }

        // ---- Procedural wedge meshes (unit radius, opening along +Z) ----

        private static readonly Dictionary<int, Mesh> fans = new Dictionary<int, Mesh>(), arcs = new Dictionary<int, Mesh>();

        private static Mesh Fan(float angle)
        {
            int key = Mathf.RoundToInt(angle);
            if (fans.TryGetValue(key, out var m) && m != null) return m;
            int seg = Mathf.Max(6, key / 6);
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * (-angle / 2f + angle * i / seg);
                verts.Add(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
                if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            }
            m = new Mesh { name = "telegraphFan" + key };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            m.normals = normals;
            m.RecalculateBounds();
            fans[key] = m;
            return m;
        }

        private static Mesh Arc(float angle)
        {
            int key = Mathf.RoundToInt(angle);
            if (arcs.TryGetValue(key, out var m) && m != null) return m;
            int seg = Mathf.Max(6, key / 6);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * (-angle / 2f + angle * i / seg);
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts.Add(dir * 0.95f);
                verts.Add(dir);
                if (i > 0)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b - 2, b - 1, b, b - 1, b + 1, b });
                }
            }
            m = new Mesh { name = "telegraphArc" + key };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            m.normals = normals;
            m.RecalculateBounds();
            arcs[key] = m;
            return m;
        }
    }
}
