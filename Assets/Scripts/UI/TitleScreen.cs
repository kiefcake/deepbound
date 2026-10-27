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
        private enum Page { Main, Join, Options, Address }

        private NetworkLauncher launcher;
        private Page page;
        private string address = "127.0.0.1";
        private float shownAt;

        public void Init(NetworkLauncher owner)
        {
            launcher = owner;
            shownAt = Time.time;
            if (!NetworkLauncher.TestRun) address = PlayerPrefs.GetString(NetworkLauncher.LastAddressKey, address);
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

            var area = new Rect(left, h * 0.33f, 360, h * 0.62f);
            GUILayout.BeginArea(area);
            switch (page)
            {
                case Page.Main: DrawMain(); break;
                case Page.Join: if (SteamPlatform.Ready) DrawSteamJoin(); else DrawJoin(); break;
                case Page.Address: DrawJoin(); break;
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
            "CHAMPIONS fight by their epithet: read the name, pick the fight",
            "WARDENS learn a second move and drop their own LEGENDARIES",
            "The Hollow King's DECREES: BE STILL, then BEGONE",
            "TONGUELESS CANTORS silence you; heavy blows DAZE",
            "BREAK bosses: weak points and stuns fill a meter - then strike",
            "New OMENS and floor EVENTS; spare gold tithes into Renown",
            "Menus work on a GAMEPAD: the stick moves the pointer",
            "PLAY WITH FRIENDS over Steam: invite from the pause menu",
        };

        private static void DrawWhatsNew(float w, float h, float fade)
        {
            float cw = 500f, ch = 44f + WhatsNew.Length * 22f;
            var r = new Rect(w - cw - 40f, h - ch - 70f, cw, ch);
            UiSkin.Rect(r, new Color(0f, 0f, 0f, 0.55f * fade));
            UiSkin.Rect(new Rect(r.x, r.y, 3, r.height), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, fade));
            GUI.Label(new Rect(r.x + 16, r.y + 10, cw - 24, 22), "NEW IN THIS BUILD", new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, fade) } });
            var st = new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = false, normal = { textColor = new Color(0.9f, 0.86f, 0.78f, fade) } };
            for (int i = 0; i < WhatsNew.Length; i++)
                GUI.Label(new Rect(r.x + 16, r.y + 36 + i * 22, cw - 24, 22), UiSkin.Fit("-  " + WhatsNew[i], st, cw - 28), st);
        }

        private void DrawMain()
        {
            if (GUILayout.Button("PLAY", UiSkin.ButtonBig, GUILayout.Height(58))) launcher.StartHost();
            GUILayout.Label(SteamPlatform.Ready ? "Host a party. Invite Steam friends from the pause menu (Esc)." : "Host a new party. Friends join you by your address.", UiSkin.Small);
            // An invite waiting: answer it right here.
            if (SteamPlatform.Ready && SteamPlatform.Instance.Invites.Count > 0) { GUILayout.Space(8); FriendsPanel.DrawInvites(); }
            GUILayout.Space(10);
            if (GUILayout.Button("JOIN A PARTY", UiSkin.Button, GUILayout.Height(44))) page = Page.Join;
            GUILayout.Space(6);
            if (GUILayout.Button("OPTIONS", UiSkin.Button, GUILayout.Height(44))) page = Page.Options;
            GUILayout.Space(6);
            if (GUILayout.Button("QUIT", UiSkin.Button, GUILayout.Height(44))) Application.Quit();
            GUILayout.Space(8);
            GUILayout.Label(SteamPlatform.Ready ? "Steam: playing as " + SteamPlatform.MyName : "Steam: off (" + (string.IsNullOrEmpty(SteamPlatform.Problem) ? "test run" : SteamPlatform.Problem) + ") - parties use addresses",
                new GUIStyle(UiSkin.Small) { wordWrap = true });
        }

        /// <summary>Join with Steam: friends hosting a party, invites, and the address route for LAN and VPNs.</summary>
        private void DrawSteamJoin()
        {
            GUILayout.Label("JOIN A FRIEND", UiSkin.Heading);
            GUILayout.Label("Friends hosting Deepbound appear here. Or accept their invite - here, or in Steam.", UiSkin.Small);
            GUILayout.Space(6);
            FriendsPanel.DrawInvites();
            FriendsPanel.DrawJoinList(Mathf.Clamp(Screen.height * 0.22f, 120f, 260f));
            GUILayout.Space(8);
            if (GUILayout.Button("JOIN BY ADDRESS", UiSkin.Button, GUILayout.Height(36))) page = Page.Address;
            GUILayout.Space(4);
            if (GUILayout.Button("HOST BY ADDRESS (LAN / VPN)", UiSkin.Button, GUILayout.Height(36))) launcher.StartHostByAddress();
            GUILayout.Space(6);
            if (GUILayout.Button("BACK", UiSkin.Button, GUILayout.Height(38))) page = Page.Main;
        }

        private void DrawJoin()
        {
            GUILayout.Label("JOIN A PARTY", UiSkin.Heading);
            GUILayout.Label("Your friend's address. When they host, their pause menu (Esc) shows it with a Copy button.", UiSkin.Small);
            GUILayout.Space(6);
            address = GUILayout.TextField(address, UiSkin.Field, GUILayout.Height(40));
            GUILayout.Space(8);
            if (GUILayout.Button("CONNECT", UiSkin.ButtonBig, GUILayout.Height(52))) launcher.StartClient(address);
            GUILayout.Space(4);
            if (GUILayout.Button("Paste", UiSkin.Button, GUILayout.Height(30)) && !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer)) address = GUIUtility.systemCopyBuffer.Trim();
            GUILayout.Space(4);
            GUILayout.Label("Same house: their 192.168... address. Over the internet: the address their pause menu shows (their router opens the port by itself if it allows it). " +
                            "If that fails, both install Tailscale or Radmin VPN and use the address it gives. You both need the same build.", new GUIStyle(UiSkin.Small) { wordWrap = true });
            GUILayout.Space(6);
            if (GUILayout.Button("BACK", UiSkin.Button, GUILayout.Height(38))) page = page == Page.Address ? Page.Join : Page.Main;
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
