using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// What a loot drop is. APPEND ONLY, like StatType. Values 0-7 and 9 are the retired
    /// stat potions (replaced by the weapon/gear system); they are kept so later values
    /// keep their numbers, and nothing drops them any more.
    /// </summary>
    public enum LootKind : byte
    {
        PotionHP = 0, PotionMP = 1, PotionATT = 2, PotionDEF = 3,
        PotionSPD = 4, PotionDEX = 5, PotionVIT = 6, PotionWIS = 7,
        Gold = 8,
        PotionAll = 9, // one potion's worth of every stat; boss-only
        Relic = 10,    // Amount carries the Relic id
        Embers = 11,   // weapon upgrade material
    }

    public static class LootInfo
    {
        /// <summary>One colour per stat, used wherever a stat is drawn (stat bars, gear lines).</summary>
        public static Color StatColor(StatType stat)
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

        public static readonly Color EmberColor = new Color(1f, 0.42f, 0.12f);

        public static string Name(LootKind kind)
        {
            if (kind == LootKind.Gold) return "Gold";
            if (kind == LootKind.Embers) return "Embers";
            if (kind == LootKind.Relic) return "a relic";
            return kind.ToString();
        }
    }
}
