using System.Collections.Generic;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;
using Deepbound.Core;

namespace Deepbound.UI
{
    public class Codex : MonoBehaviour
    {
        public struct Part { public string Key, Text; }

        public class Section
        {
            public string Title, Key, Hint, Body, Intro;
            public Part[] Parts;
        }

        private const string SaveKey = "db.codex";
        private static HashSet<string> known;

        public static int Version { get; private set; }

        private static HashSet<string> Known
        {
            get
            {
                if (known != null) return known;
                known = new HashSet<string>();
                if (Progression.Cosmetics.TestMode) return known;
                foreach (var k in Prefs.GetString(SaveKey, "").Split('|')) if (k.Length > 0) known.Add(k);
                return known;
            }
        }

        public static bool Knows(string key) => string.IsNullOrEmpty(key) || Known.Contains(key);

        public static bool Learn(string key, string what)
        {
            if (string.IsNullOrEmpty(key) || !Known.Add(key)) return false;
            Version++;
            if (!Progression.Cosmetics.TestMode)
                Prefs.SetString(SaveKey, string.Join("|", Known)); Prefs.Save();
            if (!string.IsNullOrEmpty(what)) GameHud.Feed("New in your Guide: " + what + "  (Esc > Guide)", new Color(0.85f, 0.8f, 0.6f));
            return true;
        }

        public static string TitleOf(Section s)
        {
            if (s.Parts != null)
            {
                int n = 0;
                foreach (var p in s.Parts) if (Knows(p.Key)) n++;
                return n == 0 && !Knows(s.Key) ? "???" : s.Title + "  (" + n + " / " + s.Parts.Length + ")";
            }
            return Knows(s.Key) ? s.Title : "???";
        }

