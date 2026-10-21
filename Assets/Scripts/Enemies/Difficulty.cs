using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Every number that makes the game harder with more players or deeper floors, in one
    /// place (design doc Q16: group-first, solo-possible).
    ///
    /// <para>
    /// More players means more of everything, not just bigger health bars: a four-player
    /// room has more bodies (so the tank's taunt and the Mage's Meteor have something to
    /// do) and each one is tougher, while per-hit damage rises only a little — individual
    /// hits must stay survivable, or a group game becomes a one-shot game.
    /// </para>
    /// </summary>
    public static class Difficulty
    {
        /// <summary>A difficulty setting, chosen by the host at camp.</summary>
        public class Setting
        {
            public string Name, Blurb;
            public float Health, Damage, SpawnMultiplier, EliteBonus, Gold;
        }

        public static readonly Setting[] Settings =
        {
            new Setting { Name = "Normal", Blurb = "The descent as intended.", Health = 1f, Damage = 1f, SpawnMultiplier = 1f, EliteBonus = 0f, Gold = 1f },
            new Setting { Name = "Hard", Blurb = "Tougher dead, more elites, richer spoils.", Health = 1.4f, Damage = 1.3f, SpawnMultiplier = 1.15f, EliteBonus = 0.08f, Gold = 1.5f },
            new Setting { Name = "Nightmare", Blurb = "For parties that never fall. Double gold.", Health = 1.9f, Damage = 1.6f, SpawnMultiplier = 1.3f, EliteBonus = 0.15f, Gold = 2f },
            new Setting { Name = "Torment", Blurb = "For those who brought the Last Morning. The Tallyman comes early.", Health = 2.5f, Damage = 2f, SpawnMultiplier = 1.45f, EliteBonus = 0.25f, Gold = 2.6f },
        };

        /// <summary>Torment (index 3) opens once this machine's player has finished a descent.</summary>
        public const int TormentIndex = 3;
        public static bool TormentUnlocked => Progression.Deeds.Wins >= 1;
        /// <summary>How much sooner the Tallyman comes on Torment.</summary>
        public const float TormentTallySooner = 120f;

        /// <summary>The current setting (read from the director; Normal when there is none).</summary>
        public static Setting Level
        {
            get
            {
                var d = World.DungeonDirector.Instance;
                int i = d != null ? d.DifficultyIndex.Value : 0;
                return Settings[Mathf.Clamp(i, 0, Settings.Length - 1)];
            }
        }

        // --- Party size ---

        /// <summary>Health multiplier for regular enemies: +45% per extra player.</summary>
        public static float EnemyHealth(int players) => 1f + 0.45f * Extra(players);

        /// <summary>Bosses are the whole party's target, so they scale harder: +75% per extra player.</summary>
        public static float BossHealth(int players) => 1f + 0.75f * Extra(players);

        /// <summary>Per-hit damage: only +8% per extra player.</summary>
        public static float Damage(int players) => 1f + 0.08f * Extra(players);

        /// <summary>Enemy count per room: +30% per extra player.</summary>
        public static float SpawnCount(int players) => 1f + 0.3f * Extra(players);

        // --- Depth (0 = first floor) ---

        // Ten floors: everything climbs by the floor's tier (half a step a floor, see DungeonDirector.Tier).
        public static float DepthHealth(int depth) => 1f + 0.45f * World.DungeonDirector.Tier(depth);
        public static float DepthDamage(int depth) => 1f + 0.3f * World.DungeonDirector.Tier(depth);

        /// <summary>
        /// How well the dead lead a moving target and how many tricks a boss uses: nothing on the first
        /// floors (learn the telegraphs first), half way by floor 4, fully from floor 7. Floor = depth + 1.
        /// </summary>
        public static float Lead(int depth) => Mathf.Clamp01((depth - 1) / 5f);

        /// <summary>
        /// How long boss tells last, as a fraction of their base: full on the first floors, down to
        /// 72% by floor 9 (play-test 2026-10-20: the circles and lanes were far too easy to step out of).
        /// </summary>
        public static float Tell(int depth) => Mathf.Lerp(1f, 0.72f, Mathf.Clamp01(depth / 8f));

        /// <summary>
        /// A boss's minion count for a party of <paramref name="players"/>: fewer alone (0.7x), more
        /// with four (1.15x), so a soloist is not buried and a party has something to split onto.
        /// </summary>
        public static int SummonCount(int baseCount, int players) => Mathf.Clamp(Mathf.RoundToInt(baseCount * (0.55f + 0.15f * Mathf.Max(1, players))), 1, baseCount * 2);

        /// <summary>Chance a regular enemy spawns as an elite.</summary>
        public static float EliteChance(int depth, int players) => 0.11f + 0.05f * World.DungeonDirector.Tier(depth) + 0.02f * Extra(players) + Level.EliteBonus;

        // --- Rank and file: fewer of them, each a real fight (play-test feedback 2026-10-16) ---

        /// <summary>Room crowds are this fraction of what the spawn points would hold.</summary>
        public const float RoomCrowd = 0.75f;
        /// <summary>Extra toughness and bite for every non-boss enemy.</summary>
        public const float RankHealth = 1.55f, RankDamage = 1.15f;

        /// <summary>
        /// Poise: a single hit worth this fraction of an enemy's max health breaks its
        /// wind-up. Big hits — a shattered firebolt, a Meteor, a bash — reward timing.
        /// </summary>
        public const float PoiseBreakFraction = 0.2f;

        /// <summary>Damage from behind a regular enemy.</summary>
        public const float BackstabMultiplier = 1.35f;
        /// <summary>From behind on a boss (they did not used to take backstabs at all).</summary>
        public const float BossBackstabMultiplier = 1.2f;

        private static int Extra(int players) => Mathf.Max(0, players - 1);
    }
}
