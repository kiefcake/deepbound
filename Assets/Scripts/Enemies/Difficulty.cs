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
        };

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

        public static float DepthHealth(int depth) => 1f + 0.25f * depth;
        public static float DepthDamage(int depth) => 1f + 0.15f * depth;

        /// <summary>Chance a regular enemy spawns as an elite.</summary>
        public static float EliteChance(int depth, int players) => 0.07f + 0.04f * depth + 0.02f * Extra(players) + Level.EliteBonus;

        /// <summary>
        /// Poise: a single hit worth this fraction of an enemy's max health breaks its
        /// wind-up. Big hits — a shattered firebolt, a Meteor, a bash — reward timing.
        /// </summary>
        public const float PoiseBreakFraction = 0.2f;

        /// <summary>Damage from behind a regular enemy.</summary>
        public const float BackstabMultiplier = 1.35f;

        private static int Extra(int players) => Mathf.Max(0, players - 1);
    }
}
