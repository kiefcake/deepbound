namespace Deepbound.Classes
{
    /// <summary>
    /// The two directions each class weapon can be taken at the anvil. Chosen once, at
    /// <see cref="PathLevel"/>; the path's mastery wakes at <see cref="MasteryLevel"/>.
    /// Paths are numbered 1 and 2 (0 = not chosen yet). The effects live in each kit,
    /// gated by <see cref="Progression.PlayerProgress.OnPath"/> and <c>Mastered</c>.
    /// </summary>
    public static class WeaponPaths
    {
        public const int PathLevel = 2, MasteryLevel = 4;

        public static string Name(ClassId id, int path)
        {
            switch (id)
            {
                case ClassId.Mage: return path == 1 ? "Pyromancer" : "Cryomancer";
                case ClassId.Cleric: return path == 1 ? "Zealot" : "Shepherd";
                case ClassId.Rogue: return path == 1 ? "Assassin" : "Duelist";
                case ClassId.Ranger: return path == 1 ? "Sharpshooter" : "Trapper";
                case ClassId.Flagellant: return path == 1 ? "Fanatic" : "Penitent";
                case ClassId.PlagueDoctor: return path == 1 ? "Pestilence" : "Physician";
                default: return path == 1 ? "Cleave" : "Riposte";
            }
        }

        public static string Describe(ClassId id, int path)
        {
            switch (id)
            {
                case ClassId.Mage: return path == 1
                    ? "Firebolts burst on impact, hitting everything within 2 m for half damage."
                    : "Frostbolts pierce through every enemy in a line, and chill for 6 s.";
                case ClassId.Cleric: return path == 1
                    ? "Smite hits 35% harder, and each hit heals the most wounded ally nearby for 40% of it."
                    : "Mending Beam heals 40% more; healing past full builds a shield of up to 40.";
                case ClassId.Rogue: return path == 1
                    ? "Ambush hits from behind deal x2.0 instead of x1.5 (x1.6 instead of x1.3 on bosses)."
                    : "Stabs strike two enemies and hit 25% harder from the front; every 5th stab in a row on one foe is a FLOURISH (x2.5, staggers).";
                case ClassId.Flagellant: return path == 1
                    ? "Crashes hit 30% harder and burst half again as wide."
                    : "Penance heals twice as much from every hit.";
                case ClassId.PlagueDoctor: return path == 1
                    ? "Vials splash 40% wider, and blight burns 40% hotter."
                    : "Tonics heal 40% more, shield for 20, and come back sooner.";
                case ClassId.Ranger: return path == 1
                    ? "Full-draw arrows deal 40% more damage."
                    : "Snares burst 40% wider and come back 40% sooner.";
                default: return path == 1
                    ? "Swings sweep a far wider arc (160°) and reach further."
                    : "Your counter after a block becomes a riposte: x2.2 damage instead of x1.4.";
            }
        }

        public static string Mastery(ClassId id, int path)
        {
            switch (id)
            {
                case ClassId.Mage: return path == 1
                    ? "The burst grows to 3 m and deals three quarters damage."
                    : "Shatter deals x2.5 instead of x1.75.";
                case ClassId.Cleric: return path == 1
                    ? "Consecrated ground burns twice as hot."
                    : "Divine Aegis is 60% stronger and recharges 30% faster.";
                case ClassId.Rogue: return path == 1
                    ? "Killing an enemy from behind resets Shadowstep."
                    : "A flourish every 4th stab, and Fan of Knives throws five knives.";
                case ClassId.Flagellant: return path == 1
                    ? "Crashes stun everything they burst on."
                    : "Scourge heals allies within 6 m for the health it costs you.";
                case ClassId.PlagueDoctor: return path == 1
                    ? "A blighted death spreads it to everything near, not just the nearest."
                    : "Tonics also cleanse afflictions.";
                case ClassId.Ranger: return path == 1
                    ? "A full-draw hit marks its target at once."
                    : "Volley rains nine waves over a wider circle.";
                default: return path == 1
                    ? "Every third swing chills what it hits (sets up Shatter)."
                    : "A riposte also stuns its targets for 1.2 s.";
            }
        }
    }
}
