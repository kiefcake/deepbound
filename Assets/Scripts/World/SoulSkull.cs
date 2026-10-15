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
    /// An ally picks it up with F. While carrying it they move slower and cannot fight, and a heavy
    /// blow knocks it from their hands. Standing at the start room's waystone for
    /// <see cref="RitualSeconds"/> without being hurt brings the dead player back.
    ///
    /// Server-authoritative: the position is not network-transformed. A resting skull sits at
    /// <see cref="Rest"/>; a carried one follows its carrier on every peer.
    /// </summary>
    public class SoulSkull : Interactable
    {
        public const float RitualSeconds = 2.5f, RitualRange = 3.2f, CarrySpeed = 0.72f;
        private const float DropFraction = 0.15f; // a single hit this big (of max HP) knocks it loose

        public static readonly List<SoulSkull> Skulls = new List<SoulSkull>();

        public readonly NetworkVariable<ulong> Soul = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<ulong> Carrier = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<Vector3> Rest = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<float> Ritual = new NetworkVariable<float>();

        private Transform visual;
        private Light glow;
        private float carrierHealthLast;

        public bool Carried => Carrier.Value != ulong.MaxValue;

        /// <summary>Is this player carrying a skull (slowed, hands full)?</summary>
        public static bool IsCarrying(ulong playerId)
        {
            foreach (var s in Skulls) if (s != null && s.Carrier.Value == playerId) return true;
            return false;
        }

        public static SoulSkull For(ulong soulId)
        {
            foreach (var s in Skulls) if (s != null && s.Soul.Value == soulId) return s;
            return null;
        }

        public string SoulName => NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Soul.Value, out var o) ? o.name : "a fallen ally";

        public override string Prompt(PlayerProgress who)
        {
            if (who == null || who.NetworkObjectId == Soul.Value) return null;
            if (Carrier.Value == who.NetworkObjectId) return "Set down " + SoulName + "'s soul skull";
            if (Carried || IsCarrying(who.NetworkObjectId)) return null;
            return "Carry " + SoulName + "'s soul skull to the waystone";
        }

        public override float Range => 2.4f;

        protected override void ServerUse(PlayerProgress who)
        {
            if (Carrier.Value == who.NetworkObjectId) { ServerDrop(who.transform.position); return; }
            if (Carried) return;
            Carrier.Value = who.NetworkObjectId;
            carrierHealthLast = who.GetComponent<Combat.Health>().Current.Value;
            Ritual.Value = 0f;
            PickupFxRpc();
        }

        /// <summary>Server: let go of it here.</summary>
        public void ServerDrop(Vector3 at)
        {
            Carrier.Value = ulong.MaxValue;
            Ritual.Value = 0f;
            Rest.Value = at + Vector3.up * 0.05f;
        }

        /// <summary>Server (tests): hand the skull straight to a carrier.</summary>
        public void ServerDebugCarry(PlayerProgress who) => ServerUse(who);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Skulls.Add(this);
            if (IsServer) Rest.Value = transform.position;
            BuildVisual();
        }

        public override void OnNetworkDespawn()
        {
            Skulls.Remove(this);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            Skulls.Remove(this);
            base.OnDestroy();
        }

        private void Update()
        {
            var carrier = CarrierObject();
            if (carrier != null)
            {
                // Held against the chest, a little ahead.
                transform.position = carrier.transform.position + Vector3.up * 1.15f + carrier.transform.forward * 0.45f;
                transform.rotation = carrier.transform.rotation;
            }
            else
            {
                transform.position = Rest.Value;
            }
            if (visual != null)
            {
                float bob = carrier != null ? 0f : 0.35f + Mathf.Sin(Time.time * 2f) * 0.08f;
                visual.localPosition = Vector3.up * bob;
                if (carrier == null) visual.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.6f) * 50f + FaceViewer(), 0f);
                else visual.localRotation = Quaternion.identity;
            }
            if (glow != null) glow.intensity = 1.4f + Ritual.Value * 1.6f + Mathf.Sin(Time.time * 5f) * 0.2f;

            if (IsServer) ServerTick(carrier);
        }

        /// <summary>Yaw that turns the skull's face toward the local camera.</summary>
        private float FaceViewer()
        {
            var cam = ViewCamera.Current;
            if (cam == null) return 0f;
            var to = cam.transform.position - transform.position;
            to.y = 0f;
            return to.sqrMagnitude < 0.01f ? 0f : Quaternion.LookRotation(to).eulerAngles.y;
        }

        private NetworkObject CarrierObject()
        {
            if (!Carried || NetworkManager == null) return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Carrier.Value, out var o) ? o : null;
        }

        private void ServerTick(NetworkObject carrier)
        {
            if (!Carried) return;
            var life = carrier != null ? carrier.GetComponent<PlayerLife>() : null;
            if (life == null || life.Downed.Value)
            {
                ServerDrop(carrier != null ? carrier.transform.position : Rest.Value);
                return;
            }

            // Hurt: the ritual breaks; a heavy blow knocks the skull loose.
            float hp = life.Health.Current.Value;
            if (hp < carrierHealthLast - 0.01f)
            {
                Ritual.Value = 0f;
                if (carrierHealthLast - hp >= life.Health.Max.Value * DropFraction)
                {
                    ServerDrop(carrier.transform.position + carrier.transform.forward * 0.8f);
                    KnockedLooseRpc();
                    return;
                }
            }
            carrierHealthLast = hp;

            var layout = DungeonDirector.Layout;
            if (layout == null) return;
            var to = layout.WaystonePosition - carrier.transform.position;
            to.y = 0f;
            if (to.magnitude > RitualRange) { if (Ritual.Value > 0f) Ritual.Value = 0f; return; }
            Ritual.Value = Mathf.Min(RitualSeconds, Ritual.Value + Time.deltaTime);
            if (Ritual.Value < RitualSeconds) return;

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
        private void PickupFxRpc() => Sfx.Play(Sound.Whisper, transform.position, 0.8f);

        [Rpc(SendTo.ClientsAndHost)]
        private void KnockedLooseRpc()
        {
            Sfx.Play(Sound.BoneBreak, transform.position, 0.7f);
            UI.GameHud.Feed("The soul skull is knocked loose!", new Color(0.75f, 0.55f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReturnedRpc(string who)
        {
            Sfx.Play2D(Sound.Revive, 0.9f);
            UI.GameHud.ShowTitleCard(who.ToUpperInvariant() + " RETURNS", "The waystone gives back what the dark took");
        }

        private void BuildVisual()
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
