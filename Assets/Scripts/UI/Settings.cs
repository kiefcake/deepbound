using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Player options, persisted in PlayerPrefs. Wrapped in try/catch: PlayerPrefs can
    /// throw on locked-down machines, and options must never stop the game from starting.
    /// </summary>
    public static class Settings
    {
        private const string SensKey = "deepbound.sensitivity", VolumeKey = "deepbound.volume", FovKey = "deepbound.fov";
        public const string FullscreenKey = "deepbound.fullscreen";

        private static float? sensitivity, volume, fov;

        /// <summary>Multiplier on mouse look. 1 = default.</summary>
        public static float MouseSensitivity
        {
            get => sensitivity ??= Load(SensKey, 1f);
            set { sensitivity = Mathf.Clamp(value, 0.2f, 3f); Save(SensKey, sensitivity.Value); }
        }

        /// <summary>Master volume, 0-1.</summary>
        public static float Volume
        {
            get => volume ??= Load(VolumeKey, 0.8f);
            set { volume = Mathf.Clamp01(value); Save(VolumeKey, volume.Value); AudioListener.volume = volume.Value; }
        }

        public static float FieldOfView
        {
            get => fov ??= Load(FovKey, 78f);
            set { fov = Mathf.Clamp(value, 60f, 100f); Save(FovKey, fov.Value); }
        }

        // Comfort and accessibility.
        private const string ShakeKey = "deepbound.shake", NumbersKey = "deepbound.numbers", FlashKey = "deepbound.reduceflash",
            FpsKey = "deepbound.fps", TipsKey = "deepbound.tips";
        private static float? shakeScale, numbers, reduceFlash, fps, tips;

        /// <summary>Camera shake strength, 0 (off) to 1.5.</summary>
        public static float ShakeScale
        {
            get => shakeScale ??= Load(ShakeKey, 1f);
            set { shakeScale = Mathf.Clamp(value, 0f, 1.5f); Save(ShakeKey, shakeScale.Value); }
        }

        public static bool DamageNumbers { get => (numbers ??= Load(NumbersKey, 1f)) > 0.5f; set { numbers = value ? 1f : 0f; Save(NumbersKey, numbers.Value); } }
        public static bool ReduceFlashing { get => (reduceFlash ??= Load(FlashKey, 0f)) > 0.5f; set { reduceFlash = value ? 1f : 0f; Save(FlashKey, reduceFlash.Value); } }
        public static bool ShowFps { get => (fps ??= Load(FpsKey, 0f)) > 0.5f; set { fps = value ? 1f : 0f; Save(FpsKey, fps.Value); } }
        public static bool ShowTips { get => (tips ??= Load(TipsKey, 1f)) > 0.5f; set { tips = value ? 1f : 0f; Save(TipsKey, tips.Value); } }

        /// <summary>Has this one-time tip been shown before (on this machine)?</summary>
        public static bool TipSeen(string key)
        {
            try { return PlayerPrefs.GetInt("deepbound.tip." + key, 0) == 1; } catch { return false; }
        }

        public static void MarkTipSeen(string key)
        {
            try { PlayerPrefs.SetInt("deepbound.tip." + key, 1); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>Show every tip again.</summary>
        public static void ResetTips()
        {
            foreach (var k in UI.Tips.Keys)
                try { PlayerPrefs.DeleteKey("deepbound.tip." + k); } catch { }
            try { PlayerPrefs.Save(); } catch { }
        }

        public static void Apply() => AudioListener.volume = Volume;

        private static float Load(string key, float fallback)
        {
            try { return PlayerPrefs.GetFloat(key, fallback); }
            catch { return fallback; }
        }

        private static void Save(string key, float value)
        {
            try { PlayerPrefs.SetFloat(key, value); PlayerPrefs.Save(); }
            catch { /* options just will not persist */ }
        }
    }
}
