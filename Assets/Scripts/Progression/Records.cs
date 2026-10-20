using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// The Hall of Records (2026-10-20): this machine's best runs, carved on a slab by the camp's bounty
    /// board. Deepest floor, fastest floor (entering to the guardian's fall), most kills and gold in one
    /// run, and victories. Saved in PlayerPrefs, never in tests; a new record is called out in the feed.
    /// </summary>
    public static class Records
    {
        private const string KeyDeep = "db.rec.deep", KeyKills = "db.rec.kills", KeyGold = "db.rec.gold", KeyFast = "db.rec.fast", KeyWins = "db.rec.wins", KeyRun = "db.rec.run";

        private static bool Test => Cosmetics.TestMode;
        public static int Deepest => Test ? 0 : PlayerPrefs.GetInt(KeyDeep, 0);
        public static int BestKills => Test ? 0 : PlayerPrefs.GetInt(KeyKills, 0);
        public static int BestGold => Test ? 0 : PlayerPrefs.GetInt(KeyGold, 0);
        public static int Wins => Test ? 0 : PlayerPrefs.GetInt(KeyWins, 0);
        /// <summary>Seconds; 0 = none yet.</summary>
        public static float FastestFloor => Test ? 0f : PlayerPrefs.GetFloat(KeyFast, 0f);

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return s / 60 + ":" + (s % 60).ToString("00");
        }

        /// <summary>A run has ended (once per run key): the records it beat, as lines for the feed.</summary>
        public static List<string> SubmitRun(double runKey, int floorReached, int kills, int gold, bool won)
        {
            var beaten = new List<string>();
            if (Test) return beaten;
            string key = runKey.ToString("F3");
            if (PlayerPrefs.GetString(KeyRun, "") == key) return beaten;
            PlayerPrefs.SetString(KeyRun, key);
            if (floorReached > Deepest) { PlayerPrefs.SetInt(KeyDeep, floorReached); beaten.Add("deepest descent: floor " + floorReached); }
            if (kills > BestKills) { PlayerPrefs.SetInt(KeyKills, kills); beaten.Add("most kills in a run: " + kills); }
            if (gold > BestGold) { PlayerPrefs.SetInt(KeyGold, gold); beaten.Add("most gold in a run: " + gold); }
            if (won) { PlayerPrefs.SetInt(KeyWins, Wins + 1); beaten.Add("victories: " + (Wins)); }
            PlayerPrefs.Save();
            return beaten;
        }

        /// <summary>A floor's guardian fell this many seconds after the floor began. True on a new best.</summary>
        public static bool SubmitFloorTime(float seconds)
        {
            if (Test || seconds < 20f) return false;
            float best = FastestFloor;
            if (best > 0f && seconds >= best) return false;
            PlayerPrefs.SetFloat(KeyFast, seconds);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>The slab's lines.</summary>
        public static string[] Lines => new[]
        {
            "Deepest descent      " + (Deepest > 0 ? "floor " + Deepest : "-"),
            "Fastest floor        " + (FastestFloor > 0f ? Clock(FastestFloor) : "-"),
            "Most kills in a run  " + (BestKills > 0 ? BestKills.ToString() : "-"),
            "Most gold in a run   " + (BestGold > 0 ? BestGold.ToString() : "-"),
            "Victories            " + Wins,
        };
    }
}
