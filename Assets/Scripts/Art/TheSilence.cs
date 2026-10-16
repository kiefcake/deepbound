using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Abyss's darkness: for a few seconds the light fails. Fog thickens, the ambient light
    /// drops, and every light in the level gutters low; then it all comes back. Local only, run
    /// on every peer at the same time (the floor gimmick and the Unsung's Silence both use it).
    /// </summary>
    public class TheSilence : MonoBehaviour
    {
        private static TheSilence current;
        private float start, length;
        private float fog0;
        private Color sky0, eq0, ground0;
        private Light[] lights;
        private float[] intensity0;

        public static bool Active => current != null;

        public static void Begin(float seconds)
        {
            if (current != null) { current.length = Mathf.Max(current.length, Time.time - current.start + seconds); return; }
            var go = new GameObject("TheSilence");
            current = go.AddComponent<TheSilence>();
            current.start = Time.time;
            current.length = seconds;
            current.fog0 = RenderSettings.fogDensity;
            current.sky0 = RenderSettings.ambientSkyColor;
            current.eq0 = RenderSettings.ambientEquatorColor;
            current.ground0 = RenderSettings.ambientGroundColor;
            current.lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            current.intensity0 = new float[current.lights.Length];
            for (int i = 0; i < current.lights.Length; i++) current.intensity0[i] = current.lights[i] != null ? current.lights[i].intensity : 0f;
            Sfx.Play2D(Sound.Whisper, 0.8f);
        }

        private void Update()
        {
            float t = Time.time - start;
            // Darkens over a second, holds, recovers over the last second and a half.
            float k = Mathf.Clamp01(Mathf.Min(t / 1f, (length - t) / 1.5f));
            RenderSettings.fogDensity = Mathf.Lerp(fog0, fog0 * 2.6f, k);
            RenderSettings.ambientSkyColor = Color.Lerp(sky0, sky0 * 0.3f, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(eq0, eq0 * 0.3f, k);
            RenderSettings.ambientGroundColor = Color.Lerp(ground0, ground0 * 0.3f, k);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = Mathf.Lerp(intensity0[i], intensity0[i] * 0.25f, k) * (1f + (k > 0.5f ? Mathf.Sin(Time.time * 23f + i) * 0.08f : 0f));
            if (t >= length) End();
        }

        /// <summary>Restore everything now (the floor is being rebuilt).</summary>
        public static void Cancel()
        {
            if (current != null) current.End();
        }

        private void End()
        {
            RenderSettings.fogDensity = fog0;
            RenderSettings.ambientSkyColor = sky0;
            RenderSettings.ambientEquatorColor = eq0;
            RenderSettings.ambientGroundColor = ground0;
            for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].intensity = intensity0[i];
            current = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
        }
    }
}
