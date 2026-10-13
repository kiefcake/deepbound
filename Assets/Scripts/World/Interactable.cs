using System.Collections.Generic;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Anything in the dungeon you use with F: chests, altars. The owner finds the nearest
    /// one it is looking at and asks the server to use it; the server checks range and
    /// state, so two players opening the same chest at once get one chest's worth of loot.
    /// </summary>
    public abstract class Interactable : NetworkBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();

        public virtual float Range => 2.6f;

        /// <summary>What the HUD says, e.g. "Open chest". Null when it cannot be used right now.</summary>
        public abstract string Prompt(PlayerProgress who);

        /// <summary>Server: do the thing.</summary>
        protected abstract void ServerUse(PlayerProgress who);

        public override void OnNetworkSpawn() => All.Add(this);
        public override void OnNetworkDespawn() => All.Remove(this);

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        [Rpc(SendTo.Server)]
        public void UseRpc(NetworkObjectReference user)
        {
            if (!user.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1f) return;
            if (Prompt(who) == null) return;
            ServerUse(who);
        }

        /// <summary>The usable interactable nearest the player's aim, within range, or null.</summary>
        public static Interactable Nearest(Vector3 eye, Vector3 forward, PlayerProgress who)
        {
            Interactable best = null;
            float bestScore = float.MaxValue;
            foreach (var i in All)
            {
                if (i == null || i.Prompt(who) == null) continue;
                var to = i.transform.position + Vector3.up * 0.6f - eye;
                float d = to.magnitude;
                if (d > i.Range + 0.8f) continue;
                // Prefer what you are looking at, then what is closest.
                float angle = Vector3.Angle(forward, to);
                if (angle > 70f) continue;
                float score = d + angle * 0.03f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }
}
