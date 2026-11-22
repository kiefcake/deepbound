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
        private enum Page { Main, Join, Options, Address, Credits }

        /// <summary>
        /// The first look (2026-11-19, the user: menus "seen in other games"): the title alone over black, then the scene,
        /// and PRESS ANY KEY. Once a launch; skipped in test runs. Subpages go back on Esc; the menu's buttons come in one
        /// after another; CREDITS rolls docs/CREDITS.md (Resources/Credits.txt, made by tools/make_credits.py).
        /// </summary>
        private static bool splashDone;
        private float pageAt;
        private float creditsAt;
        private string[] credits;

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

        // Layout only while the title shows (2026-11-16 perf).
        private void LateUpdate() => useGUILayout = launcher != null && !Connected;

        private void OnGUI()
        {
            UiSkin.Apply();
            if (launcher == null || Connected || !useGUILayout) return;
            float w = UiScale.W, h = UiScale.H;
            if (NetworkLauncher.TestRun) splashDone = true;
            if (!splashDone) { DrawSplash(w, h); return; }
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape && page != Page.Main)
            {
                page = page == Page.Address ? Page.Join : Page.Main;
                pageAt = Time.time;
                Art.Sfx.Play2D(Art.Sound.UiClose, 0.6f);
                ev.Use();
            }
            if (page == Page.Credits) { DrawCredits(w, h); return; }
            float fade = Mathf.Clamp01((Time.time - shownAt) / 1.2f);

            // Darken the edges so the title and menu read over the 3D scene.
            UiSkin.Rect(new Rect(0, 0, w * 0.42f, h), new Color(0f, 0f, 0f, 0.55f * fade));
            UiSkin.Rect(new Rect(0, 0, w, h * 0.12f), new Color(0f, 0f, 0f, 0.35f * fade));

            float left = Mathf.Max(40f, w * 0.06f);
            UiSkin.Shadowed(new Rect(left - 20, h * 0.12f, 560, 90), "DEEPBOUND", new GUIStyle(UiSkin.Title) { alignment = TextAnchor.MiddleLeft }, fade);
            UiSkin.Shadowed(new Rect(left, h * 0.12f + 84, 560, 30), "Descend together. Nothing comes back up but your name.",
                new GUIStyle(UiSkin.Body) { fontSize = 17, fontStyle = FontStyle.Italic }, fade);
            UiSkin.Ornament(left, h * 0.12f + 122f, 420f, fade);

            if (page == Page.Options)
            {
                // The same options as the pause menu (keys are rebound in-game, from there).
                var box = new Rect(w / 2f - 390, h / 2f - 335, 780, 680);
                UiSkin.Panel(new Rect(box.x - 20, box.y - 20, box.width + 40, box.height + 40), 0.95f);
                GUILayout.BeginArea(box);
                GUILayout.Label("OPTIONS", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
                GUILayout.Space(14);
                OptionsPanel.Draw();
                GUILayout.FlexibleSpace();
                if (UiSkin.Btn("BACK", UiSkin.Button, GUILayout.Height(42))) page = Page.Main;
                GUILayout.EndArea();
                return;
            }

            var area = new Rect(left, h * 0.33f, 360, h * 0.62f);
            GUILayout.BeginArea(area);
            switch (page)
            {
                case Page.Main: DrawMain(); break;
                case Page.Join: if (SteamPlatform.Ready) DrawSteamJoin(); else DrawJoin(); break;
                case Page.Address: DrawJoin(); break;
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
            "Every ability has its own look: shields, crosses, lashes, afterimages, flies",
            "The Knight's raised shield shows as a ward; taunted foes wear a burning mark",
            "A meteor's fire ring closes as it falls: you can read when it lands",
            "New combos: PURGE (Cleric on blight), PLAGUE CRASH (Flagellant spreads it)",
            "Sloshed? Your feet swap keys every 30 seconds",
            "New ultimates: Ranger TWIN WYRMS, Plague Doctor PANDEMIC, Flagellant RED LITANY",
            "The Tome of the Phoenix burns longer and dives far harder",
            "Every robed caster has its own shape; ghouls, archers, wraiths remade",
            "Guardians and lair keepers remade: chunkier, scarier, no bare skeletons",
            "Real furniture, statues and barrels in the halls; everything less shiny",
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

        /// <summary>Black, then the name rising out of it, then the scene behind; any key or click goes on.</summary>
        private void DrawSplash(float w, float h)
        {
            float t = Time.time - shownAt;
            float black = 1f - Mathf.Clamp01((t - 1.6f) / 1.4f) * 0.55f;
            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, black));
            float ta = Mathf.Clamp01((t - 0.3f) / 1.2f);
            float rise = (1f - Mathf.SmoothStep(0f, 1f, ta)) * 24f;
            UiSkin.Shadowed(new Rect(0, h * 0.36f + rise, w, 100), "DEEPBOUND", new GUIStyle(UiSkin.Title) { fontSize = 92, alignment = TextAnchor.MiddleCenter }, ta);
            UiSkin.Ornament(w / 2f - 240f, h * 0.36f + 104f + rise, 480f, ta);
            GUI.Label(new Rect(0, h * 0.36f + 116 + rise, w, 30), "Descend together. Nothing comes back up but your name.",
                new GUIStyle(UiSkin.Body) { fontSize = 18, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.86f, 0.78f, ta) } });
            if (t > 2.2f)
            {
                float pulse = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin((t - 2.2f) * 3f));
                GUI.Label(new Rect(0, h * 0.78f, w, 30), "PRESS ANY KEY", new GUIStyle(UiSkin.Heading) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, pulse * Mathf.Clamp01((t - 2.2f) / 0.6f)) } });
            }
            var e = Event.current;
            if (t > 0.6f && (e.type == EventType.KeyDown || e.type == EventType.MouseDown))
            {
                splashDone = true;
                shownAt = pageAt = Time.time;
                Art.Sfx.Play2D(Art.Sound.UiOpen, 0.8f);
                e.Use();
            }
        }

        /// <summary>A menu button that slides in after the ones above it.</summary>
        private bool Entry(int i, string text, GUIStyle style, float height)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - Mathf.Max(shownAt, pageAt) - 0.15f - i * 0.07f) / 0.35f));
            var was = GUI.color;
            GUI.color = new Color(was.r, was.g, was.b, was.a * k);
            GUILayout.BeginHorizontal();
            GUILayout.Space((1f - k) * -36f + 0.01f);
            bool hit = UiSkin.Btn(text, style, GUILayout.Height(height)) && k > 0.6f;
            GUILayout.EndHorizontal();
            GUI.color = was;
            return hit;
        }

        /// <summary>The credits roll up over the dimmed scene; any key or click (or the end) goes back.</summary>
        private void DrawCredits(float w, float h)
        {
            if (credits == null)
            {
                var ta = Resources.Load<TextAsset>("Credits");
                credits = ta != null ? ta.text.Replace("\r", "").Split('\n') : new[] { "#DEEPBOUND", "Thank you for playing." };
            }
            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.82f));
            float t = Time.time - creditsAt;
            float y = h + 10f - t * 42f;
            float total = 0f;
            foreach (var l in credits)
            {
                bool head = l.StartsWith("#");
                float lh = head ? 52f : l.Length == 0 ? 14f : 24f;
                if (y > -60f && y < h + 10f && l.Length > 0)
                {
                    float edge = Mathf.Clamp01(Mathf.Min(y / (h * 0.15f), (h - y) / (h * 0.15f)));
                    var st = head ? new GUIStyle(UiSkin.Heading) { fontSize = l == "#DEEPBOUND" ? 48 : 24, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, edge) } }
                        : new GUIStyle(UiSkin.Body) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.88f, 0.85f, 0.8f, edge) } };
                    GUI.Label(new Rect(0, y, w, lh), head ? l.Substring(1).ToUpperInvariant() : l, st);
                }
                y += lh;
                total += lh;
            }
            GUI.Label(new Rect(0, h - 30, w - 20, 22), "Any key: back", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleRight });
            var e = Event.current;
            if ((t > 0.4f && (e.type == EventType.KeyDown || e.type == EventType.MouseDown)) || y < -80f)
            {
                page = Page.Main;
                pageAt = Time.time;
                if (e.type == EventType.KeyDown || e.type == EventType.MouseDown) e.Use();
            }
        }

        private void DrawMain()
        {
            if (Entry(0, "PLAY", UiSkin.ButtonBig, 58)) launcher.StartHost();
            GUILayout.Label(SteamPlatform.Ready ? "Host a party. Invite Steam friends from the pause menu (Esc)." : "Host a new party. Friends join you by your address.", UiSkin.Small);
            // An invite waiting: answer it right here.
            if (SteamPlatform.Ready && SteamPlatform.Instance.Invites.Count > 0) { GUILayout.Space(8); FriendsPanel.DrawInvites(); }
            GUILayout.Space(10);
            if (Entry(1, "JOIN A PARTY", UiSkin.Button, 44)) { page = Page.Join; pageAt = Time.time; }
            GUILayout.Space(6);
            if (Entry(2, "OPTIONS", UiSkin.Button, 44)) { page = Page.Options; pageAt = Time.time; }
            GUILayout.Space(6);
            if (Entry(3, "CREDITS", UiSkin.Button, 44)) { page = Page.Credits; creditsAt = pageAt = Time.time; }
            GUILayout.Space(6);
            if (Entry(4, "QUIT", UiSkin.Button, 44)) Application.Quit();
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
            FriendsPanel.DrawJoinList(Mathf.Clamp(UiScale.H * 0.22f, 120f, 260f));
            GUILayout.Space(8);
            if (UiSkin.Btn("JOIN BY ADDRESS", UiSkin.Button, GUILayout.Height(36))) page = Page.Address;
            GUILayout.Space(4);
            if (UiSkin.Btn("HOST BY ADDRESS (LAN / VPN)", UiSkin.Button, GUILayout.Height(36))) launcher.StartHostByAddress();
            GUILayout.Space(6);
            if (UiSkin.Btn("BACK", UiSkin.Button, GUILayout.Height(38))) page = Page.Main;
        }

        private void DrawJoin()
        {
            GUILayout.Label("JOIN A PARTY", UiSkin.Heading);
            GUILayout.Label("Your friend's address. When they host, their pause menu (Esc) shows it with a Copy button.", UiSkin.Small);
            GUILayout.Space(6);
            address = GUILayout.TextField(address, UiSkin.Field, GUILayout.Height(40));
            GUILayout.Space(8);
            if (UiSkin.Btn("CONNECT", UiSkin.ButtonBig, GUILayout.Height(52))) launcher.StartClient(address);
            GUILayout.Space(4);
            if (UiSkin.Btn("Paste", UiSkin.Button, GUILayout.Height(30)) && !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer)) address = GUIUtility.systemCopyBuffer.Trim();
            GUILayout.Space(4);
            GUILayout.Label("Same house: their 192.168... address. Over the internet: the address their pause menu shows (their router opens the port by itself if it allows it). " +
                            "If that fails, both install Tailscale or Radmin VPN and use the address it gives. You both need the same build.", new GUIStyle(UiSkin.Small) { wordWrap = true });
            GUILayout.Space(6);
            if (UiSkin.Btn("BACK", UiSkin.Button, GUILayout.Height(38))) page = page == Page.Address ? Page.Join : Page.Main;
        }
    }
}
