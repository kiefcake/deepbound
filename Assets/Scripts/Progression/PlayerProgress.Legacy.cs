using Deepbound.Loot;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// The heirloom's server side (2026-11-07, see <see cref="Legacy"/>): at camp, before a run, the owner may bring the one relic
    /// they chose from their last run. It arrives as a plain tier-one relic, and the request is remembered on the server so
    /// <see cref="ServerGrantHeirloom"/> at run start can put it back if something (a class change) wiped the tray in between.
    /// </summary>
    public partial class PlayerProgress
    {
        // Server: the relic brought to camp for the coming run, or -1.
        private int heirloomPending = -1;

        /// <summary>Server: the relic brought for the coming run, or -1 (for tests).</summary>
        public int HeirloomPending => heirloomPending;

        /// <summary>Why the heirloom cannot be brought now (server's view), or null.</summary>
        public string CannotBringHeirloom(int relicPlusOne)
        {
            var d = DungeonDirector.Instance;
            if (d == null || !DungeonDirector.InLobby || d.RunInProgress || d.Phase.Value != RunPhase.Lobby) return "Only at camp, before a run";
            if (relicPlusOne < 1 || relicPlusOne - 1 >= Relics.Count) return "No heirloom chosen";
            if (heirloomPending >= 0) return "Already brought for this run";
            if (!CanTakeRelic((Relic)(relicPlusOne - 1))) return "Your tray cannot take it";
            return null;
        }

        /// <summary>Server: bring the heirloom (a relic id + 1) into the coming run. False with the reason in <see cref="Legacy.Note"/> on the owner.</summary>
        public bool ServerBringHeirloom(int relicPlusOne)
        {
            if (!IsServer || Stats == null || CannotBringHeirloom(relicPlusOne) != null) return false;
            var r = (Relic)(relicPlusOne - 1);
            if (!ServerAddRelic(r)) return false;
            heirloomPending = (int)r;
            return true;
        }

        /// <summary>Server, run start: make sure a brought heirloom is carried, then spend the flag.</summary>
        public void ServerGrantHeirloom()
        {
            if (!IsServer || heirloomPending < 0) return;
            var r = (Relic)heirloomPending;
            heirloomPending = -1;
            if (!HasRelic(r)) ServerAddRelic(r);
        }

        /// <summary>Server, for tests: forget a brought heirloom.</summary>
        public void ServerDebugClearHeirloom() => heirloomPending = -1;

        /// <summary>Owner: ask the server to bring the heirloom you chose (<see cref="Legacy.Heirloom"/>).</summary>
        [Rpc(SendTo.Server)]
        public void BringHeirloomRpc(byte relicPlusOne)
        {
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
