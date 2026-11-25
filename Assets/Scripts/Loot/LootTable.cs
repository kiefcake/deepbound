using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Loot
{
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

        public void Roll(List<(LootKind kind, int amount)> results, float goldScale)
        {
            foreach (var e in entries)
            {
                bool part = e.Kind == LootKind.Bone || e.Kind == LootKind.Cloth || e.Kind == LootKind.Iron;
                float chance = e.Kind == LootKind.Gold || e.Kind == LootKind.Embers ? e.Chance : e.Chance * (part ? Enemies.Difficulty.MaterialCut : Enemies.Difficulty.DropCut);
                if (Random.value > chance) continue;
                int amount = Random.Range(e.MinAmount, e.MaxAmount + 1);
                if (e.Kind == LootKind.Relic) amount = (int)Relics.Random();
                if (e.Kind == LootKind.Rune) amount = (int)Runes.Random();
                if (e.Kind == LootKind.RareItem) amount = (int)Progression.RareItems.Random();
                if (e.Kind == LootKind.Gold) amount = Mathf.Max(1, Mathf.RoundToInt(amount * goldScale));
                if (e.Kind == LootKind.Embers) amount = Mathf.Max(1, Mathf.RoundToInt(amount * (1f + (goldScale - 1f) * 0.5f)));
                results.Add((e.Kind, amount));
            }
        }

        public static readonly LootTable Skeleton = new LootTable()
            .Add(0.4f, LootKind.Gold, 6, 12)
            .Add(0.2f, LootKind.Embers, 2, 4)
            .Add(0.07f, LootKind.PotionHP, 1, 1)
            .Add(0.22f, LootKind.Bone, 1, 2)
            .Add(0.07f, LootKind.Cloth, 1, 1)
            .Add(0.07f, LootKind.Iron, 1, 1);

        public static readonly LootTable Elite = new LootTable()
            .Add(1f, LootKind.Gold, 20, 32)
            .Add(1f, LootKind.Embers, 7, 11)
            .Add(0.18f, LootKind.Relic)
            .Add(0.035f, LootKind.Sigil)
            .Add(0.15f, LootKind.Rune)
            .Add(0.04f, LootKind.RareItem)
            .Add(0.3f, LootKind.PotionHP, 1, 1)
            .Add(1f, LootKind.Bone, 2, 3)
            .Add(0.5f, LootKind.Cloth, 1, 2)
            .Add(0.5f, LootKind.Iron, 1, 2);

        public static readonly LootTable Chest = new LootTable()
            .Add(1f, LootKind.Gold, 30, 45)
            .Add(1f, LootKind.Embers, 9, 14)
            .Add(0.22f, LootKind.Relic)
            .Add(0.06f, LootKind.Sigil)
            .Add(0.22f, LootKind.Rune)
            .Add(0.07f, LootKind.RareItem)
            .Add(0.45f, LootKind.PotionHP, 1, 1)
            .Add(0.8f, LootKind.Cloth, 2, 4)
            .Add(0.8f, LootKind.Iron, 2, 4)
            .Add(0.5f, LootKind.Bone, 1, 3);

        public static readonly LootTable Urn = new LootTable()
            .Add(0.3f, LootKind.Gold, 3, 7)
            .Add(0.05f, LootKind.Embers, 1, 2)
            .Add(0.03f, LootKind.PotionHP, 1, 1)
            .Add(0.15f, LootKind.Cloth, 1, 1);

        public static readonly LootTable GreatUrn = new LootTable()
            .Add(1f, LootKind.Gold, 14, 24)
            .Add(0.7f, LootKind.Embers, 4, 7)
            .Add(0.3f, LootKind.PotionHP, 1, 1)
            .Add(0.1f, LootKind.Rune)
            .Add(0.07f, LootKind.Relic)
            .Add(0.07f, LootKind.RareItem)
            .Add(0.03f, LootKind.Sigil)
            .Add(0.6f, LootKind.Cloth, 1, 3)
            .Add(0.4f, LootKind.Bone, 1, 3)
            .Add(0.3f, LootKind.Iron, 1, 2);

        public static readonly LootTable SkeletonKing = new LootTable()
            .Add(1f, LootKind.Gold, 40, 60)
            .Add(1f, LootKind.Relic)
            .Add(1f, LootKind.Embers, 30, 40)
            .Add(1f, LootKind.PotionHP, 1, 1)
            .Add(1f, LootKind.Bone, 3, 5)
            .Add(1f, LootKind.Cloth, 2, 4)
            .Add(1f, LootKind.Iron, 2, 4);
    }
}
