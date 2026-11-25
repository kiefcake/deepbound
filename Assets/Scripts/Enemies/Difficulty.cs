using UnityEngine;

namespace Deepbound.Enemies
{
    public static class Difficulty
    {
        public class Setting
        {
            public string Name, Blurb;
            public float Health, Damage, SpawnMultiplier, EliteBonus, Gold, Embers = 1f;
        }

        public static readonly Setting[] Settings =
        {
            new Setting { Name = "Normal", Blurb = "The descent as intended.", Health = 1f, Damage = 1f, SpawnMultiplier = 1f, EliteBonus = 0f, Gold = 1f, Embers = 1f },
            new Setting { Name = "Hard", Blurb = "Tougher dead, more elites, richer spoils.", Health = 1.4f, Damage = 1.3f, SpawnMultiplier = 1.15f, EliteBonus = 0.08f, Gold = 1.5f, Embers = 1.3f },
            new Setting { Name = "Nightmare", Blurb = "For parties that never fall. Double gold.", Health = 1.9f, Damage = 1.6f, SpawnMultiplier = 1.3f, EliteBonus = 0.15f, Gold = 2f, Embers = 1.6f },
            new Setting { Name = "Torment", Blurb = "For those who brought the Last Morning. The Tallyman comes early.", Health = 2.5f, Damage = 2f, SpawnMultiplier = 1.45f, EliteBonus = 0.25f, Gold = 2.6f, Embers = 2f },
        };

        public const float EconomyGold = 0.62f, EconomyEmbers = 0.62f;

        public static float GoldIncome => EconomyGold * Level.Gold;

        public static float EmberIncome => EconomyEmbers * Level.Embers;

        public const float DropCut = 0.8f, MaterialCut = 0.5f;

        public const float CoinCut = DropCut * 1.15f;
        public static int Gold(int amount) => amount <= 0 ? amount : Mathf.Max(1, Mathf.RoundToInt(amount * GoldIncome));
        public static int Embers(int amount) => amount <= 0 ? amount : Mathf.Max(1, Mathf.RoundToInt(amount * EmberIncome));

        public const int TormentIndex = 3;
        public static bool TormentUnlocked => Progression.Deeds.Wins >= 1;

        public const float TormentTallySooner = 120f;

        public static Setting Level
        {
            get
            {
                var d = World.DungeonDirector.Instance;
                int i = d != null ? d.DifficultyIndex.Value : 0;
                return Settings[Mathf.Clamp(i, 0, Settings.Length - 1)];
            }
        }

        public static float EnemyHealth(int players) => 1f + 0.35f * Extra(players);

        public const float BossToughness = 5f;

        public static float BossHealth(int players) => BossToughness * (1f + 0.9f * Extra(players));

        public static float PartyHealth(int players, bool boss, int depth = 0) => boss ? BossHealth(players) * BossDepth(players, depth) : EnemyHealth(players);

        public static float BossDepth(int players, int depth) => 1f + 0.18f * Mathf.Max(0, depth) * Mathf.Min(Extra(players), 3) / 3f;

        public static float KillLootShare(int players) => Mathf.Max(1, players) * SpawnCount(1) / SpawnCount(players);
        public static float FixedLootShare(int players) => Mathf.Max(1, players);
        public static int Party => Mathf.Max(1, Player.PlayerLife.All.Count);

        public static float Damage(int players) => 1f + 0.22f * Extra(players);

        public const float PartyEmbers = 0.8f;

        public static float SpawnCount(int players) => 0.8f + 0.32f * Extra(players);

        public static float DepthHealth(int depth) { float t = World.DungeonDirector.Tier(depth); return 1f + 0.45f * t + 0.16f * Mathf.Pow(Mathf.Max(0f, t - 2.5f), 2f); }

        public static float DepthDamage(int depth) { float t = World.DungeonDirector.Tier(depth); return 1f + 0.35f * t + 0.14f * Mathf.Pow(Mathf.Max(0f, t - 2.5f), 2f); }

        public static float Lead(int depth) => Mathf.Clamp01((depth - 1) / 5f);

        public static float Tell(int depth) => Mathf.Lerp(1f, 0.72f, Mathf.Clamp01(depth / 8f));

        public static int SummonCount(int baseCount, int players) => Mathf.Clamp(Mathf.RoundToInt(baseCount * (0.55f + 0.15f * Mathf.Max(1, players))), 1, baseCount * 2);

        public static float EliteChance(int depth, int players) => 0.11f + 0.05f * World.DungeonDirector.Tier(depth) + 0.02f * Extra(players) + Level.EliteBonus;

        public const float RoomCrowd = 0.75f;

        public const float RankHealth = 1.55f, RankDamage = 1.15f;

        public const float PoiseBreakFraction = 0.2f;

        public const float BackstabMultiplier = 1.35f;

        public const float BossBackstabMultiplier = 1.2f;

        public const float UnawareMultiplier = 1.5f, UnawareCalm = 4f;

        private static int Extra(int players) => Mathf.Max(0, players - 1);
    }
}
