using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A wall lever for a vault puzzle. Pulled, it stays down for a while and then springs
    /// back — so opening a vault means getting every lever down at the same time, which is
    /// easy with friends and a sprint on your own. Once the vault opens they lock down.
    /// </summary>
    public class Lever : Interactable
    {
        public const float HoldSeconds = 15f;

        public readonly NetworkVariable<bool> Pulled = new NetworkVariable<bool>();
        public bool Locked { get; private set; }
        private float releaseAt;

        private Transform handle;
        private Renderer gem;
        private float angle;

        public override string Prompt(PlayerProgress who) => Pulled.Value ? null : "Pull the lever";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            Pulled.Value = true;
            releaseAt = Time.time + HoldSeconds;
            PullFxRpc();
        }

        /// <summary>Server: the vault opened; keep every lever down for good.</summary>
        public void ServerLock() => Locked = true;

        private void Update()
        {
            angle = Mathf.MoveTowards(angle, Pulled.Value ? 60f : -40f, Time.deltaTime * 300f);
            if (handle != null) handle.localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (gem != null)
            {
                // The gem counts down: bright when freshly pulled, dimming toward the reset.
                ArtLibrary.SetGlow(gem, Pulled.Value ? new Color(0.4f, 1f, 0.4f) * 2f : new Color(1f, 0.3f, 0.1f) * 1.2f);
            }

            if (IsServer && Pulled.Value && !Locked && Time.time >= releaseAt)
            {
                Pulled.Value = false;
                ReleaseFxRpc();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PullFxRpc() => Sfx.Play(Sound.Bash, transform.position + Vector3.up, 0.8f, 0.8f);

        [Rpc(SendTo.ClientsAndHost)]
        private void ReleaseFxRpc() => Sfx.Play(Sound.Block, transform.position + Vector3.up, 0.6f, 1.5f);

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Plate", MeshKit.Box(new Vector3(0.5f, 0.7f, 0.1f)), Mat.Steel, new Vector3(0f, 1.3f, 0f));
            handle = MeshKit.Pivot(transform, "Handle", new Vector3(0f, 1.3f, 0.1f));
            MeshKit.Part(handle, "Shaft", MeshKit.Cylinder(0.03f, 0.03f, 0.55f, 6), Mat.Steel, Vector3.zero);
            MeshKit.Part(handle, "Knob", MeshKit.Ball(0.07f, 8, 6), Mat.Leather, new Vector3(0f, 0.55f, 0f));
            gem = MeshKit.Part(transform, "Gem", MeshKit.Ball(0.06f, 8, 6), Mat.Glow, new Vector3(0f, 1.75f, 0.06f)).GetComponent<Renderer>();
        }
    }
}
