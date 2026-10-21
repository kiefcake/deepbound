using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// What this player has done, ever, on this machine: which guardians they have felled, whether
    /// they have seen the Abyss, how many descents ended badly. The camp's people remember it and
    /// say so (UI.CampChatter). PlayerPrefs, never touched by tests.
    /// </summary>
    public static class Deeds
    {
        public enum Deed { Gravewarden = 0, TideLich = 1, HollowKing = 2, Cardinal = 3, Unsung = 4, SawAbyss = 5, FirstRun = 6, Warden = 7,
            KeptOath = 8, FoundCat = 9, RepelledTallyman = 10, OutranTallyman = 11, Torchbearer = 12, DailyFloor = 13 }

        /// <summary>The Hall of Records' list (2026-10-20): every deed with the name it is carved under.</summary>
        public static readonly (Deed deed, string name)[] Listed =
        {
            (Deed.Warden, "Felled a Warden"), (Deed.Gravewarden, "The Gravewarden"), (Deed.TideLich, "The Tide Lich"),
            (Deed.HollowKing, "The Hollow King"), (Deed.Cardinal, "The Pale Cardinal"), (Deed.SawAbyss, "Saw the Abyss"),
            (Deed.Unsung, "The Unsung"), (Deed.KeptOath, "Kept an oath"), (Deed.FoundCat, "Found the cat below"),
            (Deed.Torchbearer, "Carried the light"), (Deed.RepelledTallyman, "Held back the Tallyman"), (Deed.OutranTallyman, "Outran the Tallyman"),
            (Deed.DailyFloor, "A daily descent"), (Deed.FirstRun, "A first descent"),
        };

        public static int ListedDone { get { int n = 0; foreach (var (d, _) in Listed) if (Has(d)) n++; return n; } }

        /// <summary>Record a deed and say so in the feed the first time (for the newer deeds).</summary>
        public static void Earn(Deed d, string name)
        {
            if (Add(d) && !TestMode) UI.GameHud.Feed("DEED - " + name + "  (Hall of Records)", new Color(0.9f, 0.8f, 0.55f));
        }

        private const string BitsKey = "deepbound.deeds", WipesKey = "deepbound.wipes", WinsKey = "deepbound.wins";
        public static bool TestMode;
        private static int? bits, wipes, wins;

        private static int Bits { get => bits ??= Load(BitsKey); set { bits = value; Save(BitsKey, value); } }
        public static int Wipes { get => wipes ??= Load(WipesKey); private set { wipes = value; Save(WipesKey, value); } }
        public static int Wins { get => wins ??= Load(WinsKey); private set { wins = value; Save(WinsKey, value); } }

        public static bool Has(Deed d) => (Bits & (1 << (int)d)) != 0;

        /// <summary>Record a deed. True if it is new.</summary>
        public static bool Add(Deed d)
        {
            if (Has(d)) return false;
            Bits |= 1 << (int)d;
            return true;
        }

        public static void AddWipe() => Wipes++;
        public static void AddWin() => Wins++;

        public static int BossesFelled
        {
            get { int n = 0; for (int i = 0; i <= 4; i++) if (Has((Deed)i)) n++; return n; }
        }

        private static int Load(string key)
        {
            if (TestMode) return 0;
            try { return PlayerPrefs.GetInt(key, 0); } catch { return 0; }
        }

        private static void Save(string key, int value)
        {
            if (TestMode) return;
            try { PlayerPrefs.SetInt(key, value); PlayerPrefs.Save(); } catch { }
        }
    }
}
