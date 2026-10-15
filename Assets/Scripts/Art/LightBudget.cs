using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A performance budget for a level's lights. A floor has well over a hundred torches,
    /// braziers and glows; only the ones near the camera matter. Every few frames this
    /// turns off lights beyond <see cref="CullDistance"/> and lets only the
    /// <see cref="MaxShadowed"/> nearest shadow-casting lights actually cast shadows
    /// (shadowed point lights are by far the most expensive thing on screen).
    /// </summary>
    public class LightBudget : MonoBehaviour
    {
        public const float CullDistance = 42f;
        public const int MaxShadowed = 4;

        private struct Entry { public Light Light; public LightShadows Shadows; }
        private readonly List<Entry> lights = new List<Entry>();
        private readonly List<int> shadowCandidates = new List<int>();
        private float next;

        private void Start()
        {
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.type != LightType.Directional) lights.Add(new Entry { Light = l, Shadows = l.shadows });
        }

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.25f;
            var cam = ViewCamera.Current;
            if (cam == null) return;
            var eye = cam.transform.position;
            shadowCandidates.Clear();
            for (int i = 0; i < lights.Count; i++)
            {
                var e = lights[i];
                if (e.Light == null) continue;
                float d = Vector3.Distance(eye, e.Light.transform.position) - e.Light.range * 0.5f;
                bool on = d < CullDistance;
                if (e.Light.enabled != on) e.Light.enabled = on;
                if (on && e.Shadows != LightShadows.None) shadowCandidates.Add(i);
                else if (e.Light.shadows != LightShadows.None) e.Light.shadows = LightShadows.None;
            }
            // The nearest few keep their shadows.
            shadowCandidates.Sort((a, b) =>
                (lights[a].Light.transform.position - eye).sqrMagnitude.CompareTo((lights[b].Light.transform.position - eye).sqrMagnitude));
            for (int k = 0; k < shadowCandidates.Count; k++)
            {
                var e = lights[shadowCandidates[k]];
                var want = k < MaxShadowed ? e.Shadows : LightShadows.None;
                if (e.Light.shadows != want) e.Light.shadows = want;
            }
        }
    }
}
