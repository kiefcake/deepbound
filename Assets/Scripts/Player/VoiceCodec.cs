using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// The voice codec: IMA ADPCM, 4 bits a sample at 16 kHz (64 kbps a speaker). Written here
    /// rather than pulled in as a plugin, so voice needs no outside code or service. Every frame
    /// carries its own decoder state, so a lost packet costs only its own 40 ms.
    /// Frame layout: [predictor lo, predictor hi, step index, nibbles...].
    /// </summary>
    public static class VoiceCodec
    {
        public const int SampleRate = 16000;
        public const int FrameSamples = 640;            // 40 ms
        public const int FrameBytes = 3 + FrameSamples / 2;

        private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
            107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
            5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
            27086, 29794, 32767,
        };

        /// <summary>The encoder's running state (kept between frames for continuity; each frame also stores it).</summary>
        public struct State { public int Predictor, Index; }

        public static byte[] Encode(float[] samples, int offset, ref State state)
        {
            var frame = new byte[FrameBytes];
            frame[0] = (byte)(state.Predictor & 0xFF);
            frame[1] = (byte)((state.Predictor >> 8) & 0xFF);
            frame[2] = (byte)state.Index;
            int pred = state.Predictor, index = state.Index;
            for (int i = 0; i < FrameSamples; i++)
            {
                int sample = Mathf.Clamp(Mathf.RoundToInt(samples[offset + i] * 32767f), -32768, 32767);
                int step = StepTable[index];
                int diff = sample - pred;
                int nibble = 0;
                if (diff < 0) { nibble = 8; diff = -diff; }
                int delta = step >> 3;
                if (diff >= step) { nibble |= 4; diff -= step; delta += step; }
                if (diff >= step >> 1) { nibble |= 2; diff -= step >> 1; delta += step >> 1; }
                if (diff >= step >> 2) { nibble |= 1; delta += step >> 2; }
                pred = (nibble & 8) != 0 ? pred - delta : pred + delta;
                pred = Mathf.Clamp(pred, -32768, 32767);
                index = Mathf.Clamp(index + IndexTable[nibble], 0, 88);
                if ((i & 1) == 0) frame[3 + i / 2] = (byte)nibble;
                else frame[3 + i / 2] |= (byte)(nibble << 4);
            }
            state.Predictor = pred;
            state.Index = index;
            return frame;
        }

        /// <summary>Decode one frame into <paramref name="output"/> (FrameSamples floats). False if malformed.</summary>
        public static bool Decode(byte[] frame, float[] output)
        {
            if (frame == null || frame.Length != FrameBytes || output.Length < FrameSamples) return false;
            int pred = (short)(frame[0] | (frame[1] << 8));
            int index = Mathf.Clamp(frame[2], 0, 88);
            for (int i = 0; i < FrameSamples; i++)
            {
                int b = frame[3 + i / 2];
                int nibble = (i & 1) == 0 ? b & 0x0F : (b >> 4) & 0x0F;
                int step = StepTable[index];
                int delta = step >> 3;
                if ((nibble & 4) != 0) delta += step;
                if ((nibble & 2) != 0) delta += step >> 1;
                if ((nibble & 1) != 0) delta += step >> 2;
                pred = (nibble & 8) != 0 ? pred - delta : pred + delta;
                pred = Mathf.Clamp(pred, -32768, 32767);
                index = Mathf.Clamp(index + IndexTable[nibble], 0, 88);
                output[i] = pred / 32768f;
            }
            return true;
        }

        public static float Rms(float[] s, int offset, int count)
        {
            double sum = 0;
            for (int i = 0; i < count; i++) sum += s[offset + i] * s[offset + i];
            return Mathf.Sqrt((float)(sum / Mathf.Max(1, count)));
        }

        /// <summary>Self-test for the smoke test: signal-to-noise of a round trip, in dB.</summary>
        public static float RoundTripSnr()
        {
            var tone = new float[FrameSamples * 4];
            for (int i = 0; i < tone.Length; i++)
                tone[i] = 0.3f * Mathf.Sin(i * 2f * Mathf.PI * 440f / SampleRate) + 0.1f * Mathf.Sin(i * 2f * Mathf.PI * 1250f / SampleRate);
            var state = new State();
            var outBuf = new float[FrameSamples];
            double sig = 0, err = 0;
            for (int f = 0; f < 4; f++)
            {
                var frame = Encode(tone, f * FrameSamples, ref state);
                Decode(frame, outBuf);
                if (f == 0) continue; // the first frame is the encoder settling in
                for (int i = 0; i < FrameSamples; i++)
                {
                    float a = tone[f * FrameSamples + i];
                    sig += a * a;
                    err += (a - outBuf[i]) * (a - outBuf[i]);
                }
            }
            return 10f * Mathf.Log10((float)(sig / System.Math.Max(err, 1e-12)));
        }
    }
}
