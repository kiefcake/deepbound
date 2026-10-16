using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// What leaves a broken skeleton: a small light in the colour of its eyes that spirals up out
    /// of the bones, sheds a few motes, and goes out. Local, short-lived.
    /// </summary>
    public class SoulWisp : MonoBehaviour
    {
        private const float Seconds = 1.5f;
        private Vector3 origin;
        private Color color;
        private float start, nextMote, spin;
        private Renderer core;

        public static void Release(Vector3 at, Color color)
        {
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - at).sqrMagnitude > 35f * 35f) return;
            var go = MeshKit.Part(null, "SoulWisp", MeshKit.Ball(0.07f, 8, 6), Mat.Glow, at);
            var w = go.AddComponent<SoulWisp>();
            w.origin = at;
            w.color = color;
            w.core = go.GetComponent<Renderer>();
            w.core.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            w.spin = Random.Range(0f, 360f);
        }

        private void Start() => start = Time.time;

        private void Update()
        {
            float t = (Time.time - start) / Seconds;
            if (t >= 1f) { Destroy(gameObject); return; }
            // Rises, slowing; circles wider as it goes.
            float up = 2.6f * (1f - (1f - t) * (1f - t));
            float a = spin * Mathf.Deg2Rad + t * 9f;
            float r = 0.08f + t * 0.3f;
            transform.position = origin + new Vector3(Mathf.Cos(a) * r, up, Mathf.Sin(a) * r);
            float fade = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
            transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, t);
            ArtLibrary.SetGlow(core, color * (1.6f * fade));
            if (Time.time >= nextMote)
            {
                nextMote = Time.time + 0.07f;
                Vfx.Embers(transform.position, 1, color * fade, 0.4f, 0.03f, 0.5f);
            }
        }
    }
}
