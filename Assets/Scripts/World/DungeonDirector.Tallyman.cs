using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The floor clock behind <see cref="Tallyman"/> (2026-10-20): a far bell at
    /// <see cref="TallyWarnSeconds"/>, then he walks in at <see cref="TallyArriveSeconds"/> and hunts until
    /// the guardian's arena seals. Off in tests (the smoke test calls him on purpose) and at camp.
    /// </summary>
    public partial class DungeonDirector
    {
        public const float TallyWarnSeconds = 420f, TallyArriveSeconds = 540f;

        [SerializeField] private NetworkObject tallymanPrefab;

        /// <summary>Server time the current floor began (the HUD counts down from it after the bell).</summary>
        public readonly NetworkVariable<double> FloorBegan = new NetworkVariable<double>();
        /// <summary>0 nothing yet, 1 the bell has tolled, 2 he is here (or has been).</summary>
        public readonly NetworkVariable<byte> TallyStage = new NetworkVariable<byte>();


        private void ResetTally()
        {
            FloorBegan.Value = NetworkManager.ServerTime.Time;
            TallyStage.Value = 0;
            DismissTally();
        }

        private void TickTally(double now)
        {
            if (Layout == null || Floor.Value.y < 0 || TestMode || tallymanPrefab == null) return;
            if (bossSealed) return;
            double t = now - FloorBegan.Value;
            if (TallyStage.Value == 0 && t >= TallyWarnSeconds)
            {
                TallyStage.Value = 1;
                TallyBellRpc();
            }
            else if (TallyStage.Value == 1 && t >= TallyArriveSeconds)
            {
                TallyStage.Value = 2;
                SpawnTally(TallyEntry());
            }
        }

        /// <summary>Where he walks in: the room nearest the start that no one is standing near.</summary>
        private Vector3 TallyEntry()
        {
            DungeonLayout.Room best = null;
            foreach (var room in Layout.Rooms)
            {
                if (room.Kind == RoomKind.Boss) continue;
                bool clear = true;
                foreach (var p in PlayerLife.All)
                    if (p != null && Vector3.Distance(p.transform.position, room.Center) < 26f) { clear = false; break; }
                if (clear && (best == null || room.Id < best.Id)) best = room;
            }
            return (best ?? Layout.Rooms[0]).Center;
        }

        private void SpawnTally(Vector3 at)
        {
            if (Tallyman.Active != null && Tallyman.Active.IsSpawned) return;
            Spawn(tallymanPrefab, at, Quaternion.identity);
            TallyArriveRpc();
        }

        private void DismissTally()
        {
            if (Tallyman.Active != null && Tallyman.Active.IsSpawned) Tallyman.Active.ServerLeave();
        }

        /// <summary>Server, for tests: call him now, here.</summary>
        public Tallyman ServerDebugTally(Vector3 at, bool stay = false)
        {
            if (tallymanPrefab == null) return null;
            SpawnTally(at);
            if (Tallyman.Active != null) Tallyman.Active.ServerStay = stay;
            return Tallyman.Active;
        }

        public void ServerDebugDismissTally() => DismissTally();

        [Rpc(SendTo.ClientsAndHost)]
        private void TallyBellRpc()
        {
            UI.GameHud.ShowTitleCard("A FAR BELL TOLLS", "Something below is keeping count. Do not linger on this floor", new Color(0.62f, 1f, 0.7f));
            Art.Sfx.Play2D(Art.Sound.Toll, 0.8f);
            UI.GameHud.Feed("The bell: two minutes until the Tallyman comes for the late. Find the guardian.", new Color(0.62f, 1f, 0.7f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TallyArriveRpc()
        {
            UI.GameHud.ShowTitleCard("THE TALLYMAN", "You lingered. He cannot be fought: run, and seal yourselves in with the guardian", new Color(0.62f, 1f, 0.7f));
            Art.Sfx.Play2D(Art.Sound.Summon, 0.9f);
        }
    }
}
