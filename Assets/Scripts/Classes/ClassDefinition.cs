using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>APPEND ONLY: synced as a byte and will be saved with cosmetics.</summary>
    public enum ClassId : byte { Knight = 0, Mage = 1, Cleric = 2, Rogue = 3, Ranger = 4, Flagellant = 5, PlagueDoctor = 6 }

    /// <summary>
    /// A class's base stats. Plain data; <see cref="Build"/> turns it into a run-scoped
    /// <see cref="StatBlock"/>. Growth comes from the class weapon (anvil levels) and gear.
    ///
    /// Defined in code while there are only a couple of classes. When all eight
    /// exist (M3) this becomes a ScriptableObject so balance can be tuned without a
    /// recompile.
    /// </summary>
    public class ClassDefinition
    {
        public string Name;
        public string Role;
        public float[] BaseStats = new float[Stats.Count];

        public StatBlock Build()
        {
            var block = new StatBlock();
            for (int i = 0; i < Stats.Count; i++)
            {
                block.SetBase((StatType)i, BaseStats[i]);
            }
            return block;
        }

        private static ClassDefinition Make(string name, string role,
            float hp, float mp, float att, float def, float spd, float dex, float vit, float wis)
        {
            var c = new ClassDefinition { Name = name, Role = role };
            c.BaseStats = new[] { hp, mp, att, def, spd, dex, vit, wis };
            return c;
        }

        /// <summary>
        /// The Knight: tank. High HP and DEF, deep DEF pool, slow-ish. Damage comes from
        /// steady swings rather than burst; its value is holding a doorway while the
        /// party does the killing.
        /// </summary>
        public static readonly ClassDefinition Knight = Make("Knight", "Tank",
            hp: 240, mp: 100, att: 20, def: 6, spd: 44, dex: 45, vit: 40, wis: 20);

        /// <summary>
        /// The Mage: burst and control. Fragile, no armour, a deep mana pool; all of its
        /// damage is ranged, and its best damage needs setting up (chill, then fire).
        /// </summary>
        public static readonly ClassDefinition Mage = Make("Mage", "Burst / Control",
            hp: 160, mp: 120, att: 26, def: 0, spd: 49, dex: 50, vit: 25, wis: 45);

        /// <summary>
        /// The Cleric: the healer. Middling everything, the deepest mana and WIS pools; it
        /// keeps the party standing rather than ending fights.
        /// </summary>
        public static readonly ClassDefinition Cleric = Make("Cleric", "Healer / Support",
            hp: 195, mp: 140, att: 16, def: 3, spd: 47, dex: 45, vit: 35, wis: 50);

        /// <summary>
        /// The Rogue: assassin. Fast and fragile with the highest DEX; its damage is huge
        /// from behind and ordinary from the front, so it lives on positioning.
        /// </summary>
        public static readonly ClassDefinition Rogue = Make("Rogue", "Assassin",
            hp: 175, mp: 80, att: 24, def: 2, spd: 62, dex: 60, vit: 30, wis: 20);

        /// <summary>
        /// The Ranger: marksman and controller. Long range, good DEX, light armour; it makes
        /// the party's damage better (Hunter's Mark) and keeps enemies where it wants them.
        /// </summary>
        public static readonly ClassDefinition Ranger = Make("Ranger", "Marksman / Control",
            hp: 180, mp: 90, att: 24, def: 2, spd: 54, dex: 55, vit: 30, wis: 25);

        /// <summary>
        /// The Flagellant: a penitent bruiser. Big health, little armour; hits harder the more it
        /// bleeds, heals from every hit, and spends its own health for Zeal.
        /// </summary>
        public static readonly ClassDefinition Flagellant = Make("Flagellant", "Bruiser / Lifesteal",
            hp: 225, mp: 100, att: 24, def: 2, spd: 51, dex: 46, vit: 42, wis: 18);

        /// <summary>
        /// The Plague Doctor: an alchemist. Lobs vials that blight the dead (poison, and they take
        /// more damage from everyone), tonics that heal, a mask that cleanses the party.
        /// </summary>
        public static readonly ClassDefinition PlagueDoctor = Make("Plague Doctor", "Afflicter / Support",
            hp: 185, mp: 120, att: 20, def: 2, spd: 50, dex: 48, vit: 32, wis: 42);

        public static readonly ClassDefinition[] ById = { Knight, Mage, Cleric, Rogue, Ranger, Flagellant, PlagueDoctor };

        /// <summary>One line per kit slot, for the character select screen.</summary>
        public static (string key, string name, string desc)[] Kit(ClassId id) => id == ClassId.Flagellant
            ? new[]
            {
                ("PASSIVE", "Mortification", "Deal up to 40% more damage as your health drops. Every hit restores a little health."),
                ("LMB", "Flail", "Wide, heavy swings. Every third swing is an overhead Crash that bursts on impact."),
                ("RMB", "Scourge", "Sacrifice 10% health to gain Fervor and Zeal: faster, stronger swings for 5 s."),
                ("Q", "Chains of Penance", "Pull the targeted enemy to you and stun it. Heavy enemies pull you to them instead."),
                ("E", "Penitent Leap", "Leap to the target point and slam down, spending all Fervor for bonus damage."),
            }
            : id == ClassId.PlagueDoctor
            ? new[]
            {
                ("PASSIVE", "Contagion", "Vials inflict Blight: poison damage, and the target takes 10% more damage from all sources. Repeated vials make it fester, up to four stacks; each stack strengthens the poison and adds 20% to your vial hits. Blight spreads when its host dies."),
                ("LMB", "Vial", "Lob a vial that shatters in a small splash. Costs reagents; without them, vials deal reduced damage and do not Blight. Blighted kills restore reagents."),
                ("RMB", "Tonic", "Drink a tonic and breathe it out: you and every ally within 10 m recover 18% of their health."),
                ("Q", "Miasma", "Create a lingering plague cloud that damages enemies inside it."),
                ("E", "Plague Mask", "Cleanse and heal nearby allies, and make them immune to afflictions for 4 s."),
            }
            : id == ClassId.Ranger
            ? new[]
            {
                ("PASSIVE", "Hunter's Mark", "Three consecutive hits Mark an enemy: it takes 25% more damage from all sources. Landed arrows build Focus; at full Focus, your next full draw is a Deadeye shot (2.2x damage)."),
                ("LMB", "Longbow", "Hold to draw, release to fire. Fully drawn arrows pierce."),
                ("RMB", "Disengage", "Leap backwards out of danger."),
                ("Q", "Snare Shot", "Fire a weighted net that roots and damages enemies where it lands. Snared enemies take 25% more damage from your arrows."),
                ("E", "Volley", "Rain arrows on the target area."),
            }
            : id == ClassId.Rogue
            ? new[]
            {
                ("PASSIVE", "Ambush", "Attacks from behind deal 50% more damage (30% against bosses) and cost no Energy. Frontal stabs cost Energy; at zero you are Winded and attack slower. Your crosshair turns violet when you are behind a target."),
                ("LMB", "Twin Daggers", "Rapid, short-range stabs against a single target."),
                ("RMB", "Fan of Knives", "Throw three knives in a narrow spread."),
                ("Q", "Shadowstep", "Teleport behind the targeted enemy. With no target, dash a short distance forward at half the cooldown."),
                ("E", "Smoke Bomb", "Vanish and lose all enemies. You cannot be targeted inside the cloud for 3 s, even after attacking. Your first strike from the smoke deals double damage and stuns."),
            }
            : id == ClassId.Cleric
            ? new[]
            {
                ("PASSIVE", "Grace", "Revive downed allies twice as fast."),
                ("LMB", "Smite", "Fire bolts of holy light. Deals 35% more damage when playing solo."),
                ("RMB", "Mending Beam", "Hold on an ally to heal them; with no target, slowly heal yourself. Drains mana while active."),
                ("Q", "Consecrate", "Bless an area: allies inside are healed and enemies inside burn."),
                ("E", "Divine Aegis", "Shield all nearby allies against the next 60 damage."),
            }
            : id == ClassId.Mage
            ? new[]
            {
                ("PASSIVE", "Shatter", "Fire damage against a chilled enemy deals 75% more and consumes the chill."),
                ("LMB", "Firebolt", "Hold to cast rapid firebolts. Costs no mana."),
                ("RMB", "Frostbolt", "A heavy bolt that chills the target, slowing it and priming it to Shatter."),
                ("Q", "Tidal Wave", "Send a wave forward that pushes enemies back and stuns them for 0.5 s. In water it hits harder and stuns for 1 s."),
                ("E", "Meteor", "Call a meteor down on the target point. Shatters every chilled enemy in the blast."),
            }
            : new[]
            {
                ("PASSIVE", "Bulwark", "Blocking negates 80% of frontal damage at a stamina cost. Attacking within 1.6 s of a block Counters for 40% more damage."),
                ("LMB", "Sword Swing", "A wide sweeping arc that alternates sides. Deals 25% more damage when playing solo."),
                ("RMB", "Block", "Hold to raise your shield. Reduces movement speed."),
                ("Q", "Shield Bash", "Lunge forward and stun the target. Interrupts enemy attacks, including those of bosses."),
                ("E", "War Cry", "Taunt all nearby enemies and halve the damage you take."),
            };

        /// <summary>How the class plays with the others (see <see cref="Combos"/>).</summary>
        public static string ComboLine(ClassId id) => id == ClassId.Flagellant
            ? "Chains pull enemies into your allies' reach. Crashes Shatter chilled enemies, and a Crash on a Blighted enemy spreads the Blight to everything nearby (Plague Crash)."
            : id == ClassId.PlagueDoctor
            ? "Blighted enemies take 10% more damage from all sources. The Cleric deals 25% more to them (Purge), and the Flagellant's Crash spreads your Blight. Plague Mask removes curses, poison and mania."
            : id == ClassId.Mage
            ? "Frost chills enemies so any heavy hit can Shatter them. Your fire Shatters for 75% bonus damage, and Meteor excels against stunned groups."
            : id == ClassId.Cleric
            ? "Allies in Consecrate deal 20% more damage (Blessed); your beam target deals 15% more (Inspired). A broken Aegis shield explodes. Holy damage deals 25% more to Blighted enemies (Purge)."
            : id == ClassId.Rogue
            ? "Openers stun, making enemies take 25% more damage from everyone (Exposed). Ambush hits Shatter chilled enemies. Allies inside your smoke are hidden as well."
            : id == ClassId.Ranger
            ? "Marked enemies take 25% more damage from all sources, and the Mark transfers when the target dies. Snares stun (Exposed); full draws Shatter chilled enemies."
            : "Shield Bash stuns, making enemies take 25% more damage from everyone (Exposed), and Shatters chilled enemies. Taunted enemies take 15% more damage from your allies (Distracted).";

        /// <summary>Difficulty hint for the class picker.</summary>
        public static string Difficulty(ClassId id) => id == ClassId.Flagellant ? "Medium · Strongest at low health; sustains by attacking"
            : id == ClassId.PlagueDoctor ? "Medium · Lead your throws and support your allies"
            : id == ClassId.Mage ? "Hard · Fragile; rewards combos and positioning"
            : id == ClassId.Cleric ? "Medium · Keeps the party alive"
            : id == ClassId.Rogue ? "Hard · Relies on attacking from behind"
            : id == ClassId.Ranger ? "Medium · Keep your distance and aim carefully"
            : "Easy · A durable frontline";

        /// <summary>The name of the class's Q or E, from its kit list.</summary>
        public static string AbilityName(ClassId id, AbilitySlot slot)
        {
            string key = slot == AbilitySlot.Q ? "Q" : "E";
            foreach (var (k, name, _) in Kit(id)) if (k == key) return name;
            return key;
        }

        /// <summary>The class's own weapon: never found, only tempered at the anvil.</summary>
        public static string WeaponName(ClassId id) => id == ClassId.Flagellant ? "Flail"
            : id == ClassId.PlagueDoctor ? "Alembic"
            : id == ClassId.Mage ? "Staff"
            : id == ClassId.Cleric ? "Mace"
            : id == ClassId.Rogue ? "Daggers"
            : id == ClassId.Ranger ? "Longbow"
            : "Longsword";

        /// <summary>What one anvil level adds: ATT and DEX, the same for every class.</summary>
        public const float WeaponAttPerLevel = 4f, WeaponDexPerLevel = 2f;

        public static ClassDefinition For(ClassId id) => ById[Mathf.Clamp((int)id, 0, ById.Length - 1)];

        /// <summary>One line for the class picker.</summary>
        public static string Pitch(ClassId id) => id == ClassId.Flagellant
            ? "Trades health for power and sustains through the damage dealt. Strongest near death."
            : id == ClassId.PlagueDoctor
            ? "Weakens enemies with Blight and miasma while healing allies with tonics."
            : id == ClassId.Ranger
            ? "Marks, snares and volleys from range, making the whole party hit harder."
            : id == ClassId.Rogue
            ? "A mobile assassin: lethal from behind, vulnerable from the front."
            : id == ClassId.Cleric
            ? "Heals, shields and revives. Keeps the party alive."
            : id == ClassId.Mage
            ? "Chills with frost and shatters with fire. Fragile, but devastating at range."
            : "Blocks, bashes and taunts, holding the line so others can strike.";
    }
}
