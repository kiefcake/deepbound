using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// HOLDING STILL FOR A CONVERSATION (2026-11-08, the user: "npcs must not move while in dialogue"). Errands run on the
    /// server clock (<see cref="FolkRounds"/>), the same on every peer. While anyone talks or trades with a person, the
    /// server stops that person's own clock: <see cref="FolkClock"/> = server time minus everything they have spent held,
    /// so they stand where they were and pick up the errand where they left it. Synced: the held bits, when each hold
    /// began, and the total held so far. A hold whose players have gone (disconnect) or that runs past
    /// <see cref="MaxHoldSeconds"/> lets go on its own.
    /// </summary>
    public partial class DungeonDirector
    {
        public const int FolkSlots = 16;
        public const float MaxHoldSeconds = 180f;

        public readonly NetworkVariable<ushort> FolkHeld = new NetworkVariable<ushort>();
        public NetworkList<double> FolkHeldAt = new NetworkList<double>();
        public NetworkList<double> FolkPausedTotal = new NetworkList<double>();
        private readonly Dictionary<int, HashSet<ulong>> talkers = new Dictionary<int, HashSet<ulong>>();

        private static double ServerNow => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        /// <summary>A person's own errand clock (any peer): the server clock less the time they have been held.</summary>
        public static double FolkClock(FolkId who, double clock)
        {
            var d = Instance;
            int i = (int)who;
            if (d == null || i >= d.FolkPausedTotal.Count || i >= d.FolkHeldAt.Count) return clock;
            double t = clock - d.FolkPausedTotal[i];
            // Measured on the raw server clock (the rounds clock can carry a test shift; the hold began on the raw one).
            if ((d.FolkHeld.Value & (1 << i)) != 0) t -= System.Math.Max(0.0, ServerNow - d.FolkHeldAt[i]);
            return t;
        }

        public bool IsFolkHeld(FolkId who) => (FolkHeld.Value & (1 << (int)who)) != 0;

        private void EnsureHoldLists()
        {
            while (FolkHeldAt.Count < FolkSlots) FolkHeldAt.Add(0);
            while (FolkPausedTotal.Count < FolkSlots) FolkPausedTotal.Add(0);
        }

        /// <summary>Server: <paramref name="client"/> starts or stops talking/trading with <paramref name="who"/>.</summary>
        public void ServerHoldFolk(FolkId who, ulong client, bool on)
        {
            int i = (int)who;
            if (i < 0 || i >= FolkSlots) return;
            EnsureHoldLists();
            if (!talkers.TryGetValue(i, out var set)) talkers[i] = set = new HashSet<ulong>();
            bool before = set.Count > 0;
            if (on) set.Add(client); else set.Remove(client);
            Apply(i, before, set.Count > 0);
        }

        private void Apply(int i, bool before, bool after)
        {
            if (before == after) return;
            double now = ServerNow;
            if (after)
            {
                FolkHeldAt[i] = now;
                FolkHeld.Value |= (ushort)(1 << i);
            }
            else
            {
                FolkPausedTotal[i] += System.Math.Max(0.0, now - FolkHeldAt[i]);
                FolkHeld.Value &= (ushort)~(1 << i);
            }
        }

        /// <summary>Server, every frame: holds whose players left, or that ran too long, let go.</summary>
        private void TickFolkHolds()
        {
            if (FolkHeld.Value == 0 || NetworkManager.Singleton == null) return;
            var connected = NetworkManager.Singleton.ConnectedClientsIds;
            foreach (var kv in talkers)
            {
                bool before = kv.Value.Count > 0;
                if (!before) continue;
                kv.Value.RemoveWhere(c => !Contains(connected, c));
                bool tooLong = FolkHeldAt.Count > kv.Key && ServerNow - FolkHeldAt[kv.Key] > MaxHoldSeconds;
                if (tooLong) kv.Value.Clear();
                Apply(kv.Key, before, kv.Value.Count > 0);
            }
        }

        private static bool Contains(IReadOnlyList<ulong> ids, ulong c)
        {
            for (int k = 0; k < ids.Count; k++) if (ids[k] == c) return true;
            return false;
        }
    }
}
