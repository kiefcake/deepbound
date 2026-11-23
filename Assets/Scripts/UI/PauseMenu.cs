using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// Esc while playing: resume, options, or leave the party. A co-op game cannot stop
    /// time, so this only frees your mouse and covers the screen — the dungeon keeps
    /// moving, which is also why it is quick to get out of.
    /// SOLO (2026-11-20, the user: "pause menu while solo does not work"): a host with nobody else connected
    /// stops the world while the menu is open (Time.timeScale 0); it starts again on closing, or the moment
    /// someone joins.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static bool Open { get; private set; }

        /// <summary>For the screenshot run.</summary>
        public static void SetOpen(bool open) => Open = open;
        private bool options, guide, controls, invitesOpen, legacy;
        private Bind? capturing;

        /// <summary>For the screenshot run.</summary>
        public static void DebugShowGuide(PauseMenu menu, int page = 0) { Open = true; menu.guide = true; menu.guidePage = page; }
        public static void DebugShowOptions(PauseMenu menu, int tab = 0) { Open = true; menu.guide = false; menu.options = true; menu.controls = false; OptionsPanel.Tab = tab; }
        public static void DebugShowControls(PauseMenu menu) { Open = true; menu.guide = false; menu.options = true; menu.controls = true; }

        /// <summary>Keys that can never be bound: menus, chat, the free cursor and the developer keys.</summary>
        private static bool Reserved(Key k) => k == Key.Escape || k == Key.Enter || k == Key.NumpadEnter || k == Key.Slash || k == Key.Backquote
            || k == Key.F1 || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftMeta || k == Key.RightMeta || k == Key.ContextMenu || k == Key.None;

        // The Guide: every progression system and combo on one page, in plain words.
        private static readonly (string title, string body)[] GuideLeft =
        {
            ("EMBERS & THE ANVIL", "Embers drop from enemies, chests and urns, and are gained by scrapping gear. Spend them at the anvil (F) to temper your weapon up to +10; each level adds ATT, DEX and 5% range. At +2 you choose a Path, and at +5, +7 and +9 you forge one of three Edges. Beyond +10, Hobb can hone the blade five times for +3% damage each. Hobb also scraps spare gear, refines and attunes armour, and sells plate."),
            ("WEAPON PATHS", "At +2 your weapon takes one of three Paths: two change how you deal damage, and the third improves survival. At +4 the Path is mastered and gains a second effect. Hobb can replace one Edge per run, for gold."),
            ("BUILDS", "GEAR SETS: matching armour and charm pairs grant a small bonus. MASTERIES: six of Brann's drills in one stat unlock a mastery. MAJOR SOCKET: a rune in your first socket is twice as strong. SHRINES OF TRADE-OFF, hidden behind cracked walls, offer a cursed bargain for the run."),
            ("CLASS GIFTS", "Knight: a raised shield halves trap damage and stops the Gravewarden's charge. Mage: nearby secret walls appear on the map. Cleric: bargains cost half. Rogue: hold F on a spike plate to jam it; hidden plates glint. Ranger: the map shows enemies further away. Flagellant: traps deal less damage. Plague Doctor: potions heal more."),
            ("SIGILS & ABILITY RANKS", "Sigils drop from elites, chests and bosses (one per player). At Ysolde's stones, spend them to rank up Q or E, up to rank 5. Each rank offers a choice of two upgrades that change how the ability works, locked for the run, and also increases potency and reduces cooldown. Ranks cost 1, 1, 2 and 3 Sigils; spares melt into embers."),
            ("RUNES", "Runes enchant your weapon. Sockets open at +1, +3 and +6; set runes at Ysolde's stones. Meld three identical runes (plus embers) into the next tier: II is 50% stronger, III twice as strong."),
            ("RANGE", "Every attack has a maximum range, and damage falls off beyond half of it (to 60% at the limit). Arrows and knives drop over distance. Tempering adds 5% range per level; a Farsight rune adds 25% (half for melee). The crosshair shows OUT OF RANGE when a target is too far."),
            ("NOTHING CARRIES OVER", "Everything you gain lasts one run. Only Renown, cosmetics, journal pages, deeds and the Hall of Records persist."),
        };
        private static readonly (string title, string body)[] GuideRight =
        {
            ("GEAR", "Armour and charms are rolled per player; only you see your drops. F stores a piece in your pack (I to equip). Deeper finds are stronger, and Hobb can refine or attune a favourite to keep it relevant. Bosses may drop Legendary pieces. Silas sells pendants; Hobb sells armour."),
            ("RARE DRAUGHTS & TOMES (5)", "The fifth pouch slot holds one rare item. IRONBARK: half damage and immunity to afflictions for 8 s. BLOODFURY: 40% more damage and faster attacks for 8 s. GRAVE STILLNESS: invulnerable but helpless for 3 s. TOME OF THE PHOENIX: a fire-bird fights for you for 30 s. TOME OF THE STORM: lightning strikes nearby enemies for 10 s. TOME OF SANCTUARY: a holy circle reduces damage taken by 35% and heals for 10 s."),
            ("RELICS", "Relics grant passive powers; you can carry eight. A duplicate empowers the one you have (II, III: +35% each). With a full tray, new relics empower your weakest. Drop one from the inventory (right-click) to free a slot. Cooldown reduction from all sources caps at 40%."),
            ("YOUR SECOND BAR", "Each class's resource works differently; its bar shows a short reminder. KNIGHT Stamina: spent by blocking and sprinting; when depleted, your guard drops. MAGE Mana: spent by Frostbolt and spells; Firebolt is free. CLERIC Mana: drained by the Mending Beam, which also pauses regeneration. ROGUE Energy: spent by frontal stabs; attacks from behind are free and restore it; when depleted, you are Winded. RANGER Focus: builds from landed arrows and fades when idle; when full, your next full draw is a Deadeye shot. FLAGELLANT Fervor: builds from dealing and taking damage, grants up to 20% bonus damage, and is spent by Penitent Leap. PLAGUE DOCTOR Reagents: spent by vials and restored by Blighted kills; when depleted, vials deal reduced damage and do not Blight."),
            ("COMBOS", "SHATTER: heavy hits on chilled enemies deal bonus damage. EXPOSED: stunned enemies take 25% more damage. DISTRACTED: taunted enemies take 15% more from everyone but the taunter. BLESSED: allies in Consecrate deal 20% more; the Cleric's beam target is INSPIRED (+15%). THERMAL SHOCK: frost on a scorched enemy, or fire on a chilled one, deals a burst of damage. PINCER: striking opposite an ally who just hit deals 15% more. PUNISH: enemies recovering from an attack take 20% more. WARD BROKEN: stuns and chill shatter an elite's ward. PURGE: the Cleric deals 25% more to Blighted enemies. PLAGUE CRASH: the Flagellant's Crash spreads Blight. CONVERGENCE: two players casting their ultimates within 4 s of each other unleash a stunning burst and grant the party Bloodlust. Enemies show what can be exploited: stars for stuns, a red diamond for Marks, frost or embers at their feet."),
            ("SHELTER", "Allies standing close behind a Knight with a raised shield take half damage from attacks the shield faces."),
            ("RAGE", "Enemies target whoever has damaged them most. The meter under a boss or targeted elite shows who is HUNTED; it switches only when someone exceeds that player by a fifth. Knights generate far more threat; Rogues generate less. Healing draws threat from nearby enemies, and stealth clears it."),
            ("ELITES", "Elites carry an affix. WARDED: nearly immune until stunned or chilled. VAMPIRIC: heals from its hits; a Mark stops it. VOLATILE: explodes on death. SWIFT: fast; chill slows it. SPLITTING: splits into two. THORNED: damages melee attackers. FRENZIED: attacks faster below half health. GRAVEBOUND: rises again unless you stand on its bones. HUSHING: hits Silence. CONCUSSIVE: hits Daze. EMBERBOUND: leaves burning patches as it moves; chill slows the trail. SOULBOUND: a linked pair that revive each other unless both die close together. One named CHAMPION per floor guards a hoard and fights according to its title."),
        };

        // Page two: the dungeon, its creatures and its tricks.
        private static readonly (string title, string body)[] DungeonLeft =
        {
            ("EVERY CLASS", "R: ultimate, charged by fighting. V: kick (dodge while sprinting to slide, then kick for a Slide Kick). Dodging through a blow at the last moment returns your dodge at once, and your next hit within 2 s deals 30% more. The pouch holds five of each: 1 Health Potion (heals 45% over 3 s), 2 Frost Flask and 3 Holy Water (both thrown). VIT slowly regenerates health. Hits can CRIT. Ranged headshots deal 50% more (aimed melee 25%), and glowing weak points deal double. Tap F by a downed ally to drag them; hold F to revive. A downed ally bleeds out after 40 s; carry their soul skull to the start room's waystone to bring them back."),
            ("THE DEAD", "Enemies hear you: running carries about 11 m and combat about 16, halved through walls; walking is quiet. Your first hit on an unaware enemy deals 50% more. Only three attack at once; the rest wait. Break line of sight and stay quiet to lose them. Attack warnings: ! is a strike, !! a heavy blow (sidestep), <!> a wide sweep (back off), >> a lunge along a marked line (dodge sideways). Afflictions: POISON, FROSTBITE (slow), CURSE (40% more damage taken), WEAKNESS (40% less damage dealt), MANIA (no attacking), BLIND (no map or health bars), SILENCE (no abilities) and ROOTED. Holy Water cleanses all but roots. From floor 2, patrols move between rooms, and deeper enemies lead their shots."),
            ("ROOMS", "Flame vents and spike plates deal a third of your health; cross when the fire drops. Kick enemies into chasms and pendulum blades. Pull three levers on a floor to open its vault. Falls over four metres cause damage. Jump into a ledge up to two metres high to climb it. LAIRS hold a sleeping keeper and its hoard. RELIQUARIES, carried to the waystone, revive the fallen, reward the party and weaken the boss. BLOOD ALTARS trade health for damage and relics. Special side rooms include the Treasury, Beggar, Proving Bell, Armoury, Dark Mirror, Oath Room and the Relic Crucible, which turns your weakest relic into a different one a tier higher. Lightless halls require a carried torch. The peddler's room is always safe."),
        };
        private static readonly (string title, string body)[] DungeonRight =
        {
            ("BOSS PHASES", "Bosses change phase at set health thresholds and are briefly invulnerable between them. WARDED: kill the guards first. SURVIVE: dodge until the timer ends. ARMORED: damage is reduced. From floor 2, bosses hunt with trails of red circles and aimed lances; keep changing direction. Bosses crouch and glow before heavy attacks. Arenas may include healing pylons, ballistas, chandeliers or sanctum braziers. BREAK METER: weak points, headshots, stuns and kicks fill it; when full, the boss is Broken and takes 50% more damage. SHARED DOOM: everyone inside the dark ring splits its damage; stack on the marked player."),
            ("THE DESCENT", "Ten floors across five acts: the Ossuary, Drowned Vaults, Hollow Throne, Black Cathedral and the Abyss. Each act ends at a Warden and then its master; the Unsung waits on floor 10. Some floors carry an EVENT, shown on the floor banner. Do not linger: after a few minutes a bell tolls, and two minutes later the Tallyman arrives. He cannot be harmed and drains your life on contact. Holy Water holds him briefly; sealing the guardian's arena makes him leave."),
            ("THE SEALS", "Each act's Champion in the first four acts carries a Seal fragment. Every Seal broken weakens the Unsung; every one left makes it 15% tougher."),
            ("FLOORS", "Ossuary: stamp out bone piles before they rise. Drowned Vaults: rising water halves your speed; find high ground. Hollow Throne: smash soul lanterns to heal and stun. Black Cathedral: jump the bell's shockwave. The Abyss: the light fails at times; stay together."),
            ("INVENTORY", "Press I to open it. Bone, cloth and iron drop throughout the dungeon and craft potions, flasks, run upgrades and throwable tools (Z): Caltrops slow and damage, and a Bone Rattle lures enemies away. The pack holds eight pieces: click to equip, right-click at Hobb's anvil to scrap, shift-click to drop. Loot is shared by default; the host toggles this with L at camp."),
            ("GIVING", "Look at a nearby ally and hold G to open the give wheel: potions, gold, embers, your rare item, or an emote. Flick toward a slice and release."),
            ("VOICE", "Proximity voice reaches about 30 m, muffled through walls. Shouting wakes nearby enemies. Push-to-talk (T) and all key bindings are in Options. Gamepads are fully supported. Press Enter to type in chat."),
            ("AT CAMP", "SILAS sells potions and provisions. HOBB tempers weapons, scraps gear and sells armour. YSOLDE handles runes, Sigils and ability ranks. The SHRINE offers Holy Water, relics and Aldric's prayer, which prevents one downing. At the tavern, Marta sells mead (+4% damage per level, at the cost of your footing). Take a contract from the bounty board, or turn the Omen Stone for a harder run and more Renown. BRANN sells drills that raise a stat for the run. CORWIN runs the carriage: difficulty, the Daily Descent, the solo Endless Descent, scout maps, smelling salts and climbing ropes. LEGACY (pause menu) spends Renown on permanent boons and lets you bring one relic from your last run as an Heirloom. MISTRESS VEY sells cosmetics and companions; every pet fetches gold and has a trick of its own."),
            ("RELIC SYNERGIES", "Some pairs grant extra power together: Frost Shard + Winter's Tooth, Vampiric Fang + Blood Pact, Thunder Idol + Echo Stone, Executioner's Coin + Berserker's Brand, Phoenix Feather + Heart of the Deep."),
        };
        private int guidePage, lastGuidePage = -1;
        private Vector2 guideScroll;
        private string guideCacheKey;
        private string[][] guideTexts;
        private float[][] guideHeights;
        private float guideTallest;

        /// <summary>A checkbox that shows its state (a gold square inside a box), label beside it. <paramref name="radio"/>: round-ish, for one-of choices.</summary>
        internal static bool Check(bool on, string label, bool radio = false)
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

        /// <summary>Set by other menus each frame; Esc closes them rather than opening this.</summary>
        public static bool OtherMenuOpenLastFrame;

        private void OnDestroy() { Open = false; if (worldPaused) { worldPaused = false; Time.timeScale = 1f; } }

        private static bool worldPaused;
        /// <summary>True while the solo pause holds the world still.</summary>
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

        // ---- Gamepad (2026-10-20): the main list, the guide's pages and Back work on a pad ----
        private int padFocus;
        /// <summary>For tests: is the guide showing?</summary>
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
                // The right stick scrolls a long page.
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
                // Screen order: resume, guide, options, legacy, unstuck (6), leave, quit.
                int pos = System.Array.IndexOf(PadOrder, padFocus);
                padFocus = PadOrder[(pos + (v < 0f ? 1 : PadOrder.Length - 1)) % PadOrder.Length];
            }
            else if (Mathf.Abs(v) <= 0.5f) padRepeatAt = 0f;
            if (back) { Choose(0); return; }
            if (ok) Choose(padFocus);
        }

        private static readonly int[] PadOrder = { 0, 1, 2, 3, 6, 4, 5 };

        /// <summary>0 none; 4 leave the party, 5 quit: asked once more before it happens (2026-11-19).</summary>
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
            GUI.Label(new Rect(panel.x, panel.y + 22, panel.width, 34), head, new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 26 });
            UiSkin.Ornament(panel.center.x - 100f, panel.y + 60f, 200f, 1f);
            GUI.Label(new Rect(panel.x + 24, panel.y + 74, panel.width - 48, 50), body, new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });
            if (UiSkin.Btn(new Rect(panel.x + 30, panel.yMax - 62, 190, 42), confirm == 4 ? "LEAVE" : "QUIT", UiSkin.Button)) Confirmed();
            if (UiSkin.Btn(new Rect(panel.xMax - 220, panel.yMax - 62, 190, 42), "STAY", UiSkin.Button)) confirm = 0;
            GUI.Label(new Rect(panel.x, panel.yMax - 16, panel.width, 14), "Esc / B: stay", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
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
                    // Free yourself from a snag in the walls or props (2026-11-22).
                    var me = Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
                    var np = me != null ? me.GetComponent<Player.NetworkPlayer>() : null;
                    if (np != null) { np.UnstuckRpc(); GameHud.Feed("Unstuck: moved to open floor nearby", new Color(0.8f, 0.75f, 0.7f)); }
                    Choose(0);
                    break;
                }
            }
        }

        /// <summary>A gold frame on the pad's focused button (drawn over the last layout rect).</summary>
        private void PadFrame(int i)
        {
            if (!Binds.PadActive || i != padFocus || Event.current.type != EventType.Repaint) return;
            var r = GUILayoutUtility.GetLastRect();
            var c = UiSkin.Gold;
            UiSkin.Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, 3), c); UiSkin.Rect(new Rect(r.x - 3, r.yMax, r.width + 6, 3), c);
            UiSkin.Rect(new Rect(r.x - 3, r.y, 3, r.height), c); UiSkin.Rect(new Rect(r.xMax, r.y, 3, r.height), c);
        }

        /// <summary>Options or the friends list is up: the pad drives the pointer there (UI/PadCursor), not the list focus.</summary>
        public static bool InOptions { get; private set; }

        private void Update()
        {
            TickSoloPause();
            InOptions = Open && (options || invitesOpen || legacy);
            PadNavigate();
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
            // Live meter: your level against the threshold line.
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

        // Layout only while open (2026-11-16 perf): a closed menu still paid IMGUI's Layout event every frame.
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
            GUILayout.Label(options ? "OPTIONS" : "PAUSED", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
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
                GUILayout.Space(8);
                if (UiSkin.Btn("LEAVE PARTY", UiSkin.Button, GUILayout.Height(44))) Choose(4);
                PadFrame(4);
                GUILayout.Space(8);
                if (UiSkin.Btn("QUIT GAME", UiSkin.Button, GUILayout.Height(44))) Choose(5);
                PadFrame(5);
                GUILayout.Space(10);
                var net = NetworkManager.Singleton;
                string party = net == null ? "" : (net.IsHost ? "You are hosting" : "In a party") + "   ·   " + (net.IsServer ? net.ConnectedClientsIds.Count : Player.PlayerLife.All.Count) + " in the party";
                GUILayout.Label(party, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
                var launcherHere = FindAnyObjectByType<Networking.NetworkLauncher>();
                // Hosting over Steam (2026-10-26): invite friends from a list; no addresses to copy.
                if (net != null && net.IsHost && launcherHere != null && launcherHere.OverSteam)
                {
                    GUILayout.Space(4);
                    if (UiSkin.Btn("INVITE STEAM FRIENDS", UiSkin.Button, GUILayout.Height(38))) invitesOpen = true;
                }
                // An invite from someone else's party: joining leaves this one.
                if (Networking.SteamPlatform.Ready && Networking.SteamPlatform.Instance.Invites.Count > 0)
                {
                    GUILayout.Space(4);
                    FriendsPanel.DrawInvites();
                    GUILayout.Label("Joining leaves this party.", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
                }
                // Hosting: where friends connect, with Copy buttons (2026-10-21).
                if (net != null && net.IsHost && !(launcherHere != null && launcherHere.OverSteam))
                {
                    var mid = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                    string line = Networking.PortMapper.Line;
                    if (!string.IsNullOrEmpty(line)) GUILayout.Label(line, new GUIStyle(mid) { normal = { textColor = Networking.PortMapper.Current == Networking.PortMapper.State.Open && !Networking.PortMapper.SharedAddress ? UiSkin.Gold : mid.normal.textColor } });
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
                GUILayout.Label("The dungeon does not pause for you.", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
            }
            GUILayout.EndArea();
        }

        // ---- LEGACY (2026-11-07): Renown boons and the heirloom, anywhere from the pause menu ----
        private Vector2 legacyScroll;

        private void DrawLegacy(float w, float h)
        {
            float pw = Mathf.Min(820f, w - 60f), ph = Mathf.Min(720f, h - 40f);
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.96f);
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), "LEGACY", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            GUI.Label(new Rect(panel.x, panel.y + 50, pw, 24), "RENOWN  " + Cosmetics.Renown + "      small comforts that outlive a run; nothing here makes you stronger",
                new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } });

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
            GUI.Label(new Rect(8, y, cw, 40), "Choose one relic from your last run. At camp, before a run, bring it: it joins that run as a plain relic, once. Then you must earn another.", new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = true });
            y += 44f;
            if (last.Length == 0) GUI.Label(new Rect(8, y, cw, 22), "No relics remembered yet: finish a run carrying some.", new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Italic });
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
            GUI.Label(new Rect(8, y, cw, 22), "Heirloom: " + (chosen.HasValue ? Relics.Name(chosen.Value) : "none chosen"), new GUIStyle(UiSkin.Body) { fontStyle = FontStyle.Bold });
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

        /// <summary>The host's invite list: online Steam friends, each with INVITE (2026-10-26).</summary>
        private void DrawInviteFriends(float w, float h)
        {
            var area = new Rect(w / 2f - 230, h / 2f - 260, 460, 520);
            UiSkin.Panel(new Rect(area.x - 20, area.y - 20, area.width + 40, area.height + 40), 0.95f);
            GUILayout.BeginArea(area);
            GUILayout.Label("INVITE FRIENDS", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            GUILayout.Label("They get a Steam invite and a JOIN button in Deepbound. Both of you need the same build, with Steam running.", new GUIStyle(UiSkin.Small) { wordWrap = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(8);
            FriendsPanel.DrawInviteList(330f);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (UiSkin.Btn("STEAM OVERLAY", UiSkin.Button, GUILayout.Height(40))) Networking.SteamPlatform.OpenInviteOverlay();
            if (UiSkin.Btn("BACK", UiSkin.Button, GUILayout.Height(40))) invitesOpen = false;
            GUILayout.EndHorizontal();
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
            if (UiSkin.Btn(new Rect(area.x, by, 250, 42), "RESET TO DEFAULTS", UiSkin.Button)) { Binds.ResetAll(); capturing = null; }
            if (UiSkin.Btn(new Rect(area.xMax - 250, by, 250, 42), "BACK", UiSkin.Button)) { controls = false; capturing = null; }
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
                    if (have && GUI.Button(r, (journalOpen == i ? "> " : "") + Journal.Entries[i].Title, new GUIStyle(UiSkin.Body) { fontSize = 14, normal = { textColor = journalOpen == i ? UiSkin.Gold : new Color(0.9f, 0.88f, 0.84f) } })) journalOpen = i;
                    if (!have) GUI.Label(r, f == 4 ? "a guardian not yet felled" : f == 5 ? "someone not yet brought home" : "a page not yet found", new GUIStyle(UiSkin.Body) { fontSize = 14, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.55f, 0.52f, 0.48f) } });
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
            float pw = Mathf.Min(1040f, w - 60f), ph = Mathf.Min(720f, h - 40f);
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.96f);
            string[] titles = { "GUIDE: PROGRESSION", "GUIDE: THE DUNGEON", "JOURNAL  (" + Journal.Count + " / " + Journal.Entries.Length + " pages)", "BESTIARY  (" + Bestiary.SeenCount + " / " + Bestiary.Entries.Length + " met)" };
            GUI.Label(new Rect(panel.x, panel.y + 12, pw, 40), titles[guidePage], new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
            float colW = (pw - 92f) / 2f;
            var titleStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, fontSize = 14, normal = { textColor = UiSkin.Gold } };
            var bodyStyle = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            if (guidePage != lastGuidePage) { guideScroll = Vector2.zero; lastGuidePage = guidePage; }
            if (guidePage == 2) DrawJournal(panel, colW, titleStyle, bodyStyle);
            else if (guidePage == 3) DrawBestiary(panel, colW, titleStyle, bodyStyle);
            else
            {
                // The two columns scroll (the wheel, or the bar) inside the panel, above the buttons.
                var view = new Rect(panel.x + 16, panel.y + 58, pw - 32, ph - 58 - 72);
                var sections = new[] { guidePage == 0 ? GuideLeft : DungeonLeft, guidePage == 0 ? GuideRight : DungeonRight };
                // Measuring a page of wrapped text is costly: do it once per page, width and key state.
                string key = guidePage + "|" + colW + "|" + Binds.AnyChanged + "|" + Binds.PadActive;
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
                            guideTexts[c][i] = Binds.Rekey(sections[c][i].body);
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
                        GUI.Label(new Rect(x, y, colW, 20), sections[c][i].title, titleStyle);
                        float bh = guideHeights[c][i];
                        GUI.Label(new Rect(x, y + 20, colW, bh), texts[c][i], bodyStyle);
                        y += 20 + bh + 14;
                    }
                }
                GUI.EndScrollView();
                if (tallest > view.height && guideScroll.y < tallest - view.height - 4f)
                    GUI.Label(new Rect(view.x, view.yMax - 2, view.width, 18), "scroll for more", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, normal = { textColor = UiSkin.Gold } });
            }
            string[] next = { "THE DUNGEON >", "JOURNAL >", "BESTIARY >", "PROGRESSION >" };
            if (UiSkin.Btn(new Rect(panel.x + pw / 2f - 280, panel.yMax - 56, 180, 40), next[guidePage], UiSkin.Button)) guidePage = (guidePage + 1) % 4;
            if (UiSkin.Btn(new Rect(panel.x + pw / 2f + 100, panel.yMax - 56, 180, 40), "BACK", UiSkin.Button)) guide = false;
        }
    }
}
