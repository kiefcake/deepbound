using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>APPEND ONLY: synced as a byte (the pouch's fifth slot, loot drops).</summary>
    public enum RareItem : byte { None = 0, Ironbark = 1, Bloodfury = 2, GraveStillness = 3, PhoenixTome = 4, StormTome = 5, SanctuaryTome = 6 }

    /// <summary>
    /// The pouch's fifth slot (2026-10-24, the user: "a really tanky potion and some kind of dps potion... summoning
    /// tomes, a phoenix with a scroll"). One rare thing at a time, found far more than bought, used with 5: a draught
    /// for a moment that matters, or a tome that calls something to fight beside you. A full slot leaves a new one
    /// on the floor for a teammate (or for when you have used yours).
    /// </summary>
    public static class RareItems
    {
        public const int Count = 6;
        public const float IronbarkSeconds = 8f, IronbarkTaken = 0.5f, IronbarkShield = 0.25f;
        public const float BloodfurySeconds = 8f, BloodfuryDamage = 1.4f, BloodfuryAttackTime = 0.8f;
        public const float StasisSeconds = 3f, StasisGrace = 1f;
        public const float PhoenixSeconds = 30f;
        public const float StormSeconds = 10f, StormEvery = 0.6f, StormRange = 14f, StormDamage = 24f, StormStun = 0.5f;
        public const float SanctuarySeconds = 10f, SanctuaryRadius = 6f, SanctuaryTaken = 0.65f, SanctuaryHealPerSecond = 0.03f;

        public static bool IsTome(RareItem r) => r == RareItem.PhoenixTome || r == RareItem.StormTome || r == RareItem.SanctuaryTome;

        public static string Name(RareItem r)
        {
            switch (r)
            {
                case RareItem.Ironbark: return "Ironbark Draught";
                case RareItem.Bloodfury: return "Bloodfury Draught";
                case RareItem.GraveStillness: return "Draught of Grave Stillness";
                case RareItem.PhoenixTome: return "Tome of the Phoenix";
                case RareItem.StormTome: return "Tome of the Storm";
                case RareItem.SanctuaryTome: return "Tome of Sanctuary";
                default: return "";
            }
        }

        /// <summary>Two-word label for the belt.</summary>
        public static string Short(RareItem r)
        {
            switch (r)
            {
                case RareItem.Ironbark: return "Ironbark";
                case RareItem.Bloodfury: return "Bloodfury";
                case RareItem.GraveStillness: return "Stillness";
                case RareItem.PhoenixTome: return "Phoenix";
                case RareItem.StormTome: return "Storm";
                case RareItem.SanctuaryTome: return "Sanctuary";
                default: return "empty";
            }
        }

        public static string Describe(RareItem r)
        {
            switch (r)
            {
                case RareItem.Ironbark: return "For 8 s take half damage, gain a shield of a quarter of your health, and shrug off knockback, roots, frostbite and mania.";
                case RareItem.Bloodfury: return "For 8 s deal 40% more damage and attack 25% faster.";
                case RareItem.GraveStillness: return "Turn to stone for 3 s: nothing can hurt you and the dead forget you, but you cannot move or act.";
                case RareItem.PhoenixTome: return "Call a phoenix for 30 s. It circles you and dives at the dead, scorching them; struck down, it bursts into flame and is reborn.";
                case RareItem.StormTome: return "For 10 s lightning strikes the dead within 14 m of you, again and again, stunning each it hits for a moment (Exposed).";
                case RareItem.SanctuaryTome: return "A holy circle 12 m wide for 10 s: you and your allies inside take 35% less damage and mend 3% a second.";
                default: return "";
            }
        }

        public static Color Color(RareItem r)
        {
            switch (r)
            {
                case RareItem.Ironbark: return new Color(0.72f, 0.55f, 0.35f);
                case RareItem.Bloodfury: return new Color(1f, 0.25f, 0.2f);
                case RareItem.GraveStillness: return new Color(0.78f, 0.8f, 0.86f);
                case RareItem.PhoenixTome: return new Color(1f, 0.6f, 0.2f);
                case RareItem.StormTome: return new Color(0.6f, 0.75f, 1f);
                case RareItem.SanctuaryTome: return new Color(1f, 0.92f, 0.6f);
                default: return UnityEngine.Color.white;
            }
        }

        public static RareItem Random() => (RareItem)UnityEngine.Random.Range(1, Count + 1);
        public static RareItem FromSeed(System.Random rng) => (RareItem)(1 + rng.Next(Count));

        /// <summary>Silas keeps one under the counter each visit, priced for the depth.</summary>
        public static int Price(int depth) => 150 + 40 * depth;
    }
}
