using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public enum Sound { Swing, Hit, Block, Bash, Rattle, BoneBreak, Slam, WarCry, Hurt, Revive, Summon, Coins, Potion, Purchase, Bow, Fire, Frost, Shatter, Whisper, Dodge, Relic, Step, Splash, Meow, Purr, Drink, SlotSpin, Card, Caw, Squeak, Heartbeat, KillTick, Creak, Toll, Cleared, Pipe, Graze,
        // 2026-10-24: added with recordings only.
        Growl, Roar, MonsterDie, MonsterAttack, Door, Chest, Gear, PageTurn,
        Chips, Dice, UiClick, UiOpen, UiClose, UiError, Chain, StepSoft, Shade, Slime, Unsheathe }

    /// <summary>
    /// Every sound in the game, all recorded: CC0 packs from Kenney and OpenGameArt in
    /// Resources/Audio/Sfx/&lt;Sound&gt;/, several variants each (see docs/CREDITS.md). Until 2026-11-01 a sound with no
    /// recording was synthesised from a recipe; the user asked for "no more coded noise", so the last 23 synth-only sounds
    /// got recordings and the synthesiser is gone: a sound with no folder plays nothing and warns once.
    ///
    /// <para>
    /// Playback picks a random variant and a slight random pitch so ten identical swings do not sound like a machine gun.
    /// </para>
    /// </summary>
    public static class Sfx
    {
        private static readonly Dictionary<Sound, AudioClip[]> Recorded = new Dictionary<Sound, AudioClip[]>();
        private static readonly HashSet<Sound> missing = new HashSet<Sound>();

        /// <summary>The recorded variants of a sound.</summary>
        private static AudioClip[] RecordedFor(Sound sound)
        {
            if (!Recorded.TryGetValue(sound, out var clips))
            {
                clips = Resources.LoadAll<AudioClip>("Audio/Sfx/" + sound);
                Recorded[sound] = clips;
            }
            return clips;
        }

        /// <summary>Recordings are mastered at different levels: each is trimmed to sit in the same mix.</summary>
        private static float RecordedGain(Sound s) => s == Sound.Step || s == Sound.StepSoft ? 0.35f : s == Sound.UiClick || s == Sound.UiOpen || s == Sound.UiClose || s == Sound.UiError ? 0.45f : s == Sound.Growl || s == Sound.Roar || s == Sound.MonsterDie || s == Sound.MonsterAttack ? 0.75f
            : s == Sound.Fire ? 0.6f : s == Sound.Swing ? 0.55f : s == Sound.Splash ? 0.5f : s == Sound.Shade ? 0.6f : s == Sound.Slime ? 0.7f : s == Sound.Hit || s == Sound.Hurt ? 0.7f : s == Sound.Coins || s == Sound.Purchase ? 0.6f
            // 2026-11-01's recordings (peak-normalised when installed): voices and moans are loud, ticks are frequent.
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
            // The same sound many times in one frame (a volley landing, a crowd hit by a meteor) plays a few times, louder.
            if (Time.frameCount != countFrame) { countFrame = Time.frameCount; sameFrame.Clear(); }
            sameFrame.TryGetValue(sound, out int n);
            sameFrame[sound] = n + 1;
            if (n >= MaxSameFrame) return;
            var src = Borrow();
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
            src.Play();
        }

        /// <summary>
        /// Voices are pooled (2026-10-24; every sound used to make and destroy its own GameObject): up to
        /// <see cref="MaxVoices"/> at once, a free one reused, the busiest moment drops the newest sound.
        /// </summary>
        public const int MaxVoices = 48, MaxSameFrame = 3;
        private static readonly List<AudioSource> voices = new List<AudioSource>();
        private static readonly Dictionary<Sound, int> sameFrame = new Dictionary<Sound, int>();
        private static int countFrame = -1, nextVoice;
        private static Transform voiceRoot;

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
            voices.Add(s);
            return s;
        }

        /// <summary>Non-spatial, for the local player's own actions and UI.</summary>
        private static AudioListener listener;

        public static void Play2D(Sound sound, float volume = 1f)
        {
            if (listener == null || !listener.isActiveAndEnabled) listener = Object.FindFirstObjectByType<AudioListener>();
            Play(sound, listener != null ? listener.transform.position : Vector3.zero, volume);
        }
    }
}
