using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// Esc while playing: resume, options, or leave the party. A co-op game cannot stop
    /// time, so this only frees your mouse and covers the screen — the dungeon keeps
    /// moving, which is also why it is quick to get out of.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static bool Open { get; private set; }

        /// <summary>For the screenshot run.</summary>
        public static void SetOpen(bool open) => Open = open;
        private bool options, guide;

        /// <summary>For the screenshot run.</summary>
        public static void DebugShowGuide(PauseMenu menu) { Open = true; menu.guide = true; }

        // The Guide: every progression system and combo on one page, in plain words.
        private static readonly (string title, string body)[] GuideLeft =
        {
            ("EMBERS & THE ANVIL", "Embers drop from enemies, chests and urns, and come from scrapping gear. Spend them at the anvil beside the merchant (G) to temper your class weapon from +0 to +5: each level adds ATT and DEX."),
            ("WEAPON PATHS", "At +2 your weapon chooses one of two paths, which changes how your class plays (a wider Cleave or a Riposte, Pyromancer or Cryomancer...). At +4 the path is mastered and gains a second effect."),
            ("SIGILS & ABILITY RANKS", "Sigils drop from elites and chests, and every boss drops one for each player. At the anvil, a Sigil ranks up Q or E: rank 2 is 30% stronger, rank 3 recharges a quarter faster and adds a twist."),
            ("RUNES", "Runes are weapon enchantments: chill, stun, mark, holy healing, burning kills, keen edges. Sockets open at +1 and +3; set them at the anvil."),
        };
        private static readonly (string title, string body)[] GuideRight =
        {
            ("GEAR", "Armour and a charm, rolled for each player - only you see your drops. F equips (your old piece is left in its place), X scraps it for embers. Deeper and finer pieces are stronger. At the anvil you can refine a piece's quality or attune it to a deeper floor, so a favourite keeps up. Each boss may drop its own Legendary piece: the Warden's Bulwark, the Lich's Phylactery, the Crown of the Hollow King."),
            ("RELICS", "Rare powers, one of each per player. Found on elites, in chests, from bosses, and sold by the merchant."),
            ("COMBOS", "Classes set each other up. Chill + a heavy hit = SHATTER. Stunned enemies are EXPOSED (+25% for all). Taunted enemies are DISTRACTED (+15%). Allies in Consecrate are BLESSED (+20%); the Cleric's beam target is INSPIRED (+15%). Allies in a Rogue's smoke are hidden (SMOKE SCREEN). A broken Aegis shield BURSTS. A marked enemy's mark SPREADS when it dies. Tags over enemies show what is ready to cash in."),
            ("ELITES", "Gold-ringed elites carry an affix. WARDED: barely hurt until stunned or chilled. VAMPIRIC: heals from its hits - a Mark stops it. VOLATILE: bursts after death, get clear. SWIFT: fast; chill slows it. WRAITHS are ETHEREAL: most damage passes through them until they are chilled, stunned, marked or caught in Consecrate. SHIELDBEARERS block 85% from the front: flank them, or stun them to drop the shield. GARGOYLES: from floor 2, some statues are alive; nearly immune as stone, they wake when you come close, dive along a marked line, then land exposed. FLASKS (1/2/3): Fire burns an area, Frost chills (and freezes in water), Holy Water stuns the dead and mends allies. One satchel each run; more at the merchant. OMENS: turn the Omen Stone by the carriage before a run: Blood Moon, Famine, Restless Dead or Glass Bones make it harder, pay something back, and earn 35% more Renown. CONTRACTS: take one notice at the camp's bounty board before boarding; fulfil it on the next floor for gold, embers and sometimes a Sigil each. WHEEL OF FATE: one spin per floor for the party: usually a gift, sometimes blood or an ambush. CRITS: hits sometimes deal 75% more (more often with DEX). KICK (V): every class can boot an enemy away; kick them into chasms (they die), off balconies or into blades. CHAMPIONS: one named elite per floor, with a hoard. FLAME TRAPS in some corridors fire on a beat: time your run. MIMICS: some chests bite back (watch for a lid that breathes); kill one for a big haul. BONE HOARDERS flee with a sack of gold: every hit spills coins, and they escape after 25 s. ROOMS: pendulum blades cut enemies too (lure them in); smash a ritual chamber's violet braziers to lift its ward; frost freezes anything standing in cistern water; galleries and sunken courts reward high ground. FLOORS: Ossuary bone piles rise again unless stamped out; the Drowned Vaults' tide slows everyone on low ground; Hollow Throne soul lanterns heal and stun when smashed; the Black Cathedral's bell sends shockwaves to jump."),
            ("NOTHING CARRIES OVER", "Everything above lasts one run. The camp is where you start again."),
        };

        /// <summary>Set by other menus each frame; Esc closes them rather than opening this.</summary>
        public static bool OtherMenuOpenLastFrame;

        private void OnDestroy() => Open = false;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !OtherMenuOpenLastFrame)
            {
                Open = !Open;
                options = guide = false;
            }
        }

        private void OnGUI()
        {
            UiSkin.Apply();
            if (!Open) return;
            float w = Screen.width, h = Screen.height;
            GUI.depth = -20;

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
            if (guide)
            {
                DrawGuide(w, h);
                return;
            }
            var area = new Rect(w / 2f - 180, h / 2f - 200, 360, 410);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUILayout.BeginArea(area);
            GUILayout.Label(options ? "OPTIONS" : "PAUSED", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            GUILayout.Space(14);

            if (options)
            {
                GUILayout.Label("Mouse sensitivity  " + Settings.MouseSensitivity.ToString("F2"), UiSkin.Body);
                Settings.MouseSensitivity = GUILayout.HorizontalSlider(Settings.MouseSensitivity, 0.2f, 3f);
                GUILayout.Space(10);
                GUILayout.Label("Volume  " + Mathf.RoundToInt(Settings.Volume * 100f) + "%", UiSkin.Body);
                Settings.Volume = GUILayout.HorizontalSlider(Settings.Volume, 0f, 1f);
                GUILayout.Space(10);
                GUILayout.Label("Field of view  " + Mathf.RoundToInt(Settings.FieldOfView), UiSkin.Body);
                Settings.FieldOfView = GUILayout.HorizontalSlider(Settings.FieldOfView, 60f, 100f);
                var cam = Player.NetworkPlayer.Local != null ? Player.NetworkPlayer.Local.Camera : null;
                if (cam != null) cam.fieldOfView = Settings.FieldOfView;
                GUILayout.Space(18);
                if (GUILayout.Button("BACK", UiSkin.Button, GUILayout.Height(42))) options = false;
            }
            else
            {
                if (GUILayout.Button("RESUME", UiSkin.ButtonBig, GUILayout.Height(54)))
                {
                    Open = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                GUILayout.Space(8);
                if (GUILayout.Button("GUIDE", UiSkin.Button, GUILayout.Height(44))) guide = true;
                GUILayout.Space(8);
                if (GUILayout.Button("OPTIONS", UiSkin.Button, GUILayout.Height(44))) options = true;
                GUILayout.Space(8);
                if (GUILayout.Button("LEAVE PARTY", UiSkin.Button, GUILayout.Height(44)))
                {
                    Open = false;
                    NetworkManager.Singleton.Shutdown();
                }
                GUILayout.Space(8);
                if (GUILayout.Button("QUIT GAME", UiSkin.Button, GUILayout.Height(44))) Application.Quit();
                GUILayout.Space(10);
                var net = NetworkManager.Singleton;
                string party = net == null ? "" : (net.IsHost ? "You are hosting" : "In a party") + "   ·   " + (net.IsServer ? net.ConnectedClientsIds.Count : Player.PlayerLife.All.Count) + " in the party";
                GUILayout.Label(party, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
                GUILayout.Label("The dungeon does not pause for you.", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
            }
            GUILayout.EndArea();
        }

        private void DrawGuide(float w, float h)
        {
            float pw = Mathf.Min(980f, w - 60f), ph = Mathf.Min(600f, h - 60f);
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.96f);
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), "GUIDE", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            float colW = (pw - 72f) / 2f;
            var titleStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = UiSkin.Gold } };
            var bodyStyle = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            for (int c = 0; c < 2; c++)
            {
                float x = panel.x + 24 + c * (colW + 24), y = panel.y + 64;
                foreach (var (title, body) in c == 0 ? GuideLeft : GuideRight)
                {
                    GUI.Label(new Rect(x, y, colW, 20), title, titleStyle);
                    float bh = bodyStyle.CalcHeight(new GUIContent(body), colW);
                    GUI.Label(new Rect(x, y + 20, colW, bh), body, bodyStyle);
                    y += 20 + bh + 14;
                }
            }
            if (GUI.Button(new Rect(panel.x + pw / 2f - 90, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) guide = false;
        }
    }
}
