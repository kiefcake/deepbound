using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Daily Descent (2026-10-20): chosen at Corwin's carriage before a run. Every floor's layout is
    /// seeded from the date and the depth, so everyone who plays that day walks the same dungeon (the dead
    /// and the loot still roll fresh). Your best daily floor is kept in the Hall of Records.
    /// </summary>
    public partial class DungeonDirector
    {
        public readonly NetworkVariable<bool> DailyRun = new NetworkVariable<bool>();
        /// <summary>The day the run's seeds come from (yyyymmdd), fixed when the run starts.</summary>
        public readonly NetworkVariable<int> DailyDate = new NetworkVariable<int>();

        public static int Today => System.DateTime.Now.Year * 10000 + System.DateTime.Now.Month * 100 + System.DateTime.Now.Day;

        public static string DateLabel(int yyyymmdd) => (yyyymmdd / 10000) + "-" + (yyyymmdd / 100 % 100).ToString("00") + "-" + (yyyymmdd % 100).ToString("00");

        /// <summary>The same positive seed for a day and a depth on every machine.</summary>
        public static int DailySeed(int yyyymmdd, int depth)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (int v in new[] { yyyymmdd, depth, 0x5eed })
                {
                    h ^= (uint)v;
                    h *= 16777619u;
                    h ^= h >> 13;
                }
                return (int)(h & 0x7ffffffe) + 1;
            }
        }

        /// <summary>Host → server: choose the daily road (only at camp, before a run).</summary>
        [Rpc(SendTo.Server)]
        public void SetDailyRpc(bool daily) => ServerSetDaily(daily);

        public void ServerSetDaily(bool daily)
        {
            if (Phase.Value != RunPhase.Lobby || RunInProgress) return;
            DailyRun.Value = daily;
            DailyDate.Value = Today;
        }

        /// <summary>Server: this floor's seed (the daily road, or a fresh one).</summary>
        private int FloorSeed(int depth) => DailyRun.Value ? DailySeed(DailyDate.Value != 0 ? DailyDate.Value : Today, depth) : UnityEngine.Random.Range(1, int.MaxValue);
    }
}
