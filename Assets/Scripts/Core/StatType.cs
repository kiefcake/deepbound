using System;

namespace Deepbound.Core
{
    /// <summary>
    /// The eight core stats, RotMG-style.
    ///
    /// APPEND ONLY. Unity serialises enums as raw ints in .asset YAML, so inserting a
    /// value anywhere but the end silently reassigns every value after it on every
    /// already-saved asset. Adding a ninth stat (cooldown reduction, luck) goes on the
    /// end and everything else — potions, UI, stat blocks — picks it up automatically,
    /// because nothing hardcodes the list.
    /// </summary>
    public enum StatType
    {
        /// <summary>Maximum health pool.</summary>
        HP = 0,

        /// <summary>Maximum mana / resource pool.</summary>
        MP = 1,

        /// <summary>Damage dealt per hit.</summary>
        ATT = 2,

        /// <summary>Damage reduction.</summary>
        DEF = 3,

        /// <summary>Movement speed.</summary>
        SPD = 4,

        /// <summary>Attack rate / cast rate.</summary>
        DEX = 5,

        /// <summary>Health and mana regeneration rate.</summary>
        VIT = 6,

        /// <summary>Healing and buff potency, plus a regeneration assist.</summary>
        WIS = 7,
    }

    /// <summary>
    /// Metadata about <see cref="StatType"/> that would otherwise get re-derived — and
    /// re-allocated — at every call site.
    /// </summary>
    public static class Stats
    {
        /// <summary>
        /// Every stat, cached once.
        ///
        /// <c>Enum.GetValues</c> allocates a new array on every call and boxes each value.
        /// Stat iteration happens in per-frame code (regeneration, status effect ticks,
        /// HUD refresh), so calling it there would produce steady garbage for no reason.
        /// </summary>
        public static readonly StatType[] All = (StatType[])Enum.GetValues(typeof(StatType));

        /// <summary>Number of stats. Use this to size arrays indexed by <c>(int)StatType</c>.</summary>
        public static readonly int Count = All.Length;

        /// <summary>Short display name, as shown on the stat screen and potion tooltips.</summary>
        public static string ShortName(StatType stat) => stat.ToString();

        /// <summary>One-line description of what the stat actually does, for tooltips.</summary>
        public static string Describe(StatType stat)
        {
            switch (stat)
            {
                case StatType.HP: return "Maximum health.";
                case StatType.MP: return "Maximum mana.";
                case StatType.ATT: return "Damage dealt per hit.";
                case StatType.DEF: return "Reduces incoming damage.";
                case StatType.SPD: return "Movement speed.";
                case StatType.DEX: return "Attack and cast rate.";
                case StatType.VIT: return "Slow health regeneration (melee classes mend more, ranged less).";
                case StatType.WIS: return "Healing and buff potency.";
                default: return string.Empty;
            }
        }
    }
}
