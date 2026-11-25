using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        public readonly NetworkVariable<bool> DailyRun = new NetworkVariable<bool>();

        public readonly NetworkVariable<int> DailyDate = new NetworkVariable<int>();

        public static int Today => System.DateTime.Now.Year * 10000 + System.DateTime.Now.Month * 100 + System.DateTime.Now.Day;

        public static string DateLabel(int yyyymmdd) => (yyyymmdd / 10000) + "-" + (yyyymmdd / 100 % 100).ToString("00") + "-" + (yyyymmdd % 100).ToString("00");

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

        [Rpc(SendTo.Server)]
        public void SetDailyRpc(bool daily) => ServerSetDaily(daily);

        public void ServerSetDaily(bool daily)
        {
            if (Phase.Value != RunPhase.Lobby || RunInProgress) return;
            DailyRun.Value = daily;
            DailyDate.Value = Today;
        }

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
            UI.LoadingScreen.Begin(UI.LoadingScreen.Kind.Camp, 1.5f);
            Art.Sfx.Play2D(Art.Sound.Rattle, 0.8f);
            UI.GameHud.Feed(who + " throws the rope: the party climbs back to camp. This floor will be another road next time.", new Color(0.85f, 0.75f, 0.55f));
        }

        public static int ForcedSeed;

        private int FloorSeed(int depth) => ForcedSeed != 0 ? ForcedSeed
            : DailyRun.Value ? DailySeed(DailyDate.Value != 0 ? DailyDate.Value : Today, depth) : UnityEngine.Random.Range(1, int.MaxValue);
    }
}
