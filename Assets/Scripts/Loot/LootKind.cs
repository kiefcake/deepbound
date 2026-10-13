using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// What a loot drop is. Values 0-7 deliberately mirror <see cref="StatType"/>, so a
    /// stat potion's kind casts straight to its stat. APPEND ONLY, like StatType.
    /// </summary>
    public enum LootKind : byte
    {
        PotionHP = 0, PotionMP = 1, PotionATT = 2, PotionDEF = 3,
        PotionSPD = 4, PotionDEX = 5, PotionVIT = 6, PotionWIS = 7,
        Gold = 8,
        PotionAll = 9, // one potion's worth of every stat; boss-only
        Relic = 10,    // Amount carries the Relic id
    }

    public static class LootInfo
    {
        public static bool IsStatPotion(LootKind kind) => (byte)kind < Stats.Count;

        /// <summary>
        /// RotMG-flavoured potion colours, so a potion is identifiable from across the room
        /// before you read anything.
        /// </summary>
        public static Color PotionColor(StatType stat)
        {
            switch (stat)
            {
                case StatType.HP: return new Color(0.95f, 0.15f, 0.15f);
                case StatType.MP: return new Color(0.2f, 0.4f, 1f);
                case StatType.ATT: return new Color(0.7f, 0.2f, 0.95f);
                case StatType.DEF: return new Color(0.15f, 0.15f, 0.2f);
                case StatType.SPD: return new Color(0.2f, 0.95f, 0.4f);
                case StatType.DEX: return new Color(1f, 0.55f, 0.1f);
                case StatType.VIT: return new Color(0.95f, 0.35f, 0.6f);
                case StatType.WIS: return new Color(0.3f, 0.9f, 1f);
                default: return Color.white;
            }
        }

        public static string Name(LootKind kind)
        {
            if (kind == LootKind.Gold) return "Gold";
            if (kind == LootKind.PotionAll) return "Potion of the Deep";
            if (kind == LootKind.Relic) return "a relic";
            return "Potion of " + Stats.ShortName((StatType)kind);
        }
    }
}
