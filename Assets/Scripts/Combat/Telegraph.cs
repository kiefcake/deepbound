using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// A ground warning for an area attack: a faint red disc showing the danger zone, with
    /// a brighter disc filling it from the centre. When the fill reaches the edge, the
    /// attack lands. Players learn one rule — "full circle means hit" — and it holds for
    /// every area attack in the game.
    /// </summary>
    public class Telegraph : MonoBehaviour
    {
        private static readonly Dictionary<Object, Telegraph> BySource = new Dictionary<Object, Telegraph>();

        private Transform fill;
        private float start, duration, radius;
        private Object source;
        private bool line;
        private float lineLength, lineWidth;

        /// <param name="source">Owner for cancelling. Null = an independent telegraph (several can exist at once).</param>
        public static void Show(Vector3 center, float radius, float duration, Object source)
        {
            Cancel(source);

            var go = new GameObject("Telegraph");
            go.transform.position = center + Vector3.up * 0.03f;
            var t = go.AddComponent<Telegraph>();
            t.start = Time.time;
            t.duration = duration;
            t.radius = radius;
            t.source = source;

            var disc = MeshKit.Cylinder(1f, 1f, 0.01f, 40);
            MeshKit.Part(go.transform, "Zone", disc, Mat.Telegraph, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius));
            var ring = MeshKit.Part(go.transform, "Edge", MeshKit.Lathe("telegraphRing", new[]
            {
                new Vector2(0.97f, 0f), new Vector2(1f, 0.02f), new Vector2(0.97f, 0.03f),
            }, 40, false), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius));
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.15f, 0.05f) * 3f);

            t.fill = MeshKit.Part(go.transform, "Fill", disc, Mat.Telegraph, Vector3.up * 0.01f, Vector3.zero, Vector3.zero).transform;

            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (source != null) BySource[source] = t;
        }

        /// <summary>
        /// A lane warning for a charge or a beam: a strip from <paramref name="origin"/> along
        /// <paramref name="direction"/>, filling from the near end. Same rule: full = hit.
        /// </summary>
        public static void ShowLine(Vector3 origin, Vector3 direction, float length, float width, float duration, Object source)
        {
            Cancel(source);
            direction.y = 0f;
            direction.Normalize();

            var go = new GameObject("LineTelegraph");
            go.transform.SetPositionAndRotation(origin + Vector3.up * 0.03f, Quaternion.LookRotation(direction));
            var t = go.AddComponent<Telegraph>();
            t.start = Time.time;
            t.duration = duration;
            t.source = source;
            t.line = true;
            t.lineLength = length;
            t.lineWidth = width;

            var strip = MeshKit.Box(new Vector3(1f, 0.01f, 1f));
            MeshKit.Part(go.transform, "Zone", strip, Mat.Telegraph, new Vector3(0f, 0f, length / 2f), Vector3.zero, new Vector3(width, 1f, length));
            var edgeL = MeshKit.Part(go.transform, "EdgeL", strip, Mat.Glow, new Vector3(-width / 2f, 0.005f, length / 2f), Vector3.zero, new Vector3(0.08f, 1f, length));
            var edgeR = MeshKit.Part(go.transform, "EdgeR", strip, Mat.Glow, new Vector3(width / 2f, 0.005f, length / 2f), Vector3.zero, new Vector3(0.08f, 1f, length));
            ArtLibrary.SetGlow(edgeL.GetComponent<Renderer>(), new Color(1f, 0.15f, 0.05f) * 3f);
            ArtLibrary.SetGlow(edgeR.GetComponent<Renderer>(), new Color(1f, 0.15f, 0.05f) * 3f);
            t.fill = MeshKit.Part(go.transform, "Fill", strip, Mat.Telegraph, new Vector3(0f, 0.01f, 0f), Vector3.zero, Vector3.zero).transform;

            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (source != null) BySource[source] = t;
        }

        public static void Cancel(Object source)
        {
            if (source != null && BySource.TryGetValue(source, out var existing) && existing != null) Destroy(existing.gameObject);
            if (source != null) BySource.Remove(source);
        }

        private void Update()
        {
            float k = Mathf.Clamp01((Time.time - start) / duration);
            if (line)
            {
                fill.localScale = new Vector3(lineWidth, 1f, lineLength * k);
                fill.localPosition = new Vector3(0f, 0.01f, lineLength * k / 2f);
            }
            else
            {
                fill.localScale = new Vector3(radius * k, 1f, radius * k);
            }
            if (k >= 1f)
            {
                if (source != null) BySource.Remove(source);
                Destroy(gameObject, 0.15f);
                enabled = false;
            }
        }
    }
}
