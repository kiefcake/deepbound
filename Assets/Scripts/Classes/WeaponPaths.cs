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

        public const int Count = 3;

        public static string Name(ClassId id, int path)
        {
            if (path == PathTriads.Path) return PathTriads.Name(id);
            switch (id)
            {
                case ClassId.Mage: return path == 1 ? "Inferno" : "Stormcaller";
                case ClassId.Cleric: return path == 1 ? "Zealot" : "Shepherd";
                case ClassId.Rogue: return path == 1 ? "Assassin" : "Duelist";
                case ClassId.Ranger: return path == 1 ? "Splitshot" : "Blasthead";
                case ClassId.Flagellant: return path == 1 ? "Fanatic" : "Penitent";
                case ClassId.PlagueDoctor: return path == 1 ? "Pestilence" : "Physician";
                default: return path == 1 ? "Greatblade" : "Spiked Shield";
            }
        }

        public static string Describe(ClassId id, int path)
        {
            if (path == PathTriads.Path) return PathTriads.Describe(id);
            switch (id)
            {
                case ClassId.Mage: return path == 1
                    ? "The staff breathes fire: hold LMB for a short roaring flame stream instead of bolts. It scorches all it washes over."
                    : "The staff calls lightning: LMB strikes the enemy you aim at at once, then forks to two more nearby.";
                case ClassId.Cleric: return path == 1
                    ? "Smite becomes a HAMMER OF LIGHT: slower and heavier, it bursts where it lands, and mends the most wounded ally nearby."
                    : "Smites pass through your allies and heal each one they touch, and fly on through the dead.";
                case ClassId.Rogue: return path == 1
                    ? "Stabs from behind deal x2.0 instead of x1.5 and leave the foe bleeding."
                    : "Stabs strike two enemies and hit 25% harder from the front; every 5th stab in a row on one foe is a FLOURISH (x2.5, staggers).";
                case ClassId.Flagellant: return path == 1
                    ? "The crash comes every OTHER swing, and it leaves the ground burning."
                    : "The flail whirls all round you (at 70%), and Penance heals twice as much.";
                case ClassId.PlagueDoctor: return path == 1
                    ? "Vials leave a puddle of plague where they burst that blights anything walking through it."
                    : "Vials splash your allies too, and heal what they touch.";
                case ClassId.Ranger: return path == 1
                    ? "A full draw looses THREE arrows in a fan."
                    : "Arrows have blasting heads: they burst where they strike, scorching everything near.";
                default: return path == 1
                    ? "A two-handed sweep (160°, longer reach). Every third swing in a chain, the overhead, sends a SLASH WAVE rolling ahead."
                    : "Spikes on the shield: melee blows you block hurt the one who struck, and the counter after a block is a riposte (x2.2).";
            }
        }

        public static string Mastery(ClassId id, int path)
        {
            if (path == PathTriads.Path) return PathTriads.Mastery(id);
            switch (id)
            {
                case ClassId.Mage: return path == 1
                    ? "Enemies kept in the flame for over a second catch fire and burn on."
                    : "The fork leaps to four, and every strike jolts (a short stun).";
                case ClassId.Cleric: return path == 1
                    ? "The hammer's burst stuns."
                    : "Mending Beam heals 40% more, and healing past full builds a shield.";
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
                    ? "The middle arrow of the fan marks its target at once."
                    : "Full-draw blasts are twice as wide and stagger what they hit.";
                default: return path == 1
                    ? "The slash wave runs further and chills what it cuts (sets up Shatter)."
                    : "Blocked blows also stun the one who struck (1 s), and a riposte stuns too.";
            }
        }
    }
}
