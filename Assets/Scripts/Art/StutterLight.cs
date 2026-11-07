using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>A broken lantern: steady most of the time, then it stutters and dies for a beat (and its pane with it).</summary>
    public class StutterLight : MonoBehaviour
    {
        public Renderer Pane;
        private Light l;
        private float baseIntensity, nextFit, fitEnds;

        private void Awake()
        {
            l = GetComponent<Light>();
            baseIntensity = l != null ? l.intensity : 1f;
            nextFit = Time.time + Random.Range(2f, 7f);
        }

        private void Update()
        {
            if (l == null) return;
            float t = Time.time;
            if (t >= nextFit) { fitEnds = t + Random.Range(0.4f, 1.3f); nextFit = fitEnds + Random.Range(3f, 9f); }
            bool on = t >= fitEnds || Mathf.PerlinNoise(t * 18f, 3.7f) > 0.55f;
            l.intensity = on ? baseIntensity * (0.9f + Mathf.PerlinNoise(t * 4f, 1.3f) * 0.2f) : baseIntensity * 0.05f;
            if (Pane != null && Pane.enabled != on) Pane.enabled = on;
        }
    }
}
