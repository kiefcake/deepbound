using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The tavern band's songbook (2026-11-01, the user: "music inspired by the witcher 3, lutes and everything ...
    /// ensure there is no copyright infringement"). Four ORIGINAL tunes, written for this game note by note below and
    /// played by a synthesised folk band: a plucked lute (Karplus-Strong string with a wooden body), a fiddle, a wooden
    /// whistle, a hurdy-gurdy drone and a frame drum. The inspiration is only the general style - modal Eastern-European
    /// folk, drones, jigs and laments in Dorian, Aeolian and the "Slavic" Phrygian-dominant scale - no melody, chord
    /// sequence or recording from any existing work is used.
    ///
    /// <para>Songs are rendered on a worker thread the first time they are needed (a second or so each), then kept.</para>
    /// </summary>
    public static class TavernMusic
    {
        public const int Rate = 22050;

        public class Song
        {
            public string Name;
            /// <summary>MIDI note of scale degree 0, and the mode as semitone steps from it.</summary>
            public int Root;
            public int[] Mode;
            /// <summary>Eighth notes per bar, seconds per eighth.</summary>
            public int BarEighths;
            public float Eighth;
            /// <summary>Two eight-bar parts, "degree:eighths" tokens (r = rest), bars separated by '|'.</summary>
            public string PartA, PartB;
            /// <summary>The chord (a scale degree, triad on it) for each bar of A and of B.</summary>
            public int[] ChordsA, ChordsB;
            public Voice Lead;
            public bool Drone, Drum, Whistle;
        }

        public enum Voice { Fiddle, Whistle }

        static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        static readonly int[] Aeolian = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] PhrygianDominant = { 0, 1, 4, 5, 7, 8, 10 };

        public static readonly Song[] Songs =
        {
            new Song
            {
                // A quick jig in 6/8, D Dorian: fiddle over lute, the frame drum on the ones and fours.
                Name = "The Ferryman's Jig", Root = 62, Mode = Dorian, BarEighths = 6, Eighth = 0.19f, Lead = Voice.Fiddle, Drum = true,
                PartA = "4:1 2:1 0:1 4:2 2:1|3:1 4:1 5:1 4:2 3:1|2:1 3:1 4:1 7:2 5:1|4:1 3:1 2:1 1:3|4:1 2:1 0:1 4:2 2:1|3:1 4:1 5:1 6:2 7:1|5:1 4:1 3:1 2:1 1:1 -1:1|0:5 r:1",
                PartB = "7:2 8:1 9:1 8:1 7:1|6:1 7:1 8:1 7:2 5:1|4:1 5:1 6:1 7:2 4:1|5:1 4:1 3:1 4:3|7:2 8:1 9:1 8:1 7:1|6:1 5:1 4:1 5:2 3:1|4:1 2:1 3:1 1:1 2:1 -1:1|0:5 r:1",
                ChordsA = new[] { 0, 3, 0, 6, 0, 3, 6, 0 }, ChordsB = new[] { 0, 6, 3, 4, 0, 3, 6, 0 },
            },
            new Song
            {
                // A slow lament in 3/4, A Aeolian: a wooden whistle over a hurdy-gurdy drone and a lute picking chords.
                Name = "Ash on the Wind", Root = 69, Mode = Aeolian, BarEighths = 6, Eighth = 0.31f, Lead = Voice.Whistle, Drone = true,
                PartA = "0:2 2:1 4:3|5:2 4:1 2:2 3:1|4:4 3:1 2:1|1:6|0:2 2:1 4:2 7:1|6:3 5:2 4:1|3:2 2:1 1:2 -1:1|0:6",
                PartB = "7:3 6:1 5:1 4:1|5:4 4:2|3:2 4:1 5:2 3:1|4:6|7:2 9:1 8:2 7:1|6:2 5:1 4:3|3:1 2:1 1:2 2:1 1:1|0:6",
                ChordsA = new[] { 0, 5, 2, 6, 0, 6, 3, 0 }, ChordsB = new[] { 0, 5, 3, 4, 0, 6, 3, 0 },
            },
            new Song
            {
                // A reel in 4/4, E Dorian: whistle and fiddle together, the drum driving it.
                Name = "Mead Before Morning", Root = 64, Mode = Dorian, BarEighths = 8, Eighth = 0.15f, Lead = Voice.Fiddle, Drum = true, Whistle = true,
                PartA = "0:1 4:1 4:1 2:1 4:1 7:1 4:1 2:1|3:1 5:1 5:1 3:1 5:1 7:1 5:1 3:1|0:1 4:1 4:1 2:1 4:1 5:1 7:1 8:1|9:1 8:1 7:1 5:1 4:2 3:2|0:1 4:1 4:1 2:1 4:1 7:1 4:1 2:1|3:1 5:1 5:1 3:1 5:1 7:1 5:1 3:1|7:1 8:1 9:1 7:1 8:1 6:1 4:1 5:1|3:1 1:1 2:1 -1:1 0:4",
                PartB = "7:2 9:1 7:1 8:1 6:1 4:1 6:1|7:1 8:1 9:1 10:1 9:1 7:1 8:1 6:1|7:2 9:1 7:1 8:1 6:1 4:1 2:1|3:1 5:1 4:1 3:1 2:1 1:1 0:2|7:2 9:1 7:1 8:1 6:1 4:1 6:1|7:1 8:1 9:1 10:1 9:1 7:1 8:1 6:1|11:1 10:1 9:1 7:1 8:1 9:1 10:1 8:1|7:4 4:2 0:2",
                ChordsA = new[] { 0, 3, 0, 3, 0, 3, 6, 0 }, ChordsB = new[] { 0, 6, 0, 3, 0, 6, 3, 0 },
            },
            new Song
            {
                // A dark march, D Phrygian dominant: drone, drum and fiddle - the road out of the gate.
                Name = "The Wolf Road", Root = 62, Mode = PhrygianDominant, BarEighths = 8, Eighth = 0.21f, Lead = Voice.Fiddle, Drone = true, Drum = true,
                PartA = "0:2 0:1 1:1 2:2 1:1 0:1|3:2 4:2 3:1 2:1 1:2|0:2 0:1 1:1 2:1 3:1 4:2|5:1 4:1 3:1 2:1 1:4|0:2 0:1 1:1 2:2 1:1 0:1|3:2 4:1 5:1 6:2 5:1 4:1|3:1 2:1 1:1 2:1 3:1 2:1 1:2|0:6 r:2",
                PartB = "7:3 6:1 5:2 4:2|5:1 6:1 5:1 4:1 3:4|4:2 5:1 4:1 3:2 2:1 1:1|2:2 1:2 0:4|7:3 8:1 7:2 6:2|5:2 6:1 5:1 4:4|3:1 4:1 3:1 2:1 1:2 2:1 1:1|0:8",
                ChordsA = new[] { 0, 3, 0, 1, 0, 3, 1, 0 }, ChordsB = new[] { 0, 3, 0, 0, 0, 3, 1, 0 },
            },
        };

        private static readonly AudioClip[] clips = new AudioClip[Songs.Length];
        private static readonly Task<float[]>[] jobs = new Task<float[]>[Songs.Length];

        /// <summary>The song's clip, or null while it is still being rendered (call again next frame).</summary>
        public static AudioClip Clip(int i)
        {
            i = Mathf.Clamp(i, 0, Songs.Length - 1);
            if (clips[i] != null) return clips[i];
            if (jobs[i] == null) { var song = Songs[i]; jobs[i] = Task.Run(() => Render(song)); }
            if (!jobs[i].IsCompleted) return null;
            var data = jobs[i].Result;
            if (data == null) return null;
            var clip = AudioClip.Create("tavern-" + i, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clips[i] = clip;
            return clip;
        }

        /// <summary>Render synchronously (tests).</summary>
        public static float[] RenderNow(int i) => Render(Songs[Mathf.Clamp(i, 0, Songs.Length - 1)]);

        // ------------------------------------------------------------------
        // Rendering (worker thread: no Unity API but Mathf)
        // ------------------------------------------------------------------

        private struct Note { public int Degree; public int Start, Length; public bool Rest; }

        private static float Hz(Song s, int degree, int octaveShift = 0)
        {
            int oct = Mathf.FloorToInt(degree / 7f);
            int idx = degree - oct * 7;
            int midi = s.Root + s.Mode[idx] + 12 * (oct + octaveShift);
            return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
        }

        private static Note[] Parse(string part, int barEighths, int offset)
        {
            var notes = new System.Collections.Generic.List<Note>();
            int at = offset;
            foreach (var bar in part.Split('|'))
            {
                int barStart = at;
                foreach (var tok in bar.Trim().Split(' '))
                {
                    if (tok.Length == 0) continue;
                    var kv = tok.Split(':');
                    int len = int.Parse(kv[1]);
                    notes.Add(new Note { Rest = kv[0] == "r", Degree = kv[0] == "r" ? 0 : int.Parse(kv[0]), Start = at, Length = len });
                    at += len;
                }
                at = barStart + barEighths;
            }
            return notes.ToArray();
        }

        private static float[] Render(Song s)
        {
            try
            {
                int bars = 32;   // A A B B
                int eighth = Mathf.RoundToInt(s.Eighth * Rate);
                int total = bars * s.BarEighths * eighth + Rate * 2;
                var mix = new float[total];
                var rng = new System.Random(s.Name.GetHashCode());

                var a = Parse(s.PartA, s.BarEighths, 0);
                var b = Parse(s.PartB, s.BarEighths, 0);
                int partLen = 8 * s.BarEighths;
                for (int rep = 0; rep < 4; rep++)
                {
                    var part = rep < 2 ? a : b;
                    var chords = rep < 2 ? s.ChordsA : s.ChordsB;
                    int offset = rep * partLen;
                    bool second = rep % 2 == 1;
                    // The lead: the fiddle (or whistle), the second time round a touch brighter with grace notes.
                    foreach (var n in part)
                    {
                        if (n.Rest) continue;
                        int start = (offset + n.Start) * eighth;
                        int len = n.Length * eighth;
                        float f = Hz(s, n.Degree);
                        if (s.Lead == Voice.Fiddle) Fiddle(mix, start, len, f, 0.16f, rng);
                        else WhistleNote(mix, start, len, f * 2f, 0.14f, rng);
                        if (s.Whistle && second) WhistleNote(mix, start, len, f * 2f, 0.07f, rng);
                        // A grace note before longer notes on the repeat: a quick flick from the note above.
                        if (second && n.Length >= 2 && start > eighth / 3) Lute(mix, start - eighth / 4, Hz(s, n.Degree + 1), 0.05f, 0.25f, rng);
                    }
                    // The lute: the bar's chord picked as a broken pattern, bass on the beat.
                    for (int bar = 0; bar < 8; bar++)
                    {
                        int root = chords[bar];
                        int barAt = (offset + bar * s.BarEighths) * eighth;
                        for (int e = 0; e < s.BarEighths; e++)
                        {
                            int step = PickPattern(s.BarEighths, e);
                            int deg = root + (step == 0 ? 0 : step == 1 ? 2 : step == 2 ? 4 : 7);
                            float f = Hz(s, deg, -1);
                            bool beat = e == 0 || (s.BarEighths == 6 && e == 3) || (s.BarEighths == 8 && e == 4);
                            Lute(mix, barAt + e * eighth + rng.Next(0, Rate / 400), f, beat ? 0.2f : 0.13f, beat ? 1.1f : 0.7f, rng);
                            if (e == 0) Lute(mix, barAt, Hz(s, root, -2), 0.18f, 1.6f, rng);
                        }
                        if (s.Drum) DrumBar(mix, barAt, s.BarEighths, eighth, rng, bar == 7);
                    }
                }
                if (s.Drone) Drone(mix, Hz(s, 0, -2), Hz(s, 4, -2), total);

                Room(mix);
                // Normalise, with a soft knee.
                float peak = 0.0001f;
                for (int i = 0; i < mix.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(mix[i]));
                float g = 0.85f / peak;
                for (int i = 0; i < mix.Length; i++)
                {
                    float v = mix[i] * g;
                    mix[i] = v / (1f + 0.15f * Mathf.Abs(v));
                }
                // Loop cleanly: the tail rings into the start.
                int loop = bars * s.BarEighths * eighth;
                var outp = new float[loop];
                for (int i = 0; i < loop; i++) outp[i] = mix[i] + (i < total - loop ? mix[loop + i] : 0f);
                return outp;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Which chord tone each eighth of the bar picks: 0 root, 1 third, 2 fifth, 3 octave.</summary>
        private static int PickPattern(int barEighths, int e) => barEighths == 6
            ? new[] { 0, 2, 3, 1, 2, 3 }[e]
            : new[] { 0, 2, 3, 2, 1, 2, 3, 2 }[e];

        /// <summary>A plucked string: Karplus-Strong with a little body resonance.</summary>
        private static void Lute(float[] mix, int start, float hz, float amp, float seconds, System.Random rng)
        {
            if (start < 0 || start >= mix.Length) return;
            int period = Mathf.Max(2, Mathf.RoundToInt(Rate / hz));
            var buf = new float[period];
            for (int i = 0; i < period; i++) buf[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            // Soften the pluck (a thumb, not a pick).
            for (int i = 1; i < period; i++) buf[i] = 0.5f * (buf[i] + buf[i - 1]);
            int len = Mathf.Min(mix.Length - start, Mathf.RoundToInt(seconds * Rate));
            float body = 0f;
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                int next = (idx + 1) % period;
                float v = buf[idx];
                buf[idx] = 0.4985f * (v + buf[next]);
                idx = next;
                body += (v - body) * 0.35f;
                float fade = i > len - 400 ? (len - i) / 400f : 1f;
                mix[start + i] += (v * 0.7f + body * 0.5f) * amp * fade;
            }
        }

        /// <summary>A bowed fiddle: a bright additive tone, bow swell, late vibrato, a touch of rosin noise.</summary>
        private static void Fiddle(float[] mix, int start, int len, float hz, float amp, System.Random rng)
        {
            len = Mathf.Min(len, mix.Length - start);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / Rate;
                float vib = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5.5f * t) * Mathf.Clamp01((t - 0.12f) * 4f);
                phase += 2f * Mathf.PI * hz * vib / Rate;
                float v = 0f;
                for (int k = 1; k <= 7; k++) v += Mathf.Sin(phase * k) / (k * (k > 2 ? 1.4f : 1f));
                float env = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((len - i) / (0.06f * Rate)) * (0.85f + 0.15f * Mathf.Sin(t * 3f));
                float rosin = (float)(rng.NextDouble() - 0.5) * 0.05f;
                mix[start + i] += (v * 0.45f + rosin) * amp * env;
            }
        }

        /// <summary>A wooden whistle: near-sine with a breathy edge and a soft vibrato.</summary>
        private static void WhistleNote(float[] mix, int start, int len, float hz, float amp, System.Random rng)
        {
            len = Mathf.Min(len, mix.Length - start);
            float phase = 0f, breath = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / Rate;
                float vib = 1f + 0.008f * Mathf.Sin(2f * Mathf.PI * 4.8f * t) * Mathf.Clamp01((t - 0.15f) * 3f);
                phase += 2f * Mathf.PI * hz * vib / Rate;
                breath += ((float)(rng.NextDouble() - 0.5) - breath) * 0.08f;
                float v = Mathf.Sin(phase) + 0.18f * Mathf.Sin(phase * 2f) + breath * 0.6f;
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((len - i) / (0.05f * Rate));
                mix[start + i] += v * amp * env;
            }
        }

        /// <summary>A hurdy-gurdy drone: tonic and fifth, a buzzing reedy tone that breathes slowly.</summary>
        private static void Drone(float[] mix, float root, float fifth, int total)
        {
            float p1 = 0f, p2 = 0f, lp = 0f;
            for (int i = 0; i < total; i++)
            {
                float t = (float)i / Rate;
                p1 += 2f * Mathf.PI * root / Rate;
                p2 += 2f * Mathf.PI * fifth / Rate;
                float saw = (Mathf.Repeat(p1 / (2f * Mathf.PI), 1f) * 2f - 1f) * 0.6f + (Mathf.Repeat(p2 / (2f * Mathf.PI), 1f) * 2f - 1f) * 0.4f;
                lp += (saw - lp) * 0.08f;
                float swell = 0.75f + 0.25f * Mathf.Sin(t * 0.5f);
                float fadeIn = Mathf.Clamp01(t / 1.5f);
                mix[i] += lp * 0.085f * swell * fadeIn;
            }
        }

        /// <summary>A frame drum: a low thump on the strong beats, lighter taps between, a roll into the last bar.</summary>
        private static void DrumBar(float[] mix, int barAt, int barEighths, int eighth, System.Random rng, bool last)
        {
            for (int e = 0; e < barEighths; e++)
            {
                bool strong = e == 0 || (barEighths == 6 && e == 3) || (barEighths == 8 && e == 4);
                bool tap = barEighths == 6 ? e == 2 || e == 5 : e % 2 == 0;
                if (last && e >= barEighths - 2) { Hit(mix, barAt + e * eighth, 0.6f, rng); Hit(mix, barAt + e * eighth + eighth / 2, 0.45f, rng); continue; }
                if (strong) Hit(mix, barAt + e * eighth, 1f, rng);
                else if (tap) Hit(mix, barAt + e * eighth, 0.35f, rng);
            }
        }

        private static void Hit(float[] mix, int at, float amp, System.Random rng)
        {
            int len = Mathf.Min(mix.Length - at, Rate / 3);
            float phase = 0f, noise = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / Rate;
                float hz = Mathf.Lerp(95f, 58f, Mathf.Clamp01(t / 0.12f));
                phase += 2f * Mathf.PI * hz / Rate;
                noise += ((float)(rng.NextDouble() * 2.0 - 1.0) - noise) * 0.3f;
                float env = Mathf.Exp(-t * 18f);
                mix[at + i] += (Mathf.Sin(phase) * 0.9f + noise * Mathf.Exp(-t * 60f) * 0.6f) * env * 0.22f * amp;
            }
        }

        /// <summary>A small wooden room: two short feedback echoes, mixed in low.</summary>
        private static void Room(float[] mix)
        {
            int d1 = Mathf.RoundToInt(0.031f * Rate), d2 = Mathf.RoundToInt(0.047f * Rate);
            var wet = new float[mix.Length];
            for (int i = 0; i < mix.Length; i++)
            {
                float v = mix[i];
                if (i >= d1) v += wet[i - d1] * 0.45f;
                if (i >= d2) v += wet[i - d2] * 0.35f;
                wet[i] = v * 0.6f;
            }
            for (int i = 0; i < mix.Length; i++) mix[i] = mix[i] * 0.85f + wet[i] * 0.3f;
        }
    }
}
