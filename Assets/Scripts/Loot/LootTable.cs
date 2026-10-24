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
            public LootKind Kind;
            public int MinAmount, MaxAmount;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public LootTable Add(float chance, LootKind kind, int min = 1, int max = 1)
        {
            entries.Add(new Entry { Chance = chance, Kind = kind, MinAmount = min, MaxAmount = max });
            return this;
        }

        /// <param name="goldScale">Scales gold, and embers more gently (deeper floors pay more of both).</param>
        public void Roll(List<(LootKind kind, int amount)> results, float goldScale)
        {
            foreach (var e in entries)
            {
                if (Random.value > e.Chance) continue;
                int amount = Random.Range(e.MinAmount, e.MaxAmount + 1);
                if (e.Kind == LootKind.Relic) amount = (int)Relics.Random();
                if (e.Kind == LootKind.Rune) amount = (int)Runes.Random();
                if (e.Kind == LootKind.RareItem) amount = (int)Progression.RareItems.Random();
                if (e.Kind == LootKind.Gold) amount = Mathf.Max(1, Mathf.RoundToInt(amount * goldScale));
                if (e.Kind == LootKind.Embers) amount = Mathf.Max(1, Mathf.RoundToInt(amount * (1f + (goldScale - 1f) * 0.5f)));
                results.Add((e.Kind, amount));
            }
        }

        // ------------------------------------------------------------------
        // Tables (fewer, fuller drops since 2026-10-18: most kills drop nothing, what drops is worth
        // stopping for; potions mostly come from the merchant now). Embers buy about a level a floor.
        // ------------------------------------------------------------------

        public static readonly LootTable Skeleton = new LootTable()
            .Add(0.4f, LootKind.Gold, 6, 12)
            .Add(0.2f, LootKind.Embers, 2, 4)
            .Add(0.07f, LootKind.PotionHP, 1, 1)
            .Add(0.035f, LootKind.PotionMP, 1, 1);

        /// <summary>Elites: worth hunting. A chance at a relic.</summary>
        public static readonly LootTable Elite = new LootTable()
            .Add(1f, LootKind.Gold, 20, 32)
            .Add(1f, LootKind.Embers, 7, 11)
            .Add(0.18f, LootKind.Relic)
            .Add(0.12f, LootKind.Sigil)
            .Add(0.15f, LootKind.Rune)
            .Add(0.04f, LootKind.RareItem)
            .Add(0.3f, LootKind.PotionHP, 1, 1)
            .Add(0.15f, LootKind.PotionMP, 1, 1);

        /// <summary>Side-room chests: worth the detour.</summary>
        public static readonly LootTable Chest = new LootTable()
            .Add(1f, LootKind.Gold, 30, 45)
            .Add(1f, LootKind.Embers, 9, 14)
            .Add(0.22f, LootKind.Relic)
            .Add(0.2f, LootKind.Sigil)
            .Add(0.22f, LootKind.Rune)
            .Add(0.07f, LootKind.RareItem)
            .Add(0.45f, LootKind.PotionHP, 1, 1)
            .Add(0.2f, LootKind.PotionMP, 1, 1);

        /// <summary>Clay urns: now and then some pocket change.</summary>
        public static readonly LootTable Urn = new LootTable()
            .Add(0.3f, LootKind.Gold, 3, 7)
            .Add(0.05f, LootKind.Embers, 1, 2)
            .Add(0.03f, LootKind.PotionHP, 1, 1);

        /// <summary>Every boss always pays out — a boss kill should never feel wasted.</summary>
        public static readonly LootTable SkeletonKing = new LootTable()
            .Add(1f, LootKind.Gold, 40, 60)
            .Add(1f, LootKind.Relic)
            .Add(1f, LootKind.Embers, 30, 40)
            .Add(1f, LootKind.PotionHP, 1, 1)
            .Add(0.6f, LootKind.PotionMP, 1, 1);
    }
}
