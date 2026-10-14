using System.Collections.Generic;
using Deepbound.Core;
using UnityEngine;

namespace Deepbound.Loot
{
    public enum GearSlot : byte { Armor = 0, Charm = 1 }

    /// <summary>
    /// The named gear pieces. APPEND ONLY: synced as a byte. Every piece works for every
    /// class — effects are written against things all classes share (being hit, dodging,
    /// cooldowns, kills) — so a drop is never for the wrong class.
    /// </summary>
    public enum GearId : byte
    {
        None = 0,
        OssuaryPlate = 1, GraveclothMantle = 2, TidewovenRobe = 3, ThornhideJerkin = 4, KingsguardHauberk = 5, AshenShroud = 6,
        LanternOfTheLost = 7, HuntersTooth = 8, CrackedHourglass = 9, Bloodstone = 10, EmberLocket = 11, LuckyKnucklebone = 12,
    }

    /// <summary>
    /// Gear data and maths. A piece's stats are its stat weights times the stat's unit,
    /// scaled by the depth it dropped at and its quality — so the same named piece found
    /// deeper, or finer, is the same item made stronger.
    /// </summary>
    public static class Gear
    {
        private struct Def
        {
            public GearSlot Slot;
            public string Name, Effect;
            public (StatType stat, float weight)[] Stats;
        }

        // What one weight point of each stat is worth.
        private static readonly float[] Unit = { 10f, 6f, 2f, 1f, 2f, 2f, 3f, 3f };

        private static readonly Dictionary<GearId, Def> Defs = new Dictionary<GearId, Def>
        {
            { GearId.OssuaryPlate, new Def { Slot = GearSlot.Armor, Name = "Ossuary Plate",
                Effect = "Take 15% less damage while above half health.", Stats = new[] { (StatType.DEF, 3f), (StatType.HP, 2f) } } },
            { GearId.GraveclothMantle, new Def { Slot = GearSlot.Armor, Name = "Gravecloth Mantle",
                Effect = "After a dodge, your next hit within 2 s deals +60%.", Stats = new[] { (StatType.HP, 2f), (StatType.SPD, 2f) } } },
            { GearId.TidewovenRobe, new Def { Slot = GearSlot.Armor, Name = "Tidewoven Robe",
                Effect = "All healing you receive is 30% stronger.", Stats = new[] { (StatType.HP, 2f), (StatType.WIS, 2f) } } },
            { GearId.ThornhideJerkin, new Def { Slot = GearSlot.Armor, Name = "Thornhide Jerkin",
                Effect = "Below 40% health you regenerate three times as fast, even mid-fight.", Stats = new[] { (StatType.DEF, 2f), (StatType.VIT, 2f) } } },
            { GearId.KingsguardHauberk, new Def { Slot = GearSlot.Armor, Name = "Kingsguard Hauberk",
                Effect = "Heavy: the most armour and health, at the cost of speed.", Stats = new[] { (StatType.DEF, 4f), (StatType.HP, 3f), (StatType.SPD, -1.5f) } } },
            { GearId.AshenShroud, new Def { Slot = GearSlot.Armor, Name = "Ashen Shroud",
                Effect = "Falling below 30% health erupts in flame around you (every 15 s).", Stats = new[] { (StatType.HP, 2f), (StatType.ATT, 1.5f) } } },

            { GearId.LanternOfTheLost, new Def { Slot = GearSlot.Charm, Name = "Lantern of the Lost",
                Effect = "Embers you pick up are worth 40% more.", Stats = new[] { (StatType.SPD, 2f), (StatType.WIS, 1.5f) } } },
            { GearId.HuntersTooth, new Def { Slot = GearSlot.Charm, Name = "Hunter's Tooth",
                Effect = "Deal 25% more damage to elites and bosses.", Stats = new[] { (StatType.ATT, 2f), (StatType.DEX, 1f) } } },
            { GearId.CrackedHourglass, new Def { Slot = GearSlot.Charm, Name = "Cracked Hourglass",
                Effect = "Your Q and E recharge 18% faster.", Stats = new[] { (StatType.DEX, 2f), (StatType.WIS, 1f) } } },
            { GearId.Bloodstone, new Def { Slot = GearSlot.Charm, Name = "Bloodstone",
                Effect = "Each kill heals you for 4% of your maximum health.", Stats = new[] { (StatType.ATT, 1.5f), (StatType.VIT, 2f) } } },
            { GearId.EmberLocket, new Def { Slot = GearSlot.Charm, Name = "Ember Locket",
                Effect = "Kills have a 30% chance to leave an ember behind.", Stats = new[] { (StatType.ATT, 1.5f), (StatType.HP, 1.5f) } } },
            { GearId.LuckyKnucklebone, new Def { Slot = GearSlot.Charm, Name = "Lucky Knucklebone",
                Effect = "One hit in ten simply misses you.", Stats = new[] { (StatType.SPD, 1.5f), (StatType.DEX, 1.5f) } } },
        };

