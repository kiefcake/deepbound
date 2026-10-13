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
