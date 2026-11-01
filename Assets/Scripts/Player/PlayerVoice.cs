using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// Proximity voice, R.E.P.O.-style. The owner records the microphone, encodes 40 ms frames
    /// (<see cref="VoiceCodec"/>) and sends them to the host. The host forwards each frame only
    /// to players within earshot. Every peer plays a speaker's voice from that speaker's head,
    /// in 3D, muffled through walls. Nothing leaves the game session: no voice service.
    ///
    /// <para>Shouting is noise: loud frames wake nearby enemies and statues (<see cref="ServerHeard"/>).
    /// Bled-out souls can only whisper (short range).</para>
    /// </summary>
    public class PlayerVoice : NetworkBehaviour
    {
        public const float Earshot = 30f, SoulEarshot = 7f, ShoutLevel = 0.12f, ShoutRadius = 10f;

        /// <summary>Voice level for the HUD: this player's latest frame (any peer), 0..~0.5, decays.</summary>
        public float Level { get; private set; }
        public bool Speaking => Level > 0.004f;

        /// <summary>Server: frames received from this speaker (for tests).</summary>
        public int ServerFramesFrom { get; private set; }
        /// <summary>Frames this peer has played for this speaker (for tests).</summary>
        public int FramesHeard { get; private set; }

        /// <summary>Owner: the latest microphone level (for the options meter), whether or not it is sent.</summary>
        public static float MicLevel { get; private set; }
        public static bool MicAvailable { get; private set; }
        public static PlayerVoice LocalVoice { get; private set; }

        // ---- Capture (owner) ----
        private string device;
        private AudioClip micClip;
        private int micRate, micPos;
        private float[] micRead = new float[4096];
        private readonly List<float> pending = new List<float>(4096);
        private float resampleCarry;
        private VoiceCodec.State encoder;
        private float openUntil;
        private float noiseFloor = 0.01f;
        private float hpPrev, hpOut;
        private float nextShout;
        private readonly Queue<float> injected = new Queue<float>();

        // ---- Playback (every peer, for remote speakers) ----
        private AudioSource source;
        private AudioLowPassFilter muffle;
        private readonly float[] ring = new float[VoiceCodec.SampleRate * 2];
        private int writeAt, readAt, buffered;
        private bool playing;
        private readonly object ringLock = new object();
        /// <summary>
        /// Voices are amplified on playback (play-test 2026-11-01: "make mics louder"): an AudioSource stops at volume 1,
        /// so the boost is applied to the samples, with a soft clip so a shout distorts gently instead of cracking.
        /// </summary>
        public const float PlaybackBoost = 2.4f;
        private volatile float playGain = PlaybackBoost;
        private readonly float[] decodeBuf = new float[VoiceCodec.FrameSamples];
        private float nextOcclusion;
        private PlayerLife life;
        private Health health;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            health = GetComponent<Health>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                LocalVoice = this;
                StartMicrophone();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                if (LocalVoice == this) LocalVoice = null;
                if (device != null && Microphone.IsRecording(device)) Microphone.End(device);
            }
        }

        private void StartMicrophone()
        {
            MicAvailable = false;
            if (Application.isBatchMode || Microphone.devices.Length == 0) return;
            device = Microphone.devices[0];
            Microphone.GetDeviceCaps(device, out int min, out int max);
            micRate = min == 0 && max == 0 ? VoiceCodec.SampleRate : Mathf.Clamp(VoiceCodec.SampleRate, min, max);
            micClip = Microphone.Start(device, true, 1, micRate);
            micPos = 0;
            MicAvailable = micClip != null;
        }

        /// <summary>Tests: push 16 kHz samples through the send path as if spoken (bypasses the gate).</summary>
        public void DebugInject(float[] samples)
        {
            foreach (var s in samples) injected.Enqueue(s);
        }

        private void Update()
        {
            Level = Mathf.MoveTowards(Level, 0f, Time.deltaTime * 0.6f);
            if (IsOwner) OwnerCapture();
            else if (source != null && Time.time >= nextOcclusion) UpdateOcclusion();
        }

        // ------------------------------------------------------------------
        // Capture and send (owner)
        // ------------------------------------------------------------------

        private void OwnerCapture()
        {
            bool forced = injected.Count > 0;
            if (forced)
            {
                while (injected.Count > 0) pending.Add(injected.Dequeue());
            }
            else if (MicAvailable && micClip != null)
            {
                ReadMicrophone();
            }
            else return;

            while (pending.Count >= VoiceCodec.FrameSamples)
            {
                var frame = pending.GetRange(0, VoiceCodec.FrameSamples).ToArray();
                pending.RemoveRange(0, VoiceCodec.FrameSamples);
                float rms = VoiceCodec.Rms(frame, 0, frame.Length);
                MicLevel = rms;
                if (!forced && !ShouldTransmit(rms)) { encoder = default; continue; }
                for (int s = 0; s < frame.Length; s++)
                {
                    // ~100 Hz one-pole high-pass: takes out hum and desk thumps.
                    hpOut = 0.962f * (hpOut + frame[s] - hpPrev);
                    hpPrev = frame[s];
                    frame[s] = hpOut;
                }
                var bytes = VoiceCodec.Encode(frame, 0, ref encoder);
                Level = Mathf.Max(Level, rms);
                VoiceUpRpc(bytes, (byte)Mathf.Clamp(Mathf.RoundToInt(rms * 1000f), 0, 255));
            }
        }

        private void ReadMicrophone()
        {
            int pos = Microphone.GetPosition(device);
            if (pos < 0 || pos == micPos) return;
            int len = micClip.samples;
            int count = (pos - micPos + len) % len;
            if (count <= 0) return;
            if (micRead.Length < count) micRead = new float[count];
            // Two reads if the ring wrapped.
            int first = Mathf.Min(count, len - micPos);
            var a = new float[first];
            micClip.GetData(a, micPos);
            System.Array.Copy(a, 0, micRead, 0, first);
            if (count > first)
            {
                var b = new float[count - first];
                micClip.GetData(b, 0);
                System.Array.Copy(b, 0, micRead, first, count - first);
            }
            micPos = pos;

            float gain = Settings.MicGain;
            if (micRate == VoiceCodec.SampleRate)
            {
                for (int i = 0; i < count; i++) pending.Add(Mathf.Clamp(micRead[i] * gain, -1f, 1f));
                return;
            }
            // Linear resample to 16 kHz.
            float stepIn = (float)micRate / VoiceCodec.SampleRate;
            float t = resampleCarry;
            while (t < count - 1)
            {
                int i = (int)t;
                float f = t - i;
                pending.Add(Mathf.Clamp(Mathf.Lerp(micRead[i], micRead[i + 1], f) * gain, -1f, 1f));
                t += stepIn;
            }
            resampleCarry = Mathf.Max(0f, t - count);
        }

        private bool ShouldTransmit(float rms)
        {
            switch (Settings.VoiceMode)
            {
                case 2: return false; // off
                case 1:               // push to talk
                    var k = Keyboard.current;
                    return k != null && UI.Binds.Held(UI.Bind.PushToTalk) && !PauseMenu.Open;
                default:              // open mic, voice-activated, with a short hang so words are not clipped
                    // The gate tracks the room's noise floor (hiss, fans, hum) and opens only well above it,
                    // so a quiet mic never streams static.
                    noiseFloor = rms < noiseFloor ? Mathf.Lerp(noiseFloor, rms, 0.2f) : Mathf.Lerp(noiseFloor, rms, 0.004f);
                    if (rms >= Mathf.Max(Settings.MicThreshold, noiseFloor * 3f)) openUntil = Time.time + 0.35f;
                    return Time.time < openUntil;
            }
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void VoiceUpRpc(byte[] frame, byte loudness)
        {
            if (frame == null || frame.Length != VoiceCodec.FrameBytes) return;
            ServerFramesFrom++;
            float level = loudness / 1000f;
            bool soul = life != null && life.Soul.Value;
            float range = soul ? SoulEarshot : Earshot;
            var targets = new List<ulong>(4);
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.OwnerClientId == OwnerClientId) continue;
                if (Vector3.Distance(p.transform.position, transform.position) > range) continue;
                targets.Add(p.OwnerClientId);
            }
            if (targets.Count > 0) VoiceDownRpc(frame, loudness, RpcTarget.Group(targets, RpcTargetUse.Temp));

            // R.E.P.O.: shouting carries to the dead.
            if (!soul && level >= ShoutLevel && Time.time >= nextShout && World.DungeonDirector.Layout != null && !World.DungeonDirector.InLobby)
            {
                nextShout = Time.time + 1f;
                ServerHeard?.Invoke(health, transform.position, ShoutRadius);
            }
        }

        /// <summary>Server: a player shouted here (who, where, radius). Enemies and statues listen.</summary>
        public static event System.Action<Health, Vector3, float> ServerHeard;

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void VoiceDownRpc(byte[] frame, byte loudness, RpcParams rpcParams = default)
        {
            if (IsOwner || !VoiceCodec.Decode(frame, decodeBuf)) return;
            FramesHeard++;
            Level = Mathf.Max(Level, loudness / 1000f);
            EnsureSource();
            lock (ringLock)
            {
                for (int i = 0; i < decodeBuf.Length; i++)
                {
                    ring[writeAt] = decodeBuf[i];
                    writeAt = (writeAt + 1) % ring.Length;
                }
                buffered = Mathf.Min(ring.Length, buffered + decodeBuf.Length);
                // Too far behind (a stall): drop the oldest so the voice stays live.
                if (buffered > VoiceCodec.SampleRate / 2)
                {
                    int drop = buffered - VoiceCodec.SampleRate / 5;
                    readAt = (readAt + drop) % ring.Length;
                    buffered -= drop;
                }
            }
        }

        // ------------------------------------------------------------------
        // Playback (remote speakers)
        // ------------------------------------------------------------------

        private void EnsureSource()
        {
            if (source != null) return;
            var head = new GameObject("Voice");
            head.transform.SetParent(transform, false);
            head.transform.localPosition = Vector3.up * 1.6f;
            source = head.AddComponent<AudioSource>();
            source.clip = AudioClip.Create("voice-" + NetworkObjectId, VoiceCodec.SampleRate, 1, VoiceCodec.SampleRate, true, OnAudioRead);
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = Earshot;
            source.dopplerLevel = 0f;
            source.volume = 1f;
            playGain = PlaybackBoost * Settings.VoiceVolume;
            // Stone rooms: a little echo, and walls muffle.
            var reverb = head.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.Stoneroom;
            reverb.dryLevel = 0f;
            reverb.room = -1800f;
            muffle = head.AddComponent<AudioLowPassFilter>();
            muffle.cutoffFrequency = 22000f;
            source.Play();
        }

        // Audio thread: pull from the jitter buffer. Start once ~120 ms are queued; on underrun, wait again.
        private void OnAudioRead(float[] data)
        {
            lock (ringLock)
            {
                if (!playing && buffered >= VoiceCodec.SampleRate * 12 / 100) playing = true;
                for (int i = 0; i < data.Length; i++)
                {
                    if (!playing || buffered <= 0) { data[i] = 0f; playing = false; continue; }
                    // A tanh-shaped soft clip (Pade): unity gain near zero, never past +-1.
                    float v = Mathf.Clamp(ring[readAt] * playGain, -3f, 3f);
                    data[i] = v * (27f + v * v) / (27f + 9f * v * v);
                    readAt = (readAt + 1) % ring.Length;
                    buffered--;
                }
            }
        }

        private void UpdateOcclusion()
        {
            nextOcclusion = Time.time + 0.2f;
            source.volume = 1f;
            playGain = PlaybackBoost * Settings.VoiceVolume;
            var cam = Art.ViewCamera.Current;
            bool soul = life != null && life.Soul.Value;
            source.maxDistance = soul ? SoulEarshot : Earshot;
            if (cam == null || health == null) return;
            bool clear = CombatQuery.ClearLine(cam.transform.position, health);
            float target = soul ? 1400f : clear ? 22000f : 1100f;
            muffle.cutoffFrequency = Mathf.Lerp(muffle.cutoffFrequency, target, 0.6f);
            if (!clear) source.volume *= 0.65f;
        }
    }
}
