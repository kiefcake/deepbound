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
        /// <summary>Off (default): only the essential tips (UI.Tips.Essential). On: every mechanic's tip, as before 2026-11-14.</summary>
        public static bool AllTips { get => (allTips ??= Load("deepbound.alltips", 0f)) > 0.5f; set { allTips = value ? 1f : 0f; Save("deepbound.alltips", allTips.Value); } }
        private static float? allTips;

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
            get => micGain ??= Load(MicGainKey, 1.5f);
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

        // ---- Retro look ----
        private static float? retro, retroSize;
        public static readonly string[] RetroNames = { "Fine", "Classic", "Chunky" };
        /// <summary>Lines of the small picture for Fine / Classic / Chunky.</summary>
        public static readonly int[] RetroLines = { 480, 360, 270 };

        /// <summary>The retro gothic look: the world drawn small with chunky pixels and a dithered palette (UI stays sharp).</summary>
        public static bool Retro
        {
            get => (retro ??= Load("deepbound.retro", 1f)) > 0.5f;
            set { retro = value ? 1f : 0f; Save("deepbound.retro", retro.Value); ApplyGraphics(); }
        }

        /// <summary>0 Fine, 1 Classic, 2 Chunky.</summary>
        public static int RetroSize
        {
            get => ArgRetroSize >= 0 ? ArgRetroSize : Mathf.Clamp(Mathf.RoundToInt(retroSize ??= Load("deepbound.retrosize", 1f)), 0, 2);
            set { retroSize = Mathf.Clamp(value, 0, 2); Save("deepbound.retrosize", retroSize.Value); ApplyGraphics(); }
        }

        /// <summary>`-retrosize 0|1|2` on the command line (screenshot runs): that size for this session, nothing saved.</summary>
        private static int? argRetroSize;
        private static int ArgRetroSize
        {
            get
            {
                if (argRetroSize == null)
                {
                    argRetroSize = -1;
                    var args = System.Environment.GetCommandLineArgs();
                    for (int i = 0; i + 1 < args.Length; i++)
                        if (args[i] == "-retrosize" && int.TryParse(args[i + 1], out int v)) argRetroSize = Mathf.Clamp(v, 0, 2);
                }
                return argRetroSize.Value;
            }
        }

        /// <summary>The render scale the retro look wants at this window height (never above 1).</summary>
        public static float RetroScale => Mathf.Clamp(RetroLines[RetroSize] / (float)Mathf.Max(1, Screen.height), 0.15f, 1f);

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
            brightnessVolume.weight = 0f; // the retro pass applies brightness now (UI/RetroLook); this volume never showed
        }

        // ---- Screen (2026-11-01, "add resolution settings, as well as more setting options") ----
        private const string ResWKey = "deepbound.res.w", ResHKey = "deepbound.res.h", WindowKey = "deepbound.windowmode", UiScaleKey = "deepbound.uiscale";
        public static readonly string[] WindowModeNames = { "Fullscreen", "Borderless", "Windowed" };

        /// <summary>0 exclusive fullscreen, 1 borderless (the default), 2 a window. An old save's "fullscreen off" means a window.</summary>
        public static int WindowMode
        {
            get
            {
                try { return Mathf.Clamp(PlayerPrefs.GetInt(WindowKey, PlayerPrefs.GetInt(FullscreenKey, 1) == 1 ? 1 : 2), 0, 2); }
                catch { return 1; }
            }
        }

        /// <summary>What the player chose, or the desktop's size (a window: 1280 x 720) if they never did.</summary>
        public static (int w, int h) ChosenResolution
        {
            get
            {
                int w = 0, h = 0;
                try { w = PlayerPrefs.GetInt(ResWKey, 0); h = PlayerPrefs.GetInt(ResHKey, 0); } catch { }
                if (w > 0 && h > 0) return (w, h);
                return WindowMode == 2 ? (1280, 720) : (Display.main.systemWidth, Display.main.systemHeight);
            }
        }

        /// <summary>Every size the monitor offers (each once, whatever its refresh rates), smallest first, the current one included.</summary>
        public static System.Collections.Generic.List<Vector2Int> Resolutions()
        {
            var list = new System.Collections.Generic.List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (r.height >= 600 && !list.Contains(v)) list.Add(v);
            }
            var (cw, ch) = ChosenResolution;
            foreach (var v in new[] { new Vector2Int(cw, ch), new Vector2Int(Display.main.systemWidth, Display.main.systemHeight) })
                if (v.x > 0 && !list.Contains(v)) list.Add(v);
            list.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return list;
        }

        public static void SetDisplay(int w, int h, int mode)
        {
            try
            {
                PlayerPrefs.SetInt(ResWKey, w);
                PlayerPrefs.SetInt(ResHKey, h);
                PlayerPrefs.SetInt(WindowKey, mode);
                PlayerPrefs.Save();
            }
            catch { /* it still applies for this session */ }
            ApplyDisplay(w, h, mode);
        }

        /// <summary>At start-up, and from APPLY. RetroLook notices the new size and rebuilds its pass.</summary>
        public static void ApplyDisplay() { var (w, h) = ChosenResolution; ApplyDisplay(w, h, WindowMode); }

        private static void ApplyDisplay(int w, int h, int mode) =>
            Screen.SetResolution(w, h, mode == 0 ? FullScreenMode.ExclusiveFullScreen : mode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

        public static readonly string[] UiScaleNames = { "75%", "90%", "100%", "115%", "130%", "150%" };
        private static readonly float[] UiScales = { 0.75f, 0.9f, 1f, 1.15f, 1.3f, 1.5f };
        private static float? uiScaleIndex;
        /// <summary>Steps, not a slider: the menu you are dragging in would resize under the pointer.</summary>
        public static int UiScaleIndex
        {
            get => Mathf.Clamp(Mathf.RoundToInt(uiScaleIndex ??= Load(UiScaleKey, 2f)), 0, UiScales.Length - 1);
            set { uiScaleIndex = value; Save(UiScaleKey, value); }
        }
        public static float UiScale => UiScales[UiScaleIndex];

        // ---- HUD, aim and comfort ----
        private static float? minimapSize, showMinimap, crossSize, crossColor, invertLook, padSens, headBob, muteBackground, frameLimit;
        public static float MinimapSize { get => minimapSize ??= Load("deepbound.minimapsize", 1f); set { if (Mathf.Approximately(value, MinimapSize)) return; minimapSize = Mathf.Clamp(value, 0.6f, 1.6f); Save("deepbound.minimapsize", minimapSize.Value); } }
        public static bool ShowMinimap { get => (showMinimap ??= Load("deepbound.showminimap", 1f)) > 0.5f; set { if (value == ShowMinimap) return; showMinimap = value ? 1f : 0f; Save("deepbound.showminimap", showMinimap.Value); } }
        public static float CrosshairSize { get => crossSize ??= Load("deepbound.crosssize", 1f); set { if (Mathf.Approximately(value, CrosshairSize)) return; crossSize = Mathf.Clamp(value, 0.5f, 2.5f); Save("deepbound.crosssize", crossSize.Value); } }
        public static readonly string[] CrosshairColorNames = { "White", "Green", "Yellow", "Cyan", "Pink" };
        private static readonly Color[] CrosshairColors = { Color.white, new Color(0.4f, 1f, 0.4f), new Color(1f, 0.92f, 0.3f), new Color(0.35f, 0.95f, 1f), new Color(1f, 0.45f, 0.85f) };
        public static int CrosshairColorIndex { get => Mathf.Clamp(Mathf.RoundToInt(crossColor ??= Load("deepbound.crosscolor", 0f)), 0, CrosshairColors.Length - 1); set { if (value == CrosshairColorIndex) return; crossColor = value; Save("deepbound.crosscolor", value); } }
        public static Color CrosshairColor => CrosshairColors[CrosshairColorIndex];
        public static bool InvertLook { get => (invertLook ??= Load("deepbound.invertlook", 0f)) > 0.5f; set { if (value == InvertLook) return; invertLook = value ? 1f : 0f; Save("deepbound.invertlook", invertLook.Value); } }
        public static float PadSensitivity { get => padSens ??= Load("deepbound.padsens", 1f); set { if (Mathf.Approximately(value, PadSensitivity)) return; padSens = Mathf.Clamp(value, 0.3f, 2.5f); Save("deepbound.padsens", padSens.Value); } }
        public static bool HeadBob { get => (headBob ??= Load("deepbound.headbob", 1f)) > 0.5f; set { if (value == HeadBob) return; headBob = value ? 1f : 0f; Save("deepbound.headbob", headBob.Value); } }
        public static bool MuteInBackground { get => (muteBackground ??= Load("deepbound.mutebg", 0f)) > 0.5f; set { if (value == MuteInBackground) return; muteBackground = value ? 1f : 0f; Save("deepbound.mutebg", muteBackground.Value); } }

        public static readonly string[] FrameLimitNames = { "30", "60", "120", "144", "240", "None" };
        private static readonly int[] FrameLimits = { 30, 60, 120, 144, 240, -1 };
        /// <summary>The cap while VSync is off (240, as it always was, by default).</summary>
        public static int FrameLimit
        {
            get => Mathf.Clamp(Mathf.RoundToInt(frameLimit ??= Load("deepbound.framelimit", 4f)), 0, FrameLimits.Length - 1);
            set { if (value == FrameLimit) return; frameLimit = value; Save("deepbound.framelimit", value); ApplyGraphics(); }
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
            set { if (value == VSync) return; vsync = value ? 1f : 0f; Save(VsyncKey, vsync.Value); ApplyGraphics(); }
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
            Application.targetFrameRate = uncapped ? -1 : VSync ? -1 : FrameLimits[FrameLimit];
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                baseShadowDistance ??= urp.shadowDistance;
                urp.renderScale = q == 0 ? 0.7f : q == 1 ? 0.85f : 1f;
                // Retro: the world is drawn small whatever the preset (cheaper too), and blown up with hard pixel edges.
                if (Retro) urp.renderScale = RetroScale;
                // Below full resolution, upscale with FSR 1 (sharper than bilinear, nearly free), lightly sharpened.
                try
                {
                    urp.upscalingFilter = Retro ? UnityEngine.Rendering.Universal.UpscalingFilterSelection.Point
                        : urp.renderScale < 0.99f ? UnityEngine.Rendering.Universal.UpscalingFilterSelection.FSR : UnityEngine.Rendering.Universal.UpscalingFilterSelection.Auto;
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