        public static readonly string[] QualityNames = { "Worn", "Fine", "Masterwork" };
        private static readonly float[] QualityPower = { 1f, 1.3f, 1.65f };

        public static Color QualityColor(int quality) => quality >= 2 ? new Color(1f, 0.62f, 0.2f)
            : quality == 1 ? new Color(0.45f, 0.7f, 1f) : new Color(0.82f, 0.8f, 0.76f);

        public static GearSlot SlotOf(GearId id) => Defs[id].Slot;
        public static string BaseName(GearId id) => Defs.TryGetValue(id, out var d) ? d.Name : "Nothing";
        public static string Effect(GearId id) => Defs.TryGetValue(id, out var d) ? d.Effect : "";

        public static string Name(GearItem item) => item.IsEmpty ? "Nothing" : QualityNames[item.Quality] + " " + BaseName(item.Kind);

        private static float Power(GearItem item) => (1f + 0.6f * item.Depth) * QualityPower[item.Quality];

        /// <summary>What this item adds to one stat (may be negative).</summary>
        public static float StatValue(GearItem item, StatType stat)
        {
            if (item.IsEmpty || !Defs.TryGetValue(item.Kind, out var d)) return 0f;
            foreach (var (s, weight) in d.Stats)
            {
                if (s != stat) continue;
                return Mathf.Round(Unit[(int)s] * weight * Power(item));
            }
            return 0f;
        }

        /// <summary>The stats this item touches, in display order.</summary>
        public static IEnumerable<StatType> StatsOf(GearItem item)
        {
            if (item.IsEmpty || !Defs.TryGetValue(item.Kind, out var d)) yield break;
            foreach (var (s, _) in d.Stats) yield return s;
        }

        /// <summary>"+4 DEF, +20 HP" — the stat line in one string.</summary>
        public static string StatLine(GearItem item)
        {
            var parts = new List<string>();
            foreach (var s in StatsOf(item))
            {
                float v = StatValue(item, s);
                parts.Add((v >= 0 ? "+" : "") + v + " " + Core.Stats.ShortName(s));
            }
            return string.Join(", ", parts);
        }

        /// <summary>Embers you get for breaking an unwanted piece down.</summary>
        public static int ScrapValue(GearItem item) => item.IsEmpty ? 0 : 5 + 4 * item.Quality + 3 * item.Depth;

        /// <summary>A random piece for the given depth. <paramref name="luck"/> 0 = normal, 1 = boss/vault.</summary>
        public static GearItem Roll(int depth, int luck)
        {
            int id = Random.Range(1, Defs.Count + 1);
            float r = Random.value;
            int quality = luck > 0 ? (r < 0.45f ? 2 : 1) : (r < 0.1f ? 2 : r < 0.42f ? 1 : 0);
            return new GearItem((GearId)id, quality, depth);
        }
    }
}
