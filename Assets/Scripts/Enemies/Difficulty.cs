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

        // Party scaling (reviewed 2026-11-01, "look over multiplayer scaling and improve"). A party of N deals about N
        // times the damage, so the dead must hold about N times the health in all - but as MORE bodies rather than
        // sponges, so a party has a room to split, and each kill still lands. Four players: each enemy x2.05 health,
        // x2.2 as many of them = x4.5 the room's health for x4 the damage (the extra half pays for revives and focus).

        /// <summary>Health multiplier for regular enemies: +35% per extra player (was +45%: spongy in a four).</summary>
        public static float EnemyHealth(int players) => 1f + 0.35f * Extra(players);

        /// <summary>Boss health x5 (play-test 2026-11-01: they went down far too fast).</summary>
        public const float BossToughness = 5f;

        /// <summary>
        /// Bosses are the whole party's target, so they scale nearly one for one: +90% per extra player (was +85%, so a
        /// four killed a guardian faster than a soloist did), all x <see cref="BossToughness"/>.
        /// </summary>
        public static float BossHealth(int players) => BossToughness * (1f + 0.9f * Extra(players));

        /// <summary>The health multiplier a fight was scaled with, boss or rank and file (for rescaling on joins and leaves).</summary>
        public static float PartyHealth(int players, bool boss, int depth = 0) => boss ? BossHealth(players) * BossDepth(players, depth) : EnemyHealth(players);

        /// <summary>
        /// A party's guardians harden with depth, on top of <see cref="DepthHealth"/> (2026-11-04, ten-floor run with four
        /// bots in full kit: guardians from floor 6 fell in 30 s). +18% a floor for a full four, a third of that per extra
        /// player, and nothing alone: a soloist's kit was not what this run measured. (First +25%, while Thermal Shock was
        /// doing a third of a party's guardian damage; see <see cref="Classes.Combos.ShockBossFraction"/>.)
        /// </summary>
        public static float BossDepth(int players, int depth) => 1f + 0.18f * Mathf.Max(0, depth) * Mathf.Min(Extra(players), 3) / 3f;

        /// <summary>
        /// Co-op loot (2026-10-23, "figure out the game loop, solo or a four-man run"). Gold and embers are
        /// split across the party, but drops barely grew with it: four players each got ~40% of a solo
        /// player's income. Kill loot is rolled this many times (enemies already come <see cref="SpawnCount"/>
        /// more often, so this makes up the rest); a fixed source (chest, urn, boss, champion, mimic, hoarder)
        /// rolls once per player. Each player ends up with about a solo player's share.
        /// </summary>
        public static float KillLootShare(int players) => Mathf.Max(1, players) * SpawnCount(1) / SpawnCount(players);
        public static float FixedLootShare(int players) => Mathf.Max(1, players);
        public static int Party => Mathf.Max(1, Player.PlayerLife.All.Count);

        /// <summary>
        /// Per-hit damage: +15% per extra player (was +8%). Party autoplay run 27 (Mage + Knight, 2026-11-01): from floor 3
        /// the pair went whole floors without a close call, and the Mage took half what a soloist takes, because the
        /// Knight held the rage. Hits stay survivable, but a party now has to mind them. +22% (was +15%, 2026-11-04: four bots in full kit that never
        /// dodge came through floors 2-9 with 9 close calls between them): x1.66 in a four.
        /// </summary>
        public static float Damage(int players) => 1f + 0.22f * Extra(players);

        /// <summary>
        /// A party's share of embers (2026-11-04: in a four the first weapon reached +10 on floor 5 and every sink was full by
        /// floor 8, with 1,000+ embers left over). Alone it stays whole.
        /// </summary>
        public const float PartyEmbers = 0.8f;

        /// <summary>
        /// Enemy count per room: a solo player faces 80% of the crowd, +30% per extra player (2026-10-21:
        /// the autoplay run killed 100 on one solo floor and could not reach the guardian in 11 minutes).
        /// </summary>
        public static float SpawnCount(int players) => 0.8f + 0.32f * Extra(players);

        // --- Depth (0 = first floor) ---

        // Ten floors: everything climbs by the floor's tier (half a step a floor, see DungeonDirector.Tier).
        // Past the second act it climbs faster (2026-10-22, autoplay: a +10 Knight with 16 relics cleared floors 9-10 in
        // four to five minutes and the Unsung in under two; the first six floors are unchanged).
        public static float DepthHealth(int depth) { float t = World.DungeonDirector.Tier(depth); return 1f + 0.45f * t + 0.16f * Mathf.Pow(Mathf.Max(0f, t - 2.5f), 2f); }
        // Damage climbs a little harder (2026-10-23, autoplay run 12: with the boss heal fixed, a bot that cannot dodge came
        // through floors 3-10 with 0-7 close calls a floor): +6% by floor 5, +14% by floor 10.
        public static float DepthDamage(int depth) { float t = World.DungeonDirector.Tier(depth); return 1f + 0.35f * t + 0.14f * Mathf.Pow(Mathf.Max(0f, t - 2.5f), 2f); }

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
