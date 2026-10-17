using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Room tone, synthesised like everything else: the camp's night (gusting wind over the walls,
    /// a few crickets) and the dungeon's breath (a deep rumble and a cold draught that swells and
    /// fades). Each is a seamless loop: the tail is cross-faded into the head.
    /// </summary>
    public static class AmbienceSynth
    {
        private const int Rate = 22050;
        private static uint rng = 0x9E3779B9u;

        private static float N()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
        }

        public static AudioClip Camp()
        {
            const float seconds = 16f;
            var d = new float[(int)(seconds * Rate)];
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                // Wind: soft low-passed noise, gusting on a few slow, unrelated cycles.
                lp1 += 0.04f * (N() - lp1);
                lp2 += 0.08f * (lp1 - lp2);
                float gust = 0.35f + 0.3f * Mathf.Sin(t * 0.39f) + 0.2f * Mathf.Sin(t * 0.83f + 1.3f) + 0.15f * Mathf.Sin(t * 1.71f + 0.4f);
                float wind = lp2 * 2.6f * Mathf.Max(0.1f, gust);
                // Crickets: three of them, each chirping bursts of three pulses at its own rate.
                float crick = 0f;
                for (int c = 0; c < 3; c++)
                {
                    float rate = 0.9f + c * 0.23f, hz = 4300f + c * 520f;
                    float cyc = (t * rate + c * 0.37f) % 1f;
                    if (cyc < 0.11f)
                    {
                        float pulse = Mathf.Max(0f, Mathf.Sin(cyc / 0.11f * Mathf.PI * 3f));
                        crick += Mathf.Sin(t * hz * Mathf.PI * 2f) * pulse * (0.05f - c * 0.012f);
                    }
                }
                d[i] = wind + crick;
            }
            return Loop("CampAmbience", d);
        }

        public static AudioClip Deep()
        {
            const float seconds = 18f;
            var d = new float[(int)(seconds * Rate)];
            float r1 = 0f, r2 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                // The rumble: very low, slow, always there.
                r1 += 0.008f * (N() - r1);
                r2 += 0.012f * (r1 - r2);
                // The draught: a hollow band of noise swelling as air moves through the corridors.
                w1 += 0.06f * (N() - w1);
                w2 += 0.06f * (w1 - w2);
                w3 = w1 - w2; // crude band-pass
                float swell = 0.25f + 0.25f * Mathf.Sin(t * 0.31f) + 0.2f * Mathf.Sin(t * 0.73f + 2f);
                d[i] = r2 * 9f + w3 * 1.6f * Mathf.Max(0.05f, swell);
            }
            return Loop("DeepAmbience", d);
        }

        /// <summary>Cross-fade the last second into the first, trim it off, normalise, make a clip.</summary>
        private static AudioClip Loop(string name, float[] d)
        {
            int fade = Rate;
            int len = d.Length - fade;
            var o = new float[len];
            for (int i = 0; i < len; i++) o[i] = d[i];
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                o[i] = d[i] * k + d[len + i] * (1f - k);
            }
            float peak = 0.0001f;
            foreach (var v in o) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < len; i++) o[i] = o[i] / peak * 0.8f;
            var clip = AudioClip.Create(name, len, 1, Rate, false);
            clip.SetData(o, 0);
            return clip;
        }
    }
}
