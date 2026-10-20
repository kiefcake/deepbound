using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>APPEND ONLY: synced as a byte and will be saved with cosmetics.</summary>
    public enum ClassId : byte { Knight = 0, Mage = 1, Cleric = 2, Rogue = 3, Ranger = 4 }

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

        public static readonly ClassDefinition[] ById = { Knight, Mage, Cleric, Rogue, Ranger };

        /// <summary>One line per kit slot, for the character select screen.</summary>
        public static (string key, string name, string desc)[] Kit(ClassId id) => id == ClassId.Ranger
            ? new[]
            {
                ("PASSIVE", "Hunter's Mark", "Three hits on one enemy mark it: +25% damage from everyone."),
                ("LMB", "Longbow", "Hold to draw, release to loose. A full draw pierces."),
                ("RMB", "Disengage", "Leap backwards out of reach."),
                ("Q", "Bear Trap", "Set a trap at your crosshair that snaps and holds an enemy."),
                ("E", "Volley", "Rain arrows on the circle at your crosshair."),
            }
            : id == ClassId.Rogue
            ? new[]
            {
                ("PASSIVE", "Ambush", "Hits from behind deal a further +50%."),
                ("LMB", "Twin Daggers", "Very fast, short stabs, one target at a time."),
                ("RMB", "Fan of Knives", "Throw three knives in a narrow spread."),
                ("Q", "Shadowstep", "Reappear behind the enemy under your crosshair."),
                ("E", "Smoke Bomb", "Vanish: enemies lose you. Your first strike from the smoke deals double and stuns."),
            }
            : id == ClassId.Cleric
            ? new[]
            {
                ("PASSIVE", "Grace", "You revive fallen allies twice as fast."),
                ("LMB", "Smite", "Hurl bolts of holy light."),
                ("RMB", "Mending Beam", "Hold on an ally to heal them. Aim at nothing to heal yourself, slowly."),
                ("Q", "Consecrate", "Bless the ground: allies inside heal, enemies inside burn."),
                ("E", "Divine Aegis", "Shield every ally nearby against the next 60 damage."),
            }
            : id == ClassId.Mage
            ? new[]
            {
                ("PASSIVE", "Shatter", "Fire on a chilled enemy deals +75% and consumes the chill."),
                ("LMB", "Firebolt", "Hold to hurl fast, cheap bolts of fire."),
                ("RMB", "Frostbolt", "A slower bolt that chills: the target is slowed and primed to shatter."),
                ("Q", "Blink", "Teleport a few metres the way you are looking."),
                ("E", "Meteor", "Call down a meteor at your crosshair. Shatters everything chilled beneath it."),
            }
            : new[]
            {
                ("PASSIVE", "Bulwark", "Blocking stops 80% of damage from the front, at a stamina cost."),
                ("LMB", "Sword Swing", "A quick wind-up, then a wide arc. Alternates sides."),
                ("RMB", "Block", "Hold to raise your shield. Slows you while held."),
                ("Q", "Shield Bash", "Lunge and stun. Cancels enemy wind-ups — even a boss's."),
                ("E", "War Cry", "Taunt every enemy nearby onto you and halve the damage you take."),
            };

        /// <summary>How the class plays with the others (see <see cref="Combos"/>).</summary>
        public static string ComboLine(ClassId id) => id == ClassId.Mage
            ? "Frost chills for everyone's heavy hits to Shatter. Your fire Shatters for x1.75; Meteor feasts on stunned crowds."
            : id == ClassId.Cleric
            ? "Consecrate Blesses allies inside (+20% damage); the ally in your Mending Beam is Inspired (+15%). A broken Aegis shield bursts."
            : id == ClassId.Rogue
            ? "Openers stun (Exposed: +25% for all); Ambush hits Shatter chill. Allies in your smoke cloud are hidden too (Smoke Screen)."
            : id == ClassId.Ranger
            ? "Marks give everyone +25% and spread when the target dies. Traps stun (Exposed); full draws Shatter chill."
            : "Bash stuns (Exposed: +25% for all) and Shatters chill. War Cry Distracts enemies: +15% damage from everyone else.";

        /// <summary>Difficulty hint for the class picker.</summary>
        public static string Difficulty(ClassId id) => id == ClassId.Mage ? "Hard - fragile, rewards combos and positioning"
            : id == ClassId.Cleric ? "Medium - watch your party, not just the enemy"
            : id == ClassId.Rogue ? "Hard - all about getting behind things"
            : id == ClassId.Ranger ? "Medium - keep your distance, aim your draws"
            : "Easy - forgiving, holds the line";

        /// <summary>The name of the class's Q or E, from its kit list.</summary>
        public static string AbilityName(ClassId id, AbilitySlot slot)
        {
            string key = slot == AbilitySlot.Q ? "Q" : "E";
            foreach (var (k, name, _) in Kit(id)) if (k == key) return name;
            return key;
        }

        /// <summary>The class's own weapon: never found, only tempered at the anvil.</summary>
        public static string WeaponName(ClassId id) => id == ClassId.Mage ? "Staff"
            : id == ClassId.Cleric ? "Mace"
            : id == ClassId.Rogue ? "Daggers"
            : id == ClassId.Ranger ? "Longbow"
            : "Longsword";

        /// <summary>What one anvil level adds: ATT and DEX, the same for every class.</summary>
        public const float WeaponAttPerLevel = 4f, WeaponDexPerLevel = 2f;

        public static ClassDefinition For(ClassId id) => ById[Mathf.Clamp((int)id, 0, ById.Length - 1)];

        /// <summary>One line for the class picker.</summary>
        public static string Pitch(ClassId id) => id == ClassId.Ranger
            ? "Ranger: mark the target, trap the path, rain arrows. The party hits harder with you."
            : id == ClassId.Rogue
            ? "Rogue: vanish, step behind them, strike. Deadly from behind, fragile in front."
            : id == ClassId.Cleric
            ? "Cleric: heal, shield and raise the fallen. The reason the party lives."
            : id == ClassId.Mage
            ? "Mage: chill with frost, shatter with fire. Fragile, deadly at range."
            : "Knight: block, bash and taunt. Holds the line so others can kill.";
    }
}
