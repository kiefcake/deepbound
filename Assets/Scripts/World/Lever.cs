using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public class Lever : Interactable
    {
        public readonly NetworkVariable<bool> Pulled = new NetworkVariable<bool>();
        public bool Locked { get; private set; }

        private Transform handle;
        private Renderer gem;
        private float angle;

        public override string Prompt(PlayerProgress who) => Pulled.Value ? null : "Pull the vault lever";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            Pulled.Value = true;
            PullFxRpc();
        }

        public void ServerLock() => Locked = true;

        private void Update()
        {
            angle = Mathf.MoveTowards(angle, Pulled.Value ? 60f : -40f, Time.deltaTime * 300f);
            if (handle != null) handle.localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (gem != null)
            {
                ArtLibrary.SetGlow(gem, Pulled.Value ? new Color(0.4f, 1f, 0.4f) * 2f : new Color(1f, 0.3f, 0.1f) * 1.2f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PullFxRpc() { Sfx.Play(Sound.Bash, transform.position + Vector3.up, 0.8f, 0.8f); Sfx.Play(Sound.Chain, transform.position + Vector3.up, 0.6f, 1f); }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Surround", MeshKit.Box(new Vector3(0.7f, 0.95f, 0.06f)), Mat.StoneWall, new Vector3(0f, 1.3f, -0.02f));
            MeshKit.Part(transform, "Plate", MeshKit.Box(new Vector3(0.5f, 0.7f, 0.08f)), Mat.Steel, new Vector3(0f, 1.3f, 0.03f));
            foreach (float x in new[] { -0.2f, 0.2f })
                foreach (float y in new[] { 1.0f, 1.6f })
                    MeshKit.Part(transform, "Rivet", MeshKit.Ball(0.025f, 6, 4), Mat.Steel, new Vector3(x, y, 0.08f));
            handle = MeshKit.Pivot(transform, "Handle", new Vector3(0f, 1.3f, 0.1f));
            MeshKit.Part(handle, "Shaft", MeshKit.Cylinder(0.03f, 0.03f, 0.55f, 6), Mat.Steel, Vector3.zero);
            MeshKit.Part(handle, "Knob", MeshKit.Ball(0.07f, 8, 6), Mat.Leather, new Vector3(0f, 0.55f, 0f));
            gem = MeshKit.Part(transform, "Gem", MeshKit.Ball(0.06f, 8, 6), Mat.Glow, new Vector3(0f, 1.75f, 0.06f)).GetComponent<Renderer>();
        }
    }
}
