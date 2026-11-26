using Deepbound.Loot;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    public partial class PlayerProgress
    {
        private int heirloomPending = -1;

        public int HeirloomPending => heirloomPending;

        public string CannotBringHeirloom(int relicPlusOne)
        {
            var d = DungeonDirector.Instance;
            if (d == null || !DungeonDirector.InLobby || d.RunInProgress || d.Phase.Value != RunPhase.Lobby) return "Only at camp, before a run";
            if (relicPlusOne < 1 || relicPlusOne - 1 >= Relics.Count) return "No heirloom chosen";
            if (heirloomPending >= 0) return "Already brought for this run";
            if (!CanTakeRelic((Relic)(relicPlusOne - 1))) return "Your tray cannot take it";
            return null;
        }

        public bool ServerBringHeirloom(int relicPlusOne)
        {
            if (!IsServer || Stats == null || CannotBringHeirloom(relicPlusOne) != null) return false;
            var r = (Relic)(relicPlusOne - 1);
            if (!ServerAddRelic(r)) return false;
            heirloomPending = (int)r;
            return true;
        }

        public void ServerGrantHeirloom()
        {
            if (!IsServer || heirloomPending < 0) return;
            var r = (Relic)heirloomPending;
            heirloomPending = -1;
            if (!HasRelic(r)) ServerAddRelic(r);
        }

        public void ServerDebugClearHeirloom() => heirloomPending = -1;

        [Rpc(SendTo.Server)]
        public void BringHeirloomRpc(byte relicPlusOne)
        {
            RunLog.Note("action", "BringHeirloom" + " " + relicPlusOne, this);
            string why = CannotBringHeirloom(relicPlusOne);
            bool ok = why == null && ServerBringHeirloom(relicPlusOne);
            HeirloomResultRpc(ok, ok ? "The heirloom is with you." : why ?? "It could not be brought.", RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void HeirloomResultRpc(bool ok, string note, RpcParams p)
        {
            Legacy.Note = note;
            if (!ok) return;
            Legacy.ConsumeHeirloom();
            Art.Sfx.Play2D(Art.Sound.Purchase, 0.8f);
        }
    }
}
