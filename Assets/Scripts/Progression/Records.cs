using System.Collections.Generic;
using UnityEngine;
using Deepbound.Core;

namespace Deepbound.Progression
{
    public static class Records
    {
        private const string KeyDeep = "db.rec.deep", KeyKills = "db.rec.kills", KeyGold = "db.rec.gold", KeyFast = "db.rec.fast", KeyWins = "db.rec.wins", KeyRun = "db.rec.run", KeyDailyDate = "db.rec.dailydate", KeyDailyFloor = "db.rec.dailyfloor", KeyFallen = "db.rec.fallen", KeyEndless = "db.rec.endless";

        public const int FallenKept = 6;

        private const char Sep = (char)10;

        private static bool Test => Cosmetics.TestMode;
        public static int Deepest => Test ? 0 : Prefs.GetInt(KeyDeep, 0);
        public static int BestKills => Test ? 0 : Prefs.GetInt(KeyKills, 0);
        public static int BestGold => Test ? 0 : Prefs.GetInt(KeyGold, 0);
        public static int Wins => Test ? 0 : Prefs.GetInt(KeyWins, 0);

        public static float FastestFloor => Test ? 0f : Prefs.GetFloat(KeyFast, 0f);

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return s / 60 + ":" + (s % 60).ToString("00");
        }

        public static int EndlessBest => Test ? 0 : Prefs.GetInt(KeyEndless, 0);

        public static int DailyBest(int today) => Test || Prefs.GetInt(KeyDailyDate, 0) != today ? 0 : Prefs.GetInt(KeyDailyFloor, 0);

        public static string[] Fallen
        {
            get
            {
                if (Test) return new string[0];
                string all = Prefs.GetString(KeyFallen, "");
                return all.Length == 0 ? new string[0] : all.Split(Sep);
            }
        }

        public static string FallenLine(string names, int floorReached) => (string.IsNullOrEmpty(names) ? "Nameless delvers" : names) + "  -  fell on floor " + floorReached;

        public static List<string> SubmitRun(double runKey, int floorReached, int kills, int gold, bool won, int dailyDate = 0, string party = null, bool endless = false)
        {
            var beaten = new List<string>();
            if (Test) return beaten;
            string key = runKey.ToString("F3");
            if (Prefs.GetString(KeyRun, "") == key) return beaten;
            Prefs.SetString(KeyRun, key);

            if (endless) { if (floorReached > EndlessBest) { Prefs.SetInt(KeyEndless, floorReached); beaten.Add("endless descent: floor " + floorReached); } }
            else if (floorReached > Deepest) { Prefs.SetInt(KeyDeep, floorReached); beaten.Add("deepest descent: floor " + floorReached); }
            if (kills > BestKills) { Prefs.SetInt(KeyKills, kills); beaten.Add("most kills in a run: " + kills); }
            if (gold > BestGold) { Prefs.SetInt(KeyGold, gold); beaten.Add("most gold in a run: " + gold); }
            if (won) { Prefs.SetInt(KeyWins, Wins + 1); beaten.Add("victories: " + (Wins)); }
            else
            {
                var list = new List<string> { FallenLine(party, floorReached).Replace(Sep, ' ') };
                foreach (var old in Fallen) if (list.Count < FallenKept) list.Add(old);
                Prefs.SetString(KeyFallen, string.Join(Sep.ToString(), list));
            }
            if (dailyDate != 0 && !endless && floorReached > DailyBest(dailyDate))
            {
                Prefs.SetInt(KeyDailyDate, dailyDate);
                Prefs.SetInt(KeyDailyFloor, floorReached);
                beaten.Add("today's daily descent: floor " + floorReached);
            }
            Prefs.Save();
            return beaten;
        }

        public static bool SubmitFloorTime(float seconds)
        {
            if (Test || seconds < 20f) return false;
            float best = FastestFloor;
            if (best > 0f && seconds >= best) return false;
            Prefs.SetFloat(KeyFast, seconds);
            Prefs.Save();
            return true;
        }

        public static string[] Lines => new[]
        {
            "Deepest descent      " + (Deepest > 0 ? "floor " + Deepest : "-"),
            "Fastest floor        " + (FastestFloor > 0f ? Clock(FastestFloor) : "-"),
            "Most kills in a run  " + (BestKills > 0 ? BestKills.ToString() : "-"),
            "Most gold in a run   " + (BestGold > 0 ? BestGold.ToString() : "-"),
            "Victories            " + Wins,
            "Today's daily        " + (DailyBest(World.DungeonDirector.Today) > 0 ? "floor " + DailyBest(World.DungeonDirector.Today) : "-"),
            "Endless descent      " + (EndlessBest > 0 ? "floor " + EndlessBest : "-"),
        };
    }
}
