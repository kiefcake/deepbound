using Deepbound.Core;

namespace Deepbound.Progression
{
    /// <summary>
    /// The merchant's catalogue. Everything bought is run-scoped, like everything else.
    ///
    /// Gold has one job — turn a good floor into a stronger next floor — so the shop sells
    /// the same three kinds of power the dungeon drops: healing, stat potions, and gear
    /// bonuses. Prices are fixed so players can plan around them.
    /// </summary>
    public static class Shop
    {
        public enum ItemKind : byte { Heal, Potion, Bonus }

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
        }

        public const float MerchantRange = 3.2f;

        public static readonly Item[] Items =
        {
            new Item { Name = "Healing Draught", Description = "Restore half your health", Price = 12, Kind = ItemKind.Heal, Amount = 0.5f },
            new Item { Name = "Potion of HP", Description = "+1 HP potion", Price = 45, Kind = ItemKind.Potion, Stat = StatType.HP },
            new Item { Name = "Potion of ATT", Description = "+1 ATT potion", Price = 45, Kind = ItemKind.Potion, Stat = StatType.ATT },
            new Item { Name = "Potion of DEF", Description = "+1 DEF potion", Price = 45, Kind = ItemKind.Potion, Stat = StatType.DEF },
            new Item { Name = "Potion of SPD", Description = "+1 SPD potion", Price = 45, Kind = ItemKind.Potion, Stat = StatType.SPD },
            new Item { Name = "Potion of DEX", Description = "+1 DEX potion", Price = 45, Kind = ItemKind.Potion, Stat = StatType.DEX },
            new Item { Name = "Potion of VIT", Description = "+1 VIT potion", Price = 45, Kind = ItemKind.Potion, Stat = StatType.VIT },
            new Item { Name = "Whetstone", Description = "+3 ATT for the run (max +9)", Price = 60, Kind = ItemKind.Bonus, Stat = StatType.ATT, Amount = 3f, Cap = 9f },
            new Item { Name = "Riveted Plate", Description = "+2 DEF for the run (max +6)", Price = 60, Kind = ItemKind.Bonus, Stat = StatType.DEF, Amount = 2f, Cap = 6f },
        };
    }
}
