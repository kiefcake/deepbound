using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The soundtrack, composed in code. Three layers in D minor at one tempo and one
    /// length, so they can all play from the same start and be cross-faded freely:
    ///
    /// <list type="bullet">
    /// <item><b>Explore</b> — a low drone with a slow minor chord pad and a sparse plucked
    ///   melody. Unsettling, never loud: the dungeon's room tone.</item>
    /// <item><b>Combat</b> — a frame-drum pulse and shaker over the same harmony. Fades in
    ///   when enemies have noticed you.</item>
    /// <item><b>Boss</b> — taiko hits and a low horn ostinato. Fades in for boss fights.</item>
    /// </list>
    ///
    /// Plucks are Karplus-Strong (a noise burst in a feedback delay line), which gives a
    /// convincing harp-or-lute string for a few lines of code.
    /// </summary>
    public static class MusicSynth
    {
        public const int Rate = 22050;
        public const float Bpm = 72f;
        public const int Bars = 8;
        public static float BarSeconds => 4f * 60f / Bpm;
        public static float LoopSeconds => Bars * BarSeconds;

        // D minor progression, one chord per bar: Dm  Bb  F  A  Dm  Gm  Bb  A
        private static readonly float[][] Chords =
        {
            new[] { 146.83f, 174.61f, 220.00f }, new[] { 116.54f, 146.83f, 174.61f },
            new[] { 174.61f, 220.00f, 261.63f }, new[] { 110.00f, 138.59f, 164.81f },
            new[] { 146.83f, 174.61f, 220.00f }, new[] { 98.00f, 116.54f, 146.83f },
            new[] { 116.54f, 146.83f, 174.61f }, new[] { 110.00f, 138.59f, 164.81f },
        };

        private static int Samples => Mathf.RoundToInt(LoopSeconds * Rate);
        private static float Sec(int i) => (float)i / Rate;

        public static AudioClip Explore()
        {
            var d = new float[Samples];
            float bar = BarSeconds;

            // Drone: D2 and A2, slowly beating against a detuned copy.
            for (int i = 0; i < d.Length; i++)
            {
                float t = Sec(i);
                d[i] += (Mathf.Sin(2f * Mathf.PI * 73.42f * t) + Mathf.Sin(2f * Mathf.PI * 73.7f * t)) * 0.12f;
                d[i] += Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.05f * (0.6f + 0.4f * Mathf.Sin(t * 0.4f));
            }

            // Pad: each bar's chord, swelling in and out.
            for (int b = 0; b < Bars; b++)
            {
                int start = Mathf.RoundToInt(b * bar * Rate), len = Mathf.RoundToInt(bar * Rate);
                foreach (var f in Chords[b])
                {
                    for (int i = 0; i < len && start + i < d.Length; i++)
                    {
                        float t = Sec(i), k = (float)i / len;
                        float env = Mathf.Sin(k * Mathf.PI);
                        float tone = Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2.003f * t);
                        d[start + i] += tone * env * 0.035f;
                    }
                }
            }

            // Melody: a sparse plucked line on chord tones, an octave up.
            var rng = new System.Random(7);
            for (int b = 0; b < Bars; b++)
            {
                for (int beat = 0; beat < 8; beat++)
                {
                    if (rng.NextDouble() < 0.55) continue;
                    var chord = Chords[b];
                    float f = chord[rng.Next(chord.Length)] * (rng.NextDouble() < 0.5 ? 2f : 4f);
                    int at = Mathf.RoundToInt((b * bar + beat * bar / 8f) * Rate);
                    Pluck(d, at, f, 0.16f, 2.2f);
                }
            }

            Echo(d, 0.42f, 0.35f);
            return Make("Explore", d, 0.7f);
        }

        public static AudioClip Combat()
        {
            var d = new float[Samples];
            float beat = 60f / Bpm, eighth = beat / 2f;
            var rng = new System.Random(11);

            for (int b = 0; b < Bars * 8; b++)
            {
                int at = Mathf.RoundToInt(b * eighth * Rate);
                bool downbeat = b % 8 == 0, backbeat = b % 4 == 2;
                if (b % 2 == 0) Drum(d, at, downbeat ? 70f : 95f, downbeat ? 0.55f : 0.32f, 0.35f);
                if (backbeat) Drum(d, at, 180f, 0.28f, 0.18f);
                Shaker(d, at, b % 2 == 1 ? 0.09f : 0.05f, rng);
            }

            // A low pulsing bass on each chord root, in eighths.
            for (int bar = 0; bar < Bars; bar++)
            {
                float root = Chords[bar][0] / 2f;
                for (int e = 0; e < 8; e++)
                {
                    int at = Mathf.RoundToInt((bar * BarSeconds + e * eighth) * Rate);
                    Pluck(d, at, root, 0.14f, 0.35f);
                }
            }
            return Make("Combat", d, 0.8f);
        }

        public static AudioClip Boss()
        {
            var d = new float[Samples];
            float beat = 60f / Bpm;

            // Taiko: big hits on 1 and the "and" of 3, a roll into every fourth bar.
            for (int bar = 0; bar < Bars; bar++)
            {
                float t0 = bar * BarSeconds;
                Drum(d, Mathf.RoundToInt(t0 * Rate), 55f, 0.8f, 0.6f);
                Drum(d, Mathf.RoundToInt((t0 + beat * 2.5f) * Rate), 60f, 0.6f, 0.5f);
                if (bar % 4 == 3)
                {
                    for (int r = 0; r < 6; r++) Drum(d, Mathf.RoundToInt((t0 + beat * 3f + r * beat / 6f) * Rate), 80f + r * 6f, 0.25f + r * 0.05f, 0.2f);
                }
            }

            // Horn ostinato: a rising minor figure on the root, buzzy and low.
            float[] figure = { 1f, 1.189f, 1.335f, 1.189f };
            for (int bar = 0; bar < Bars; bar++)
            {
                float root = Chords[bar][0];
                for (int n = 0; n < 4; n++)
                {
                    int at = Mathf.RoundToInt((bar * BarSeconds + n * beat) * Rate);
                    Horn(d, at, root * figure[n] / 2f, beat * 0.9f, 0.09f);
                }
            }
            return Make("Boss", d, 0.75f);
        }

        // ------------------------------------------------------------------
        // Instruments
        // ------------------------------------------------------------------

        private static void Pluck(float[] d, int start, float freq, float gain, float seconds)
        {
            int period = Mathf.Max(2, Mathf.RoundToInt(Rate / freq));
            var line = new float[period];
            var rng = new System.Random(start);
            for (int i = 0; i < period; i++) line[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

            int len = Mathf.RoundToInt(seconds * Rate);
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                int j = (start + i) % d.Length;
                float v = line[idx];
                int next = (idx + 1) % period;
                line[idx] = 0.497f * (line[idx] + line[next]);
                idx = next;
                d[j] += v * gain * (1f - (float)i / len);
            }
        }

        private static void Drum(float[] d, int start, float freq, float gain, float seconds)
        {
            int len = Mathf.RoundToInt(seconds * Rate);
            float phase = 0f;
            var rng = new System.Random(start + 3);
            for (int i = 0; i < len; i++)
            {
                int j = (start + i) % d.Length;
                float k = (float)i / len;
                float f = freq * (1f + 1.5f * Mathf.Exp(-k * 25f));
                phase += 2f * Mathf.PI * f / Rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-k * 6f);
                float skin = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-k * 40f) * 0.3f;
                d[j] += (body + skin) * gain;
            }
        }

        private static void Shaker(float[] d, int start, float gain, System.Random rng)
        {
            int len = Rate / 20;
            float hp = 0f, prev = 0f;
            for (int i = 0; i < len; i++)
            {
                int j = (start + i) % d.Length;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                hp = 0.8f * (hp + n - prev);
                prev = n;
                d[j] += hp * gain * (1f - (float)i / len);
            }
        }

        private static void Horn(float[] d, int start, float freq, float seconds, float gain)
        {
            int len = Mathf.RoundToInt(seconds * Rate);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < len; i++)
            {
                int j = (start + i) % d.Length;
                float k = (float)i / len;
                phase += freq / Rate;
                float saw = (phase % 1f) * 2f - 1f;
                lp += 0.08f * (saw - lp); // soften the buzz
                float env = Mathf.Clamp01(k * 12f) * Mathf.Clamp01((1f - k) * 5f);
                d[j] += lp * env * gain;
            }
        }

        /// <summary>A feedback delay that wraps around the loop, so the tail continues seamlessly.</summary>
        private static void Echo(float[] d, float seconds, float feedback)
        {
            int delay = Mathf.RoundToInt(seconds * Rate);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < d.Length; i++) d[(i + delay) % d.Length] += d[i] * feedback * (pass == 0 ? 1f : 0.5f);
            }
        }

        private static AudioClip Make(string name, float[] d, float peak)
        {
            float max = 0.0001f;
            foreach (var s in d) max = Mathf.Max(max, Mathf.Abs(s));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / max;
            var clip = AudioClip.Create("Music_" + name, d.Length, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