        public static string BodyOf(Section s)
        {
            if (s.Parts != null)
            {
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(s.Intro)) sb.Append(s.Intro);
                foreach (var p in s.Parts)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(Knows(p.Key) ? p.Text : "???");
                }
                return sb.ToString();
            }
            return Knows(s.Key) ? s.Body : "Not yet discovered. " + s.Hint;
        }

        public static readonly Section[] GuideLeft =
        {
            new Section { Title = "EMBERS & THE ANVIL", Key = "embers", Hint = "Something glows in the ashes of the dead.", Body = "Embers drop from enemies, chests and urns, and are gained by scrapping gear. Spend them at the anvil (F) to temper your weapon up to +10; each level adds ATT, DEX and 5% range. At +2 you choose a Path, and at +5, +7 and +9 you forge one of three Edges. Beyond +10, Hobb can hone the blade five times for +3% damage each. Hobb also scraps spare gear, refines and attunes armour, and sells plate." },
            new Section { Title = "WEAPON PATHS", Key = "paths", Hint = "Temper a weapon far enough and it asks which way to go.", Body = "At +2 your weapon takes one of three Paths: two change how you deal damage, and the third improves survival. At +4 the Path is mastered and gains a second effect. Hobb can replace one Edge per run, for gold." },
            new Section { Title = "BUILDS", Key = "builds", Hint = "Brann's yard and the cracked walls hold more than they show.", Body = "GEAR SETS: matching armour and charm pairs grant a small bonus. MASTERIES: six of Brann's drills in one stat unlock a mastery. MAJOR SOCKET: a rune in your first socket is twice as strong. SHRINES OF TRADE-OFF, hidden behind cracked walls, offer a cursed bargain for the run." },
            new Section { Title = "CLASS GIFTS", Body = "Knight: a raised shield halves trap damage and stops the Gravewarden's charge. Mage: nearby secret walls appear on the map. Cleric: bargains cost half. Rogue: hold F on a spike plate to jam it; hidden plates glint. Ranger: the map shows enemies further away. Flagellant: traps deal less damage. Plague Doctor: potions heal more." },
            new Section { Title = "SIGILS & ABILITY RANKS", Key = "sigils", Hint = "Elites and guardians sometimes carry a mark worth keeping.", Body = "Sigils drop from elites, chests and bosses (one per player). At Ysolde's stones, spend them to rank up Q or E, up to rank 5. Each rank offers a choice of two upgrades that change how the ability works, locked for the run, and also increases potency and reduces cooldown. Ranks cost 1, 1, 2 and 3 Sigils; spares melt into embers." },
            new Section { Title = "RUNES", Key = "runes", Hint = "Carved stones turn up in the deep. Ysolde would know.", Body = "Runes enchant your weapon. Sockets open at +1, +3 and +6; set runes at Ysolde's stones. Meld three identical runes (plus embers) into the next tier: II is 50% stronger, III twice as strong." },
            new Section { Title = "RANGE", Body = "Every attack has a maximum range, and damage falls off beyond half of it (to 60% at the limit). Arrows and knives drop over distance. Tempering adds 5% range per level; a Farsight rune adds 25% (half for melee). The crosshair shows OUT OF RANGE when a target is too far." },
            new Section { Title = "NOTHING CARRIES OVER", Body = "Everything you gain lasts one run. Only Renown, cosmetics, journal pages, deeds, this Guide and the Hall of Records persist." },
        };

        public static readonly Section[] GuideRight =
        {
            new Section { Title = "GEAR", Key = "gear", Hint = "The dead wear armour. Some of it still fits.", Body = "Armour and charms are rolled per player; only you see your drops. F stores a piece in your pack (I to equip). Deeper finds are stronger, and Hobb can refine or attune a favourite to keep it relevant. Bosses may drop Legendary pieces. Silas sells pendants; Hobb sells armour." },
            new Section { Title = "RARE DRAUGHTS & TOMES (5)", Key = "rare", Hint = "The fifth pouch slot waits for something rare.", Body = "The fifth pouch slot holds one rare item. IRONBARK: half damage and immunity to afflictions for 8 s. BLOODFURY: 40% more damage and faster attacks for 8 s. GRAVE STILLNESS: invulnerable but helpless for 3 s. TOME OF THE PHOENIX: a fire-bird fights for you for 30 s. TOME OF THE STORM: lightning strikes nearby enemies for 10 s. TOME OF SANCTUARY: a holy circle reduces damage taken by 35% and heals for 10 s." },
            new Section { Title = "RELICS", Key = "relic", Hint = "Old things of power lie in chests and on altars.", Body = "Relics grant passive powers; you can carry eight. A duplicate empowers the one you have (II, III: +35% each). With a full tray, new relics empower your weakest. Drop one from the inventory (right-click) to free a slot. Cooldown reduction from every source adds up, with no cap." },
            new Section { Title = "YOUR SECOND BAR", Body = "Each class's resource works differently; its bar shows a short reminder. KNIGHT Stamina: spent by blocking and sprinting; when depleted, your guard drops. MAGE Mana: spent by Frostbolt and spells; Firebolt is free. CLERIC Mana: drained by the Mending Beam, which also pauses regeneration. ROGUE Energy: spent by frontal stabs; attacks from behind are free and restore it; when depleted, you are Winded. RANGER Focus: builds from landed arrows and fades when idle; when full, your next full draw is a Deadeye shot. FLAGELLANT Fervor: builds from dealing and taking damage, grants up to 20% bonus damage, and is spent by Penitent Leap. PLAGUE DOCTOR Reagents: spent by vials and restored by Blighted kills; when depleted, vials deal reduced damage and do not Blight." },
            new Section { Title = "COMBOS", Intro = "The classes play off each other. Each combo is written here the first time you see it land.", Parts = new[]
            {
                new Part { Key = "combo.Shatter", Text = "SHATTER: heavy hits on chilled enemies deal bonus damage." },
                new Part { Key = "combo.Exposed", Text = "EXPOSED: stunned enemies take 25% more damage." },
                new Part { Key = "combo.Distracted", Text = "DISTRACTED: taunted enemies take 15% more from everyone but the taunter." },
                new Part { Key = "combo.Blessed", Text = "BLESSED: allies in Consecrate deal 20% more; the Cleric's beam target is INSPIRED (+15%)." },
                new Part { Key = "combo.ThermalShock", Text = "THERMAL SHOCK: frost on a scorched enemy, or fire on a chilled one, deals a burst of damage." },
                new Part { Key = "combo.Pincer", Text = "PINCER: striking opposite an ally who just hit deals 15% more." },
                new Part { Key = "combo.Punish", Text = "PUNISH: enemies recovering from an attack take 20% more." },
                new Part { Key = "combo.WardBroken", Text = "WARD BROKEN: stuns and chill shatter an elite's ward." },
                new Part { Key = "combo.Purge", Text = "PURGE: the Cleric deals 25% more to Blighted enemies." },
                new Part { Key = "combo.PlagueCrash", Text = "PLAGUE CRASH: the Flagellant's Crash spreads Blight." },
                new Part { Key = "combo.Convergence", Text = "CONVERGENCE: two players casting their ultimates within 4 s of each other unleash a stunning burst and grant the party Bloodlust." },
                new Part { Key = "combo.AegisBurst", Text = "AEGIS BURST: a broken Aegis shield explodes, damaging nearby enemies." },
                new Part { Key = "combo.MarkSpread", Text = "MARK SPREADS: when a Marked enemy dies, the Mark passes to the nearest enemy." },
                new Part { Key = "combo.Hallowed", Text = "HALLOWED FIRE: a meteor landing on consecrated ground heals allies standing on it." },
            } },
            new Section { Title = "SHELTER", Body = "Allies standing close behind a Knight with a raised shield take half damage from attacks the shield faces." },
            new Section { Title = "RAGE", Body = "Enemies target whoever has damaged them most. The meter under a targeted elite shows who is HUNTED; it switches only when someone exceeds that player by a fifth. Knights generate far more threat; Rogues generate less. Healing draws threat from nearby enemies, and stealth clears it." },
            new Section { Title = "ELITES", Intro = "Elites carry an affix. Each is written here the first time you meet one.", Parts = new[]
            {
                new Part { Key = "elite.Warded", Text = "WARDED: nearly immune until stunned or chilled." },
                new Part { Key = "elite.Vampiric", Text = "VAMPIRIC: heals from its hits; a Mark stops it." },
                new Part { Key = "elite.Volatile", Text = "VOLATILE: explodes on death." },
                new Part { Key = "elite.Swift", Text = "SWIFT: fast; chill slows it." },
                new Part { Key = "elite.Splitting", Text = "SPLITTING: splits into two." },
                new Part { Key = "elite.Thorned", Text = "THORNED: damages melee attackers." },
                new Part { Key = "elite.Frenzied", Text = "FRENZIED: attacks faster below half health." },
                new Part { Key = "elite.Gravebound", Text = "GRAVEBOUND: rises again unless you stand on its bones." },
                new Part { Key = "elite.Hushing", Text = "HUSHING: hits Silence." },
                new Part { Key = "elite.Concussive", Text = "CONCUSSIVE: hits Daze." },
                new Part { Key = "elite.Emberbound", Text = "EMBERBOUND: leaves burning patches as it moves; chill slows the trail." },
                new Part { Key = "elite.Soulbound", Text = "SOULBOUND: a linked pair that revive each other unless both die close together." },
                new Part { Key = "champion", Text = "CHAMPIONS: one named champion per floor guards a hoard and fights according to its title." },
            } },
        };

        public static readonly Section[] DungeonLeft =
        {
            new Section { Title = "EVERY CLASS", Body = "R: ultimate, charged by fighting. V: kick (dodge while sprinting to slide, then kick for a Slide Kick). Dodging through a blow at the last moment returns your dodge at once, and your next hit within 2 s deals 30% more. The pouch holds five of each: 1 Health Potion (heals 45% over 3 s), 2 Frost Flask and 3 Holy Water (both thrown). VIT slowly regenerates health. Hits can CRIT. Ranged headshots deal 50% more (aimed melee 25%), and glowing weak points deal double. Tap F by a downed ally to drag them; hold F to revive. A downed ally bleeds out after 40 s; carry their soul skull to the start room's waystone to bring them back." },
            new Section { Title = "THE DEAD", Body = "Enemies hear you: running carries about 11 m and combat about 16, halved through walls; walking is quiet. Your first hit on an unaware enemy deals 50% more. Only three attack at once; the rest wait. Break line of sight and stay quiet to lose them. Attack warnings: ! is a strike, !! a heavy blow (sidestep), <!> a wide sweep (back off), >> a lunge along a marked line (dodge sideways). Afflictions: POISON, FROSTBITE (slow), CURSE (40% more damage taken), WEAKNESS (40% less damage dealt), MANIA (no attacking), BLIND (no map or health bars), SILENCE (no abilities) and ROOTED. Holy Water cleanses all but roots. From floor 2, patrols move between rooms, and deeper enemies lead their shots." },
            new Section { Title = "ROOMS", Body = "Flame vents and spike plates deal a third of your health; cross when the fire drops. Kick enemies into chasms and pendulum blades. Pull three levers on a floor to open its vault. Falls over four metres cause damage. Jump into a ledge up to two metres high to climb it. LAIRS hold a sleeping keeper and its hoard. RELIQUARIES, carried to the waystone, revive the fallen, reward the party and weaken the boss. BLOOD ALTARS trade health for damage and relics. Special side rooms include the Treasury, Beggar, Proving Bell, Armoury, Dark Mirror, Oath Room and the Relic Crucible, which turns your weakest relic into a different one a tier higher. Lightless halls require a carried torch. The peddler's room is always safe." },
        };

        public static readonly Section[] DungeonRight =
        {
            new Section { Title = "BOSS PHASES", Key = "boss", Hint = "Every floor ends in an arena. What waits there has rules of its own.", Body = "Bosses change phase at set health thresholds and are briefly invulnerable between them. WARDED: kill the guards first. SURVIVE: dodge until the timer ends. ARMORED: damage is reduced. From floor 2, bosses hunt with trails of red circles and aimed lances; keep changing direction. Bosses crouch and glow before heavy attacks. Arenas may include healing pylons, ballistas, chandeliers or sanctum braziers. BREAK METER: weak points, headshots, stuns and kicks fill it; when full, the boss is Broken and takes 50% more damage. SHARED DOOM: everyone inside the dark ring splits its damage; stack on the marked player." },
            new Section { Title = "THE DESCENT", Body = "Ten floors across five acts: the Ossuary, Drowned Vaults, Hollow Throne, Black Cathedral and the Abyss. Each act ends at a Warden and then its master; the Unsung waits on floor 10. Some floors carry an EVENT, shown on the floor banner. Do not linger: after a few minutes a bell tolls, and two minutes later the Tallyman arrives. He cannot be harmed and drains your life on contact. Holy Water holds him briefly; sealing the guardian's arena makes him leave." },
            new Section { Title = "THE SEALS", Key = "seals", Hint = "A champion of the first acts carries a broken piece of something.", Body = "Each act's Champion in the first four acts carries a Seal fragment. Every Seal broken weakens the Unsung; every one left makes it 15% tougher." },
            new Section { Title = "FLOORS", Intro = "Each act has a trick of its own, written here when you first reach it.", Parts = new[]
            {
                new Part { Key = "act.0", Text = "OSSUARY: stamp out bone piles before they rise." },
                new Part { Key = "act.1", Text = "DROWNED VAULTS: rising water halves your speed; find high ground." },
                new Part { Key = "act.2", Text = "HOLLOW THRONE: smash soul lanterns to heal and stun." },
                new Part { Key = "act.3", Text = "BLACK CATHEDRAL: jump the bell's shockwave." },
                new Part { Key = "act.4", Text = "THE ABYSS: the light fails at times; stay together." },
            } },
            new Section { Title = "INVENTORY", Body = "Press I to open it. Bone, cloth and iron drop throughout the dungeon and craft potions, flasks, run upgrades and throwable tools (Z): Caltrops slow and damage, and a Bone Rattle lures enemies away. The pack holds eight pieces: click to equip, right-click at Hobb's anvil to scrap, shift-click to drop. Loot is shared by default; the host toggles this with L at camp." },
            new Section { Title = "GIVING", Body = "Look at a nearby ally and hold G to open the give wheel: potions, gold, embers, your rare item, or an emote. Flick toward a slice and release." },
            new Section { Title = "VOICE", Body = "Proximity voice reaches about 30 m, muffled through walls. Shouting wakes nearby enemies. Push-to-talk (T) and all key bindings are in Options. Gamepads are fully supported. Press Enter to type in chat." },
            new Section { Title = "AT CAMP", Body = "SILAS sells potions and provisions. HOBB tempers weapons, scraps gear and sells armour. YSOLDE handles runes, Sigils and ability ranks. The SHRINE offers Holy Water, relics and Aldric's prayer, which prevents one downing. At the tavern, Marta sells mead (+4% damage per level, at the cost of your footing). Take a contract from the bounty board, or turn the Omen Stone for a harder run and more Renown. BRANN sells drills that raise a stat for the run. CORWIN runs the carriage: difficulty, the Daily Descent, the solo Endless Descent, scout maps, smelling salts and climbing ropes. LEGACY (pause menu) spends Renown on permanent boons and lets you bring one relic from your last run as an Heirloom. MISTRESS VEY sells cosmetics and companions; every pet fetches gold and has a trick of its own." },
            new Section { Title = "RELIC SYNERGIES", Intro = "Some relics awaken something more together. A pair is written here once you have carried both.", Parts = SynergyParts() },
        };

        private static Part[] SynergyParts()
        {
            var list = new List<Part>();
            foreach (var s in Relics.Synergies)
                list.Add(new Part { Key = "syn." + s.kind, Text = s.name.ToUpperInvariant() + " (" + Relics.Name(s.a) + " + " + Relics.Name(s.b) + "): " + s.text + "." });
            return list.ToArray();
        }

        public static void OnPopup(string text)
        {
            foreach (Classes.ComboKind k in System.Enum.GetValues(typeof(Classes.ComboKind)))
            {
                if (Classes.Combos.Label(k) != text) continue;

                Learn("combo." + k, "the " + text + " combo");
                return;
            }
        }

        public static void OnPickup(string key)
        {
            switch (key)
            {
                case "embers": Learn("embers", "Embers and the Anvil"); break;
                case "sigil": Learn("sigils", "Sigils and Ability Ranks"); break;
                case "rune": Learn("runes", "Runes"); break;
                case "relic": Learn("relic", "Relics"); break;
                case "rare": Learn("rare", "Rare Draughts and Tomes"); break;
                case "gear": Learn("gear", "Gear"); break;
            }
        }

        public static void OnBossIntro() => Learn("boss", "Boss Phases");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("Codex");
            DontDestroyOnLoad(go);
            go.AddComponent<Codex>();
        }

        private static readonly string[] ActNames = { "the Ossuary", "the Drowned Vaults", "the Hollow Throne", "the Black Cathedral", "the Abyss" };
        private float nextPoll, nextKills;
        private readonly HashSet<EnemyBrain> alive = new HashSet<EnemyBrain>();
        private readonly HashSet<EnemyBrain> counted = new HashSet<EnemyBrain>();

        public const float KillReach = 30f;

        private void Update()
        {
            var me = NetworkPlayer.Local;
            if (me == null || DungeonDirector.Instance == null) return;
            if (Time.time >= nextKills)
            {
                nextKills = Time.time + 0.25f;
                WatchKills(me.transform.position);
            }
            if (Time.time < nextPoll) return;
            nextPoll = Time.time + 1f;
            var p = me.GetComponent<Progression.PlayerProgress>();
            if (p == null) return;
            WatchDamage(p);
            if (p.Embers.Value > 0) Learn("embers", "Embers and the Anvil");
            if (p.WeaponLevel.Value >= 2) Learn("paths", "Weapon Paths");
            if (p.Sigils.Value > 0 || p.RankQ.Value > 1 || p.RankE.Value > 1) Learn("sigils", "Sigils and Ability Ranks");
            if (p.Socket0.Value != 0 || p.Socket1.Value != 0 || p.Socket2.Value != 0) Learn("runes", "Runes");
            if (p.Drills.Value != 0 || p.Bargains.Value != 0) Learn("builds", "Builds");
            if (p.RelicCount > 0) Learn("relic", "Relics");
            foreach (var s in Relics.Synergies)
                if (p.HasRelic(s.a) && p.HasRelic(s.b)) Learn("syn." + s.kind, "the " + s.name + " synergy");
            var d = DungeonDirector.Instance;
            if (d.SealCount > 0) Learn("seals", "The Seals");
            if (!DungeonDirector.InLobby && DungeonDirector.Layout != null && !DungeonDirector.InTutorial)
            {
                int act = DungeonDirector.Act(DungeonDirector.Layout.Depth);
                Learn("act." + act, ActNames[Mathf.Clamp(act, 0, ActNames.Length - 1)]);
            }
            if (BossBrain.Active != null && !BossBrain.Active.IsDead && Vector3.Distance(BossBrain.Active.transform.position, me.transform.position) < 30f)
                Learn("boss", "Boss Phases");
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || e.IsDead || (e.transform.position - me.transform.position).sqrMagnitude > 20f * 20f) continue;
                if (e.AffixKind != EliteAffix.None) Learn("elite." + e.AffixKind, "the " + e.AffixKind.ToString().ToUpperInvariant() + " elite");
                if (e.IsChampion) Learn("champion", "Champions");
            }
        }

        private float announced = -1f;
        private Classes.ClassId announcedFor;

        private void WatchDamage(Progression.PlayerProgress p)
        {
            var lines = Progression.DamageSheet.For(p);
            if (lines.Count == 0) return;
            float now = lines[0].Now;
            if (announced < 0f || announcedFor != p.ClassId.Value || now < announced) { announced = now; announcedFor = p.ClassId.Value; return; }

            if (p.Bloodlust || p.Rallied || (p.TryGetComponent<Player.Flasks>(out var fl) && fl.Bloodfury)) return;
            if (now < announced * 1.03f || now - announced < 1f) return;
            GameHud.Feed(lines[0].Name + " now hits for " + Mathf.RoundToInt(now) + " (was " + Mathf.RoundToInt(announced) + "). Hold " + Binds.Label(Bind.Stats) + " for all your damage.", new Color(1f, 0.85f, 0.5f));
            announced = now;
        }

        private void WatchKills(Vector3 at)
        {
            foreach (var e in EnemyBrain.All)
            {
                if (e == null) continue;
                bool near = (e.transform.position - at).sqrMagnitude < KillReach * KillReach;
                if (!e.IsDead) { if (near) alive.Add(e); continue; }
                if (!alive.Contains(e) || !counted.Add(e)) continue;
                if (near) Bestiary.Killed(e.GetType().Name);
            }
            alive.RemoveWhere(e => e == null);
            counted.RemoveWhere(e => e == null);
        }
    }
}
