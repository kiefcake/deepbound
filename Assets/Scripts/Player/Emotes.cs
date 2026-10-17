using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// H: raise a hand. On its own it is a wave; two players doing it near each other within
    /// a couple of seconds is a HIGH FIVE, and both fight a little harder for a while
    /// (Brothers in Arms: +10% damage for 20 s).
    /// </summary>
    public class Emotes : NetworkBehaviour
    {
        public const float Window = 2f, Reach = 3.5f, BuffSeconds = 20f;

        private float readyAt, lastRaisedAt = -10f;
        private PlayerLife life;
        private NetworkPlayer player;

        /// <summary>Server clock time the buff runs out (synced so the HUD can show it).</summary>
        public readonly NetworkVariable<double> BrothersUntil = new NetworkVariable<double>();

        public bool Brothers => NetworkManager != null && NetworkManager.IsListening && NetworkManager.ServerTime.Time < BrothersUntil.Value;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            player = GetComponent<NetworkPlayer>();
        }

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.hKey.wasPressedThisFrame || Time.time < readyAt || life.Downed.Value) return;
            readyAt = Time.time + 1.5f;
            RaiseRpc();
        }

        [Rpc(SendTo.Server)]
        private void RaiseRpc()
        {
            lastRaisedAt = Time.time;
            foreach (var other in FindObjectsByType<Emotes>(FindObjectsSortMode.None))
            {
                if (other == this || Time.time - other.lastRaisedAt > Window) continue;
                if (Vector3.Distance(other.transform.position, transform.position) > Reach) continue;
                double until = NetworkManager.ServerTime.Time + BuffSeconds;
                BrothersUntil.Value = until;
                other.BrothersUntil.Value = until;
                other.lastRaisedAt = -10f;
                lastRaisedAt = -10f;
                HighFiveRpc((transform.position + other.transform.position) / 2f + Vector3.up * 1.7f, name, other.name);
                return;
            }
            WaveRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WaveRpc()
        {
            player.Rig?.Play(RigAction.Cast, 0.6f);
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f, 0.35f, 1.6f);
            // No ally to slap? The camp folk answer a raised hand in their own ways.
            if (World.DungeonDirector.InLobby)
            {
                float best = 3.2f * 3.2f; int who = -1;
                foreach (var (id, at) in World.Lobby.FolkBodies)
                {
                    var d = at - transform.position; d.y = 0f;
                    if (d.sqrMagnitude < best) { best = d.sqrMagnitude; who = (int)id; }
                }
                if (who >= 0) UI.CampChatter.Waved((World.FolkId)who, name, GetComponent<Progression.PlayerProgress>(), transform.position);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HighFiveRpc(Vector3 at, string a, string b)
        {
            Sfx.Play(Sound.Block, at, 1f, 1.8f);
            Sfx.Play(Sound.Relic, at, 0.7f, 1.3f);
            Vfx.Flash(at, new Color(1f, 0.85f, 0.4f), 5f, 6f, 0.3f);
            Vfx.Embers(at, 16, new Color(1f, 0.85f, 0.4f), 4f, 0.05f, 0.6f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "HIGH FIVE!", new Color(1f, 0.85f, 0.4f));
            UI.GameHud.Feed(a + " and " + b + ": Brothers in Arms (+10% damage)", new Color(1f, 0.85f, 0.4f));
        }
    }
}
