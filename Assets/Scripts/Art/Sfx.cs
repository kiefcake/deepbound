using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public enum Sound { Swing, Hit, Block, Bash, Rattle, BoneBreak, Slam, WarCry, Hurt, Revive, Summon, Coins, Potion, Purchase, Bow, Fire, Frost, Shatter, Whisper, Dodge, Relic, Step, Splash, Meow, Purr, Drink, SlotSpin, Card, Caw, Squeak, Heartbeat, KillTick, Creak, Toll, Cleared, Pipe, Graze,

        Growl, Roar, MonsterDie, MonsterAttack, Door, Chest, Gear, PageTurn,
        Chips, Dice, UiClick, UiOpen, UiClose, UiError, Chain, StepSoft, Shade, Slime, Unsheathe }

    public static class Sfx
    {
        private static readonly Dictionary<Sound, AudioClip[]> Recorded = new Dictionary<Sound, AudioClip[]>();
        private static readonly HashSet<Sound> missing = new HashSet<Sound>();

        private static AudioClip[] RecordedFor(Sound sound)
        {
            if (!Recorded.TryGetValue(sound, out var clips))
            {
                clips = Resources.LoadAll<AudioClip>("Audio/Sfx/" + sound);
                Recorded[sound] = clips;
            }
            return clips;
        }

        private static float RecordedGain(Sound s) => s == Sound.Step || s == Sound.StepSoft ? 0.35f : s == Sound.UiClick || s == Sound.UiOpen || s == Sound.UiClose || s == Sound.UiError ? 0.45f : s == Sound.Growl || s == Sound.Roar || s == Sound.MonsterDie || s == Sound.MonsterAttack ? 0.75f
            : s == Sound.Fire ? 0.6f : s == Sound.Swing ? 0.55f : s == Sound.Splash ? 0.5f : s == Sound.Shade ? 0.6f : s == Sound.Slime ? 0.7f : s == Sound.Hit || s == Sound.Hurt ? 0.7f : s == Sound.Coins || s == Sound.Purchase ? 0.6f

            : s == Sound.WarCry ? 0.65f : s == Sound.Whisper ? 0.4f : s == Sound.Meow || s == Sound.Purr || s == Sound.Caw ? 0.6f : s == Sound.Squeak ? 0.45f
            : s == Sound.Cleared ? 0.45f : s == Sound.Pipe ? 0.55f : s == Sound.Graze || s == Sound.KillTick ? 0.6f : s == Sound.Dodge ? 0.5f : 0.8f;

        public static void Play(Sound sound, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var rec = RecordedFor(sound);
            if (rec.Length == 0)
            {
                if (missing.Add(sound)) Debug.LogWarning("[Sfx] no recording for " + sound + " (Resources/Audio/Sfx/" + sound + "/)");
                return;
            }
            var clip = rec[Random.Range(0, rec.Length)];
            volume *= RecordedGain(sound);

            if (Time.frameCount != countFrame) { countFrame = Time.frameCount; sameFrame.Clear(); }
            sameFrame.TryGetValue(sound, out int n);
            sameFrame[sound] = n + 1;
            if (n >= MaxSameFrame) return;

            if (Background(sound) && Busy() >= MaxVoices - BackgroundReserve) return;
            var src = Borrow();

            if (src == null && Vital(sound)) src = Steal();
            if (src == null) return;
            src.transform.position = position;
            src.clip = clip;
            src.volume = volume * UI.Settings.SfxVolume;
            src.pitch = pitch * Random.Range(0.92f, 1.08f);
            src.spatialBlend = 1f;
            src.minDistance = 2f;
            src.maxDistance = 35f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;

            bool dry = Dry(sound);
            src.reverbZoneMix = dry ? 0f : 1f;
            float cutoff = 22000f;
            if (!dry && TryEar(out var ear))
            {
                float dist = Vector3.Distance(ear, position);
                cutoff = Mathf.Lerp(22000f, AirCutoff, Mathf.InverseLerp(6f, src.maxDistance, dist));
                if (dist > 2.5f && Occluded(ear, position))
                {
                    cutoff = WallCutoff;
                    src.volume *= WallGain;
                }
            }
            var lp = src.GetComponent<AudioLowPassFilter>();
            if (lp != null) lp.cutoffFrequency = cutoff;
            LastCutoff = cutoff;
            src.Play();
        }

        public const float WallCutoff = 1100f, WallGain = 0.55f, AirCutoff = 6500f;
        public const int BackgroundReserve = 14;

        public static float LastCutoff { get; private set; } = 22000f;

        private static bool Dry(Sound s) => s == Sound.UiClick || s == Sound.UiOpen || s == Sound.UiClose || s == Sound.UiError;
        private static bool Background(Sound s) => s == Sound.Step || s == Sound.StepSoft || s == Sound.Squeak;

        private static int Busy()
        {
            int n = 0;
            foreach (var v in voices) if (v != null && v.isPlaying) n++;
            return n;
        }

        private static bool TryEar(out Vector3 ear)
        {
            var cam = ViewCamera.Current;
            if (cam != null) { ear = cam.transform.position; return true; }
            ear = Vector3.zero;
            return false;
        }

        public static bool Occluded(Vector3 ear, Vector3 at)
        {
            var to = at + Vector3.up * 0.6f;
            if (!Physics.Linecast(ear, to, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore)) return false;
            if ((to - hit.point).sqrMagnitude < 0.35f * 0.35f) return false;
            var c = hit.collider;
            if (c.attachedRigidbody != null || c is CharacterController) return false;
            return c.GetComponentInParent<Combat.Health>() == null;
        }

        public const int MaxVoices = 48, MaxSameFrame = 3;
        private static readonly List<AudioSource> voices = new List<AudioSource>();
        private static readonly Dictionary<Sound, int> sameFrame = new Dictionary<Sound, int>();
        private static int countFrame = -1, nextVoice;
        private static Transform voiceRoot;

        private static bool Vital(Sound s) => s == Sound.Hurt || s == Sound.Heartbeat || s == Sound.Revive || s == Sound.Roar || s == Sound.Slam || s == Sound.UiError || s == Sound.Toll;

        private static AudioSource Steal()
        {
            AudioSource quietest = null;
            foreach (var v in voices)
                if (v != null && (quietest == null || v.volume < quietest.volume)) quietest = v;
            if (quietest != null) quietest.Stop();
            return quietest;
        }

        private static AudioSource Borrow()
        {
            if (voiceRoot == null)
            {
                voices.Clear();
                var root = new GameObject("Sfx Voices");
                Object.DontDestroyOnLoad(root);
                voiceRoot = root.transform;
            }
            for (int i = 0; i < voices.Count; i++)
            {
                int k = (nextVoice + i) % voices.Count;
                var v = voices[k];
                if (v != null && !v.isPlaying) { nextVoice = k + 1; return v; }
            }
            if (voices.Count >= MaxVoices) return null;
            var go = new GameObject("Sfx");
            go.transform.SetParent(voiceRoot, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 22000f;
            voices.Add(s);
            return s;
        }

        private static AudioListener listener;

        public static void Play2D(Sound sound, float volume = 1f)
        {
            if (listener == null || !listener.isActiveAndEnabled) listener = Object.FindFirstObjectByType<AudioListener>();
            Play(sound, listener != null ? listener.transform.position : Vector3.zero, volume);
        }
    }
}
