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

        /// <summary>
        /// The climbing rope (2026-10-20, an extraction choice): from floor 2, before the guardian's arena
        /// seals, a rope-holder at the waystone takes the party back to camp, run intact. The floor is lost:
        /// the carriage will take you to the same depth again, by a new road.
        /// </summary>
        public bool CanRetreat => Phase.Value == RunPhase.Exploring && Floor.Value.y >= 1 && !BossSealed;

        public void ServerRetreat(string who)
        {
            if (!CanRetreat) return;
            RetreatRpc(who);
            StartCoroutine(AfterRetreat());
        }

        private System.Collections.IEnumerator AfterRetreat()
        {
            yield return new WaitForSeconds(1.6f);
            NextDepth.Value = Floor.Value.y;
            EnterLobby(keepRun: true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RetreatRpc(string who)
        {
            UI.GameHud.BeginFadeOut(1.5f);
            Art.Sfx.Play2D(Art.Sound.Rattle, 0.8f);
            UI.GameHud.Feed(who + " throws the rope: the party climbs back to camp. This floor will be another road next time.", new Color(0.85f, 0.75f, 0.55f));
        }

        /// <summary>Server: this floor's seed (the daily road, or a fresh one).</summary>
        private int FloorSeed(int depth) => DailyRun.Value ? DailySeed(DailyDate.Value != 0 ? DailyDate.Value : Today, depth) : UnityEngine.Random.Range(1, int.MaxValue);
    }
}
