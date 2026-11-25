using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    public class PauseMenu : MonoBehaviour
    {
        public static bool Open { get; private set; }

        public static void SetOpen(bool open) => Open = open;
        private bool options, guide, controls, invitesOpen, legacy;
        private Bind? capturing;

        public static void DebugShowGuide(PauseMenu menu, int page = 0) { Open = true; menu.guide = true; menu.guidePage = page; }
        public static void DebugShowOptions(PauseMenu menu, int tab = 0) { Open = true; menu.guide = false; menu.options = true; menu.controls = false; OptionsPanel.Tab = tab; }
        public static void DebugShowControls(PauseMenu menu) { Open = true; menu.guide = false; menu.options = true; menu.controls = true; }

        private static bool Reserved(Key k) => k == Key.Escape || k == Key.Enter || k == Key.NumpadEnter || k == Key.Slash || k == Key.Backquote
            || k == Key.F1 || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftMeta || k == Key.RightMeta || k == Key.ContextMenu || k == Key.None;

        private static Codex.Section[] GuideLeft => Codex.GuideLeft;
        private static Codex.Section[] GuideRight => Codex.GuideRight;
        private static Codex.Section[] DungeonLeft => Codex.DungeonLeft;
        private static Codex.Section[] DungeonRight => Codex.DungeonRight;
        private int guidePage, lastGuidePage = -1;
        private Vector2 guideScroll;
        private string guideCacheKey;
        private string[][] guideTexts;
        private float[][] guideHeights;
        private float guideTallest;

        internal static bool Check(bool on, string label, bool radio = false)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent("      " + label), UiSkin.Body, GUILayout.Height(24));
            var box = new Rect(rect.x + 2f, rect.y + 4f, 16f, 16f);
            var gold = UiSkin.Gold;
            UiSkin.Rect(box, new Color(gold.r * 0.55f, gold.g * 0.55f, gold.b * 0.55f, 0.95f));
            UiSkin.Rect(new Rect(box.x + 1f, box.y + 1f, 14f, 14f), new Color(0.05f, 0.04f, 0.03f, 1f));
            if (on) UiSkin.Rect(radio ? new Rect(box.x + 5f, box.y + 5f, 6f, 6f) : new Rect(box.x + 3.5f, box.y + 3.5f, 9f, 9f), gold);
            GUI.Label(new Rect(rect.x + 26f, rect.y, rect.width - 26f, rect.height), label, UiSkin.Derive(UiSkin.Body, on ? Color.white : new Color(0.8f, 0.78f, 0.72f)));
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) on = !on;
            return on;
        }

        internal static int Choice(int current, string[] names)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++)
            {
                if (UiSkin.Btn(names[i], UiSkin.Button, GUILayout.Height(30))) current = i;
                if (i == current && Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    UiSkin.Rect(new Rect(r.x + 4f, r.yMax - 4f, r.width - 8f, 3f), UiSkin.Gold);
                }
            }
            GUILayout.EndHorizontal();
            return current;
        }

        public static bool OtherMenuOpenLastFrame;

        private void OnDestroy() { Open = false; if (worldPaused) { worldPaused = false; Time.timeScale = 1f; } }

        private static bool worldPaused;

        public static bool WorldPaused => worldPaused;

        private static bool SoloHost()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && nm.IsServer && nm.ConnectedClientsIds.Count <= 1 && !Networking.NetworkLauncher.TestRun;
        }

        private static void TickSoloPause()
        {
            bool want = Open && SoloHost();
            if (want == worldPaused) return;
            worldPaused = want;
            Time.timeScale = want ? 0f : 1f;
        }

        private int padFocus;

        public bool GuideShowing => Open && guide;
        private float padRepeatAt;

        private void PadNavigate()
        {
            var g = Gamepad.current;
            if (g == null || !Open || capturing.HasValue) return;
            bool back = g.buttonEast.wasPressedThisFrame, ok = g.buttonSouth.wasPressedThisFrame;
            if (confirm != 0) { if (back) confirm = 0; else if (ok) Confirmed(); return; }
            if (invitesOpen) { if (back) invitesOpen = false; return; }
            if (guide)
            {
                if (back) guide = false;
                else if (ok || g.rightShoulder.wasPressedThisFrame) guidePage = (guidePage + 1) % 4;
                else if (g.leftShoulder.wasPressedThisFrame) guidePage = (guidePage + 3) % 4;

                guideScroll.y = Mathf.Max(0f, guideScroll.y - g.rightStick.ReadValue().y * 900f * Time.unscaledDeltaTime);
                return;
            }
            if (legacy)
            {
                if (back) legacy = false;
                return;
            }
            if (options)
            {
                if (back) { if (controls) controls = false; else options = false; }
                return;
            }
            float v = g.dpad.ReadValue().y + g.leftStick.ReadValue().y;
            if (Mathf.Abs(v) > 0.5f && Time.unscaledTime >= padRepeatAt)
            {
                padRepeatAt = Time.unscaledTime + 0.22f;

                int pos = System.Array.IndexOf(PadOrder, padFocus);
                padFocus = PadOrder[(pos + (v < 0f ? 1 : PadOrder.Length - 1)) % PadOrder.Length];
            }
            else if (Mathf.Abs(v) <= 0.5f) padRepeatAt = 0f;
            if (back) { Choose(0); return; }
            if (ok) Choose(padFocus);
        }

        private static readonly int[] PadOrder = { 0, 1, 2, 3, 6, 4, 5 };

        private int confirm;

        private void Confirmed()
        {
            int what = confirm;
            confirm = 0;
            if (what == 4) { Open = false; NetworkManager.Singleton.Shutdown(); }
            else if (what == 5) Application.Quit();
        }

        private void DrawConfirm(float w, float h)
        {
            var net = NetworkManager.Singleton;
            bool hosting = net != null && net.IsHost && net.ConnectedClientsIds.Count > 1;
            bool midRun = World.DungeonDirector.Instance != null && World.DungeonDirector.Instance.RunInProgress;
            string head = confirm == 4 ? "LEAVE THE PARTY?" : "QUIT THE GAME?";
            string body = hosting ? "You are hosting: everyone else is sent back to the title."
                : midRun ? "The run goes on without you." + (confirm == 5 ? " Your camp progress is saved." : "")
                : confirm == 5 ? "Your camp progress is saved." : "You go back to the title screen.";
            var panel = new Rect(w / 2f - 230f, h / 2f - 105f, 460f, 210f);
            UiSkin.Panel(panel, 0.97f);
            GUI.Label(new Rect(panel.x, panel.y + 22, panel.width, 34), head, UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 26, align: TextAnchor.MiddleCenter));
            UiSkin.Ornament(panel.center.x - 100f, panel.y + 60f, 200f, 1f);
            GUI.Label(new Rect(panel.x + 24, panel.y + 74, panel.width - 48, 50), body, UiSkin.Derive(UiSkin.Body, UiSkin.Body.normal.textColor, align: TextAnchor.MiddleCenter));
            if (UiSkin.Btn(new Rect(panel.x + 30, panel.yMax - 62, 190, 42), confirm == 4 ? "LEAVE" : "QUIT", UiSkin.Button)) Confirmed();
            if (UiSkin.Btn(new Rect(panel.xMax - 220, panel.yMax - 62, 190, 42), "STAY", UiSkin.Button)) confirm = 0;
            GUI.Label(new Rect(panel.x, panel.yMax - 16, panel.width, 14), "Esc / B: stay", UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, fontSize: 10, align: TextAnchor.MiddleCenter));
        }

        private void Choose(int i)
        {
            switch (i)
            {
                case 0: Open = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; break;
                case 1: guide = true; break;
                case 2: options = true; break;
                case 3: legacy = true; break;
                case 4: confirm = 4; break;
                case 5: confirm = 5; break;
                case 6:
                {
                    var me = Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
                    var np = me != null ? me.GetComponent<Player.NetworkPlayer>() : null;
                    if (np != null) { np.UnstuckRpc(); GameHud.Feed("Unstuck: moved to open floor nearby", new Color(0.8f, 0.75f, 0.7f)); }
                    Choose(0);
                    break;
                }
            }
        }

        private void PadFrame(int i)
        {
            if (!Binds.PadActive || i != padFocus || Event.current.type != EventType.Repaint) return;
            var r = GUILayoutUtility.GetLastRect();
            var c = UiSkin.Gold;
            UiSkin.Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, 3), c); UiSkin.Rect(new Rect(r.x - 3, r.yMax, r.width + 6, 3), c);
            UiSkin.Rect(new Rect(r.x - 3, r.y, 3, r.height), c); UiSkin.Rect(new Rect(r.xMax, r.y, 3, r.height), c);
        }

        public static bool InOptions { get; private set; }

        private void Update()
        {
            TickSoloPause();
            InOptions = Open && (options || invitesOpen || legacy);
            PadNavigate();
            var keyboard = Keyboard.current;
            if (capturing.HasValue && keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { capturing = null; return; }
                foreach (var key in keyboard.allKeys)
                {
                    if (key == null || !key.wasPressedThisFrame || Reserved(key.keyCode)) continue;
                    Binds.Set(capturing.Value, key.keyCode);
                    capturing = null;
                    break;
                }
                return;
            }
            if (confirm != 0 && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { confirm = 0; Deepbound.Art.Sfx.Play2D(Deepbound.Art.Sound.UiClose, 0.6f); return; }
            if (((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || Binds.PauseDown) && !OtherMenuOpenLastFrame)
            {
                confirm = 0;
                Open = !Open;
                Deepbound.Art.Sfx.Play2D(Open ? Deepbound.Art.Sound.UiOpen : Deepbound.Art.Sound.UiClose, 0.8f);
                options = guide = controls = invitesOpen = legacy = false;
            }
        }

        internal static void DrawVoiceOptions()
        {
            GUILayout.Label("PROXIMITY VOICE", UiSkin.Heading);
            GUILayout.Label(Player.PlayerVoice.MicAvailable ? "Players near you hear you; walls muffle. Shouting draws the dead." : "No microphone found: you can still hear others.", UiSkin.Body);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Settings.VoiceModeNames.Length; i++)
            {
                bool on = Settings.VoiceMode == i;
                if (Check(on, Settings.VoiceModeNames[i], true) && !on) Settings.VoiceMode = i;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("Voice volume  " + Mathf.RoundToInt(Settings.VoiceVolume * 100f) + "%", UiSkin.Body);
            Settings.VoiceVolume = GUILayout.HorizontalSlider(Settings.VoiceVolume, 0f, 1.5f);
            GUILayout.Space(10);
            GUILayout.Label("Microphone boost  " + Settings.MicGain.ToString("F1") + "x", UiSkin.Body);
            Settings.MicGain = GUILayout.HorizontalSlider(Settings.MicGain, 0.25f, 4f);
            GUILayout.Space(10);
            GUILayout.Label("Open-mic sensitivity (the bar must pass the line)", UiSkin.Body);
            Settings.MicThreshold = GUILayout.HorizontalSlider(Settings.MicThreshold, 0.002f, 0.08f);

            var r = GUILayoutUtility.GetRect(360, 14);
            UiSkin.Rect(r, new Color(0f, 0f, 0f, 0.6f));
            float level = Mathf.Clamp01(Player.PlayerVoice.MicLevel / 0.08f);
            bool open = Player.PlayerVoice.MicLevel >= Settings.MicThreshold;
            UiSkin.Rect(new Rect(r.x, r.y, r.width * level, r.height), open ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.6f, 0.6f, 0.6f));
            float tx = r.x + r.width * Mathf.Clamp01(Settings.MicThreshold / 0.08f);
            UiSkin.Rect(new Rect(tx - 1, r.y - 2, 2, r.height + 4), new Color(1f, 0.85f, 0.4f));
        }

                private void OnGUI()
        {
            UiTimer.Begin();
            OnGUIBody();
            UiTimer.Mark("PauseMenu");
        }

        private void LateUpdate() => useGUILayout = Open;

        private void OnGUIBody()
        {
            UiSkin.Apply();
            if (!Open || !useGUILayout) return;
            float w = UiScale.W, h = UiScale.H;
            GUI.depth = -20;

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
            if (confirm != 0)
            {
                DrawConfirm(w, h);
                return;
            }
            if (guide)
            {
                DrawGuide(w, h);
                return;
            }
            if (legacy)
            {
                DrawLegacy(w, h);
                return;
            }
            if (options && controls)
            {
                DrawControls(w, h);
                return;
            }
            if (invitesOpen)
            {
                DrawInviteFriends(w, h);
                return;
            }
            bool steamExtra = Networking.SteamPlatform.Ready;
            var area = options ? new Rect(w / 2f - 390, h / 2f - 335, 780, 680) : new Rect(w / 2f - 180, h / 2f - (steamExtra ? 278 : 253), 360, steamExtra ? 556 : 511);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUILayout.BeginArea(area);
            GUILayout.Label(options ? "OPTIONS" : "PAUSED", UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 30, align: TextAnchor.MiddleCenter));
            if (Event.current.type == EventType.Repaint)
            {
                var head = GUILayoutUtility.GetLastRect();
                UiSkin.Ornament(head.center.x - 110f, head.yMax + 3f, 220f, 1f);
            }
            GUILayout.Space(14);

            if (options)
            {
                OptionsPanel.Draw();
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (UiSkin.Btn("CONTROLS", UiSkin.Button, GUILayout.Height(42))) controls = true;
                if (UiSkin.Btn("BACK", UiSkin.Button, GUILayout.Height(42))) options = false;
                GUILayout.EndHorizontal();
            }
            else
            {
                if (UiSkin.Btn("RESUME", UiSkin.ButtonBig, GUILayout.Height(54))) Choose(0);
                PadFrame(0);
                GUILayout.Space(8);
                if (UiSkin.Btn("GUIDE", UiSkin.Button, GUILayout.Height(44))) Choose(1);
                PadFrame(1);
                GUILayout.Space(8);
                if (UiSkin.Btn("OPTIONS", UiSkin.Button, GUILayout.Height(44))) Choose(2);
                PadFrame(2);
                GUILayout.Space(8);
                if (UiSkin.Btn("LEGACY", UiSkin.Button, GUILayout.Height(44))) Choose(3);
                PadFrame(3);
                GUILayout.Space(8);
                if (UiSkin.Btn("UNSTUCK", UiSkin.Button, GUILayout.Height(36))) Choose(6);
                PadFrame(6);
                if (DungeonDirector.InTutorial && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    GUILayout.Space(8);
                    if (UiSkin.Btn("LEAVE THE PROVING PATH", UiSkin.Button, GUILayout.Height(40))) { DungeonDirector.Instance.ServerLeaveTutorial(); Choose(0); }
                }
                GUILayout.Space(8);
                if (UiSkin.Btn("LEAVE PARTY", UiSkin.Button, GUILayout.Height(44))) Choose(4);
                PadFrame(4);
                GUILayout.Space(8);
                if (UiSkin.Btn("QUIT GAME", UiSkin.Button, GUILayout.Height(44))) Choose(5);
                PadFrame(5);
                GUILayout.Space(10);
                var net = NetworkManager.Singleton;
                string party = net == null ? "" : (net.IsHost ? "You are hosting" : "In a party") + "   ·   " + (net.IsServer ? net.ConnectedClientsIds.Count : Player.PlayerLife.All.Count) + " in the party";
                GUILayout.Label(party, UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, align: TextAnchor.MiddleCenter));
                var launcherHere = FindAnyObjectByType<Networking.NetworkLauncher>();

                if (net != null && net.IsHost && launcherHere != null && launcherHere.OverSteam)
                {
                    GUILayout.Space(4);
                    if (UiSkin.Btn("INVITE STEAM FRIENDS", UiSkin.Button, GUILayout.Height(38))) invitesOpen = true;
                }

                if (Networking.SteamPlatform.Ready && Networking.SteamPlatform.Instance.Invites.Count > 0)
                {
                    GUILayout.Space(4);
                    FriendsPanel.DrawInvites();
                    GUILayout.Label("Joining leaves this party.", UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, align: TextAnchor.MiddleCenter));
                }

                if (net != null && net.IsHost && !(launcherHere != null && launcherHere.OverSteam))
                {
                    var mid = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                    string line = Networking.PortMapper.Line;
                    if (!string.IsNullOrEmpty(line)) GUILayout.Label(line, UiSkin.Derive(mid, Networking.PortMapper.Current == Networking.PortMapper.State.Open && !Networking.PortMapper.SharedAddress ? UiSkin.Gold : mid.normal.textColor));
                    var launcherNow = FindAnyObjectByType<Networking.NetworkLauncher>();
                    string portTag = launcherNow != null && launcherNow.Port != 7777 ? ":" + launcherNow.Port : "";
                    string lan = Networking.PortMapper.LocalIp ?? Networking.PortMapper.GuessLocalIp(null);
                    GUILayout.BeginHorizontal();
                    if (Networking.PortMapper.Current == Networking.PortMapper.State.Open && !Networking.PortMapper.SharedAddress && !string.IsNullOrEmpty(Networking.PortMapper.ExternalIp)
                        && UiSkin.Btn("Copy internet address", UiSkin.Button, GUILayout.Height(30)))
                        GUIUtility.systemCopyBuffer = Networking.PortMapper.ExternalIp + portTag;
                    if (!string.IsNullOrEmpty(lan) && UiSkin.Btn("Copy home address (" + lan + ")", UiSkin.Button, GUILayout.Height(30)))
                        GUIUtility.systemCopyBuffer = lan + portTag;
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("The dungeon does not pause for you.", UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, align: TextAnchor.MiddleCenter));
            }
            GUILayout.EndArea();
        }

        private Vector2 legacyScroll;

        private void DrawLegacy(float w, float h)
        {
            float pw = Mathf.Min(820f, w - 60f), ph = Mathf.Min(720f, h - 40f);
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.96f);
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), "LEGACY", UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 30, align: TextAnchor.MiddleCenter));
            GUI.Label(new Rect(panel.x, panel.y + 50, pw, 24), "RENOWN  " + Cosmetics.Renown + "      small comforts that outlive a run; nothing here makes you stronger",
                UiSkin.Derive(UiSkin.Body, UiSkin.Gold, fontStyle: FontStyle.Bold, align: TextAnchor.MiddleCenter));

            var view = new Rect(panel.x + 16, panel.y + 82, pw - 32, ph - 82 - 72);
            const float rowH = 52f;
            var last = Legacy.LastRelics();
            float contentH = 8 * rowH + 36f + 24f + 110f + (last.Length > 0 ? (last.Length / 3 + 1) * 36f : 30f);
            var content = new Rect(0, 0, view.width - 20, Mathf.Max(contentH, view.height));
            legacyScroll = GUI.BeginScrollView(view, legacyScroll, content);
            float cw = content.width;
            var nameStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 15, normal = { textColor = UiSkin.Gold } };
            var effectStyle = new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = false };
            GUI.Label(new Rect(8, 0, cw, 22), "BOONS", nameStyle);
            for (int i = 0; i < Legacy.All.Length; i++)
            {
                var b = (Boon)i;
                var info = Legacy.All[i];
                var r = new Rect(0, 26 + i * rowH, cw, rowH - 6f);
                bool owned = Legacy.Owned(b);
                UiSkin.Rect(r, new Color(1f, 1f, 1f, owned ? 0.07f : 0.035f));
                if (owned) UiSkin.Rect(new Rect(r.x, r.y, 3, r.height), UiSkin.Gold);
                GUI.Label(new Rect(r.x + 12, r.y + 3, r.width - 200, 20), info.Name.ToUpperInvariant(), nameStyle);
                GUI.Label(new Rect(r.x + 12, r.y + 23, r.width - 200, 20), info.Effect, effectStyle);
                string why = Legacy.CannotBuy(b);
                var btn = new Rect(r.xMax - 176, r.y + 6, 166, r.height - 12);
                bool was = GUI.enabled;
                GUI.enabled = why == null;
                if (GUI.Button(btn, owned ? "OWNED" : why == null ? "BUY  " + info.Cost : info.Cost + " RENOWN", UiSkin.Button)) Legacy.Buy(b);
                GUI.enabled = was;
            }

            float y = 26 + 8 * rowH + 14f;
            GUI.Label(new Rect(8, y, cw, 22), "HEIRLOOM", nameStyle);
            y += 24f;
            GUI.Label(new Rect(8, y, cw, 40), "Choose one relic from your last run. At camp, before a run, bring it: it joins that run as a plain relic, once. Then you must earn another.", UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, fontSize: 13, wrap: true));
            y += 44f;
            if (last.Length == 0) GUI.Label(new Rect(8, y, cw, 22), "No relics remembered yet: finish a run carrying some.", UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, fontStyle: FontStyle.Italic));
            var chosen = Legacy.Heirloom;
            float bw = (cw - 16f - 20f) / 3f;
            for (int i = 0; i < last.Length; i++)
            {
                var rect = new Rect(8f + (i % 3) * (bw + 10f), y + (i / 3) * 36f, bw, 32f);
                if (GUI.Button(rect, Relics.Name(last[i]), UiSkin.Button)) Legacy.ChooseHeirloom(last[i]);
                if (chosen.HasValue && chosen.Value == last[i]) UiSkin.Rect(new Rect(rect.x + 4f, rect.yMax - 4f, rect.width - 8f, 3f), UiSkin.Gold);
            }
            if (last.Length > 0) y += ((last.Length + 2) / 3) * 36f;
            y += 8f;
            GUI.Label(new Rect(8, y, cw, 22), "Heirloom: " + (chosen.HasValue ? Relics.Name(chosen.Value) : "none chosen"), UiSkin.Derive(UiSkin.Body, UiSkin.Body.normal.textColor, fontStyle: FontStyle.Bold));
            y += 28f;
            var net = NetworkPlayer.Local;
            var prog = net != null ? net.GetComponent<PlayerProgress>() : null;
            var dir = DungeonDirector.Instance;
            bool atCamp = dir != null && DungeonDirector.InLobby && !dir.RunInProgress;
            string block = !chosen.HasValue ? "Choose an heirloom first." : prog == null ? "Not in a party." : !atCamp ? "Only at camp, before a run starts." : "";
            bool ready = chosen.HasValue && atCamp && prog != null;
            bool was2 = GUI.enabled;
            GUI.enabled = ready;
            if (UiSkin.Btn(new Rect(8, y, 260, 40), "BRING THE HEIRLOOM", UiSkin.Button) && ready) { Legacy.Note = ""; prog.BringHeirloomRpc((byte)((int)chosen.Value + 1)); }
            GUI.enabled = was2;
            string note = !string.IsNullOrEmpty(Legacy.Note) ? Legacy.Note : block;
            GUI.Label(new Rect(280, y + 10, cw - 280, 24), note, UiSkin.Small);
            GUI.EndScrollView();
            if (UiSkin.Btn(new Rect(panel.x + pw / 2f - 90, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) legacy = false;
        }

        private void DrawInviteFriends(float w, float h)
        {
            var area = new Rect(w / 2f - 230, h / 2f - 260, 460, 520);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUILayout.BeginArea(area);
            GUILayout.Label("INVITE FRIENDS", UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 30, align: TextAnchor.MiddleCenter));
            GUILayout.Label("They get a Steam invite and a JOIN button in Deepbound. Both of you need the same build, with Steam running.", UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, align: TextAnchor.MiddleCenter, wrap: true));
            GUILayout.Space(8);
            FriendsPanel.DrawInviteList(330f);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (UiSkin.Btn("STEAM OVERLAY", UiSkin.Button, GUILayout.Height(40))) Networking.SteamPlatform.OpenInviteOverlay();
            if (UiSkin.Btn("BACK", UiSkin.Button, GUILayout.Height(40))) invitesOpen = false;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawControls(float w, float h)
        {
            var area = new Rect(w / 2f - 390, h / 2f - 330, 780, 660);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUI.Label(new Rect(area.x, area.y, area.width, 40), "CONTROLS", UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 30, align: TextAnchor.MiddleCenter));
            GUI.Label(new Rect(area.x, area.y + 40, area.width, 20), capturing.HasValue ? "Press a key for " + LabelOf(capturing.Value).ToUpperInvariant() + "   (Esc cancels)" : "Click an action, then press its new key. Mouse buttons, Esc, Enter (chat) and Alt (free cursor) stay put.",
                UiSkin.Derive(UiSkin.Small, capturing.HasValue ? UiSkin.Gold : UiSkin.Small.normal.textColor, align: TextAnchor.MiddleCenter));
            int n = Binds.Defaults.Length, half = (n + 1) / 2;
            float colW = 370f, rowH = 34f, top = area.y + 74f;
            var label = new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleLeft, fontSize = 15 };
            var keyStyle = new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
            for (int i = 0; i < n; i++)
            {
                var d = Binds.Defaults[i];
                var r = new Rect(area.x + (i < half ? 0f : colW + 40f), top + (i < half ? i : i - half) * rowH, colW, rowH - 4f);
                bool live = capturing == d.bind, hover = r.Contains(Event.current.mousePosition);
                UiSkin.Rect(r, new Color(1f, 1f, 1f, live ? 0.12f : hover ? 0.07f : 0.03f));
                GUI.Label(new Rect(r.x + 10, r.y, r.width - 120, r.height), d.label, label);
                var key = new Rect(r.xMax - 104, r.y + 3, 96, r.height - 6);
                UiSkin.Panel(key, live ? 1f : 0.85f);
                bool changed = Binds.KeyOf(d.bind) != d.key;
                GUI.Label(key, live ? (Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f ? "..." : "") : Binds.Label(d.bind),
                    UiSkin.Derive(keyStyle, live || changed ? UiSkin.Gold : Color.white));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) capturing = live ? (Bind?)null : d.bind;
            }
            float by = area.yMax - 46f;
            if (UiSkin.Btn(new Rect(area.x, by, 250, 42), "RESET TO DEFAULTS", UiSkin.Button)) { Binds.ResetAll(); capturing = null; }
            if (UiSkin.Btn(new Rect(area.xMax - 250, by, 250, 42), "BACK", UiSkin.Button)) { controls = false; capturing = null; }
        }

        private static string LabelOf(Bind b)
        {
            foreach (var d in Binds.Defaults) if (d.bind == b) return d.label;
            return b.ToString();
        }

        private int journalOpen = -1;

        private Vector2 bestiaryScroll;

        private void DrawBestiary(Rect panel, float colW, GUIStyle titleStyle, GUIStyle bodyStyle)
        {
            var view = new Rect(panel.x + 20, panel.y + 58, panel.width - 40, panel.height - 124);
            float rowH = 70f;
            int rows = (Bestiary.Entries.Length + 1) / 2;
            var content = new Rect(0, 0, view.width - 20, rows * rowH);
            bestiaryScroll = GUI.BeginScrollView(view, bestiaryScroll, content);
            float cw = (content.width - 20f) / 2f;
            for (int i = 0; i < Bestiary.Entries.Length; i++)
            {
                var e = Bestiary.Entries[i];
                bool seen = Bestiary.Seen(i);
                var r = new Rect((i % 2) * (cw + 20f), (i / 2) * rowH, cw, rowH - 6);
                UiSkin.Rect(r, new Color(1f, 1f, 1f, seen ? 0.05f : 0.02f));
                if (seen) UiSkin.Rect(new Rect(r.x, r.y, 3, r.height), e.Boss ? new Color(0.9f, 0.3f, 0.22f) : UiSkin.Gold);
                GUI.Label(new Rect(r.x + 10, r.y + 3, r.width - 14, 20), seen ? e.Name.ToUpperInvariant() : "???", UiSkin.Derive(titleStyle, seen ? (e.Boss ? new Color(0.95f, 0.45f, 0.35f) : UiSkin.Gold) : new Color(0.5f, 0.48f, 0.45f)));
                GUI.Label(new Rect(r.x + 10, r.y + 22, r.width - 14, r.height - 24), Bestiary.TextFor(i), UiSkin.Derive(bodyStyle, seen ? bodyStyle.normal.textColor : new Color(0.5f, 0.48f, 0.45f), fontSize: 12));
            }
            GUI.EndScrollView();
        }

        private void DrawJournal(Rect panel, float colW, GUIStyle titleStyle, GUIStyle bodyStyle)
        {
            string[] floors = { "THE OSSUARY", "THE DROWNED VAULTS", "THE HOLLOW THRONE", "THE BLACK CATHEDRAL", "EULOGIES", "THE FIRST EXPEDITION" };
            float listX = panel.x + 24, y = panel.y + 58, listW = colW * 0.75f;
            for (int f = 0; f < 6; f++)
            {
                GUI.Label(new Rect(listX, y, listW, 20), floors[f], titleStyle);
                y += 20;
                for (int i = 0; i < Journal.Entries.Length; i++)
                {
                    if (Journal.Entries[i].Floor != f) continue;
                    bool have = Journal.Has(i);
                    var r = new Rect(listX + 8, y, listW - 8, 19);
                    if (have && GUI.Button(r, (journalOpen == i ? "> " : "") + Journal.Entries[i].Title, UiSkin.Derive(UiSkin.Body, journalOpen == i ? UiSkin.Gold : new Color(0.9f, 0.88f, 0.84f), fontSize: 14))) journalOpen = i;
                    if (!have) GUI.Label(r, f == 4 ? "a guardian not yet felled" : f == 5 ? "someone not yet brought home" : "a page not yet found", UiSkin.Derive(UiSkin.Body, new Color(0.55f, 0.52f, 0.48f), fontSize: 14, fontStyle: FontStyle.Italic));
                    y += 19;
                }
                y += 4;
            }
            float tx = listX + listW + 24, tw = panel.width - (tx - panel.x) - 24;
            if (journalOpen >= 0 && Journal.Has(journalOpen))
            {
                var e = Journal.Entries[journalOpen];
                GUI.Label(new Rect(tx, panel.y + 64, tw, 30), e.Title, UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 22));
                GUI.Label(new Rect(tx, panel.y + 100, tw, panel.height - 180), e.Text, UiSkin.Derive(bodyStyle, bodyStyle.normal.textColor, fontSize: 16, fontStyle: FontStyle.Italic));
            }
            else GUI.Label(new Rect(tx, panel.y + 64, tw, 60), Journal.Count == 0 ? "Pages of Cathedra's history lie open on lecterns in the dungeon, one on every floor. Read them (F) and they are kept here." : "Choose a page to read it.", bodyStyle);
        }

        private void DrawGuide(float w, float h)
        {
            float pw = Mathf.Min(1040f, w - 60f), ph = Mathf.Min(720f, h - 40f);
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.96f);
            string[] titles = { "GUIDE: PROGRESSION", "GUIDE: THE DUNGEON", "JOURNAL  (" + Journal.Count + " / " + Journal.Entries.Length + " pages)", "BESTIARY  (" + Bestiary.SeenCount + " / " + Bestiary.Entries.Length + " met)" };
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), titles[guidePage], UiSkin.Derive(UiSkin.Heading, UiSkin.Heading.normal.textColor, fontSize: 30, align: TextAnchor.MiddleCenter));
            float colW = (pw - 92f) / 2f;
            var titleStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = UiSkin.Gold } };
            var bodyStyle = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            if (guidePage != lastGuidePage) { guideScroll = Vector2.zero; lastGuidePage = guidePage; }
            if (guidePage == 2) DrawJournal(panel, colW, titleStyle, bodyStyle);
            else if (guidePage == 3) DrawBestiary(panel, colW, titleStyle, bodyStyle);
            else
            {
                var view = new Rect(panel.x + 16, panel.y + 58, pw - 32, ph - 58 - 72);
                var sections = new[] { guidePage == 0 ? GuideLeft : DungeonLeft, guidePage == 0 ? GuideRight : DungeonRight };

                string key = guidePage + "|" + colW + "|" + Binds.AnyChanged + "|" + Binds.PadActive + "|" + Codex.Version;
                if (key != guideCacheKey)
                {
                    guideCacheKey = key;
                    guideTexts = new string[2][];
                    guideHeights = new float[2][];
                    guideTallest = 0f;
                    for (int c = 0; c < 2; c++)
                    {
                        guideTexts[c] = new string[sections[c].Length];
                        guideHeights[c] = new float[sections[c].Length];
                        float y = 6f;
                        for (int i = 0; i < sections[c].Length; i++)
                        {
                            guideTexts[c][i] = Binds.Rekey(Codex.BodyOf(sections[c][i]));
                            guideHeights[c][i] = bodyStyle.CalcHeight(new GUIContent(guideTexts[c][i]), colW);
                            y += 20 + guideHeights[c][i] + 14;
                        }
                        guideTallest = Mathf.Max(guideTallest, y);
                    }
                }
                var texts = guideTexts;
                float tallest = guideTallest;
                var content = new Rect(0, 0, view.width - 20, tallest);
                guideScroll = GUI.BeginScrollView(view, guideScroll, content);
                for (int c = 0; c < 2; c++)
                {
                    float x = 8 + c * (colW + 24), y = 6f;
                    for (int i = 0; i < sections[c].Length; i++)
                    {
                        GUI.Label(new Rect(x, y, colW, 20), Codex.TitleOf(sections[c][i]), titleStyle);
                        float bh = guideHeights[c][i];
                        GUI.Label(new Rect(x, y + 20, colW, bh), texts[c][i], bodyStyle);
                        y += 20 + bh + 14;
                    }
                }
                GUI.EndScrollView();
                if (tallest > view.height && guideScroll.y < tallest - view.height - 4f)
                    GUI.Label(new Rect(view.x, view.yMax - 2, view.width, 18), "scroll for more", UiSkin.Derive(UiSkin.Small, UiSkin.Gold, fontStyle: FontStyle.Italic, align: TextAnchor.MiddleCenter));
            }
            string[] next = { "THE DUNGEON >", "JOURNAL >", "BESTIARY >", "PROGRESSION >" };
            if (UiSkin.Btn(new Rect(panel.x + pw / 2f - 280, panel.yMax - 56, 180, 40), next[guidePage], UiSkin.Button)) guidePage = (guidePage + 1) % 4;
            if (UiSkin.Btn(new Rect(panel.x + pw / 2f + 100, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) guide = false;
        }
    }
}
