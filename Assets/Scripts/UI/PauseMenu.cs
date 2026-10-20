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
        private bool options, guide, controls;
        private Bind? capturing;

        /// <summary>For the screenshot run.</summary>
        public static void DebugShowGuide(PauseMenu menu, int page = 0) { Open = true; menu.guide = true; menu.guidePage = page; }
        public static void DebugShowOptions(PauseMenu menu) { Open = true; menu.guide = false; menu.options = true; menu.controls = false; }
        public static void DebugShowControls(PauseMenu menu) { Open = true; menu.guide = false; menu.options = true; menu.controls = true; }

        /// <summary>Keys that can never be bound: menus, chat, the free cursor and the developer keys.</summary>
        private static bool Reserved(Key k) => k == Key.Escape || k == Key.Enter || k == Key.NumpadEnter || k == Key.Slash || k == Key.Backquote
            || k == Key.F1 || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftMeta || k == Key.RightMeta || k == Key.ContextMenu || k == Key.None;

        // The Guide: every progression system and combo on one page, in plain words.
        private static readonly (string title, string body)[] GuideLeft =
        {
            ("EMBERS & THE ANVIL", "Embers drop from enemies, chests and urns, and come from scrapping gear. Spend them at the anvil (G) to temper your class weapon up to +10: each level adds ATT, DEX and 5% RANGE. Hobb the smith also scraps your spare gear for embers, refines and attunes armour, and sells plate and a piece fitted for you."),
            ("WEAPON PATHS", "At +2 your weapon chooses one of two paths, which changes how your class plays (a wider Cleave or a Riposte, Pyromancer or Cryomancer...). At +4 the path is mastered and gains a second effect."),
            ("SIGILS & ABILITY RANKS", "Sigils drop from elites and chests, and every boss drops one for each player. At YSOLDE'S STONES (the runewright, by the standing stones), a Sigil ranks up Q or E, up to rank 8: rank 2 is 30% stronger, rank 3 recharges a quarter faster and adds a twist, and ranks 4-8 keep adding power and speed (rank 8 is MASTERED). Higher ranks cost more Sigils. She sells Sigils too."),
            ("RUNES", "Runes are weapon enchantments: chill, stun, mark, holy healing, burning kills, keen edges, and FARSIGHT (+25% range). Sockets open at +1, +3 and +6; set them at Ysolde's stones. MELD three of the same rune there (plus embers) into one of the next tier: II is 50% stronger, III twice as strong."),
            ("RANGE", "Every attack has a reach. Bolts fizzle, arrows and knives fall past it: Mage bolts ~20 m, Cleric smites 18 m, Ranger arrows 20 m (snap) to 34 m (full draw), Rogue knives 14 m, and the Knight's and Rogue's blades a short reach. Aim past it and the crosshair says OUT OF RANGE. Tempering adds 5% a level; a Farsight rune 25% (melee gains half). Tab shows yours. Past half its range a shot hits softer (60% at the very end), and arrows and knives drop in flight."),
            ("NOTHING CARRIES OVER", "Everything here lasts one run. Only Renown and the tailor's cosmetics, your journal pages and your deeds are kept. The camp is where you start again."),
        };
        private static readonly (string title, string body)[] GuideRight =
        {
            ("GEAR", "Armour and a charm, rolled for each player - only you see your drops. F puts it in your PACK (I to wear it); X at Hobb's anvil scraps it for embers. Deeper and finer pieces are stronger. At the anvil you can refine a piece's quality or attune it to a deeper floor, so a favourite keeps up. Each boss may drop its own Legendary piece: the Warden's Bulwark, the Lich's Phylactery, the Crown of the Hollow King, the Pale Cardinal's Vestments. Silas sells two PENDANTS (charms) at camp; Hobb sells armour."),
            ("RELICS", "Rare powers, one of each per player. Found on elites, in chests and from bosses; the camp shrine offers two each visit."),
            ("COMBOS", "Classes set each other up. Chill + a heavy hit = SHATTER. Stunned enemies are EXPOSED (+25% for all). Taunted enemies are DISTRACTED (+15%). Allies in Consecrate are BLESSED (+20%); the Cleric's beam target is INSPIRED (+15%). Allies in a Rogue's smoke are hidden (SMOKE SCREEN). A broken Aegis shield BURSTS. A marked enemy's mark SPREADS when it dies. Tags over enemies show what is ready to cash in."),
            ("RAGE", "Every enemy hates whoever hurts it most, and hunts them: the meter under a boss (or an elite or champion you aim at) shows the party's rage and who is HUNTED. It changes target only when someone passes the hunted one by a fifth (the red mark). The Knight angers it far faster (2.6x from hits, more from blocking and Shield Bash, and War Cry seizes the top); the Rogue barely (0.7x) - so a Knight holds the boss's face while a Rogue works its back, where every class hits harder (bosses too, now). Healing angers everything near the healer; smoke and shadow wipe your rage."),
            ("ELITES", "Gold-ringed elites carry an affix. WARDED: barely hurt until stunned or chilled. VAMPIRIC: heals from its hits - a Mark stops it. VOLATILE: bursts after death, get clear. SWIFT: fast; chill slows it. SPLITTING: breaks into two skeletons. THORNED: hurts melee attackers - fight it from range. One named CHAMPION per floor guards a hoard."),
        };

        // Page two: the dungeon, its creatures and its tricks.
        private static readonly (string title, string body)[] DungeonLeft =
        {
            ("EVERY CLASS", "R: your ultimate, charged by fighting. V: kick (dodge while sprinting to slide, then kick for a SLIDE KICK). The POTION POUCH holds up to 5 of each: 1 Health Potion (mends 45% over 3 s), 2 Mana Potion, 3 Frost Flask and 4 Holy Water (thrown). There is no natural regeneration: potions drop from the dead and from chests; Silas sells them at camp (Holy Water comes from the shrine). Hits sometimes CRIT. HEADSHOTS deal +50% with ranged attacks (+25% aimed melee); glowing WEAK POINTS (the Brute's back, a lich's phylactery, a Gargoyle's heart, the Hoarder's sack, every boss) deal double. H: high five a nearby ally who does the same for +10% damage. Press G by a downed ally to DRAG them out of danger (you move slowly and cannot fight; they bleed half as fast). A downed ally BLEEDS OUT after 40 s: then carry their glowing SOUL SKULL to the waystone in the start room to bring them back. SEVEN CLASSES: Knight (tank), Mage (burst), Cleric (healer), Rogue (assassin, the fastest), Ranger (marksman), FLAGELLANT (bruiser: harder hits and lifesteal the more hurt you are, Scourge yourself for Zeal, Chains drag enemies in, Penitent Leap spends Fervor) and PLAGUE DOCTOR (afflicter/support: vials BLIGHT the dead so they take +10% from everyone, Tonics heal, Miasma rots, the Plague Mask cleanses and wards the party)."),
            ("THE DEAD", "A red ! over an enemy means it is about to strike (larger and redder when it is close to you). !! is a heavy blow: sidestep it. <!> is a wide sweep: back off. >> is a lunge along a marked line: dodge sideways. WRAITHS are ethereal until chilled, stunned, marked or consecrated. SHIELDBEARERS block from the front: flank them, or KICK (or Shield Bash) the shield aside for a few seconds. GARGOYLES pose as statues from floor 2 and dive along a marked line. MIMICS breathe when you are not looking. MOURNERS (floor 2+) move only while nobody looks at them: keep someone facing them, and listen for scraping stone. BONE HOARDERS run with a sack of gold. AFFLICTERS: Plaguebearers poison (and burst into a cloud), Frost Wights frostbite (slow), Hexers curse (less damage and healing until the Hexer dies). Holy Water cleanses. From floor 2 a PATROL (an elite and its band) marches between rooms, so a cleared room is not always safe. Deeper down they get smarter: they lead their shots (keep changing direction), bosses answer kiting with fast bursts, and ENTLINGS sprint in and root you until killed. Afflictions: POISON, FROSTBITE, CURSE, MANIA (you run wild for a moment and cannot attack) and ROOTED (your feet are frozen; you can still fight). Holy Water cleanses all but an Entling's roots."),
            ("ROOMS", "Kick the dead into chasms and pendulum blades. A VAULT opens when three glowing levers, bolted to walls in different rooms, are all pulled. Smash a ritual chamber's violet braziers to lift its ward. Frost freezes anything standing in water. Balconies and pits reward high ground. Corridor flame traps fire on a beat. Most floors hide a RELIQUARY in a side room: haul it to the waystone for the whole party, guarding the carrier (hits crack it, and its hymn draws the dead). LIBRARIES: shelves block sight both ways. PYRE HALLS: the great fire erupts on a beat; when the red ring fills, get out past it and leave the dead inside. SPIKE GAUNTLETS: a wave of spikes runs down the hall; rows glow before they fire. BLOOD ALTARS want three offerings from the party (a fifth of your health each, embers back each time): when the chalice brims, everyone who bled gets a relic and everyone gets BLOODLUST (+15% damage for 90 s). The RELIQUARY's hymn mends whoever walks near its carrier; delivered, it raises the fallen, pays everyone (a relic each from 50% intact, a Sigil from 70%) and HALLOWS the floor: its boss wakes at 75% health and takes 10% more damage. SPECIAL ROOMS off the main path (like Isaac's): a TREASURY of three relics (one each), a BEGGAR who pays back the generous, a PROVING BELL (two waves, then gear for everyone), an ARMOURY whetstone (a free temper each) a DARK MIRROR (a relic and gold for a fifth of your health until camp) and an OATH ROOM (hold both plates at once - two of you, or one and the Oath Stone - and its coffer opens). TERRACES and ZIGGURATS give high ground: take the ramps. Spike plates hide in some rooms too. The peddler's room is always peaceful."),
        };
        private static readonly (string title, string body)[] DungeonRight =
        {
            ("BOSS PHASES", "Bosses change phase at set health; no burst skips one. Between phases they are briefly INVULNERABLE. WARDED: kill the guards that shield them. SURVIVE: they are immune while a pattern plays out; dodge until the timer ends. ARMORED: a third of your damage gets through. From floor 2 every boss also HUNTS: chains of red circles walk after each of you where you are going (keep changing direction, do not strafe in one line), and fans of lances aimed ahead of you. Tells get shorter the deeper you go. Alone, it comes at you more often; with a party, everyone is hunted and it calls more guards. Bosses gather themselves before a big blow: they crouch, glow and draw embers in. Every guardian's arena has a shape (colonnade, dais or ruins) and often a QUIRK: PYLONS that heal it (break them), BALLISTAS (F, then reload), a CHANDELIER over a lit circle (lure it under, cut the chain) or SANCTUM braziers (each one lit: it takes more damage; it snuffs them). When it falls, the GRAVE LIFT comes down on chains: gather in the cage to ride up to camp. Stunned enemies sway with stars round their heads."),
            ("THE DESCENT", "Ten floors in five acts of two: Ossuary, Drowned Vaults, Hollow Throne, Black Cathedral, then the Abyss. The first floor of an act ends at a WARDEN, the second at the act's master; the Unsung waits on floor 10. After every floor the carriage takes you back to camp. Some floors carry an EVENT, named on the floor banner and in your status badges: GILDED HALLS (more gold), THE SWARM (more dead, more embers), THE DEAD MARCH (more patrols, more gold), BLOOD TITHE (kills mend you, blows hurt more) or BOUNTIFUL (every cleared room leaves something). DO NOT LINGER: about seven minutes into a floor a far bell tolls, and two minutes later THE TALLYMAN walks in. He cannot be hurt or stopped, he gets faster, and his touch drains a quarter of your life a second. Sprint, and seal yourselves into the guardian's arena: then he leaves."),
            ("THE SEALS", "Each act's CHAMPION (a named elite) carries a fragment of an old Seal in the first four acts. Every Seal you break weakens the Unsung at the bottom; every one you leave makes it 15% tougher."),
            ("FLOORS", "Ossuary: stamp out bone piles before they rise. Drowned Vaults: every so often the water thickens to a mire that halves your speed - get onto stairs, balconies or stones. Hollow Throne: smash soul lanterns to heal and stun. Black Cathedral: jump the bell's shockwave. The Abyss (with all three Seals): the light fails now and then - stay together."),
            ("INVENTORY", "I opens it. Gear you pick up goes into your PACK (8 slots); wearing a piece sends the old one back to it. In the pack: click to wear, right-click to scrap for embers (only at Hobb's anvil), shift-click to drop. Right-click worn gear to take it off. Everything is lost when the run ends. With friends: loot is SHARED by default (gold and embers split evenly; the host toggles it with L at camp). Give gold or even out the purse in the inventory, and ctrl-click a pack piece to hand it to the nearest ally."),
            ("PINGS", "Tap Z or middle mouse: mark the enemy, loot or spot you aim at. Hold it for the wheel and flick the mouse: up DANGER, right ON MY WAY, down NEED HELP (pinned to you), left LOOK. Downed, a tap calls for help."),
            ("VOICE", "Proximity voice: anyone within about 30 m hears you, muffled through walls. Shouting wakes the dead within 10 m, statues too. The bled-out can only whisper. Push to talk (T) or turn it off in Options. Every key can be moved in Options > Controls. A GAMEPAD plays too: sticks move and look, A jump, B dodge, X use, Y talk/trade, LB/RB abilities, RT attack, LT secondary, L3 sprint, R3 kick, d-pad up ultimate, left/right/down potions, View the map, Start pauses (menus still want the mouse). TEXT CHAT: press Enter, type, Enter to send (Esc cancels); the game ignores your keys while you type."),
            ("AT CAMP", "Trade is split by craft: SILAS sells potions and provisions, HOBB at the anvil tempers weapons, scraps gear, refines armour and sells embers, plate and a piece fitted for you, YSOLDE at the standing stones does runes, sigils and ability ranks (and melds runes), and the SHRINE offers holy water, relics and Brother Aldric's paid PRAYER (it catches one blow that would down you). Take a CONTRACT at the bounty board for the next floor. Turn the OMEN STONE before a run for a harder descent and more Renown. Spin the WHEEL OF FATE when you find one: it usually gives. From floor 2 a PEDDLER sometimes trades in a side room, at a markup. SERGEANT BRANN in the training yard sells drills (a stat for the run, dearer each time), MISTER BONES deals blackjack for up to four players (bets 5-100; win 300 in one visit and he cuts you off until the next floor), and CORWIN at the carriage sells a SCOUT'S MAP of the next floor and SMELLING SALTS (get up on your own once). From +6 the anvil wants gold as well as embers; it goes to +10 (+8 ASCENDED, +10 MASTERWORK, a third rune socket at +6). Sigils take abilities to rank 8. All cosmetics at the tailor are free for now. The host has a DEVELOPER MENU on F1."),
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
            if (capturing.HasValue && keyboard != null)
            {
                // Waiting for a key: the first one pressed takes the binding; Esc gives up.
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
            if (((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || Binds.PauseDown) && !OtherMenuOpenLastFrame)
            {
                Open = !Open;
                options = guide = controls = false;
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
            if (options && controls)
            {
                DrawControls(w, h);
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
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("CONTROLS", UiSkin.Button, GUILayout.Height(42))) controls = true;
                if (GUILayout.Button("BACK", UiSkin.Button, GUILayout.Height(42))) options = false;
                GUILayout.EndHorizontal();
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

        /// <summary>
        /// Options > Controls (2026-10-20): every gameplay key in two columns. Click one, press the new key
        /// (Esc cancels); a key already in use swaps with it, so nothing is ever left unbound.
        /// </summary>
        private void DrawControls(float w, float h)
        {
            var area = new Rect(w / 2f - 390, h / 2f - 330, 780, 660);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUI.Label(new Rect(area.x, area.y, area.width, 40), "CONTROLS", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            GUI.Label(new Rect(area.x, area.y + 40, area.width, 20), capturing.HasValue ? "Press a key for " + LabelOf(capturing.Value).ToUpperInvariant() + "   (Esc cancels)" : "Click an action, then press its new key. Mouse buttons, Esc, Enter (chat) and Alt (free cursor) stay put.",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = capturing.HasValue ? UiSkin.Gold : UiSkin.Small.normal.textColor } });
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
                    new GUIStyle(keyStyle) { normal = { textColor = live || changed ? UiSkin.Gold : Color.white } });
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) capturing = live ? (Bind?)null : d.bind;
            }
            float by = area.yMax - 46f;
            if (GUI.Button(new Rect(area.x, by, 250, 42), "RESET TO DEFAULTS", UiSkin.Button)) { Binds.ResetAll(); capturing = null; }
            if (GUI.Button(new Rect(area.xMax - 250, by, 250, 42), "BACK", UiSkin.Button)) { controls = false; capturing = null; }
        }

        private static string LabelOf(Bind b)
        {
            foreach (var d in Binds.Defaults) if (d.bind == b) return d.label;
            return b.ToString();
        }

        // The journal: the pages found so far, by floor; the rest are blanks to find.
        private Vector2 journalScroll;
        private int journalOpen = -1;

        private Vector2 bestiaryScroll;

        /// <summary>Two columns of the dead you have met (bosses after), "???" for the rest.</summary>
        private void DrawBestiary(Rect panel, float colW, GUIStyle titleStyle, GUIStyle bodyStyle)
        {
            var view = new Rect(panel.x + 20, panel.y + 58, panel.width - 40, panel.height - 124);
            float rowH = 62f;
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
                GUI.Label(new Rect(r.x + 10, r.y + 3, r.width - 14, 20), seen ? e.Name.ToUpperInvariant() : "???", new GUIStyle(titleStyle) { normal = { textColor = seen ? (e.Boss ? new Color(0.95f, 0.45f, 0.35f) : UiSkin.Gold) : new Color(0.5f, 0.48f, 0.45f) } });
                GUI.Label(new Rect(r.x + 10, r.y + 22, r.width - 14, r.height - 24), seen ? e.Text : (e.Boss ? "A guardian you have not faced." : "Not yet met."), new GUIStyle(bodyStyle) { fontSize = 12, normal = { textColor = seen ? bodyStyle.normal.textColor : new Color(0.5f, 0.48f, 0.45f) } });
            }
            GUI.EndScrollView();
        }

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
            string[] titles = { "GUIDE: PROGRESSION", "GUIDE: THE DUNGEON", "JOURNAL  (" + Journal.Count + " / " + Journal.Entries.Length + " pages)", "BESTIARY  (" + Bestiary.SeenCount + " / " + Bestiary.Entries.Length + " met)" };
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), titles[guidePage], new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            float colW = (pw - 72f) / 2f;
            var titleStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = UiSkin.Gold } };
            var bodyStyle = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            if (guidePage == 2) DrawJournal(panel, colW, titleStyle, bodyStyle);
            else if (guidePage == 3) DrawBestiary(panel, colW, titleStyle, bodyStyle);
            else for (int c = 0; c < 2; c++)
            {
                float x = panel.x + 24 + c * (colW + 24), y = panel.y + 64;
                foreach (var (title, body) in guidePage == 0 ? (c == 0 ? GuideLeft : GuideRight) : (c == 0 ? DungeonLeft : DungeonRight))
                {
                    GUI.Label(new Rect(x, y, colW, 20), title, titleStyle);
                    string text = Binds.Rekey(body);
                    float bh = bodyStyle.CalcHeight(new GUIContent(text), colW);
                    GUI.Label(new Rect(x, y + 20, colW, bh), text, bodyStyle);
                    y += 20 + bh + 14;
                }
            }
            string[] next = { "THE DUNGEON >", "JOURNAL >", "BESTIARY >", "PROGRESSION >" };
            if (GUI.Button(new Rect(panel.x + pw / 2f - 280, panel.yMax - 56, 180, 40), next[guidePage], UiSkin.Button)) guidePage = (guidePage + 1) % 4;
            if (GUI.Button(new Rect(panel.x + pw / 2f + 100, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) guide = false;
        }
    }
}
