using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE ENDLESS DESCENT (2026-11-19, the user: "a new game mode, where a single player can infinitely progress through
    /// the dungeon floors until they are downed earning them a floor highscore"). Chosen at Corwin's carriage before a run,
    /// for one player only. The foundation:
    /// <list type="bullet">
    /// <item>No last floor: the Unsung's floor goes back to camp like any other (<see cref="ExitFinal"/> never set).</item>
    /// <item>Past floor 10 the acts come round again (<see cref="Act"/> cycles: Ossuary, Vaults, Throne, Cathedral,
    /// Abyss, Ossuary...), each lap a <see cref="Lap"/> deeper, while <see cref="Tier"/> keeps climbing with the true
    /// depth - floor 11 looks like floor 1 and hits like floor 11.</item>
    /// <item>The first time you go down the run is over (<see cref="PartyWiped"/>): no bleed-out, no Smelling Salts.</item>
    /// <item>The floor you fell on is the score: <see cref="Progression.Records.EndlessBest"/>, on the Hall of Records
    /// slab and the run summary. It is kept apart from the ten-floor records.</item>
    /// </list>
    /// Everything else (camp between floors, shops, the Tallyman, omens, contracts) is the normal run. Open for later:
    /// lap modifiers, a leaderboard, refusing late joiners mid-descent (today a second player who joins mid-run is
    /// simply in it, and either of you going down ends it).
    /// </summary>
    public partial class DungeonDirector
    {
        public readonly NetworkVariable<bool> EndlessRun = new NetworkVariable<bool>();

        /// <summary>Any peer: this run is the Endless Descent.</summary>
        public static bool Endless => Instance != null && Instance.EndlessRun.Value;

        /// <summary>How many times the acts have come round (0 on the first ten floors).</summary>
        public static int Lap(int depth) => Mathf.Max(0, depth) / FloorsPerRun;

        /// <summary>Host → server: choose the endless road (camp, before a run, alone).</summary>
        [Rpc(SendTo.Server)]
        public void SetEndlessRpc(bool on) => ServerSetEndless(on);

        public void ServerSetEndless(bool on)
        {
            if (Phase.Value != RunPhase.Lobby || RunInProgress) return;
            if (on && PlayerLife.All.Count > 1) return;
            EndlessRun.Value = on;
        }

        /// <summary>Server, as a run starts: the endless road is for one. A party that boards it gets the ordinary descent.</summary>
        private void EndlessCheckAtStart()
        {
            if (!EndlessRun.Value || PlayerLife.All.Count <= 1) return;
            EndlessRun.Value = false;
            ServerFeedAll("Corwin shakes his head: \"The endless road takes one at a time.\" The ordinary descent, then.", new Color(0.85f, 0.75f, 0.55f));
        }

        /// <summary>The ordinary run's floor name, or the endless one (the act's name, and which time round).</summary>
        public static string EndlessFloorName(string baseName, int depth) => Lap(depth) > 0 ? baseName + " (" + Ordinal(Lap(depth) + 1) + " time down)" : baseName;

        private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }
}
