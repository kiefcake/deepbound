using System.Collections.Generic;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public abstract class Interactable : NetworkBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();

        public virtual float Range => 2.6f;

        public abstract string Prompt(PlayerProgress who);

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
            Progression.RunLog.Note("use", GetType().Name, who);
            ServerUse(who);
        }

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

                float angle = Vector3.Angle(forward, to);
                if (angle > 70f) continue;
                float score = d + angle * 0.03f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }
}
