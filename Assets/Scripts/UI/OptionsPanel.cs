using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The options, shared by the pause menu and the title screen (the title used to have only four of them): tabs for
    /// the game, the display and the sound. Draw inside a GUILayout area about 780 x 600.
    /// Resolution and window mode wait for APPLY, so browsing the list does not flip the screen with every click.
    /// </summary>
    public static class OptionsPanel
    {
        private static readonly string[] Tabs = { "GAME", "DISPLAY", "AUDIO" };
        public static int Tab { get; set; }

        private static int pendingW = -1, pendingH, pendingMode;

        private static void Slider(string label, ref float value, float min, float max)
        {
            GUILayout.Label(label, UiSkin.Body);
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Space(8);
        }

        public static void Draw()
        {
            int t = PauseMenu.Choice(Tab, Tabs);
            if (t != Tab) { Tab = t; pendingW = -1; }
            GUILayout.Space(12);
            switch (Tab)
            {
                case 0: DrawGame(); break;
                case 1: DrawDisplay(); break;
                default: DrawAudio(); break;
            }
        }

        private static void DrawGame()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("AIM", UiSkin.Heading);
            float sens = Settings.MouseSensitivity;
            Slider("Mouse sensitivity  " + sens.ToString("F2"), ref sens, 0.2f, 3f);
            Settings.MouseSensitivity = sens;
            float pad = Settings.PadSensitivity;
            Slider("Gamepad look speed  " + pad.ToString("F2"), ref pad, 0.3f, 2.5f);
            Settings.PadSensitivity = pad;
            Settings.InvertLook = PauseMenu.Check(Settings.InvertLook, "Invert vertical look");
            GUILayout.Space(8);
            float fov = Settings.FieldOfView;
            Slider("Field of view  " + Mathf.RoundToInt(fov), ref fov, 60f, 100f);
            Settings.FieldOfView = fov;
            var cam = Player.NetworkPlayer.Local != null ? Player.NetworkPlayer.Local.Camera : null;
            if (cam != null) cam.fieldOfView = Settings.FieldOfView;
            float shake = Settings.ShakeScale;
            Slider("Screen shake  " + Mathf.RoundToInt(shake * 100f) + "%", ref shake, 0f, 1.5f);
            Settings.ShakeScale = shake;
            Settings.HeadBob = PauseMenu.Check(Settings.HeadBob, "Head bob and lean when moving");
            GUILayout.EndVertical();
            GUILayout.Space(50);
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("PLAY", UiSkin.Heading);
            Settings.ToggleSprint = PauseMenu.Check(Settings.ToggleSprint, "Toggle sprint (tap to run, stops when you stop)");
            Settings.DamageNumbers = PauseMenu.Check(Settings.DamageNumbers, "Damage numbers");
            Settings.ReduceFlashing = PauseMenu.Check(Settings.ReduceFlashing, "Reduce flashing effects");
            Settings.ColorBlind = PauseMenu.Check(Settings.ColorBlind, "Colour-blind telegraphs (magenta and yellow)");
            Settings.ShowTips = PauseMenu.Check(Settings.ShowTips, "Show tips");
            GUILayout.Space(6);
            if (GUILayout.Button("Show all tips again", UiSkin.Button, GUILayout.Height(30))) Settings.ResetTips();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void DrawDisplay()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("SCREEN", UiSkin.Heading);
            if (pendingW < 0)
            {
                var (cw, ch) = Settings.ChosenResolution;
                pendingW = cw; pendingH = ch; pendingMode = Settings.WindowMode;
            }
            GUILayout.Label("Window", UiSkin.Body);
            pendingMode = PauseMenu.Choice(pendingMode, Settings.WindowModeNames);
            GUILayout.Space(8);
            GUILayout.Label("Resolution", UiSkin.Body);
            var list = Settings.Resolutions();
            int at = list.FindIndex(r => r.x == pendingW && r.y == pendingH);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", UiSkin.Button, GUILayout.Width(44), GUILayout.Height(32)) && list.Count > 0)
            {
                var r = list[Mathf.Clamp(at < 0 ? 0 : at - 1, 0, list.Count - 1)];
                pendingW = r.x; pendingH = r.y;
            }
            GUILayout.Label(pendingW + " x " + pendingH, new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }, GUILayout.Height(32));
            if (GUILayout.Button(">", UiSkin.Button, GUILayout.Width(44), GUILayout.Height(32)) && list.Count > 0)
            {
                var r = list[Mathf.Clamp(at + 1, 0, list.Count - 1)];
                pendingW = r.x; pendingH = r.y;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            var (nowW, nowH) = Settings.ChosenResolution;
            bool changed = pendingW != nowW || pendingH != nowH || pendingMode != Settings.WindowMode;
            GUI.enabled = changed;
            if (GUILayout.Button(changed ? "APPLY" : "Applied", UiSkin.Button, GUILayout.Height(32))) Settings.SetDisplay(pendingW, pendingH, pendingMode);
            GUI.enabled = true;
            GUILayout.Space(10);
            Settings.VSync = PauseMenu.Check(Settings.VSync, "VSync");
            if (!Settings.VSync)
            {
                GUILayout.Label("Frame limit", UiSkin.Body);
                Settings.FrameLimit = PauseMenu.Choice(Settings.FrameLimit, Settings.FrameLimitNames);
            }
            GUILayout.Space(8);
            GUILayout.Label("Graphics  (lower helps slower PCs)", UiSkin.Body);
            int gq = PauseMenu.Choice(Settings.GraphicsQuality, Settings.GraphicsNames);
            if (gq != Settings.GraphicsQuality) Settings.GraphicsQuality = gq;
            bool rl = PauseMenu.Check(Settings.Retro, "Retro look (small pixels, dithered colour)");
            if (rl != Settings.Retro) Settings.Retro = rl;
            if (Settings.Retro)
            {
                int rs = PauseMenu.Choice(Settings.RetroSize, Settings.RetroNames);
                if (rs != Settings.RetroSize) Settings.RetroSize = rs;
            }
            GUILayout.Space(8);
            float bright = Settings.Brightness;
            Slider("Brightness  " + (bright >= 0f ? "+" : "") + Mathf.RoundToInt(bright * 100f), ref bright, -0.3f, 0.4f);
            Settings.Brightness = bright;
            GUILayout.EndVertical();
            GUILayout.Space(50);
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("INTERFACE", UiSkin.Heading);
            GUILayout.Label("Interface size  (it already follows the resolution; this is on top)", UiSkin.Body);
            int us = PauseMenu.Choice(Settings.UiScaleIndex, Settings.UiScaleNames);
            if (us != Settings.UiScaleIndex) Settings.UiScaleIndex = us;
            GUILayout.Space(10);
            Settings.ShowMinimap = PauseMenu.Check(Settings.ShowMinimap, "Show the minimap (M still opens the full map)");
            float map = Settings.MinimapSize;
            Slider("Minimap size  " + Mathf.RoundToInt(map * 100f) + "%", ref map, 0.6f, 1.6f);
            Settings.MinimapSize = map;
            float cross = Settings.CrosshairSize;
            Slider("Crosshair size  " + Mathf.RoundToInt(cross * 100f) + "%", ref cross, 0.5f, 2.5f);
            Settings.CrosshairSize = cross;
            GUILayout.Label("Crosshair colour", UiSkin.Body);
            Settings.CrosshairColorIndex = PauseMenu.Choice(Settings.CrosshairColorIndex, Settings.CrosshairColorNames);
            GUILayout.Space(8);
            Settings.ShowFps = PauseMenu.Check(Settings.ShowFps, "Show frame rate");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void DrawAudio()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("SOUND", UiSkin.Heading);
            float v = Settings.Volume;
            Slider("Master volume  " + Mathf.RoundToInt(v * 100f) + "%", ref v, 0f, 1f);
            Settings.Volume = v;
            float m = Settings.MusicVolume;
            Slider("Music  " + Mathf.RoundToInt(m * 100f) + "%", ref m, 0f, 1f);
            Settings.MusicVolume = m;
            float s = Settings.SfxVolume;
            Slider("Effects  " + Mathf.RoundToInt(s * 100f) + "%", ref s, 0f, 1f);
            Settings.SfxVolume = s;
            Settings.MuteInBackground = PauseMenu.Check(Settings.MuteInBackground, "Mute when the game is in the background");
            GUILayout.EndVertical();
            GUILayout.Space(50);
            GUILayout.BeginVertical(GUILayout.Width(360));
            PauseMenu.DrawVoiceOptions();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
    }
}
