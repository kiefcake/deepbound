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
        public static void DebugShowOptions(PauseMenu menu) { Open = true; menu.guide = false; menu.options = true; }

        // The Guide: every progression system and combo on one page, in plain words.
        private static readonly (string title, string body)[] GuideLeft =
        {
            ("EMBERS & THE ANVIL", "Embers drop from enemies, chests and urns, and come from scrapping gear. Spend them at the anvil (G) to temper your class weapon from +0 to +5: each level adds ATT, DEX and 5% RANGE. Hobb the smith sells embers there too."),
            ("WEAPON PATHS", "At +2 your weapon chooses one of two paths, which changes how your class plays (a wider Cleave or a Riposte, Pyromancer or Cryomancer...). At +4 the path is mastered and gains a second effect."),
            ("SIGILS & ABILITY RANKS", "Sigils drop from elites and chests, and every boss drops one for each player. At the anvil, a Sigil ranks up Q or E: rank 2 is 30% stronger, rank 3 recharges a quarter faster and adds a twist."),
            ("RUNES", "Runes are weapon enchantments: chill, stun, mark, holy healing, burning kills, keen edges, and FARSIGHT (+25% range). Sockets open at +1 and +3; set them at the anvil."),
            ("RANGE", "Every attack has a reach. Bolts fizzle, arrows and knives fall past it: Mage bolts ~20 m, Cleric smites 18 m, Ranger arrows 20 m (snap) to 34 m (full draw), Rogue knives 14 m, and the Knight's and Rogue's blades a short reach. Aim past it and the crosshair says OUT OF RANGE. Tempering adds 5% a level; a Farsight rune 25% (melee gains half). Tab shows yours. Past half its range a shot hits softer (60% at the very end), and arrows and knives drop in flight."),
            ("NOTHING CARRIES OVER", "Everything here lasts one run. Only Renown and the tailor's cosmetics, your journal pages and your deeds are kept. The camp is where you start again."),
        };
        private static readonly (string title, string body)[] GuideRight =
        {
            ("GEAR", "Armour and a charm, rolled for each player - only you see your drops. F equips (your old piece is left in its place), X scraps it for embers. Deeper and finer pieces are stronger. At the anvil you can refine a piece's quality or attune it to a deeper floor, so a favourite keeps up. Each boss may drop its own Legendary piece: the Warden's Bulwark, the Lich's Phylactery, the Crown of the Hollow King, the Pale Cardinal's Vestments."),
            ("RELICS", "Rare powers, one of each per player. Found on elites, in chests and from bosses; the camp shrine offers two each visit."),
            ("COMBOS", "Classes set each other up. Chill + a heavy hit = SHATTER. Stunned enemies are EXPOSED (+25% for all). Taunted enemies are DISTRACTED (+15%). Allies in Consecrate are BLESSED (+20%); the Cleric's beam target is INSPIRED (+15%). Allies in a Rogue's smoke are hidden (SMOKE SCREEN). A broken Aegis shield BURSTS. A marked enemy's mark SPREADS when it dies. Tags over enemies show what is ready to cash in."),
            ("RAGE", "Every enemy hates whoever hurts it most, and hunts them: the meter under a boss (or an elite or champion you aim at) shows the party's rage and who is HUNTED. It changes target only when someone passes the hunted one by a fifth (the red mark). The Knight angers it far faster (2.6x from hits, more from blocking and Shield Bash, and War Cry seizes the top); the Rogue barely (0.7x) - so a Knight holds the boss's face while a Rogue works its back, where every class hits harder (bosses too, now). Healing angers everything near the healer; smoke and shadow wipe your rage."),
            ("ELITES", "Gold-ringed elites carry an affix. WARDED: barely hurt until stunned or chilled. VAMPIRIC: heals from its hits - a Mark stops it. VOLATILE: bursts after death, get clear. SWIFT: fast; chill slows it. SPLITTING: breaks into two skeletons. THORNED: hurts melee attackers - fight it from range. One named CHAMPION per floor guards a hoard."),
        };

        // Page two: the dungeon, its creatures and its tricks.
        private static readonly (string title, string body)[] DungeonLeft =
        {
            ("EVERY CLASS", "R: your ultimate, charged by fighting. V: kick (dodge while sprinting to slide, then kick for a SLIDE KICK). The POTION POUCH holds up to 5 of each: 1 Health Potion (mends 45% over 3 s), 2 Mana Potion, 3 Frost Flask and 4 Holy Water (thrown). There is no natural regeneration: potions drop from the dead and from chests; Silas sells them at camp (Holy Water comes from the shrine). Hits sometimes CRIT. HEADSHOTS deal +50% with ranged attacks (+25% aimed melee); glowing WEAK POINTS (the Brute's back, a lich's phylactery, a Gargoyle's heart, the Hoarder's sack, every boss) deal double. H: high five a nearby ally who does the same for +10% damage. A downed ally BLEEDS OUT after 40 s: then carry their glowing SOUL SKULL to the waystone in the start room to bring them back."),
            ("THE DEAD", "A red ! over an enemy means it is about to strike (larger and redder when it is close to you). !! is a heavy blow: sidestep it. <!> is a wide sweep: back off. >> is a lunge along a marked line: dodge sideways. WRAITHS are ethereal until chilled, stunned, marked or consecrated. SHIELDBEARERS block from the front: flank them, or KICK (or Shield Bash) the shield aside for a few seconds. GARGOYLES pose as statues from floor 2 and dive along a marked line. MIMICS breathe when you are not looking. MOURNERS (floor 2+) move only while nobody looks at them: keep someone facing them, and listen for scraping stone. BONE HOARDERS run with a sack of gold. AFFLICTERS: Plaguebearers poison (and burst into a cloud), Frost Wights frostbite (slow), Hexers curse (less damage and healing until the Hexer dies). Holy Water cleanses."),
            ("ROOMS", "Kick the dead into chasms and pendulum blades. A VAULT opens when three glowing levers, bolted to walls in different rooms, are all pulled. Smash a ritual chamber's violet braziers to lift its ward. Frost freezes anything standing in water. Balconies and pits reward high ground. Corridor flame traps fire on a beat. Most floors hide a RELIQUARY in a side room: haul it to the waystone for the whole party, guarding the carrier (hits crack it, and its hymn draws the dead)."),
        };
        private static readonly (string title, string body)[] DungeonRight =
        {
            ("BOSS PHASES", "Bosses change phase at set health; no burst skips one. Between phases they are briefly INVULNERABLE. WARDED: kill the guards that shield them. SURVIVE: they are immune while a pattern plays out; dodge until the timer ends. ARMORED: a third of your damage gets through."),
            ("THE DESCENT", "Ten floors in five acts of two: Ossuary, Drowned Vaults, Hollow Throne, Black Cathedral, then the Abyss. The first floor of an act ends at a WARDEN, the second at the act's master; the Unsung waits on floor 10. After every floor the carriage takes you back to camp."),
            ("THE SEALS", "Each act's CHAMPION (a named elite) carries a fragment of an old Seal in the first four acts. Every Seal you break weakens the Unsung at the bottom; every one you leave makes it 15% tougher."),
            ("FLOORS", "Ossuary: stamp out bone piles before they rise. Drowned Vaults: every so often the water thickens to a mire that halves your speed - get onto stairs, balconies or stones. Hollow Throne: smash soul lanterns to heal and stun. Black Cathedral: jump the bell's shockwave. The Abyss (with all three Seals): the light fails now and then - stay together."),
            ("INVENTORY", "I opens it. Equipping found gear sends what you wore to your PACK (8 slots); G stows a piece without wearing it. In the pack: click to wear, right-click to scrap for embers, shift-click to drop. Right-click worn gear to take it off. Everything is lost when the run ends. With friends: loot is SHARED by default (gold and embers split evenly; the host toggles it with L at camp). Give gold or even out the purse in the inventory, and ctrl-click a pack piece to hand it to the nearest ally."),
            ("PINGS", "Tap Z or middle mouse: mark the enemy, loot or spot you aim at. Hold it for the wheel and flick the mouse: up DANGER, right ON MY WAY, down NEED HELP (pinned to you), left LOOK. Downed, a tap calls for help."),
            ("VOICE", "Proximity voice: anyone within about 30 m hears you, muffled through walls. Shouting wakes the dead within 10 m, statues too. The bled-out can only whisper. Push to talk (T) or turn it off in Options."),
            ("AT CAMP", "Trade is split by craft: SILAS sells potions and provisions, HOBB at the anvil sells embers, plate, sigils and a piece fitted for you, and the SHRINE offers holy water, relics and Brother Aldric's paid PRAYER (it catches one blow that would down you). Take a CONTRACT at the bounty board for the next floor. Turn the OMEN STONE before a run for a harder descent and more Renown. Spin the WHEEL OF FATE when you find one: it usually gives. From floor 2 a PEDDLER sometimes trades in a side room, at a markup. The TRAINING YARD board sells drills (a stat for the run, dearer each time), and CORWIN at the carriage sells a SCOUT'S MAP of the next floor and SMELLING SALTS (get up on your own once). From +6 the anvil wants gold as well as embers; it goes to +10 (+8 ASCENDED, +10 MASTERWORK, a third rune socket at +6). Sigils take abilities to rank 5."),
            ("RELIC SYNERGIES", "Some pairs awaken more: Frost Charm + Winter's Tooth, Vampiric Fang + Blood Pact, Thunder Sigil + Echo Stone, Executioner's Coin + Berserker's Brand, Phoenix Feather + Heart of the Deep."),
        };
        private int guidePage;

        /// <summary>A checkbox that shows its state (a gold square inside a box), label beside it. <paramref name="radio"/>: round-ish, for one-of choices.</summary>
        private static bool Check(bool on, string label, bool radio = false)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent("      " + label), UiSkin.Body, GUILayout.Height(24));
            var box = new Rect(rect.x + 2f, rect.y + 4f, 16f, 16f);
            var gold = UiSkin.Gold;
            UiSkin.Rect(box, new Color(gold.r * 0.55f, gold.g * 0.55f, gold.b * 0.55f, 0.95f));
            UiSkin.Rect(new Rect(box.x + 1f, box.y + 1f, 14f, 14f), new Color(0.05f, 0.04f, 0.03f, 1f));
            if (on) UiSkin.Rect(radio ? new Rect(box.x + 5f, box.y + 5f, 6f, 6f) : new Rect(box.x + 3.5f, box.y + 3.5f, 9f, 9f), gold);
            GUI.Label(new Rect(rect.x + 26f, rect.y, rect.width - 26f, rect.height), label, new GUIStyle(UiSkin.Body) { normal = { textColor = on ? Color.white : new Color(0.8f, 0.78f, 0.72f) } });
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) on = !on;
            return on;
        }

        /// <summary>A row of buttons, the chosen one underlined in gold.</summary>
        private static int Choice(int current, string[] names)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++)
            {
                if (GUILayout.Button(names[i], UiSkin.Button, GUILayout.Height(30))) current = i;
                if (i == current && Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    UiSkin.Rect(new Rect(r.x + 4f, r.yMax - 4f, r.width - 8f, 3f), UiSkin.Gold);
                }
            }
            GUILayout.EndHorizontal();
            return current;
        }

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
            var area = options ? new Rect(w / 2f - 390, h / 2f - 335, 780, 680) : new Rect(w / 2f - 180, h / 2f - 200, 360, 410);
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
                Settings.DamageNumbers = Check(Settings.DamageNumbers, "Damage numbers");
                Settings.ReduceFlashing = Check(Settings.ReduceFlashing, "Reduce flashing effects");
                Settings.ShowTips = Check(Settings.ShowTips, "Show tips");
                Settings.ShowFps = Check(Settings.ShowFps, "Show frame rate");
                if (GUILayout.Button("Show all tips again", UiSkin.Button, GUILayout.Height(30))) Settings.ResetTips();
                GUILayout.Space(10);
                GUILayout.Label("Graphics  (lower helps slower PCs)", UiSkin.Body);
                int gq = Choice(Settings.GraphicsQuality, Settings.GraphicsNames);
                if (gq != Settings.GraphicsQuality) Settings.GraphicsQuality = gq;
                bool vs = Check(Settings.VSync, "VSync (off: uncapped frame rate)");
                if (vs != Settings.VSync) Settings.VSync = vs;
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

        // The journal: the pages found so far, by floor; the rest are blanks to find.
        private Vector2 journalScroll;
        private int journalOpen = -1;

        private void DrawJournal(Rect panel, float colW, GUIStyle titleStyle, GUIStyle bodyStyle)
        {
            string[] floors = { "THE OSSUARY", "THE DROWNED VAULTS", "THE HOLLOW THRONE", "THE BLACK CATHEDRAL", "EULOGIES" };
            float listX = panel.x + 24, y = panel.y + 58, listW = colW * 0.75f;
            for (int f = 0; f < 5; f++)
            {
                GUI.Label(new Rect(listX, y, listW, 20), floors[f], titleStyle);
                y += 20;
                for (int i = 0; i < Journal.Entries.Length; i++)
                {
                    if (Journal.Entries[i].Floor != f) continue;
                    bool have = Journal.Has(i);
                    var r = new Rect(listX + 8, y, listW - 8, 19);
                    if (have && GUI.Button(r, (journalOpen == i ? "> " : "") + Journal.Entries[i].Title, new GUIStyle(UiSkin.Body) { fontSize = 14, normal = { textColor = journalOpen == i ? UiSkin.Gold : new Color(0.9f, 0.88f, 0.84f) } })) journalOpen = i;
                    if (!have) GUI.Label(r, f == 4 ? "a guardian not yet felled" : "a page not yet found", new GUIStyle(UiSkin.Body) { fontSize = 14, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.55f, 0.52f, 0.48f) } });
                    y += 19;
                }
                y += 4;
            }
            float tx = listX + listW + 24, tw = panel.width - (tx - panel.x) - 24;
            if (journalOpen >= 0 && Journal.Has(journalOpen))
            {
                var e = Journal.Entries[journalOpen];
                GUI.Label(new Rect(tx, panel.y + 64, tw, 30), e.Title, new GUIStyle(UiSkin.Heading) { fontSize = 22 });
                GUI.Label(new Rect(tx, panel.y + 100, tw, panel.height - 180), e.Text, new GUIStyle(bodyStyle) { fontSize = 16, fontStyle = FontStyle.Italic });
            }
            else GUI.Label(new Rect(tx, panel.y + 64, tw, 60), Journal.Count == 0 ? "Pages of Cathedra's history lie open on lecterns in the dungeon, one on every floor. Read them (F) and they are kept here." : "Choose a page to read it.", bodyStyle);
        }

        private void DrawGuide(float w, float h)
        {
            float pw = Mathf.Min(980f, w - 60f), ph = Mathf.Min(600f, h - 60f);
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.96f);
            string[] titles = { "GUIDE: PROGRESSION", "GUIDE: THE DUNGEON", "JOURNAL  (" + Journal.Count + " / " + Journal.Entries.Length + " pages)" };
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), titles[guidePage], new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            float colW = (pw - 72f) / 2f;
            var titleStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = UiSkin.Gold } };
            var bodyStyle = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            if (guidePage == 2) DrawJournal(panel, colW, titleStyle, bodyStyle);
            else for (int c = 0; c < 2; c++)
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
            string[] next = { "THE DUNGEON >", "JOURNAL >", "PROGRESSION >" };
            if (GUI.Button(new Rect(panel.x + pw / 2f - 280, panel.yMax - 56, 180, 40), next[guidePage], UiSkin.Button)) guidePage = (guidePage + 1) % 3;
            if (GUI.Button(new Rect(panel.x + pw / 2f + 100, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) guide = false;
        }
    }
}
