using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The Bestiary (2026-10-20): every kind of dead you have met, with how to beat it. A page of the
    /// pause-menu guide; an entry fills in the first time one is close and on screen (saved on this
    /// machine, never in tests). Unmet ones stay "???" so the page is also a list of what is left to find.
    /// </summary>
    public static class Bestiary
    {
        public struct Entry { public string Type, Name, Text; public bool Boss; }

        /// <summary>Append only: an entry's index is its bit in the save.</summary>
        public static readonly Entry[] Entries =
        {
            new Entry { Type = "EnemyBrain", Name = "Skeleton", Text = "The rank and file. Watch the red !: a heavy blow (!!) sidesteps, a sweep (<!>) backs off, a lunge (>>) dodges sideways." },
            new Entry { Type = "ArcherBrain", Name = "Skeleton Archer", Text = "Shoots, then sidesteps. Close the gap or break line of sight; it leads you deeper down." },
            new Entry { Type = "GhoulBrain", Name = "Ghoul", Text = "Comes in pairs and pounces. Fragile: kill it on the way in." },
            new Entry { Type = "BruteBrain", Name = "Bone Brute", Text = "Huge and slow, with a ground slam. Get behind it: the rune on its back is a weak point." },
            new Entry { Type = "NecromancerBrain", Name = "Necromancer", Text = "Heals and raises the dead. Kill it first; its phylactery is a weak point." },
            new Entry { Type = "WraithBrain", Name = "Wraith", Text = "Ethereal: takes half damage until it is chilled, stunned, marked or consecrated. Dodge its Wail." },
            new Entry { Type = "ShieldbearerBrain", Name = "Shieldbearer", Text = "Its tower shield blocks from the front. Flank it, or kick or bash the shield aside." },
            new Entry { Type = "MimicBrain", Name = "Mimic", Text = "A chest that breathes when you are not looking. It bites at once; it drops a chest's worth." },
            new Entry { Type = "HoarderBrain", Name = "Bone Hoarder", Text = "Runs with a sack of gold and spills it when hit. Escapes if you are slow." },
            new Entry { Type = "GargoyleBrain", Name = "Gargoyle", Text = "Stone until you come close. Dives along a marked line, then lands and takes more damage." },
            new Entry { Type = "PlaguebearerBrain", Name = "Plaguebearer", Text = "Spits poison and bursts into a cloud when it dies. Holy Water cleanses." },
            new Entry { Type = "FrostWightBrain", Name = "Frost Wight", Text = "Frostbite slows you; its Frost Nova can be stunned out. Deeper down it roots." },
            new Entry { Type = "HexerBrain", Name = "Hexer", Text = "Curses whoever it sees: less damage and healing until it dies. Always the first target." },
            new Entry { Type = "MournerBrain", Name = "Mourner", Text = "Moves only while nobody looks. Keep someone facing it; listen for scraping stone." },
            new Entry { Type = "EntlingBrain", Name = "Entling", Text = "Sprints in and roots you until it dies. Kill it on the way in, or have a friend free you." },
            new Entry { Type = "CantorBrain", Name = "Tongueless Cantor", Text = "Its pale circle SILENCES whoever it closes on: no abilities for a few seconds. Step out; Holy Water lifts it." },
            new Entry { Type = "BonePiperBrain", Name = "Bone Piper", Text = "Plays tunes of soulfire: Rose, Scissors, Seedpods, March. Stands still to play; its bag is a weak point." },
            new Entry { Type = "OrbweaverBrain", Name = "Orbweaver", Text = "Conjures orbs that fire spirals, gapped rings and aimed bursts. Kill it and its orbs die." },
            new Entry { Type = "WardenBrain", Name = "The Wardens", Text = "A guardian at the end of each act's first floor. Each act's Warden has its own trick.", Boss = true },
            new Entry { Type = "GravewardenBrain", Name = "The Gravewarden", Text = "Shield in front; charge it into a wall to daze it. Jump its shockwaves.", Boss = true },
            new Entry { Type = "TideLichBrain", Name = "The Tide Lich", Text = "Volleys, pools and a whirlpool. Its spiral reverses: keep moving.", Boss = true },
            new Entry { Type = "HollowKingBrain", Name = "The Hollow King", Text = "Cleaves and slams; share his Judgement by standing together in the circle.", Boss = true },
            new Entry { Type = "CardinalBrain", Name = "The Pale Cardinal", Text = "Judgement pillars land where you are going. Break the choir's wards.", Boss = true },
            new Entry { Type = "UnsungBrain", Name = "The Unsung", Text = "What waits on the tenth floor. Every Seal you broke weakens it.", Boss = true },
            new Entry { Type = "ChoirAcolyteBrain", Name = "Choir Acolyte", Text = "Hangs back and sings: a golden circle fills, then every one of the dead in it is SHIELDED. Stun it or hit it hard mid-hymn to break the song." },
            new Entry { Type = "AbyssEyeBrain", Name = "Abyssal Eye", Text = "Floats and stares. When it locks on, a violet line is drawn on the floor: step off it before the beam. The eye is its head; the iris a weak point." },
            new Entry { Type = "Tallyman", Name = "The Tallyman", Text = "Comes for those who linger on a floor. Cannot be fought: outrun him (holy water holds him a moment) and seal yourselves in with the guardian.", Boss = true },
        };

        private const string Key = "db.bestiary";
        private static int? mask;

        public static int Mask => mask ??= Progression.Cosmetics.TestMode ? 0 : PlayerPrefs.GetInt(Key, 0);
        public static bool Seen(int i) => (Mask & (1 << i)) != 0;
        public static int SeenCount { get { int n = 0; for (int i = 0; i < Entries.Length; i++) if (Seen(i)) n++; return n; } }

        /// <summary>Note a creature met. True the first time.</summary>
        public static bool See(Enemies.EnemyBrain brain) => brain != null && See(brain.GetType().Name);

        /// <summary>Note a creature met by its type name (for the few that are not EnemyBrains).</summary>
        public static bool See(string type)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Type != type) continue;
                if (Seen(i)) return false;
                mask = Mask | (1 << i);
                if (!Progression.Cosmetics.TestMode) { PlayerPrefs.SetInt(Key, mask.Value); PlayerPrefs.Save(); }
                GameHud.Feed("New in your Bestiary: " + Entries[i].Name + "  (Esc > Guide)", new Color(0.85f, 0.8f, 0.6f));
                return true;
            }
            return false;
        }
    }
}
