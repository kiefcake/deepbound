using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// Meta-progression that outlives a run: <b>Renown</b>, earned at the end of every run,
    /// spent at the camp tailor on cosmetics — cloak dyes and auras. Nothing here changes
    /// power; it is how you show where you have been. Some pieces also need a boss felled
    /// at least once.
    ///
    /// <para>
    /// Saved per machine in PlayerPrefs (the client owns its own wardrobe); what you wear is
    /// synced to the party through <see cref="Player.NetworkPlayer.CloakDye"/> and
    /// <see cref="Player.NetworkPlayer.Aura"/>.
    /// </para>
    /// </summary>
    public static class Cosmetics
    {
        public enum Kind { Cloak, Aura }

        public struct Item
        {
            public string Name;
            public Color Cloak, Trim;   // for an aura, Cloak is the mote colour
            public int Cost;
            public int NeedsBoss;       // -1 = none; otherwise the depth of the boss that must have been felled
        }

        /// <summary>Index 0 is "your class's own colours" (no override). Append only: indices are saved and synced.</summary>
        public static readonly Item[] Dyes =
        {
            new Item { Name = "Class Colours", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Ashen Grey", Cloak = new Color(0.3f, 0.29f, 0.28f), Trim = new Color(0.7f, 0.7f, 0.72f), Cost = 40, NeedsBoss = -1 },
            new Item { Name = "Blood Crimson", Cloak = new Color(0.42f, 0.04f, 0.05f), Trim = new Color(0.8f, 0.6f, 0.25f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Midnight", Cloak = new Color(0.05f, 0.07f, 0.18f), Trim = new Color(0.65f, 0.68f, 0.75f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Royal Violet", Cloak = new Color(0.26f, 0.07f, 0.36f), Trim = new Color(0.85f, 0.65f, 0.25f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Bone White", Cloak = new Color(0.82f, 0.79f, 0.7f), Trim = new Color(0.5f, 0.06f, 0.06f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Raven Black", Cloak = new Color(0.035f, 0.035f, 0.045f), Trim = new Color(0.55f, 0.05f, 0.06f), Cost = 150, NeedsBoss = -1 },
            new Item { Name = "Warden's Ember", Cloak = new Color(0.32f, 0.11f, 0.04f), Trim = new Color(1f, 0.5f, 0.15f), Cost = 120, NeedsBoss = 0 },
            new Item { Name = "Drowned Teal", Cloak = new Color(0.04f, 0.2f, 0.2f), Trim = new Color(0.35f, 0.85f, 0.7f), Cost = 160, NeedsBoss = 1 },
            new Item { Name = "Hollow Gold", Cloak = new Color(0.45f, 0.33f, 0.08f), Trim = new Color(0.45f, 0.7f, 1f), Cost = 220, NeedsBoss = 2 },
            new Item { Name = "Cardinal's Scarlet", Cloak = new Color(0.55f, 0.02f, 0.04f), Trim = new Color(1f, 0.82f, 0.4f), Cost = 300, NeedsBoss = 3 },
        };

        /// <summary>Motes that drift up around you. Index 0 is none. Append only.</summary>
        public static readonly Item[] Auras =
        {
            new Item { Name = "No Aura", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Cinders", Cloak = new Color(1f, 0.45f, 0.12f), Cost = 90, NeedsBoss = -1 },
            new Item { Name = "Grave Mist", Cloak = new Color(0.55f, 0.8f, 0.55f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Frostbreath", Cloak = new Color(0.55f, 0.85f, 1f), Cost = 140, NeedsBoss = 1 },
            new Item { Name = "Hollow Starlight", Cloak = new Color(0.65f, 0.55f, 1f), Cost = 200, NeedsBoss = 2 },
            new Item { Name = "Last Rites", Cloak = new Color(1f, 0.82f, 0.4f), Cost = 280, NeedsBoss = 3 },
        };

        public static Item[] Of(Kind kind) => kind == Kind.Aura ? Auras : Dyes;

        private const string KeyRenown = "db.renown", KeyBosses = "db.bosses", KeyLastRun = "db.lastRun", KeyRuns = "db.runs";
        private static string KeyOwned(Kind k) => k == Kind.Aura ? "db.auras" : "db.dyes";
        private static string KeyWorn(Kind k) => k == Kind.Aura ? "db.aura" : "db.cloak";

        /// <summary>Set by the smoke/screenshot tests: nothing is read from or written to the real save.</summary>
        public static bool TestMode;

        public static int Renown => TestMode ? 0 : PlayerPrefs.GetInt(KeyRenown, 0);
        public static int RunsFinished => PlayerPrefs.GetInt(KeyRuns, 0);
        private static int BossMask => PlayerPrefs.GetInt(KeyBosses, 0);

        public static int Worn(Kind kind) => TestMode ? 0 : Mathf.Clamp(PlayerPrefs.GetInt(KeyWorn(kind), 0), 0, Of(kind).Length - 1);
        public static int Equipped => Worn(Kind.Cloak);

        public static bool Owns(Kind kind, int i) => i == 0 || (PlayerPrefs.GetInt(KeyOwned(kind), 0) & (1 << i)) != 0;
        public static bool BossFelled(int depth) => (BossMask & (1 << depth)) != 0;
        public static bool Unlocked(Kind kind, int i) => Of(kind)[i].NeedsBoss < 0 || BossFelled(Of(kind)[i].NeedsBoss);

        /// <summary>The colours to dress a cloak in, or null for the class's own.</summary>
        public static Item? Get(int dye) => dye > 0 && dye < Dyes.Length ? Dyes[dye] : (Item?)null;

        /// <summary>An aura's mote colour, or null for none.</summary>
        public static Color? AuraColor(int aura) => aura > 0 && aura < Auras.Length ? Auras[aura].Cloak : (Color?)null;

        public static bool Buy(Kind kind, int i)
        {
            var items = Of(kind);
            if (TestMode || Owns(kind, i) || !Unlocked(kind, i) || Renown < items[i].Cost) return false;
            PlayerPrefs.SetInt(KeyRenown, Renown - items[i].Cost);
            PlayerPrefs.SetInt(KeyOwned(kind), PlayerPrefs.GetInt(KeyOwned(kind), 0) | (1 << i));
            PlayerPrefs.Save();
            return true;
        }

        public static bool Wear(Kind kind, int i)
        {
            if (TestMode || !Owns(kind, i)) return false;
            PlayerPrefs.SetInt(KeyWorn(kind), i);
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
        public static int AwardRun(double runKey, int bossesFelled, int kills, int combos, bool cleared, int difficulty, float bonus = 1f)
        {
            if (TestMode) return -1;
            string key = runKey.ToString("F3");
            if (PlayerPrefs.GetString(KeyLastRun, "") == key) return -1;
            int earned = Mathf.RoundToInt(RenownFor(bossesFelled, kills, combos, cleared, difficulty) * bonus);
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
