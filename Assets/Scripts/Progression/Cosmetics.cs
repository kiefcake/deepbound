using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// Meta-progression that outlives a run: <b>Renown</b>, earned at the end of every run,
    /// spent at the camp tailor on cloak dyes. Nothing here changes power; it is how you
    /// show where you have been. Some dyes also need a boss felled at least once.
    ///
    /// <para>
    /// Saved per machine in PlayerPrefs (the client owns its own wardrobe); the equipped
    /// dye is synced to the party through <see cref="Player.NetworkPlayer.CloakDye"/>.
    /// </para>
    /// </summary>
    public static class Cosmetics
    {
        public struct Dye
        {
            public string Name;
            public Color Cloak, Trim;
            public int Cost;
            public int NeedsBoss;   // -1 = none; otherwise the depth of the boss that must have been felled
        }

        /// <summary>Index 0 is "your class's own colours" (no override). Append only: indices are saved and synced.</summary>
        public static readonly Dye[] Dyes =
        {
            new Dye { Name = "Class Colours", Cost = 0, NeedsBoss = -1 },
            new Dye { Name = "Ashen Grey", Cloak = new Color(0.3f, 0.29f, 0.28f), Trim = new Color(0.7f, 0.7f, 0.72f), Cost = 40, NeedsBoss = -1 },
            new Dye { Name = "Blood Crimson", Cloak = new Color(0.42f, 0.04f, 0.05f), Trim = new Color(0.8f, 0.6f, 0.25f), Cost = 60, NeedsBoss = -1 },
            new Dye { Name = "Midnight", Cloak = new Color(0.05f, 0.07f, 0.18f), Trim = new Color(0.65f, 0.68f, 0.75f), Cost = 80, NeedsBoss = -1 },
            new Dye { Name = "Royal Violet", Cloak = new Color(0.26f, 0.07f, 0.36f), Trim = new Color(0.85f, 0.65f, 0.25f), Cost = 100, NeedsBoss = -1 },
            new Dye { Name = "Bone White", Cloak = new Color(0.82f, 0.79f, 0.7f), Trim = new Color(0.5f, 0.06f, 0.06f), Cost = 120, NeedsBoss = -1 },
            new Dye { Name = "Raven Black", Cloak = new Color(0.035f, 0.035f, 0.045f), Trim = new Color(0.55f, 0.05f, 0.06f), Cost = 150, NeedsBoss = -1 },
            new Dye { Name = "Warden's Ember", Cloak = new Color(0.32f, 0.11f, 0.04f), Trim = new Color(1f, 0.5f, 0.15f), Cost = 120, NeedsBoss = 0 },
            new Dye { Name = "Drowned Teal", Cloak = new Color(0.04f, 0.2f, 0.2f), Trim = new Color(0.35f, 0.85f, 0.7f), Cost = 160, NeedsBoss = 1 },
            new Dye { Name = "Hollow Gold", Cloak = new Color(0.45f, 0.33f, 0.08f), Trim = new Color(0.45f, 0.7f, 1f), Cost = 220, NeedsBoss = 2 },
            new Dye { Name = "Cardinal's Scarlet", Cloak = new Color(0.55f, 0.02f, 0.04f), Trim = new Color(1f, 0.82f, 0.4f), Cost = 300, NeedsBoss = 3 },
        };

        private const string KeyRenown = "db.renown", KeyOwned = "db.dyes", KeyEquipped = "db.cloak", KeyBosses = "db.bosses", KeyLastRun = "db.lastRun", KeyRuns = "db.runs";

        /// <summary>Set by the smoke/screenshot tests: nothing is read from or written to the real save.</summary>
        public static bool TestMode;

        public static int Renown => TestMode ? 0 : PlayerPrefs.GetInt(KeyRenown, 0);
        public static int RunsFinished => PlayerPrefs.GetInt(KeyRuns, 0);
        public static int Equipped => TestMode ? 0 : Mathf.Clamp(PlayerPrefs.GetInt(KeyEquipped, 0), 0, Dyes.Length - 1);
        private static int BossMask => PlayerPrefs.GetInt(KeyBosses, 0);

        public static bool Owns(int dye) => dye == 0 || (PlayerPrefs.GetInt(KeyOwned, 0) & (1 << dye)) != 0;
        public static bool BossFelled(int depth) => (BossMask & (1 << depth)) != 0;
        public static bool Unlocked(int dye) => Dyes[dye].NeedsBoss < 0 || BossFelled(Dyes[dye].NeedsBoss);

        /// <summary>The colours to dress a cloak in, or null for the class's own.</summary>
        public static Dye? Get(int dye) => dye > 0 && dye < Dyes.Length ? Dyes[dye] : (Dye?)null;

        public static bool Buy(int dye)
        {
            if (TestMode || Owns(dye) || !Unlocked(dye) || Renown < Dyes[dye].Cost) return false;
            PlayerPrefs.SetInt(KeyRenown, Renown - Dyes[dye].Cost);
            PlayerPrefs.SetInt(KeyOwned, PlayerPrefs.GetInt(KeyOwned, 0) | (1 << dye));
            PlayerPrefs.Save();
            return true;
        }

        public static bool Equip(int dye)
        {
            if (TestMode || !Owns(dye)) return false;
            PlayerPrefs.SetInt(KeyEquipped, dye);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>What a run is worth: bosses, kills and combos, a big bonus for clearing it, scaled by difficulty.</summary>
        public static int RenownFor(int bossesFelled, int kills, int combos, bool cleared, int difficulty)
        {
            float r = bossesFelled * 25 + kills / 4 + combos / 5 + (cleared ? 100 : 0);
            return Mathf.RoundToInt(r * (1f + 0.5f * Mathf.Max(0, difficulty)));
        }

        /// <summary>
        /// Award a finished run once (keyed by its start time, so the summary can call this
        /// every frame). Returns the Renown earned, or -1 if this run was already counted.
        /// </summary>
        public static int AwardRun(double runKey, int bossesFelled, int kills, int combos, bool cleared, int difficulty)
        {
            if (TestMode) return -1;
            string key = runKey.ToString("F3");
            if (PlayerPrefs.GetString(KeyLastRun, "") == key) return -1;
            int earned = RenownFor(bossesFelled, kills, combos, cleared, difficulty);
            PlayerPrefs.SetString(KeyLastRun, key);
            PlayerPrefs.SetInt(KeyRenown, Renown + earned);
            PlayerPrefs.SetInt(KeyRuns, RunsFinished + 1);
            int mask = BossMask;
            for (int i = 0; i < bossesFelled; i++) mask |= 1 << i;
            PlayerPrefs.SetInt(KeyBosses, mask);
            PlayerPrefs.Save();
            return earned;
        }
    }
}
