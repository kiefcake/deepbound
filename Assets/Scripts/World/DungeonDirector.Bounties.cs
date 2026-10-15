using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>Append only: synced.</summary>
    public enum Contract : byte { None, Slayer, ChampionHunt, Untouchable, TreasureHunt, SwiftBlade }

    /// <summary>
    /// Contracts from the camp's bounty board: before boarding, the party takes one of three
    /// notices, and it rides down with them for the next floor. Fulfil it by the time the
    /// boss falls for gold, embers and (for the hard ones) a Sigil each.
    /// </summary>
    public partial class DungeonDirector
    {
        public enum ContractStatus : byte { None, Taken, Active, Fulfilled, Failed }

        public readonly NetworkVariable<byte> ActiveContract = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> ContractState = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> ContractProgress = new NetworkVariable<int>();
        public readonly NetworkVariable<int> ContractGoal = new NetworkVariable<int>();
        private float contractStartedAt;
        public const float SwiftSeconds = 420f;

        public static string ContractName(Contract c) =>
            c == Contract.Slayer ? "Slayer" : c == Contract.ChampionHunt ? "Champion Hunt" : c == Contract.Untouchable ? "Untouchable"
            : c == Contract.TreasureHunt ? "Treasure Hunt" : c == Contract.SwiftBlade ? "Swift Blade" : "";

        public static string ContractTask(Contract c, int depth) =>
            c == Contract.Slayer ? "Slay " + SlayerGoal(depth) + " of the dead on the next floor"
            : c == Contract.ChampionHunt ? "Kill the next floor's champion"
            : c == Contract.Untouchable ? "Clear the next floor with nobody going down"
            : c == Contract.TreasureHunt ? "Find a secret wall on the next floor"
            : c == Contract.SwiftBlade ? "Kill the next boss within " + (int)(SwiftSeconds / 60f) + " minutes of arriving"
            : "";

        public static bool HardContract(Contract c) => c == Contract.Untouchable || c == Contract.SwiftBlade || c == Contract.ChampionHunt;

        public static string ContractReward(Contract c) => HardContract(c) ? "gold, embers and a Sigil each" : "gold and embers each";

        private static int SlayerGoal(int depth) => 30 + 10 * Mathf.Max(0, depth);

        /// <summary>Server: the party takes a notice at camp.</summary>
        public void ServerTakeContract(Contract c)
        {
            if (ContractState.Value == (byte)ContractStatus.Taken || ContractState.Value == (byte)ContractStatus.Active) return;
            ActiveContract.Value = (byte)c;
            ContractState.Value = (byte)ContractStatus.Taken;
            ContractProgress.Value = 0;
            ContractGoal.Value = 0;
        }

        /// <summary>Server: a floor begins — a taken contract becomes live.</summary>
        private void BeginContract(int depth)
        {
            if (ContractState.Value != (byte)ContractStatus.Taken) return;
            var c = (Contract)ActiveContract.Value;
            ContractState.Value = (byte)ContractStatus.Active;
            ContractProgress.Value = 0;
            ContractGoal.Value = c == Contract.Slayer ? SlayerGoal(depth) : 1;
            contractStartedAt = Time.time;
        }

        /// <summary>Server: called on every kill on a floor.</summary>
        private void ContractOnKill(Health target)
        {
            if (ContractState.Value != (byte)ContractStatus.Active) return;
            var c = (Contract)ActiveContract.Value;
            if (c == Contract.Slayer && target.Team == Team.Enemies)
            {
                ContractProgress.Value++;
                if (ContractProgress.Value >= ContractGoal.Value) FulfilContract();
            }
            else if (c == Contract.ChampionHunt && target.TryGetComponent<EnemyBrain>(out var b) && b.IsChampion)
            {
                ContractProgress.Value = 1;
                FulfilContract();
            }
            else if (c == Contract.Untouchable && target.Team == Team.Players) FailContract();
        }

        /// <summary>Server: a secret wall was smashed open.</summary>
        public void ContractOnSecret()
        {
            if (ContractState.Value != (byte)ContractStatus.Active || (Contract)ActiveContract.Value != Contract.TreasureHunt) return;
            ContractProgress.Value = 1;
            FulfilContract();
        }

        private void TickContract()
        {
            if (ContractState.Value != (byte)ContractStatus.Active) return;
            if ((Contract)ActiveContract.Value == Contract.SwiftBlade && Time.time - contractStartedAt > SwiftSeconds) FailContract();
        }

        /// <summary>Server: the floor's boss fell — settle what is still open.</summary>
        private void ContractOnFloorCleared()
        {
            if (ContractState.Value != (byte)ContractStatus.Active) return;
            var c = (Contract)ActiveContract.Value;
            if (c == Contract.Untouchable || c == Contract.SwiftBlade) FulfilContract();
            else FailContract();
        }

        private void FulfilContract()
        {
            ContractState.Value = (byte)ContractStatus.Fulfilled;
            var c = (Contract)ActiveContract.Value;
            int depth = Layout != null ? Layout.Depth : 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var prog = p.GetComponent<PlayerProgress>();
                prog.ServerAddGold(50 + 40 * depth);
                ServerDropEmbers(p.transform.position + Vector3.up * 0.5f, 8 + 3 * depth);
                if (HardContract(c)) ServerDropLoot(p.transform.position + p.transform.forward + Vector3.up * 0.5f, LootKind.Sigil, 1);
            }
            TitleCardRpc("CONTRACT FULFILLED", ContractName(c) + ": " + ContractReward(c));
        }

        private void FailContract()
        {
            ContractState.Value = (byte)ContractStatus.Failed;
            ContractFailedRpc(ContractName((Contract)ActiveContract.Value));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ContractFailedRpc(string name) => UI.GameHud.Feed("Contract failed: " + name, new Color(0.85f, 0.4f, 0.35f));

        /// <summary>Server: back at camp — a settled contract comes off the board.</summary>
        private void ClearSettledContract()
        {
            if (ContractState.Value == (byte)ContractStatus.Fulfilled || ContractState.Value == (byte)ContractStatus.Failed ||
                ContractState.Value == (byte)ContractStatus.Active)
            {
                ActiveContract.Value = 0;
                ContractState.Value = 0;
            }
        }
    }
}
