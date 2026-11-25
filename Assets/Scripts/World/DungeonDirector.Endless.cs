using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        public readonly NetworkVariable<bool> EndlessRun = new NetworkVariable<bool>();

        public static bool Endless => Instance != null && Instance.EndlessRun.Value;

        public static int Lap(int depth) => Mathf.Max(0, depth) / FloorsPerRun;

        [Rpc(SendTo.Server)]
        public void SetEndlessRpc(bool on) => ServerSetEndless(on);

        public void ServerSetEndless(bool on)
        {
            if (Phase.Value != RunPhase.Lobby || RunInProgress) return;
            if (on && PlayerLife.All.Count > 1) return;
            EndlessRun.Value = on;
        }

        private void EndlessCheckAtStart()
        {
            if (!EndlessRun.Value || PlayerLife.All.Count <= 1) return;
            EndlessRun.Value = false;
            ServerFeedAll("Corwin shakes his head: \"The endless road takes one at a time.\" The ordinary descent, then.", new Color(0.85f, 0.75f, 0.55f));
        }

        public static string EndlessFloorName(string baseName, int depth) => Lap(depth) > 0 ? baseName + " (" + Ordinal(Lap(depth) + 1) + " time down)" : baseName;

        public const float LapEliteCap = 0.1f, LapPace = 0.85f, LapTallySooner = 45f;

        public static int EndlessLap(int depth) => Endless ? Lap(depth) : 0;

        public static float LapElitePace => Layout != null && EndlessLap(Layout.Depth) >= 2 ? LapPace : 1f;

        public static float LapTallyCut(int depth) => Mathf.Max(0, EndlessLap(depth) - 2) * LapTallySooner;

        public static float EliteCapFor(int depth) => Mathf.Min(0.85f, 0.6f + LapEliteCap * EndlessLap(depth));

        public static string LapLine(int depth)
        {
            int lap = EndlessLap(depth);
            if (lap <= 0) return "";
            string s = "Lap " + (lap + 1) + ": every floor has an event, more elites";
            if (lap >= 2) s += ", elites strike sooner";
            if (lap >= 3) s += ", the Tallyman comes early";
            return s;
        }

        private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
