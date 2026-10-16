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
        public static void DebugShowGuide(PauseMenu menu, int page = 0) { Open = true; menu.guide = true; menu.guidePage = page; }

        // The Guide: every progression system and combo on one page, in plain words.
        private static readonly (string title, string body)[] GuideLeft =
        {
            ("EMBERS & THE ANVIL", "Embers drop from enemies, chests and urns, and come from scrapping gear. Spend them at the anvil beside the merchant (G) to temper your class weapon from +0 to +5: each level adds ATT and DEX."),
            ("WEAPON PATHS", "At +2 your weapon chooses one of two paths, which changes how your class plays (a wider Cleave or a Riposte, Pyromancer or Cryomancer...). At +4 the path is mastered and gains a second effect."),
            ("SIGILS & ABILITY RANKS", "Sigils drop from elites and chests, and every boss drops one for each player. At the anvil, a Sigil ranks up Q or E: rank 2 is 30% stronger, rank 3 recharges a quarter faster and adds a twist."),
            ("RUNES", "Runes are weapon enchantments: chill, stun, mark, holy healing, burning kills, keen edges. Sockets open at +1 and +3; set them at the anvil."),
            ("NOTHING CARRIES OVER", "Everything here lasts one run (only Renown and cosmetics from the tailor are kept). The camp is where you start again."),
        };
        private static readonly (string title, string body)[] GuideRight =
        {
            ("GEAR", "Armour and a charm, rolled for each player - only you see your drops. F equips (your old piece is left in its place), X scraps it for embers. Deeper and finer pieces are stronger. At the anvil you can refine a piece's quality or attune it to a deeper floor, so a favourite keeps up. Each boss may drop its own Legendary piece: the Warden's Bulwark, the Lich's Phylactery, the Crown of the Hollow King, the Pale Cardinal's Vestments."),
            ("RELICS", "Rare powers, one of each per player. Found on elites, in chests, from bosses, and sold by the merchant."),
            ("COMBOS", "Classes set each other up. Chill + a heavy hit = SHATTER. Stunned enemies are EXPOSED (+25% for all). Taunted enemies are DISTRACTED (+15%). Allies in Consecrate are BLESSED (+20%); the Cleric's beam target is INSPIRED (+15%). Allies in a Rogue's smoke are hidden (SMOKE SCREEN). A broken Aegis shield BURSTS. A marked enemy's mark SPREADS when it dies. Tags over enemies show what is ready to cash in."),
            ("ELITES", "Gold-ringed elites carry an affix. WARDED: barely hurt until stunned or chilled. VAMPIRIC: heals from its hits - a Mark stops it. VOLATILE: bursts after death, get clear. SWIFT: fast; chill slows it. SPLITTING: breaks into two skeletons. THORNED: hurts melee attackers - fight it from range. One named CHAMPION per floor guards a hoard."),
        };

        // Page two: the dungeon, its creatures and its tricks.
        private static readonly (string title, string body)[] DungeonLeft =
        {
            ("EVERY CLASS", "R: your ultimate, charged by fighting. V: kick (dodge while sprinting to slide, then kick for a SLIDE KICK). The POTION POUCH holds up to 5 of each: 1 Health Potion (mends 45% over 3 s), 2 Mana Potion, 3 Frost Flask and 4 Holy Water (thrown). There is no natural regeneration: potions drop from the dead and from chests, and the merchant sells them. Hits sometimes CRIT. HEADSHOTS deal +50% with ranged attacks (+25% aimed melee); glowing WEAK POINTS (the Brute's back, a lich's phylactery, a Gargoyle's heart, the Hoarder's sack, every boss) deal double. H: high five a nearby ally who does the same for +10% damage. A downed ally BLEEDS OUT after 40 s: then carry their glowing SOUL SKULL to the waystone in the start room to bring them back."),
            ("THE DEAD", "A red ! over an enemy means it is about to strike (larger and redder when it is close to you). !! is a heavy blow: sidestep it. <!> is a wide sweep: back off. >> is a lunge along a marked line: dodge sideways. WRAITHS are ethereal until chilled, stunned, marked or consecrated. SHIELDBEARERS block from the front: flank them, or KICK (or Shield Bash) the shield aside for a few seconds. GARGOYLES pose as statues from floor 2 and dive along a marked line. MIMICS breathe when you are not looking. MOURNERS (floor 2+) move only while nobody looks at them: keep someone facing them, and listen for scraping stone. BONE HOARDERS run with a sack of gold. AFFLICTERS: Plaguebearers poison (and burst into a cloud), Frost Wights frostbite (slow), Hexers curse (less damage and healing until the Hexer dies). Holy Water cleanses."),
            ("ROOMS", "Kick the dead into chasms and pendulum blades. A VAULT opens when three glowing levers, bolted to walls in different rooms, are all pulled. Smash a ritual chamber's violet braziers to lift its ward. Frost freezes anything standing in water. Balconies and pits reward high ground. Corridor flame traps fire on a beat. Most floors hide a RELIQUARY in a side room: haul it to the waystone for the whole party, guarding the carrier (hits crack it, and its hymn draws the dead)."),
        };
        private static readonly (string title, string body)[] DungeonRight =
        {
            ("BOSS PHASES", "Bosses change phase at set health; no burst skips one. Between phases they are briefly INVULNERABLE. WARDED: kill the guards that shield them. SURVIVE: they are immune while a pattern plays out; dodge until the timer ends. ARMORED: a third of your damage gets through."),
            ("FLOORS", "Ossuary: stamp out bone piles before they rise. Drowned Vaults: get to high ground when the tide surges. Hollow Throne: smash soul lanterns to heal and stun. Black Cathedral: jump the bell's shockwave."),
            ("INVENTORY", "I opens it. Equipping found gear sends what you wore to your PACK (8 slots); G stows a piece without wearing it. In the pack: click to wear, right-click to scrap for embers, shift-click to drop. Right-click worn gear to take it off. Everything is lost when the run ends."),
            ("PINGS", "Tap Z or middle mouse: mark the enemy, loot or spot you aim at. Hold it for the wheel and flick the mouse: up DANGER, right ON MY WAY, down NEED HELP (pinned to you), left LOOK. Downed, a tap calls for help."),
            ("VOICE", "Proximity voice: anyone within about 30 m hears you, muffled through walls. Shouting wakes the dead within 10 m, statues too. The bled-out can only whisper. Push to talk (T) or turn it off in Options."),
            ("AT CAMP", "Take a CONTRACT at the bounty board for the next floor. Turn the OMEN STONE before a run for a harder descent and more Renown. Spin the WHEEL OF FATE when you find one: it usually gives. From floor 2 a PEDDLER sometimes trades in a side room, at a markup."),
            ("RELIC SYNERGIES", "Some pairs awaken more: Frost Charm + Winter's Tooth, Vampiric Fang + Blood Pact, Thunder Sigil + Echo Stone, Executioner's Coin + Berserker's Brand, Phoenix Feather + Heart of the Deep."),
        };
        private int guidePage;

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

        private static void DrawVoiceOptions()
        {
            GUILayout.Label("PROXIMITY VOICE", UiSkin.Heading);
            GUILayout.Label(Player.PlayerVoice.MicAvailable ? "Players near you hear you; walls muffle. Shouting draws the dead." : "No microphone found: you can still hear others.", UiSkin.Body);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Settings.VoiceModeNames.Length; i++)
            {
                bool on = Settings.VoiceMode == i;
                if (GUILayout.Toggle(on, " " + Settings.VoiceModeNames[i], UiSkin.Body) && !on) Settings.VoiceMode = i;
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
            // Live meter: your level against the threshold line.
            var r = GUILayoutUtility.GetRect(370, 14);
            UiSkin.Rect(r, new Color(0f, 0f, 0f, 0.6f));
            float level = Mathf.Clamp01(Player.PlayerVoice.MicLevel / 0.08f);
            bool open = Player.PlayerVoice.MicLevel >= Settings.MicThreshold;
            UiSkin.Rect(new Rect(r.x, r.y, r.width * level, r.height), open ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.6f, 0.6f, 0.6f));
            float tx = r.x + r.width * Mathf.Clamp01(Settings.MicThreshold / 0.08f);
            UiSkin.Rect(new Rect(tx - 1, r.y - 2, 2, r.height + 4), new Color(1f, 0.85f, 0.4f));
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
            var area = options ? new Rect(w / 2f - 390, h / 2f - 290, 780, 590) : new Rect(w / 2f - 180, h / 2f - 200, 360, 410);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUILayout.BeginArea(area);
            GUILayout.Label(options ? "OPTIONS" : "PAUSED", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            GUILayout.Space(14);

            if (options)
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(370));
                GUILayout.Label("Mouse sensitivity  " + Settings.MouseSensitivity.ToString("F2"), UiSkin.Body);
                Settings.MouseSensitivity = GUILayout.HorizontalSlider(Settings.MouseSensitivity, 0.2f, 3f);
                GUILayout.Space(10);
                GUILayout.Label("Volume  " + Mathf.RoundToInt(Settings.Volume * 100f) + "%", UiSkin.Body);
                Settings.Volume = GUILayout.HorizontalSlider(Settings.Volume, 0f, 1f);
                GUILayout.Space(10);
                GUILayout.Label("Field of view  " + Mathf.RoundToInt(Settings.FieldOfView), UiSkin.Body);
                Settings.FieldOfView = GUILayout.HorizontalSlider(Settings.FieldOfView, 60f, 100f);
                GUILayout.Space(10);
                GUILayout.Label("Screen shake  " + Mathf.RoundToInt(Settings.ShakeScale * 100f) + "%", UiSkin.Body);
                Settings.ShakeScale = GUILayout.HorizontalSlider(Settings.ShakeScale, 0f, 1.5f);
                GUILayout.Space(8);
                Settings.DamageNumbers = GUILayout.Toggle(Settings.DamageNumbers, "  Damage numbers", UiSkin.Body);
                Settings.ReduceFlashing = GUILayout.Toggle(Settings.ReduceFlashing, "  Reduce flashing effects", UiSkin.Body);
                Settings.ShowTips = GUILayout.Toggle(Settings.ShowTips, "  Show tips", UiSkin.Body);
                Settings.ShowFps = GUILayout.Toggle(Settings.ShowFps, "  Show frame rate", UiSkin.Body);
                if (GUILayout.Button("Show all tips again", UiSkin.Button, GUILayout.Height(30))) Settings.ResetTips();
                var cam = Player.NetworkPlayer.Local != null ? Player.NetworkPlayer.Local.Camera : null;
                if (cam != null) cam.fieldOfView = Settings.FieldOfView;
                GUILayout.EndVertical();
                GUILayout.Space(40);
                GUILayout.BeginVertical(GUILayout.Width(370));
                DrawVoiceOptions();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
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
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), guidePage == 0 ? "GUIDE: PROGRESSION" : "GUIDE: THE DUNGEON", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            float colW = (pw - 72f) / 2f;
            var titleStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = UiSkin.Gold } };
            var bodyStyle = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            for (int c = 0; c < 2; c++)
            {
                float x = panel.x + 24 + c * (colW + 24), y = panel.y + 64;
                foreach (var (title, body) in guidePage == 0 ? (c == 0 ? GuideLeft : GuideRight) : (c == 0 ? DungeonLeft : DungeonRight))
                {
                    GUI.Label(new Rect(x, y, colW, 20), title, titleStyle);
                    float bh = bodyStyle.CalcHeight(new GUIContent(body), colW);
                    GUI.Label(new Rect(x, y + 20, colW, bh), body, bodyStyle);
                    y += 20 + bh + 14;
                }
            }
            if (GUI.Button(new Rect(panel.x + pw / 2f - 280, panel.yMax - 56, 180, 40), guidePage == 0 ? "THE DUNGEON >" : "< PROGRESSION", UiSkin.Button)) guidePage = 1 - guidePage;
            if (GUI.Button(new Rect(panel.x + pw / 2f + 100, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) guide = false;
        }
    }
}
