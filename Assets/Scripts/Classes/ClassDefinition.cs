using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>APPEND ONLY: synced as a byte and will be saved with cosmetics.</summary>
    public enum ClassId : byte { Knight = 0, Mage = 1, Cleric = 2, Rogue = 3 }

    /// <summary>
    /// A class's base stats and potion pools. Plain data; <see cref="Build"/> turns it into
    /// a run-scoped <see cref="StatBlock"/>.
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
        public float[] PotionPools = new float[Stats.Count];

        public StatBlock Build()
        {
            var block = new StatBlock();
            for (int i = 0; i < Stats.Count; i++)
            {
                block.SetBase((StatType)i, BaseStats[i]);
                block.SetPotionPool((StatType)i, PotionPools[i]);
            }
            return block;
        }

        private static ClassDefinition Make(string name, string role,
            float hp, float mp, float att, float def, float spd, float dex, float vit, float wis,
            float hpPool, float mpPool, float attPool, float defPool, float spdPool, float dexPool, float vitPool, float wisPool)
        {
            var c = new ClassDefinition { Name = name, Role = role };
            c.BaseStats = new[] { hp, mp, att, def, spd, dex, vit, wis };
            c.PotionPools = new[] { hpPool, mpPool, attPool, defPool, spdPool, dexPool, vitPool, wisPool };
            return c;
        }

        /// <summary>
        /// The Knight: tank. High HP and DEF, deep DEF pool, slow-ish. Damage comes from
        /// steady swings rather than burst; its value is holding a doorway while the
        /// party does the killing.
        /// </summary>
        public static readonly ClassDefinition Knight = Make("Knight", "Tank",
            hp: 240, mp: 100, att: 20, def: 6, spd: 45, dex: 45, vit: 40, wis: 20,
            hpPool: 160, mpPool: 60, attPool: 25, defPool: 14, spdPool: 20, dexPool: 25, vitPool: 30, wisPool: 25);

        /// <summary>
        /// The Mage: burst and control. Fragile, no armour, a deep mana pool; all of its
        /// damage is ranged, and its best damage needs setting up (chill, then fire).
        /// </summary>
        public static readonly ClassDefinition Mage = Make("Mage", "Burst / Control",
            hp: 160, mp: 120, att: 26, def: 0, spd: 52, dex: 50, vit: 25, wis: 45,
            hpPool: 100, mpPool: 80, attPool: 25, defPool: 10, spdPool: 20, dexPool: 25, vitPool: 20, wisPool: 30);

        /// <summary>
        /// The Cleric: the healer. Middling everything, the deepest mana and WIS pools; it
        /// keeps the party standing rather than ending fights.
        /// </summary>
        public static readonly ClassDefinition Cleric = Make("Cleric", "Healer / Support",
            hp: 195, mp: 140, att: 16, def: 3, spd: 48, dex: 45, vit: 35, wis: 50,
            hpPool: 120, mpPool: 70, attPool: 20, defPool: 12, spdPool: 20, dexPool: 20, vitPool: 25, wisPool: 35);

        /// <summary>
        /// The Rogue: assassin. Fast and fragile with the highest DEX; its damage is huge
        /// from behind and ordinary from the front, so it lives on positioning.
        /// </summary>
        public static readonly ClassDefinition Rogue = Make("Rogue", "Assassin",
            hp: 175, mp: 80, att: 24, def: 2, spd: 58, dex: 60, vit: 30, wis: 20,
            hpPool: 110, mpPool: 40, attPool: 25, defPool: 10, spdPool: 15, dexPool: 25, vitPool: 20, wisPool: 15);

        public static readonly ClassDefinition[] ById = { Knight, Mage, Cleric, Rogue };

        /// <summary>One line per kit slot, for the character select screen.</summary>
        public static (string key, string name, string desc)[] Kit(ClassId id) => id == ClassId.Rogue
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

        /// <summary>Difficulty hint for the class picker.</summary>
        public static string Difficulty(ClassId id) => id == ClassId.Mage ? "Hard - fragile, rewards combos and positioning"
            : id == ClassId.Cleric ? "Medium - watch your party, not just the enemy"
            : id == ClassId.Rogue ? "Hard - all about getting behind things"
            : "Easy - forgiving, holds the line";

        public static ClassDefinition For(ClassId id) => ById[Mathf.Clamp((int)id, 0, ById.Length - 1)];

        /// <summary>One line for the class picker.</summary>
        public static string Pitch(ClassId id) => id == ClassId.Rogue
            ? "Rogue: vanish, step behind them, strike. Deadly from behind, fragile in front."
            : id == ClassId.Cleric
            ? "Cleric: heal, shield and raise the fallen. The reason the party lives."
            : id == ClassId.Mage
            ? "Mage: chill with frost, shatter with fire. Fragile, deadly at range."
            : "Knight: block, bash and taunt. Holds the line so others can kill.";
    }
}
