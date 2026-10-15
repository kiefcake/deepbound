using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// What is left of a player who bled out (Dark and Darker's soul heart, carried R.E.P.O.-style).
    /// An ally carries it (<see cref="Carryable"/>) to the start room's waystone; delivered, the
    /// dead player gets up beside the stone at half health.
    /// </summary>
    public class SoulSkull : Carryable
    {
        public const float RitualSeconds = 2.5f;

        /// <summary>The soul skulls on this peer (a view of <see cref="Carryable.All"/>).</summary>
        public static IEnumerable<SoulSkull> Skulls
        {
            get { foreach (var c in All) if (c is SoulSkull s) yield return s; }
        }

        public readonly NetworkVariable<ulong> Soul = new NetworkVariable<ulong>(ulong.MaxValue);

        private Light glow;

        public static SoulSkull For(ulong soulId)
        {
            foreach (var s in Skulls) if (s != null && s.Soul.Value == soulId) return s;
            return null;
        }

        public string SoulName => NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Soul.Value, out var o) ? o.name : "a fallen ally";
        public override string ItemName => SoulName + "'s soul skull";
        public override float DeliverSeconds => RitualSeconds;
        protected override bool CanCarry(PlayerProgress who) => who.NetworkObjectId != Soul.Value;

        protected override void VisualTick(bool carried)
        {
            if (visual != null)
            {
                float bob = carried ? 0f : 0.35f + Mathf.Sin(Time.time * 2f) * 0.08f;
                visual.localPosition = Vector3.up * bob;
                visual.localRotation = carried ? Quaternion.identity : Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.6f) * 50f + FaceViewer(), 0f);
            }
            if (glow != null) glow.intensity = 1.4f + Ritual.Value * 1.6f + Mathf.Sin(Time.time * 5f) * 0.2f;
        }

        protected override void ServerDelivered(NetworkObject carrier, DungeonLayout layout)
        {
            // The waystone answers: the dead get up beside it.
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Soul.Value, out var soulObj) && soulObj.TryGetComponent<PlayerLife>(out var soul))
            {
                var at = layout.WaystonePosition + (layout.StartRoom.Center - layout.WaystonePosition).normalized * 2f + Vector3.up * 0.1f;
                // Announce first: reviving despawns this skull.
                ReturnedRpc(soulObj.name);
                soulObj.GetComponent<NetworkPlayer>().RescueRpc(at);
                soul.ServerRevive(0.5f);
                return;
            }
            if (NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReturnedRpc(string who)
        {
            Sfx.Play2D(Sound.Revive, 0.9f);
            UI.GameHud.ShowTitleCard(who.ToUpperInvariant() + " RETURNS", "The waystone gives back what the dark took");
        }

        protected override void BuildVisual()
        {
            visual = new GameObject("Skull").transform;
            visual.SetParent(transform, false);
            // Spectral: faintly self-lit, so torchlight cannot turn it into an orange ball.
            var ghost = new Color(0.78f, 0.74f, 0.98f) * 0.45f;
            var cranium = MeshKit.Part(visual, "Cranium", MeshKit.Ball(0.17f, 10, 8), Mat.Glow, Vector3.zero, default, new Vector3(1f, 1.05f, 1.15f));
            ArtLibrary.SetGlow(cranium.GetComponent<Renderer>(), ghost);
            var jaw = MeshKit.Part(visual, "Jaw", MeshKit.Box(new Vector3(0.2f, 0.07f, 0.16f)), Mat.Glow, new Vector3(0f, -0.15f, 0.06f));
            ArtLibrary.SetGlow(jaw.GetComponent<Renderer>(), ghost * 0.8f);
            // Dark sockets and nose, with a burning pupil in each eye: a skull from any side but the back.
            MeshKit.Part(visual, "Nose", MeshKit.Box(new Vector3(0.04f, 0.05f, 0.03f)), Mat.Dark, new Vector3(0f, -0.06f, 0.19f));
            for (int i = -1; i <= 1; i += 2)
            {
                MeshKit.Part(visual, "Socket", MeshKit.Ball(0.055f, 8, 6), Mat.Dark, new Vector3(0.068f * i, 0.01f, 0.15f));
                var eye = MeshKit.Part(visual, "Eye", MeshKit.Ball(0.026f, 6, 4), Mat.Glow, new Vector3(0.068f * i, 0.01f, 0.195f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.7f, 0.45f, 1f) * 3f);
            }
            // A ring of soul-fire round it, so it reads from across a room.
            var halo = MeshKit.Part(visual, "Halo", MeshKit.Cylinder(0.3f, 0.3f, 0.02f, 16), Mat.Glow, Vector3.up * -0.25f);
            ArtLibrary.SetGlow(halo.GetComponent<Renderer>(), new Color(0.45f, 0.3f, 0.9f) * 0.9f);
            halo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cranium.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = new GameObject("SoulLight").AddComponent<Light>();
            glow.transform.SetParent(visual, false);
            glow.color = new Color(0.6f, 0.4f, 1f);
            glow.range = 5f;
            glow.intensity = 1.4f;
            glow.shadows = LightShadows.None;
        }
    }
}
