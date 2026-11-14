using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// THE FIRST EXPEDITION (2026-11-14, the lore's open thread 2: "The anvil came back up. The people who carried it did
    /// not, all of them. One of them is the dealer at Deadman's Hand. It has never said which.").
    /// Five went down. Four lie below, one an act, each with a keepsake and a last note (<see cref="World.ExpeditionRemains"/>);
    /// the fifth was never found, because the fifth deals cards in the tent. Bring all four home and the dealer says its name.
    /// What has been found is the HOST's, for good: four bits of the camp's keep (<see cref="World.CampMemory.PrefKey"/>, bits
    /// <see cref="FirstBit"/>..+3), so it rides to every peer in <see cref="World.DungeonDirector.CampKeeps"/>. Never in tests.
    /// </summary>
    public static class Expedition
    {
        public const int Members = 4, FirstBit = 28;
        public const float SpawnChance = 0.4f;

        public struct Member { public string Name, Short, Keepsake, Note; public Color Light; }

        /// <summary>One per act (0 Ossuary .. 3 Black Cathedral), in the order they fell behind.</summary>
        public static readonly Member[] Roll =
        {
            new Member { Name = "Old Maud, the guide", Short = "MAUD", Keepsake = "a tin whistle", Light = new Color(0.85f, 0.8f, 0.65f),
                Note = "Told them. A fire with no wood is not a fire. My knees are done, so they go on without me, and I can hear the thing ringing up the stair every time Gerrit sets it down. Leave me the whistle. If anyone comes looking, I will blow it. Nobody has come looking." },
            new Member { Name = "Pell, the lamp boy", Short = "PELL", Keepsake = "a cracked lantern", Light = new Color(1f, 0.7f, 0.35f),
                Note = "The water is up to the third step. They said wait by the lamp and they would come back for it, because we only have the one. I am keeping it lit. I am keeping it lit. I am keeping it" },
            new Member { Name = "Sister Ines, the chaplain", Short = "INES", Keepsake = "a string of prayer beads", Light = new Color(0.75f, 0.8f, 1f),
                Note = "The king asked me to stay and sing at his court. I said I would sing him one verse if he let the others pass with the anvil. He kept his word, so I am keeping mine. It has been a very long verse. I think he likes it." },
            new Member { Name = "Gerrit Hale, who led them", Short = "GERRIT", Keepsake = "an iron ring", Light = new Color(1f, 0.55f, 0.3f),
                Note = "The anvil is up. I walked it to the foot of the shaft and sent it up on the rope myself. Then the singing started under the floor, and I came back down for the others. Jory went up with the anvil, so somebody will tell them. Jory always did talk." },
        };

        /// <summary>The fifth, never found below: the dealer.</summary>
        public const string Fifth = "JORY";

        public static bool TestMode;

        private static bool Off => TestMode || World.CampMemory.TestMode || Cosmetics.TestMode;

        public static uint Bit(int member) => 1u << (FirstBit + member);

        /// <summary>Host: has this machine brought <paramref name="member"/> home?</summary>
        public static bool Found(int member) => !Off && member >= 0 && member < Members && ((uint)PlayerPrefs.GetInt(World.CampMemory.PrefKey, 0) & Bit(member)) != 0;

        /// <summary>How many of <paramref name="keeps"/> (the synced camp keep bits) are found.</summary>
        public static int Count(uint keeps)
        {
            int n = 0;
            for (int i = 0; i < Members; i++) if ((keeps & Bit(i)) != 0) n++;
            return n;
        }

        public static int FoundCount { get { int n = 0; for (int i = 0; i < Members; i++) if (Found(i)) n++; return n; } }

        /// <summary>Host: remember <paramref name="member"/> as brought home; returns how many are home now.</summary>
        public static int Remember(int member)
        {
            if (Off || member < 0 || member >= Members) return FoundCount;
            PlayerPrefs.SetInt(World.CampMemory.PrefKey, (int)((uint)PlayerPrefs.GetInt(World.CampMemory.PrefKey, 0) | Bit(member)));
            PlayerPrefs.Save();
            return FoundCount;
        }
    }
}
