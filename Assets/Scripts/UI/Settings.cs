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
        /// <summary>Colour-blind telegraphs: red danger becomes magenta, green poison becomes yellow.</summary>
        public static bool ColorBlind { get => (colorBlind ??= Load("deepbound.colorblind", 0f)) > 0.5f; set { colorBlind = value ? 1f : 0f; Save("deepbound.colorblind", colorBlind.Value); } }
        private static float? colorBlind;
        /// <summary>Sprint latches on a press (until you stop pushing forward) instead of being held.</summary>
        public static bool ToggleSprint { get => (toggleSprint ??= Load("deepbound.togglesprint", 0f)) > 0.5f; set { toggleSprint = value ? 1f : 0f; Save("deepbound.togglesprint", toggleSprint.Value); } }
        private static float? toggleSprint;
        public static bool ShowFps { get => (fps ??= Load(FpsKey, 0f)) > 0.5f; set { fps = value ? 1f : 0f; Save(FpsKey, fps.Value); } }
        public static bool ShowTips { get => (tips ??= Load(TipsKey, 1f)) > 0.5f; set { tips = value ? 1f : 0f; Save(TipsKey, tips.Value); } }

        // Proximity voice.
        private const string VoiceModeKey = "deepbound.voicemode", VoiceVolKey = "deepbound.voicevol", MicThreshKey = "deepbound.micthresh", MicGainKey = "deepbound.micgain";
        private static float? voiceMode, voiceVol, micThresh, micGain;

        /// <summary>0 = open mic (voice-activated), 1 = push to talk (hold T), 2 = off.</summary>
        public static int VoiceMode
        {
            get => Mathf.RoundToInt(voiceMode ??= Load(VoiceModeKey, 0f));
            set { voiceMode = Mathf.Clamp(value, 0, 2); Save(VoiceModeKey, voiceMode.Value); }
        }

        public static readonly string[] VoiceModeNames = { "Open mic", "Push to talk (T)", "Off" };

        /// <summary>Music and room tone, 0-1 (under the master volume).</summary>
        public static float MusicVolume
        {
            get => musicVol ??= Load("deepbound.musicvol", 0.8f);
            set { musicVol = Mathf.Clamp01(value); Save("deepbound.musicvol", musicVol.Value); }
        }
        private static float? musicVol;

        /// <summary>Sound effects (every <see cref="Art.Sfx"/> voice and the torch crackle), 0-1 (under the master volume).</summary>
        public static float SfxVolume
        {
            get => sfxVol ??= Load("deepbound.sfxvol", 1f);
            set { sfxVol = Mathf.Clamp01(value); Save("deepbound.sfxvol", sfxVol.Value); }
        }
        private static float? sfxVol;

        /// <summary>How loud other players' voices are, 0-1.5.</summary>
        public static float VoiceVolume
        {
            get => voiceVol ??= Load(VoiceVolKey, 1f);
            set { voiceVol = Mathf.Clamp(value, 0f, 1.5f); Save(VoiceVolKey, voiceVol.Value); }
        }

        /// <summary>Open mic: the microphone level (RMS) that opens it.</summary>
        public static float MicThreshold
        {
            get => micThresh ??= Load(MicThreshKey, 0.025f);
            set { micThresh = Mathf.Clamp(value, 0.002f, 0.08f); Save(MicThreshKey, micThresh.Value); }
        }

        public static float MicGain
        {
            get => micGain ??= Load(MicGainKey, 1f);
            set { micGain = Mathf.Clamp(value, 0.25f, 4f); Save(MicGainKey, micGain.Value); }
        }

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

        public static void Apply()
        {
            AudioListener.volume = Volume;
            ApplyGraphics();
        }

        // ---- Brightness ----
        private static float? brightness;
        private static UnityEngine.Rendering.Volume brightnessVolume;
        private static UnityEngine.Rendering.Universal.LiftGammaGain brightnessGrade;

        /// <summary>-0.3 (darker) .. +0.4 (brighter), 0 the look as made. A gamma offset, so the dark keeps its shape.</summary>
        public static float Brightness
        {
            get => brightness ??= Load("deepbound.brightness", 0f);
            set { brightness = Mathf.Clamp(value, -0.3f, 0.4f); Save("deepbound.brightness", brightness.Value); ApplyBrightness(); }
        }

        public static void ApplyBrightness()
        {
            if (brightnessVolume == null)
            {
                var go = new GameObject("Brightness");
                Object.DontDestroyOnLoad(go);
                brightnessVolume = go.AddComponent<UnityEngine.Rendering.Volume>();
                brightnessVolume.isGlobal = true;
                brightnessVolume.priority = 30f;
                var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                brightnessGrade = profile.Add<UnityEngine.Rendering.Universal.LiftGammaGain>(true);
                brightnessVolume.sharedProfile = profile;
            }
            float b = Brightness;
            brightnessGrade.gamma.Override(new Vector4(1f, 1f, 1f, b));
            brightnessVolume.weight = Mathf.Abs(b) < 0.001f ? 0f : 1f;
        }

        // ---- Graphics ----
        private const string GraphicsKey = "deepbound.graphics", VsyncKey = "deepbound.vsync";
        private static float? graphics, vsync;
        public static readonly string[] GraphicsNames = { "Low", "Medium", "High" };

        /// <summary>0 low, 1 medium, 2 high (the full look).</summary>
        public static int GraphicsQuality
        {
            get => Mathf.Clamp(Mathf.RoundToInt(graphics ??= Load(GraphicsKey, 2f)), 0, 2);
            set { graphics = Mathf.Clamp(value, 0, 2); Save(GraphicsKey, graphics.Value); ApplyGraphics(); }
        }

        public static bool VSync
        {
            get => (vsync ??= Load(VsyncKey, 1f)) > 0.5f;
            set { vsync = value ? 1f : 0f; Save(VsyncKey, vsync.Value); ApplyGraphics(); }
        }

        private static float? baseShadowDistance;

        /// <summary>
        /// Push the graphics preset into the running pipeline: render scale, shadow distance (0 = no
        /// shadows) and the ambient-occlusion pass; and the frame cap (VSync, or uncapped to 240).
        /// </summary>
        public static void ApplyGraphics()
        {
            ApplyBrightness();
            int q = GraphicsQuality;
            // -uncapped (perf measurement runs): no VSync, no frame cap, whatever the player saved.
            bool uncapped = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-uncapped") >= 0;
            QualitySettings.vSyncCount = VSync && !uncapped ? 1 : 0;
            Application.targetFrameRate = uncapped ? -1 : VSync ? -1 : 240;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                baseShadowDistance ??= urp.shadowDistance;
                urp.renderScale = q == 0 ? 0.7f : q == 1 ? 0.85f : 1f;
                // Below full resolution, upscale with FSR 1 (sharper than bilinear, nearly free), lightly sharpened.
                try
                {
                    urp.upscalingFilter = urp.renderScale < 0.99f ? UnityEngine.Rendering.Universal.UpscalingFilterSelection.FSR : UnityEngine.Rendering.Universal.UpscalingFilterSelection.Auto;
                    urp.fsrOverrideSharpness = true;
                    urp.fsrSharpness = 0.75f;
                }
                catch { /* older pipeline: keep the default filter */ }
                urp.shadowDistance = q == 0 ? 0f : q == 1 ? Mathf.Min(baseShadowDistance.Value, 25f) : baseShadowDistance.Value;
                // The SSAO renderer feature, found by type on the active renderer.
                try
                {
                    var renderer = urp.GetRenderer(0);
                    var prop = typeof(UnityEngine.Rendering.Universal.ScriptableRenderer).GetProperty("rendererFeatures",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (renderer != null && prop != null && prop.GetValue(renderer) is System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> features)
                        foreach (var f in features)
                            if (f != null && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion")) f.SetActive(q == 2);
                }
                catch { /* the look is optional; never stop the game over it */ }
            }
        }

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
