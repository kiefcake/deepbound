using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The delver's journal: pages of Cathedra's history found on lecterns in the dungeon, three
    /// per floor (see docs/LORE.md). Found pages are kept on this machine across runs (PlayerPrefs,
    /// never touched by tests) and read in the pause menu's Guide.
    /// </summary>
    public static class Journal
    {
        public struct Entry { public int Floor; public string Title, Text; }

        public static readonly Entry[] Entries =
        {
            // The Ossuary
            new Entry { Floor = 0, Title = "A digger's prayer", Text = "Lord, I have carried four hundred of my neighbours down to the niches this year, and stacked them neat as cordwood so that they lie close to You. When the Last Morning comes, let them wake in the right order. Let them wake knowing their own faces. Let them not wake before." },
            new Entry { Floor = 0, Title = "The Baron's oath", Text = "Writ at the first door of the Ossuary, in the Baron's own hand: Nothing passes. Not the dead rising, not the living descending, not my son, not my God. I will hold this door until the Last Morning or until the stone forgets me. Below it, in another hand: the stone has not forgotten him." },
            new Entry { Floor = 0, Title = "First expedition, day three", Text = "We have found the anvil. It was standing in a chapel of skulls with its fire still in it, and no smith. Gerrit says we carry it up. Old Maud says we leave it, that a fire that burns four hundred years without wood is not a fire. We are carrying it up." },
            // The Drowned Vaults
            new Entry { Floor = 1, Title = "A ledger, water-stained", Text = "Interest owed by the estate of the late Aldous Vane: eleven hundred crowns. Interest owed by the estate of the late Maren Holt: four hundred crowns. Interest owed by the river: everything. The river does not pay. Note to self: speak to the river." },
            new Entry { Floor = 1, Title = "The year the river froze", Text = "The water came in under the vault doors in the spring and froze in the autumn with the drowned still standing in it. We cut a road through them with picks. They watched us cut it. In the thaw they were gone, and the road had been walked." },
            new Entry { Floor = 1, Title = "On the Mourners", Text = "The weeping statues were carved for the merchant families, a mourner for every grave, so the dead would never lie unwept. The carvers were paid by the tear. I have counted the statues on the Lantern Stair three times this week. The count goes up." },
            // The Hollow Throne
            new Entry { Floor = 2, Title = "The coronation of Ossric", Text = "And the Cardinal set the crown upon him and said: you shall rule the city of the buried until the Last Morning. And Ossric asked: and after? And the Cardinal did not answer, and the king took that for a promise." },
            new Entry { Floor = 2, Title = "Rules of the Hollow Court", Text = "One. The king's judgement falls upon one subject at a time. Two. A judgement borne by many is borne lightly. Three. The Royal Guard shields the king while they stand. Four. The lanterns of the court hold the light of courtiers who displeased him. Do not break them. (Someone has written beneath: break them.)" },
            new Entry { Floor = 2, Title = "A courtier's last letter", Text = "My love, they are putting my light in a lantern by the throne, so that I may serve forever. If you come down for me, do not try to carry me home in my body. Break the glass. I will find my own way to you." },
            // The Black Cathedral
            new Entry { Floor = 3, Title = "The hymn, first verse", Text = "Sleep, you who were carried down. Sleep, you who carried. The morning is coming and it is not yet. I will sing until it is. I will sing until it is. I will sing until" },
            new Entry { Floor = 3, Title = "The Cardinal's confession", Text = "Each night the hymn takes a little more of me. Tonight it took my name. I do not need a name to sing. When it has taken everything, I wonder what will be left holding the note." },
            new Entry { Floor = 3, Title = "Beneath the altar", Text = "There are three seals under the altar of the Black Cathedral, and the hymn was never meant for the dead of Cathedra at all. We sang them to sleep only so that they would not wake the thing we were singing to. If you are reading this, the singing has stopped." },
        };

        private const string Key = "deepbound.journal";
        private static HashSet<int> found;
        /// <summary>Tests read pages without touching the real save.</summary>
        public static bool TestMode;

        private static HashSet<int> Found
        {
            get
            {
                if (found != null) return found;
                found = new HashSet<int>();
                if (TestMode) return found;
                try
                {
                    int bits = PlayerPrefs.GetInt(Key, 0);
                    for (int i = 0; i < Entries.Length; i++) if ((bits & (1 << i)) != 0) found.Add(i);
                }
                catch { }
                return found;
            }
        }

        public static bool Has(int i) => Found.Contains(i);
        public static int Count => Found.Count;

        /// <summary>Record a page as found. True if it is new.</summary>
        public static bool Add(int i)
        {
            if (i < 0 || i >= Entries.Length || !Found.Add(i)) return false;
            if (TestMode) return true;
            int bits = 0;
            foreach (var f in Found) bits |= 1 << f;
            try { PlayerPrefs.SetInt(Key, bits); PlayerPrefs.Save(); } catch { }
            return true;
        }

        /// <summary>A page for floor <paramref name="depth"/>: the first one this machine has not found, else any.</summary>
        public static int PickFor(int depth, System.Random rng)
        {
            var options = new List<int>();
            for (int i = 0; i < Entries.Length; i++) if (Entries[i].Floor == Mathf.Clamp(depth, 0, 3)) options.Add(i);
            foreach (var i in options) if (!Has(i)) return i;
            return options.Count > 0 ? options[rng.Next(options.Count)] : 0;
        }
    }
}
