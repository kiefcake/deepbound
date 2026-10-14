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
        // Boss legendaries: each boss's own piece, never rolled at random.
        WardensBulwark = 13, LichPhylactery = 14, HollowCrown = 15, CardinalVestments = 16,
        // More rollable pieces (after the legendaries: ids are synced, append only).
        BellringersHauberk = 17, GravediggersCoat = 18, RosaryOfAsh = 19, WitchboneFetish = 20,
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

            { GearId.WardensBulwark, new Def { Slot = GearSlot.Armor, Name = "Warden's Bulwark",
                Effect = "Every 8 s, the next hit you take is halved and stuns its attacker (Exposed).", Stats = new[] { (StatType.DEF, 3f), (StatType.HP, 3f) } } },
            { GearId.LichPhylactery, new Def { Slot = GearSlot.Charm, Name = "Lich's Phylactery",
                Effect = "Every fourth hit you land chills its target (sets up Shatter).", Stats = new[] { (StatType.ATT, 2f), (StatType.WIS, 2f) } } },
            { GearId.HollowCrown, new Def { Slot = GearSlot.Charm, Name = "Crown of the Hollow King",
                Effect = "Your Q and E count as one rank higher (up to rank 3).", Stats = new[] { (StatType.ATT, 2f), (StatType.HP, 2f) } } },
            { GearId.BellringersHauberk, new Def { Slot = GearSlot.Armor, Name = "Bellringer's Hauberk",
                Effect = "Every 10 s, being hit tolls out a shockwave that hurls nearby enemies away.", Stats = new[] { (StatType.DEF, 3f), (StatType.HP, 1.5f) } } },
            { GearId.GravediggersCoat, new Def { Slot = GearSlot.Armor, Name = "Gravedigger's Coat",
                Effect = "Stand your ground: after a second without moving, take 25% less damage.", Stats = new[] { (StatType.HP, 2.5f), (StatType.VIT, 1.5f) } } },
            { GearId.RosaryOfAsh, new Def { Slot = GearSlot.Charm, Name = "Rosary of Ash",
                Effect = "Your ultimate (R) charges 35% faster.", Stats = new[] { (StatType.WIS, 2f), (StatType.ATT, 1f) } } },
            { GearId.WitchboneFetish, new Def { Slot = GearSlot.Charm, Name = "Witchbone Fetish",
                Effect = "Every 8 s, your next hit Marks its target (+25% damage from everyone).", Stats = new[] { (StatType.ATT, 1.5f), (StatType.DEX, 1.5f) } } },
            { GearId.CardinalVestments, new Def { Slot = GearSlot.Armor, Name = "Vestments of the Pale Cardinal",
                Effect = "Your kills call down Judgement: a pillar of holy fire erupts there a moment later (once a second).", Stats = new[] { (StatType.DEF, 2f), (StatType.WIS, 2f), (StatType.ATT, 1.5f) } } },
        };

        /// <summary>Random rolls draw from these; the boss pieces only come from their boss.</summary>
        private static readonly GearId[] Rollable =
        {
            GearId.OssuaryPlate, GearId.GraveclothMantle, GearId.TidewovenRobe, GearId.ThornhideJerkin, GearId.KingsguardHauberk, GearId.AshenShroud,
            GearId.LanternOfTheLost, GearId.HuntersTooth, GearId.CrackedHourglass, GearId.Bloodstone, GearId.EmberLocket, GearId.LuckyKnucklebone,
            GearId.BellringersHauberk, GearId.GravediggersCoat, GearId.RosaryOfAsh, GearId.WitchboneFetish,
        };

        /// <summary>The legendary a boss can drop, by its type.</summary>
        public static GearId LegendaryOf(Enemies.BossBrain boss) => boss is Enemies.GravewardenBrain ? GearId.WardensBulwark
            : boss is Enemies.TideLichBrain ? GearId.LichPhylactery
            : boss is Enemies.CardinalBrain ? GearId.CardinalVestments
            : GearId.HollowCrown;

        public static readonly string[] QualityNames = { "Worn", "Fine", "Masterwork", "Legendary" };
        private static readonly float[] QualityPower = { 1f, 1.3f, 1.65f, 1.85f };

        public static Color QualityColor(int quality) => quality >= 3 ? new Color(0.85f, 0.45f, 1f)
            : quality >= 2 ? new Color(1f, 0.62f, 0.2f)
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

        /// <summary>Embers to refine a piece one quality step (Worn→Fine→Masterwork). 0 if it cannot be.</summary>
        public static int RefineCost(GearItem item) => item.IsEmpty || item.Quality >= 2 ? 0 : 30 + 30 * item.Quality;

        /// <summary>Embers to attune a piece to a deeper floor, so a favourite keeps up.</summary>
        public static int AttuneCost(GearItem item, int depth) => item.IsEmpty || depth <= item.Depth ? 0 : 20 * (depth - item.Depth);

        /// <summary>Embers you get for breaking an unwanted piece down.</summary>
        public static int ScrapValue(GearItem item) => item.IsEmpty ? 0 : 5 + 4 * item.Quality + 3 * item.Depth;

        /// <summary>A random piece for the given depth. <paramref name="luck"/> 0 = normal, 1 = boss/vault.</summary>
        public static GearItem Roll(int depth, int luck)
        {
            int id = (int)Rollable[Random.Range(0, Rollable.Length)];
            float r = Random.value;
            int quality = luck > 0 ? (r < 0.45f ? 2 : 1) : (r < 0.1f ? 2 : r < 0.42f ? 1 : 0);
            return new GearItem((GearId)id, quality, depth);
        }
    }
}
