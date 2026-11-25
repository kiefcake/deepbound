using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.UI
{
    public static class Bestiary
    {
        public struct Entry { public string Type, Name, Text; public bool Boss; }

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
            new Entry { Type = "HexerBrain", Name = "Hexer", Text = "WEAKENS whoever it sees: 40% less damage and half the healing until it dies. Always the first target." },
            new Entry { Type = "MournerBrain", Name = "Mourner", Text = "Moves only while nobody looks. Keep someone facing it; listen for scraping stone." },
            new Entry { Type = "EntlingBrain", Name = "Entling", Text = "Sprints in and roots you until it dies. Kill it on the way in, or have a friend free you." },
            new Entry { Type = "RimeMagusBrain", Name = "Rime Magus", Text = "Calls a BLIZZARD that follows you about: inside it you are slowed and bitten by the cold. Outwalk it or kill the Magus - the storm dies with it. A stun scatters it." },
            new Entry { Type = "InkLampreyBrain", Name = "Ink Lamprey", Text = "Spits ink where you are going (a dark circle): under it you are BLINDED - no map, no health bars, a small clear hole to see through - and the ink pools a while. Sidestep; Holy Water washes your eyes." },
            new Entry { Type = "WitheringHagBrain", Name = "Withering Hag", Text = "Fixes her EVIL EYE down a lane; caught in it when it fills you are CURSED: +40% damage taken, until she dies. Step out of the lane." },
            new Entry { Type = "BellringerBrain", Name = "Bell-ringer", Text = "A sexton with a bell on its back. It heaves and RINGS: a low pale ring rolls out and SILENCES whoever it passes under. Jump it." },
            new Entry { Type = "CantorBrain", Name = "Tongueless Cantor", Text = "Its pale circle SILENCES whoever it closes on: no abilities for a few seconds. Step out; Holy Water lifts it." },
            new Entry { Type = "BonePiperBrain", Name = "Bone Piper", Text = "Plays tunes of soulfire: Rose, Scissors, Seedpods, March. Stands still to play; its bag is a weak point." },
            new Entry { Type = "OrbweaverBrain", Name = "Orbweaver", Text = "Conjures orbs that fire spirals, gapped rings and aimed bursts. Kill it and its orbs die." },
            new Entry { Type = "WardenBrain", Name = "The Wardens", Text = "A guardian at the end of each act's first floor. Each act's Warden has its own trick.", Boss = true },
            new Entry { Type = "GravewardenBrain", Name = "The Gravewarden", Text = "Shield in front; charge it into a wall to daze it. Jump its shockwaves.", Boss = true },
            new Entry { Type = "TideLichBrain", Name = "The Tide Lich", Text = "Volleys, pools and a whirlpool. Its spiral reverses: keep moving.", Boss = true },
            new Entry { Type = "HollowKingBrain", Name = "The Hollow King", Text = "Cleaves and slams; share his Judgement by standing together in the circle.", Boss = true },
            new Entry { Type = "CardinalBrain", Name = "The Pale Cardinal", Text = "Judgement pillars land where you are going. Break the choir's wards.", Boss = true },
            new Entry { Type = "UnsungBrain", Name = "The Unsung", Text = "What waits on the tenth floor. Every Seal you broke weakens it.", Boss = true },
            new Entry { Type = "MotherMarrowBrain", Name = "Mother Marrow", Text = "A lair keeper. She knits herself a cocoon no blade bites while her knitters stand: cut them down. Step off her lash lines.", Boss = true },
            new Entry { Type = "LanternFisherBrain", Name = "The Lantern Fisher", Text = "A lair keeper under black water. Only its lure shows; when it surfaces, strike. Hooked, hurt it hard or be reeled in.", Boss = true },
            new Entry { Type = "TwiceCrownedBrain", Name = "The Twice-Crowned", Text = "A lair keeper: a king and his shadow. Only the one wearing the crown can be hurt, and the crown changes heads.", Boss = true },
            new Entry { Type = "MirrorNunBrain", Name = "Mother Veyle", Text = "A lair keeper among glass copies of herself. Strike a copy and it bursts on you: find the one that casts a shadow.", Boss = true },
            new Entry { Type = "VharothBrain", Name = "Vharoth", Text = "Lord of the Cinder Pit. He strides, kindles imps, races the walls as an ember, then looses seekers that hunt you: his turns repeat.", Boss = true },
            new Entry { Type = "ChoirAcolyteBrain", Name = "Choir Acolyte", Text = "Hangs back and sings: a golden circle fills, then every one of the dead in it is SHIELDED. Stun it or hit it hard mid-hymn to break the song." },
            new Entry { Type = "AbyssEyeBrain", Name = "Abyssal Eye", Text = "Floats and stares. When it locks on, a violet line is drawn on the floor: step off it before the beam. The eye is its head; the iris a weak point." },
            new Entry { Type = "Tallyman", Name = "The Tallyman", Text = "Comes for those who linger on a floor. Cannot be fought: outrun him (holy water holds him a moment) and seal yourselves in with the guardian.", Boss = true },
        };

        private const string Key = "db.bestiary", Key2 = "db.bestiary2";

        public const int StudyKills = 5;

        private static Dictionary<string, int> met;

        private static Dictionary<string, int> Met
        {
            get
            {
                if (met != null) return met;
                met = new Dictionary<string, int>();
                if (Progression.Cosmetics.TestMode) return met;
                try
                {
                    string saved = PlayerPrefs.GetString(Key2, null);
                    if (saved == null)
                    {
                        int bits = PlayerPrefs.GetInt(Key, 0);
                        for (int i = 0; i < Entries.Length && i < 32; i++) if ((bits & (1 << i)) != 0) met[Entries[i].Type] = 0;
                        Save();
                    }
                    else foreach (var pair in saved.Split('|'))
                    {
                        int c = pair.IndexOf(':');
                        if (c > 0 && int.TryParse(pair.Substring(c + 1), out int n)) met[pair.Substring(0, c)] = n;
                    }
                }
                catch { }
                return met;
            }
        }

        private static void Save()
        {
            if (Progression.Cosmetics.TestMode || met == null) return;
            var parts = new List<string>();
            foreach (var kv in met) parts.Add(kv.Key + ":" + kv.Value);
            try { PlayerPrefs.SetString(Key2, string.Join("|", parts)); PlayerPrefs.Save(); } catch { }
        }

        public static bool Seen(int i) => Met.ContainsKey(Entries[i].Type);
        public static int Kills(int i) => Met.TryGetValue(Entries[i].Type, out int n) ? n : 0;
        public static int SeenCount { get { int n = 0; for (int i = 0; i < Entries.Length; i++) if (Seen(i)) n++; return n; } }

        private static bool Unbeatable(int i) => Entries[i].Type == "Tallyman";

        public static int Stage(int i)
        {
            if (!Seen(i)) return 0;
            if (Unbeatable(i)) return 3;
            int k = Kills(i);
            return k <= 0 ? 1 : k >= (Entries[i].Boss ? 1 : StudyKills) ? 3 : 2;
        }

        public static string TextFor(int i)
        {
            var e = Entries[i];
            switch (Stage(i))
            {
                case 0: return e.Boss ? "A guardian you have not faced." : "Not yet met.";
                case 1: return "Not yet understood. Defeat one to learn how it fights.";
                case 2:
                {
                    int dot = e.Text.IndexOf(". ");
                    string first = dot > 0 ? e.Text.Substring(0, dot + 1) : e.Text;
                    return first + (first.Length < e.Text.Length ? "  [" + Kills(i) + " / " + StudyKills + " defeated to learn more]" : "");
                }
                default: return e.Text + (Unbeatable(i) ? "" : "  [" + Kills(i) + " defeated]");
            }
        }

        public static bool See(Enemies.EnemyBrain brain) => brain != null && See(brain.GetType().Name);

        public static bool See(string type)
        {
            int i = IndexOf(type);
            if (i < 0 || Met.ContainsKey(type)) return false;
            Met[type] = 0;
            Save();
            GameHud.Feed("New in your Bestiary: " + Entries[i].Name + "  (Esc > Guide)", new Color(0.85f, 0.8f, 0.6f));
            return true;
        }

        public static void Killed(string type)
        {
            int i = IndexOf(type);
            if (i < 0) return;
            int before = Stage(i);
            Met[type] = (Met.TryGetValue(type, out int n) ? n : 0) + 1;
            Save();
            int after = Stage(i);
            if (after > before && after >= 2)
                GameHud.Feed((after == 3 ? "Bestiary entry complete: " : "Bestiary entry updated: ") + Entries[i].Name, new Color(0.85f, 0.8f, 0.6f));
        }

        public static int IndexOf(string type)
        {
            for (int i = 0; i < Entries.Length; i++) if (Entries[i].Type == type) return i;
            return -1;
        }
    }
}
