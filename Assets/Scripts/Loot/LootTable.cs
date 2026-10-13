using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// RotMG-style loot: every entry rolls independently, so one kill can drop nothing,
    /// one thing, or several. Ported in spirit from the old prototype's LootTable.
    ///
    /// <para>
    /// Independent rolls are what make a kill exciting — the chance at a potion never
    /// "uses up" the chance at gold — and they keep the maths legible: a 12% potion is a
    /// 12% potion, whatever else is in the table.
    /// </para>
    /// </summary>
    public class LootTable
    {
        private struct Entry
        {
            public float Chance;
            public LootKind? Kind;   // null = random stat potion (excluding MP until a class uses it)
            public int MinAmount, MaxAmount;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public LootTable Add(float chance, LootKind kind, int min = 1, int max = 1)
        {
            entries.Add(new Entry { Chance = chance, Kind = kind, MinAmount = min, MaxAmount = max });
            return this;
        }

        public LootTable AddRandomPotion(float chance)
        {
            entries.Add(new Entry { Chance = chance, Kind = null, MinAmount = 1, MaxAmount = 1 });
            return this;
        }

        public void Roll(List<(LootKind kind, int amount)> results, float goldScale)
        {
            foreach (var e in entries)
            {
                if (Random.value > e.Chance) continue;
                var kind = e.Kind ?? RandomStatPotion();
                int amount = Random.Range(e.MinAmount, e.MaxAmount + 1);
                if (kind == LootKind.Relic) amount = (int)Relics.Random();
                if (kind == LootKind.Gold) amount = Mathf.Max(1, Mathf.RoundToInt(amount * goldScale));
                results.Add((kind, amount));
            }
        }

        private static readonly LootKind[] RandomPool =
        {
            LootKind.PotionHP, LootKind.PotionATT, LootKind.PotionDEF, LootKind.PotionSPD,
            LootKind.PotionDEX, LootKind.PotionVIT, LootKind.PotionWIS,
        };

        private static LootKind RandomStatPotion() => RandomPool[Random.Range(0, RandomPool.Length)];

        // ------------------------------------------------------------------
        // Tables
        // ------------------------------------------------------------------

        public static readonly LootTable Skeleton = new LootTable()
            .Add(0.75f, LootKind.Gold, 3, 8)
            .AddRandomPotion(0.12f);

        /// <summary>Elites: worth hunting. A real chance at a relic.</summary>
        public static readonly LootTable Elite = new LootTable()
            .Add(1f, LootKind.Gold, 15, 25)
            .AddRandomPotion(0.6f)
            .Add(0.3f, LootKind.Relic);

        /// <summary>Side-room chests: worth the detour.</summary>
        public static readonly LootTable Chest = new LootTable()
            .Add(1f, LootKind.Gold, 25, 40)
            .AddRandomPotion(1f)
            .AddRandomPotion(0.5f)
            .Add(0.3f, LootKind.Relic);

        /// <summary>Clay urns: pocket change, now and then a potion.</summary>
        public static readonly LootTable Urn = new LootTable()
            .Add(0.6f, LootKind.Gold, 2, 6)
            .AddRandomPotion(0.05f);

        /// <summary>Every boss always pays out — a boss kill should never feel wasted.</summary>
        public static readonly LootTable SkeletonKing = new LootTable()
            .Add(1f, LootKind.Gold, 40, 60)
            .Add(1f, LootKind.Relic)
            .Add(1f, LootKind.PotionAll)
            .AddRandomPotion(1f)
            .AddRandomPotion(1f)
            .AddRandomPotion(0.5f);
    }
}
