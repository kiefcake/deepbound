using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public class LightBudget : MonoBehaviour
    {
        public const float CullDistance = 42f;
        public const int MaxShadowed = 4;

        public const float FadeSeconds = 0.5f, Keep = 0.8f;

        private class Entry { public Light Light; public LightShadows Shadows; public float BaseStrength; public float Strength; public bool Wanted; }
        private readonly List<Entry> lights = new List<Entry>();
        private readonly List<Entry> shadowCandidates = new List<Entry>();
        private float next;

        private void Start()
        {
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.type != LightType.Directional)
                    lights.Add(new Entry { Light = l, Shadows = l.shadows, BaseStrength = l.shadowStrength, Strength = 0f });
        }

        private void Update()
        {
            FadeShadows();
            if (Time.time < next) return;
            next = Time.time + 0.25f;
            var cam = ViewCamera.Current;
            if (cam == null) return;
            var eye = cam.transform.position;
            shadowCandidates.Clear();
            foreach (var e in lights)
            {
                if (e.Light == null) continue;
                float d = Vector3.Distance(eye, e.Light.transform.position) - e.Light.range * 0.5f;
                bool on = d < CullDistance;
                if (e.Light.enabled != on) e.Light.enabled = on;
                e.Wanted = false;
                if (on && e.Shadows != LightShadows.None) shadowCandidates.Add(e);
                else if (!on && e.Light.shadows != LightShadows.None) { e.Light.shadows = LightShadows.None; e.Strength = 0f; }
            }

            float Rank(Entry e) => (e.Light.transform.position - eye).sqrMagnitude * (e.Strength > 0f ? Keep * Keep : 1f);
            shadowCandidates.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            for (int k = 0; k < shadowCandidates.Count && k < MaxShadowed; k++) shadowCandidates[k].Wanted = true;
        }

        private void FadeShadows()
        {
            float step = Time.deltaTime / FadeSeconds;
            foreach (var e in lights)
            {
                if (e.Light == null || !e.Light.enabled) continue;
                if (e.Wanted && e.Light.shadows == LightShadows.None) { e.Light.shadows = e.Shadows; e.Strength = 0f; }
                if (e.Light.shadows == LightShadows.None) continue;
                e.Strength = Mathf.MoveTowards(e.Strength, e.Wanted ? 1f : 0f, step);
                e.Light.shadowStrength = e.BaseStrength * e.Strength;
                if (!e.Wanted && e.Strength <= 0f) e.Light.shadows = LightShadows.None;
            }
        }
    }
}
