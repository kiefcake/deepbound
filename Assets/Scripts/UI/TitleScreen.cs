using Deepbound.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The start screen: the title over the knight-and-king backdrop, and the way into a
    /// session. Play hosts a new party; Join connects to a friend's; Options holds the
    /// handful of settings that matter for a first-person game.
    ///
    /// Drawn only while not connected. Joining a party will move to Steam invites later
    /// (design doc 2.2.1) — the address field is the development stand-in.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        private enum Page { Main, Join, Options }

        private NetworkLauncher launcher;
        private Page page;
        private string address = "127.0.0.1";
        private float shownAt;

        public void Init(NetworkLauncher owner)
        {
            launcher = owner;
            shownAt = Time.time;
            Settings.Apply();
        }

        private bool Connected => NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        private void OnGUI()
        {
            UiSkin.Apply();
            if (launcher == null || Connected) return;
            float w = Screen.width, h = Screen.height;
            float fade = Mathf.Clamp01((Time.time - shownAt) / 1.2f);

            // Darken the edges so the title and menu read over the 3D scene.
            UiSkin.Rect(new Rect(0, 0, w * 0.42f, h), new Color(0f, 0f, 0f, 0.55f * fade));
            UiSkin.Rect(new Rect(0, 0, w, h * 0.12f), new Color(0f, 0f, 0f, 0.35f * fade));

            float left = Mathf.Max(40f, w * 0.06f);
            UiSkin.Shadowed(new Rect(left - 20, h * 0.12f, 560, 90), "DEEPBOUND", new GUIStyle(UiSkin.Title) { alignment = TextAnchor.MiddleLeft }, fade);
            UiSkin.Shadowed(new Rect(left, h * 0.12f + 84, 560, 30), "Descend together. Nothing comes back up but your name.",
                new GUIStyle(UiSkin.Body) { fontSize = 17, fontStyle = FontStyle.Italic }, fade);

            var area = new Rect(left, h * 0.36f, 340, h * 0.5f);
            GUILayout.BeginArea(area);
            switch (page)
            {
                case Page.Main: DrawMain(); break;
                case Page.Join: DrawJoin(); break;
                case Page.Options: DrawOptions(); break;
            }

            if (!string.IsNullOrEmpty(launcher.Status))
            {
                GUILayout.Space(12);
                GUILayout.Label(launcher.Status, new GUIStyle(UiSkin.Body) { normal = { textColor = UiSkin.Ember } });
            }
            GUILayout.EndArea();

            if (page == Page.Main) DrawWhatsNew(w, h, fade);

            GUI.Label(new Rect(w - 330, h - 34, 310, 24), "1-4 player co-op  ·  prototype build",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.LowerRight });
        }

        /// <summary>What the latest builds added, so a returning player knows where to look (keep it short).</summary>
        private static readonly string[] WhatsNew =
        {
            "THE BONE PIPER plays bullet patterns: Rose, Scissors, March...",
            "GRAZE: shots that slip past you close charge your ultimate",
            "Cloak PATTERNS at the tailor (skulls, bones), brighter dyes",
            "THE TALLYMAN hunts those who linger (holy water holds him)",
            "New dead: Choir Acolyte, Abyssal Eye; Frenzied, Gravebound",
            "Drag a downed friend clear (G); pointers to the fallen",
            "Bosses make an entrance; the grave lift carries you up",
            "DAILY DESCENT (Corwin) and the HALL OF RECORDS (camp)",
            "Rebind keys (Options > Controls), gamepads, colour-blind mode",
            "Corwin's CLIMBING ROPE; TORMENT once you finish a descent",
        };

        private static void DrawWhatsNew(float w, float h, float fade)
        {
            float cw = 500f, ch = 44f + WhatsNew.Length * 22f;
            var r = new Rect(w - cw - 40f, h - ch - 70f, cw, ch);
            UiSkin.Rect(r, new Color(0f, 0f, 0f, 0.55f * fade));
            UiSkin.Rect(new Rect(r.x, r.y, 3, r.height), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, fade));
            GUI.Label(new Rect(r.x + 16, r.y + 10, cw - 24, 22), "NEW IN THIS BUILD", new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, fade) } });
            var st = new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.9f, 0.86f, 0.78f, fade) } };
            for (int i = 0; i < WhatsNew.Length; i++)
                GUI.Label(new Rect(r.x + 16, r.y + 36 + i * 22, cw - 24, 20), "-  " + WhatsNew[i], st);
        }

        private void DrawMain()
        {
            if (GUILayout.Button("PLAY", UiSkin.ButtonBig, GUILayout.Height(58))) launcher.StartHost();
            GUILayout.Label("Host a new party. Friends join you.", UiSkin.Small);
            GUILayout.Space(10);
            if (GUILayout.Button("JOIN A PARTY", UiSkin.Button, GUILayout.Height(44))) page = Page.Join;
            GUILayout.Space(6);
            if (GUILayout.Button("OPTIONS", UiSkin.Button, GUILayout.Height(44))) page = Page.Options;
            GUILayout.Space(6);
            if (GUILayout.Button("QUIT", UiSkin.Button, GUILayout.Height(44))) Application.Quit();
        }

        private void DrawJoin()
        {
            GUILayout.Label("JOIN A PARTY", UiSkin.Heading);
            GUILayout.Label("Your friend's address (they will see it when they host on the same network):", UiSkin.Small);
            GUILayout.Space(6);
            address = GUILayout.TextField(address, UiSkin.Field, GUILayout.Height(40));
            GUILayout.Space(8);
            if (GUILayout.Button("CONNECT", UiSkin.ButtonBig, GUILayout.Height(52))) launcher.StartClient(address);
            GUILayout.Space(6);
            if (GUILayout.Button("BACK", UiSkin.Button, GUILayout.Height(38))) page = Page.Main;
        }

        private void DrawOptions()
        {
            GUILayout.Label("OPTIONS", UiSkin.Heading);
            GUILayout.Space(6);

            GUILayout.Label("Mouse sensitivity  " + Settings.MouseSensitivity.ToString("F2"), UiSkin.Body);
            Settings.MouseSensitivity = GUILayout.HorizontalSlider(Settings.MouseSensitivity, 0.2f, 3f);
            GUILayout.Space(10);

            GUILayout.Label("Volume  " + Mathf.RoundToInt(Settings.Volume * 100f) + "%", UiSkin.Body);
            Settings.Volume = GUILayout.HorizontalSlider(Settings.Volume, 0f, 1f);
            GUILayout.Space(10);

            GUILayout.Label("Field of view  " + Mathf.RoundToInt(Settings.FieldOfView), UiSkin.Body);
            Settings.FieldOfView = GUILayout.HorizontalSlider(Settings.FieldOfView, 60f, 100f);
            GUILayout.Space(10);

            bool full = Screen.fullScreen;
            bool want = GUILayout.Toggle(full, "  Fullscreen", new GUIStyle(GUI.skin.toggle) { fontSize = 15 });
            if (want != full)
            {
                Screen.fullScreen = want;
                PlayerPrefs.SetInt(Settings.FullscreenKey, want ? 1 : 0);
                PlayerPrefs.Save();
            }

            GUILayout.Space(14);
            if (GUILayout.Button("BACK", UiSkin.Button, GUILayout.Height(38))) page = Page.Main;
        }
    }
}
