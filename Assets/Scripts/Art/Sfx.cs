using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public enum Sound { Swing, Hit, Block, Bash, Rattle, BoneBreak, Slam, WarCry, Hurt, Revive, Summon, Coins, Potion, Purchase, Bow, Fire, Frost, Shatter, Whisper, Dodge, Relic, Step, Splash, Meow, Purr, Drink, SlotSpin, Card, Caw, Squeak, Heartbeat, KillTick, Creak, Toll, Cleared, Pipe, Graze }

    /// <summary>
    /// Every sound in the game, synthesised at startup. Like the art, nothing is
    /// downloaded.
    ///
    /// <para>
    /// Each sound is a short recipe — filtered noise, decaying partials, pitch sweeps —
    /// rendered once into an <see cref="AudioClip"/> and cached. Playback picks a slight
    /// random pitch so ten identical swings do not sound like a machine gun.
    /// </para>
    /// </summary>
    public static class Sfx
    {
        private const int Rate = 44100;
        private static readonly Dictionary<Sound, AudioClip> Clips = new Dictionary<Sound, AudioClip>();
        private static System.Random noise = new System.Random(1234);

        public static void Play(Sound sound, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var clip = Get(sound);
            var go = new GameObject("Sfx " + sound);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch * Random.Range(0.92f, 1.08f);
            src.spatialBlend = 1f;
            src.minDistance = 2f;
            src.maxDistance = 35f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length / src.pitch + 0.1f);
        }

        /// <summary>Non-spatial, for the local player's own actions and UI.</summary>
        private static AudioListener listener;

        public static void Play2D(Sound sound, float volume = 1f)
        {
            if (listener == null || !listener.isActiveAndEnabled) listener = Object.FindFirstObjectByType<AudioListener>();
            Play(sound, listener != null ? listener.transform.position : Vector3.zero, volume);
        }

        private static AudioClip Get(Sound sound)
        {
            if (Clips.TryGetValue(sound, out var clip)) return clip;

            float[] data;
            switch (sound)
            {
                case Sound.Swing: data = Whoosh(0.28f, 900f, 2600f); break;
                case Sound.Hit: data = Mix(Thump(0.18f, 140f, 60f), Burst(0.08f, 0.5f)); break;
                case Sound.Block: data = Mix(Clang(0.5f, new[] { 620f, 1180f, 1710f, 2690f }), Burst(0.03f, 0.6f)); break;
                case Sound.Bash: data = Mix(Thump(0.25f, 110f, 45f), Clang(0.25f, new[] { 410f, 830f }), Burst(0.06f, 0.4f)); break;
                case Sound.Rattle: data = Rattle(0.35f, 14); break;
                case Sound.BoneBreak: data = Mix(Rattle(0.5f, 24), Thump(0.2f, 90f, 40f)); break;
                case Sound.Slam: data = Mix(Thump(0.9f, 70f, 28f), Burst(0.4f, 0.5f), Rattle(0.6f, 10)); break;
                case Sound.WarCry: data = Horn(0.9f, 150f); break;
                case Sound.Hurt: data = Mix(Thump(0.15f, 180f, 90f), Burst(0.05f, 0.3f)); break;
                case Sound.Revive: data = Chime(0.8f, new[] { 523f, 659f, 784f }); break;
                case Sound.Summon: data = Mix(Horn(1.2f, 55f), Whoosh(1.2f, 200f, 900f)); break;
                case Sound.Coins: data = Mix(Clang(0.25f, new[] { 2600f, 3900f }), Delay(Clang(0.25f, new[] { 3100f, 4400f }), 0.06f), Delay(Clang(0.2f, new[] { 2900f }), 0.11f)); break;
                case Sound.Potion: data = Mix(Chime(0.5f, new[] { 784f, 1046f }), Whoosh(0.3f, 300f, 1200f)); break;
                case Sound.Fire: data = Mix(Whoosh(0.32f, 350f, 1700f), Burst(0.18f, 0.35f), Thump(0.2f, 120f, 70f)); break;
                case Sound.Frost: data = Mix(Chime(0.35f, new[] { 1568f, 2093f }), Whoosh(0.25f, 2200f, 5200f)); break;
                case Sound.Shatter: data = Mix(Clang(0.55f, new[] { 2200f, 3300f, 4700f, 6100f }), Rattle(0.35f, 22), Burst(0.05f, 0.8f)); break;
                case Sound.Whisper: data = Mix(Whoosh(0.45f, 600f, 2400f), Horn(0.45f, 90f)); break;
                case Sound.Dodge: data = Whoosh(0.22f, 500f, 2200f); break;
                case Sound.Relic: data = Mix(Chime(1.2f, new[] { 523f, 659f, 784f, 1046f }), Clang(0.8f, new[] { 1318f, 1975f })); break;
                case Sound.Step: data = Mix(Thump(0.09f, 110f, 70f), Burst(0.05f, 0.25f)); break;
                case Sound.Splash: data = Mix(Whoosh(0.25f, 700f, 2600f), Burst(0.12f, 0.35f), Thump(0.1f, 160f, 90f)); break;
                case Sound.Bow: data = Mix(Thump(0.12f, 320f, 180f), Whoosh(0.22f, 1500f, 3200f)); break;
                case Sound.Purchase: data = Mix(Chime(0.5f, new[] { 659f, 988f }), Clang(0.3f, new[] { 2600f, 3900f })); break;
                case Sound.Meow: data = Meow(0.6f); break;
                case Sound.Purr: data = Purr(1.6f); break;
                case Sound.Drink: data = Mix(Gulp(0.12f, 0f), Gulp(0.12f, 0.2f), Gulp(0.12f, 0.4f), Delay(Chime(0.4f, new[] { 784f, 1175f }), 0.5f)); break;
                case Sound.SlotSpin: data = Rattle(1.1f, 40); break;
                case Sound.Card: data = Mix(Burst(0.04f, 0.5f), Whoosh(0.08f, 1800f, 4000f)); break;
                case Sound.Caw: data = Mix(Caw(0.32f, 0f), Caw(0.28f, 0.4f)); break;
                case Sound.Squeak: data = Squeak(0.09f); break;
                case Sound.Heartbeat: data = Mix(Thump(0.16f, 70f, 42f), Delay(Thump(0.14f, 62f, 40f), 0.22f)); break;
                case Sound.Creak: data = Creak(0.75f); break;
                case Sound.KillTick: data = Mix(Clang(0.18f, new[] { 1760f, 2637f }), Burst(0.03f, 0.3f)); break;
                case Sound.Toll: data = Toll(3.6f, 98f); break;
                case Sound.Cleared: data = Mix(Chime(0.7f, new[] { 659f, 988f }), Delay(Chime(0.6f, new[] { 1318f }), 0.09f), Clang(0.25f, new[] { 2637f })); break;
                case Sound.Pipe: data = Pipe(0.42f, 587f); break;
                case Sound.Graze: data = Mix(Chime(0.12f, new[] { 3136f }), Whoosh(0.1f, 5000f, 2500f)); break;
                default: data = new float[Rate / 10]; break;
            }

            Normalize(data, 0.8f);
            clip = AudioClip.Create(sound.ToString(), data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            Clips[sound] = clip;
            return clip;
        }

        // ------------------------------------------------------------------
        // Recipes
        // ------------------------------------------------------------------

        private static float N() => (float)(noise.NextDouble() * 2.0 - 1.0);

        /// <summary>Band-swept noise with a swell-and-fade envelope: blade through air.</summary>
        private static float[] Whoosh(float seconds, float fromHz, float toHz)
        {
            var d = new float[(int)(seconds * Rate)];
            float lp = 0f, bp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                float cutoff = Mathf.Lerp(fromHz, toHz, Mathf.Sin(t * Mathf.PI));
                float a = Mathf.Clamp01(2f * Mathf.PI * cutoff / Rate);
                lp += a * (N() - lp);
                bp += a * (lp - bp);
                d[i] = (lp - bp) * Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI);
            }
            return d;
        }

        /// <summary>A pitched-down sine thud: body impact.</summary>
        private static float[] Thump(float seconds, float fromHz, float toHz)
        {
            var d = new float[(int)(seconds * Rate)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                float hz = Mathf.Lerp(fromHz, toHz, t);
                phase += 2f * Mathf.PI * hz / Rate;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 6f);
            }
            return d;
        }

        /// <summary>A short noise crack: the transient that makes an impact feel sharp.</summary>
        private static float[] Burst(float seconds, float gain)
        {
            var d = new float[(int)(seconds * Rate)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                d[i] = N() * gain * Mathf.Exp(-t * 8f);
            }
            return d;
        }

        /// <summary>Inharmonic decaying partials: struck metal.</summary>
        /// <summary>
        /// A great church bell: the hum, prime, minor-third tierce, quint and nominal of a real bell's
        /// partials, each with its own slow decay, two close copies of the hum beating against each
        /// other, and a hard strike on top. The far bell before the Tallyman.
        /// </summary>
        private static float[] Toll(float seconds, float hz)
        {
            var d = new float[(int)(seconds * Rate)];
            float[] ratio = { 0.5f, 0.503f, 1f, 1.2f, 1.5f, 2f, 2.51f, 3.01f };
            float[] decay = { 0.55f, 0.55f, 0.9f, 1.3f, 1.7f, 1.6f, 2.6f, 3.4f };
            float[] level = { 0.55f, 0.45f, 0.8f, 0.6f, 0.35f, 0.7f, 0.3f, 0.22f };
            for (int i = 0; i < d.Length; i++)
            {
                float time = (float)i / Rate;
                float s = 0f;
                for (int p = 0; p < ratio.Length; p++)
                    s += Mathf.Sin(2f * Mathf.PI * hz * ratio[p] * time) * Mathf.Exp(-time * decay[p]) * level[p];
                // The clapper: a brief bright knock.
                s += N() * Mathf.Exp(-time * 60f) * 0.4f;
                d[i] = s * Mathf.Clamp01(time * 400f);
            }
            return d;
        }

        private static float[] Clang(float seconds, float[] partials)
        {
            var d = new float[(int)(seconds * Rate)];
            for (int i = 0; i < d.Length; i++)
            {
                float time = (float)i / Rate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    s += Mathf.Sin(2f * Mathf.PI * partials[p] * time) * Mathf.Exp(-time * (6f + p * 4f)) / (1 + p * 0.6f);
                }
                d[i] = s;
            }
            return d;
        }

        /// <summary>A scatter of tiny woody clicks: bones knocking together.</summary>
        private static float[] Rattle(float seconds, int clicks)
        {
            var d = new float[(int)(seconds * Rate)];
            var rng = new System.Random(clicks * 31);
            for (int c = 0; c < clicks; c++)
            {
                int start = rng.Next(d.Length - Rate / 50);
                float hz = 900f + (float)rng.NextDouble() * 1600f;
                float gain = 0.4f + (float)rng.NextDouble() * 0.6f;
                for (int i = 0; i < Rate / 50 && start + i < d.Length; i++)
                {
                    float time = (float)i / Rate;
                    d[start + i] += Mathf.Sin(2f * Mathf.PI * hz * time) * Mathf.Exp(-time * 300f) * gain;
                }
            }
            return d;
        }

        /// <summary>A buzzy sawtooth with vibrato: a war horn, or a shout.</summary>
        private static float[] Horn(float seconds, float hz)
        {
            var d = new float[(int)(seconds * Rate)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                float f = hz * (1f + 0.02f * Mathf.Sin(t * 40f)) * (1f + t * 0.1f);
                phase += f / Rate;
                float saw = (phase % 1f) * 2f - 1f;
                float env = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((1f - t) * 4f);
                d[i] = saw * env * 0.6f;
            }
            return d;
        }

        /// <summary>A cat: a nasal tone that rises and falls ("mi-aow"), its harmonics opening as it goes.</summary>
        /// <summary>A raven's caw: a raspy, falling tone, its pitch rattled by noise, starting <paramref name="at"/> seconds in.</summary>
        private static float[] Caw(float seconds, float at)
        {
            int off = (int)(at * Rate);
            var d = new float[off + (int)(seconds * Rate)];
            float phase = 0f, rough = 0f;
            for (int i = off; i < d.Length; i++)
            {
                float t = (float)(i - off) / (d.Length - off);
                rough += 0.3f * (N() - rough);
                float hz = Mathf.Lerp(720f, 430f, t) * (1f + 0.18f * rough);
                phase += 2f * Mathf.PI * hz / Rate;
                float v = Mathf.Sin(phase) + 0.7f * Mathf.Sin(phase * 2f) + 0.5f * Mathf.Sign(Mathf.Sin(phase * 3f)) * 0.4f;
                float env = Mathf.Clamp01(t * 14f) * Mathf.Clamp01((1f - t) * 4f);
                d[i] = (v * 0.55f + N() * 0.25f) * env;
            }
            return d;
        }

        /// <summary>A rat's squeak: a quick high chirp.</summary>
        /// <summary>An old hinge: a slow, uneven sawtooth groan with a little grit.</summary>
        private static float[] Creak(float seconds)
        {
            var d = new float[(int)(seconds * Rate)];
            float phase = 0f, wob = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                wob += 0.002f * (N() - wob);
                float hz = Mathf.Lerp(95f, 150f, Mathf.Sin(t * Mathf.PI)) * (1f + wob * 3f);
                phase += hz / Rate;
                float saw = (phase % 1f) * 2f - 1f;
                float env = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(t * 37f));
                d[i] = (saw * 0.5f + N() * 0.12f) * env;
            }
            return d;
        }

        private static float[] Squeak(float seconds)
        {
            var d = new float[(int)(seconds * Rate)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                phase += 2f * Mathf.PI * Mathf.Lerp(3200f, 4100f, Mathf.Sin(t * Mathf.PI)) / Rate;
                d[i] = Mathf.Sin(phase) * Mathf.Sin(t * Mathf.PI) * 0.6f;
            }
            return d;
        }

        private static float[] Meow(float seconds)
        {
            var d = new float[(int)(seconds * Rate)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / d.Length;
                float hz = t < 0.35f ? Mathf.Lerp(520f, 820f, t / 0.35f) : Mathf.Lerp(820f, 480f, (t - 0.35f) / 0.65f);
                phase += 2f * Mathf.PI * hz * (1f + 0.01f * Mathf.Sin(t * 60f)) / Rate;
                float open = Mathf.Sin(Mathf.Clamp01(t * 1.4f) * Mathf.PI);
                float v = Mathf.Sin(phase) + 0.5f * open * Mathf.Sin(phase * 2f) + 0.35f * open * Mathf.Sin(phase * 3f) + 0.15f * Mathf.Sin(phase * 4f);
                float env = Mathf.Clamp01(t * 10f) * Mathf.Clamp01((1f - t) * 5f);
                d[i] = v * env * 0.5f;
            }
            return d;
        }

        /// <summary>A purr: low, soft noise pulsing about 25 times a second, in and out breaths.</summary>
        private static float[] Purr(float seconds)
        {
            var d = new float[(int)(seconds * Rate)];
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                lp += 0.02f * (N() - lp);
                float pulse = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 26f * t);
                float breath = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.8f * t);
                float env = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((seconds - t) * 4f);
                d[i] = lp * pulse * pulse * breath * env * 6f;
            }
            return d;
        }

        /// <summary>One swallow: a short wet low thump, starting <paramref name="at"/> seconds in.</summary>
        private static float[] Gulp(float seconds, float at)
        {
            int off = (int)(at * Rate);
            var d = new float[off + (int)(seconds * Rate)];
            float phase = 0f;
            for (int i = off; i < d.Length; i++)
            {
                float t = (float)(i - off) / (d.Length - off);
                phase += 2f * Mathf.PI * Mathf.Lerp(260f, 140f, t) / Rate;
                d[i] = Mathf.Sin(phase) * Mathf.Sin(t * Mathf.PI) * 0.7f;
            }
            return d;
        }

        /// <summary>A breathy reed note: a few harmonics with vibrato over a little filtered breath.</summary>
        private static float[] Pipe(float seconds, float hz)
        {
            var d = new float[(int)(seconds * Rate)];
            float phase = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Min(1f, t / 0.03f) * Mathf.Clamp01((seconds - t) / 0.18f);
                phase += 2f * Mathf.PI * hz * (1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5.5f * t)) / Rate;
                float tone = Mathf.Sin(phase) + 0.35f * Mathf.Sin(2f * phase) + 0.18f * Mathf.Sin(3f * phase) + 0.08f * Mathf.Sin(5f * phase);
                lp += (N() - lp) * 0.25f;
                d[i] = env * (tone * 0.55f + lp * 0.25f);
            }
            return d;
        }

        private static float[] Chime(float seconds, float[] notes)
        {
            var d = new float[(int)(seconds * Rate)];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * 0.09f * Rate);
                for (int i = start; i < d.Length; i++)
                {
                    float time = (float)(i - start) / Rate;
                    d[i] += Mathf.Sin(2f * Mathf.PI * notes[n] * time) * Mathf.Exp(-time * 4f) * 0.5f;
                }
            }
            return d;
        }

        private static float[] Delay(float[] src, float seconds)
        {
            int offset = (int)(seconds * Rate);
            var d = new float[src.Length + offset];
            System.Array.Copy(src, 0, d, offset, src.Length);
            return d;
        }

        private static float[] Mix(params float[][] parts)
        {
            int len = 0;
            foreach (var p in parts) len = Mathf.Max(len, p.Length);
            var d = new float[len];
            foreach (var p in parts)
                for (int i = 0; i < p.Length; i++) d[i] += p[i];
            return d;
        }

        private static void Normalize(float[] d, float peak)
        {
            float max = 0.0001f;
            foreach (var s in d) max = Mathf.Max(max, Mathf.Abs(s));
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
