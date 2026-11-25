using UnityEngine;
using Deepbound.Core;

namespace Deepbound.Progression
{
    public static class Expedition
    {
        public const int Members = 4, FirstBit = 28;
        public const float SpawnChance = 0.4f;

        public struct Member { public string Name, Short, Keepsake, Note; public Color Light; }

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

        public const string Fifth = "JORY";

        public static bool TestMode;

        private static bool Off => TestMode || World.CampMemory.TestMode || Cosmetics.TestMode;

        public static uint Bit(int member) => 1u << (FirstBit + member);

        public static bool Found(int member) => !Off && member >= 0 && member < Members && ((uint)Prefs.GetInt(World.CampMemory.PrefKey, 0) & Bit(member)) != 0;

        public static int Count(uint keeps)
        {
            int n = 0;
            for (int i = 0; i < Members; i++) if ((keeps & Bit(i)) != 0) n++;
            return n;
        }

        public static int FoundCount { get { int n = 0; for (int i = 0; i < Members; i++) if (Found(i)) n++; return n; } }

        public static int Remember(int member)
        {
            if (Off || member < 0 || member >= Members) return FoundCount;
            Prefs.SetInt(World.CampMemory.PrefKey, (int)((uint)Prefs.GetInt(World.CampMemory.PrefKey, 0) | Bit(member)));
            Prefs.Save();
            return FoundCount;
        }
    }
}
