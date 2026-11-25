using System;

namespace Deepbound.Core
{
    public enum StatType
    {
        HP = 0,

        MP = 1,

        ATT = 2,

        DEF = 3,

        SPD = 4,

        DEX = 5,

        VIT = 6,

        WIS = 7,
    }

    public static class Stats
    {
        public static readonly StatType[] All = (StatType[])Enum.GetValues(typeof(StatType));

        public static readonly int Count = All.Length;

        public static string ShortName(StatType stat) => stat.ToString();

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
                case StatType.WIS: return "Every point gained charges your ultimate 1.5% faster. Mana and reagents return faster, and Cleric heals are stronger.";
                default: return string.Empty;
            }
        }
    }
}
