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
        public enum Deed { Gravewarden = 0, TideLich = 1, HollowKing = 2, Cardinal = 3, Unsung = 4, SawAbyss = 5, FirstRun = 6 }

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
