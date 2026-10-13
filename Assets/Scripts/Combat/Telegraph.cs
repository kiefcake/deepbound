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
            BySource[source] = t;
        }

        public static void Cancel(Object source)
        {
            if (source != null && BySource.TryGetValue(source, out var existing) && existing != null) Destroy(existing.gameObject);
            if (source != null) BySource.Remove(source);
        }

        private void Update()
        {
            float k = Mathf.Clamp01((Time.time - start) / duration);
            fill.localScale = new Vector3(radius * k, 1f, radius * k);
            if (k >= 1f)
            {
                if (source != null) BySource.Remove(source);
                Destroy(gameObject, 0.15f);
                enabled = false;
            }
        }
    }
}
