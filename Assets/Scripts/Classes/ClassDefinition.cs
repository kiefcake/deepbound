using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>APPEND ONLY: synced as a byte and will be saved with cosmetics.</summary>
    public enum ClassId : byte { Knight = 0, Mage = 1 }

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

        public static readonly ClassDefinition[] ById = { Knight, Mage };

        /// <summary>One line per kit slot, for the character select screen.</summary>
        public static (string key, string name, string desc)[] Kit(ClassId id) => id == ClassId.Mage
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
        public static string Difficulty(ClassId id) => id == ClassId.Mage ? "Hard - fragile, rewards combos and positioning" : "Easy - forgiving, holds the line";

        public static ClassDefinition For(ClassId id) => ById[Mathf.Clamp((int)id, 0, ById.Length - 1)];

        /// <summary>One line for the class picker.</summary>
        public static string Pitch(ClassId id) => id == ClassId.Mage
            ? "Mage: chill with frost, shatter with fire. Fragile, deadly at range."
            : "Knight: block, bash and taunt. Holds the line so others can kill.";
    }
}
