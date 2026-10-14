using System.Collections.Generic;
using Deepbound.Core;
using Deepbound.Loot;

namespace Deepbound.Progression
{
    /// <summary>
    /// The merchant's catalogue. Everything bought is run-scoped, like everything else.
    ///
    /// Gold has one job — turn a good floor into a stronger next floor — so the shop sells
    /// healing, embers for the anvil next door, small stat bonuses, and two relics.
    /// Prices are fixed so players can plan around them.
    /// </summary>
    public static class Shop
    {
        /// <summary>Value 1 was the retired stat potion.</summary>
        public enum ItemKind : byte { Heal, Embers = 1, Bonus = 2, Relic = 3, Gear = 4, Sigil = 5 }

        public struct Item
        {
            public string Name;
            public string Description;
            public int Price;
            public ItemKind Kind;
            public StatType Stat;
            public float Amount;
            /// <summary>For bonuses: the most this stat can be raised by purchases.</summary>
            public float Cap;
            /// <summary>For gear: the piece on offer.</summary>
            public Loot.GearItem Gear;
        }

        public const float MerchantRange = 3.2f;

        /// <summary>
        /// Today's stock: the fixed catalogue plus two relics, chosen from the floor's seed so
        /// every peer sees the same two (and they change each floor). Relic id rides in Amount.
        /// </summary>
        public static Item[] Current
        {
            get
            {
                var director = World.DungeonDirector.Instance;
                int seed = director != null ? director.Floor.Value.x : 0;
                int depth = director != null ? UnityEngine.Mathf.Max(0, director.Floor.Value.y) : 0;
                if (seed == cachedSeed && cached != null) return cached;

                var rng = new System.Random(seed);
                var list = new List<Item>(Items);
                int a = rng.Next(Relics.Count), b = rng.Next(Relics.Count - 1);
                if (b >= a) b++;
                foreach (int r in new[] { a, b })
                {
                    var relic = (Relic)r;
                    list.Add(new Item { Name = Relics.Name(relic), Description = Relics.Describe(relic), Price = 110 + 30 * depth, Kind = ItemKind.Relic, Amount = r });
                }
                list.Add(new Item { Name = "Sigil", Description = "Empower an ability one rank, at the anvil", Price = 150 + 50 * depth, Kind = ItemKind.Sigil });
                cached = list.ToArray();
                cachedSeed = seed;
                return cached;
            }
        }

        /// <summary>
        /// One player's stock: today's catalogue plus a gear piece picked for them alone
        /// (seeded by floor and player, so it is stable and differs between players).
        /// </summary>
        public static Item[] For(ulong clientId)
        {
            var common = Current;
            var director = World.DungeonDirector.Instance;
            int seed = director != null ? director.Floor.Value.x : 0;
            int depth = director != null ? UnityEngine.Mathf.Max(0, director.Floor.Value.y) : 0;
            var rng = new System.Random(seed * 31 + (int)clientId * 7919 + 5);
            int id = rng.Next(1, 13);
            double r = rng.NextDouble();
            int quality = r < 0.2 ? 2 : r < 0.7 ? 1 : 0;
            var gear = new Loot.GearItem((Loot.GearId)id, quality, depth);
            var list = new List<Item>(common)
            {
                new Item { Name = Loot.Gear.Name(gear), Description = Loot.Gear.StatLine(gear) + ". " + Loot.Gear.Effect(gear.Kind), Price = 60 + 40 * quality + 25 * depth, Kind = ItemKind.Gear, Gear = gear },
            };
            return list.ToArray();
        }

        private static Item[] cached;
        private static int cachedSeed = int.MinValue;

        public static readonly Item[] Items =
        {
            new Item { Name = "Healing Draught", Description = "Restore half your health", Price = 12, Kind = ItemKind.Heal, Amount = 0.5f },
            new Item { Name = "Ember Pouch", Description = "10 embers for the anvil", Price = 45, Kind = ItemKind.Embers, Amount = 10f },
            new Item { Name = "Ember Sack", Description = "25 embers for the anvil", Price = 100, Kind = ItemKind.Embers, Amount = 25f },
            new Item { Name = "Hearty Stew", Description = "+30 max HP for the run (max +90)", Price = 55, Kind = ItemKind.Bonus, Stat = StatType.HP, Amount = 30f, Cap = 90f },
            new Item { Name = "Riveted Plate", Description = "+2 DEF for the run (max +6)", Price = 60, Kind = ItemKind.Bonus, Stat = StatType.DEF, Amount = 2f, Cap = 6f },
            new Item { Name = "Swift Boots", Description = "+5 SPD for the run (max +10)", Price = 50, Kind = ItemKind.Bonus, Stat = StatType.SPD, Amount = 5f, Cap = 10f },
        };
    }
}
