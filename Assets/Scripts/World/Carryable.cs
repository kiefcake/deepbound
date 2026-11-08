using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Something a player carries in both hands to the start room's waystone (R.E.P.O.'s carrying):
    /// F picks it up or sets it down. The carrier moves slower (<see cref="CarrySpeed"/>) and cannot
    /// fight, and a heavy blow (<see cref="DropFraction"/> of max HP in one hit) knocks it loose.
    /// Standing unhurt at the waystone for <see cref="DeliverSeconds"/> delivers it.
    ///
    /// Server-authoritative and not network-transformed: a resting one sits at <see cref="Rest"/>;
    /// a carried one follows its carrier on every peer. Subclasses: <see cref="SoulSkull"/>, <see cref="Reliquary"/>.
    /// </summary>
    public abstract class Carryable : Interactable
    {
        public static readonly List<Carryable> All = new List<Carryable>();

        public readonly NetworkVariable<ulong> Carrier = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<Vector3> Rest = new NetworkVariable<Vector3>();
        /// <summary>Seconds of the delivery spent so far at the waystone.</summary>
        public readonly NetworkVariable<float> Ritual = new NetworkVariable<float>();

        protected Transform visual;
        private float carrierHealthLast;

        public bool Carried => Carrier.Value != ulong.MaxValue;

        public virtual float CarrySpeed => 0.72f;
        protected virtual float DropFraction => 0.15f;
        public virtual float DeliverSeconds => 2.5f;
        public virtual float DeliverRange => 3.2f;
        /// <summary>Where it is held, relative to the carrier: (side, height, ahead).</summary>
        protected virtual Vector3 HoldOffset => new Vector3(0f, 1.15f, 0.45f);
        public abstract string ItemName { get; }
        public virtual Color MarkerColor => new Color(0.78f, 0.6f, 1f);
        protected virtual bool CanCarry(PlayerProgress who) => true;
        /// <summary>Delivered by standing at the start room's waystone (quest cargo goes out on the grave lift instead).</summary>
        protected virtual bool DeliversAtWaystone => true;
        /// <summary>The carrier may put it down now (quest cargo cannot be put down in the guardian's sealed arena).</summary>
        protected virtual bool CanSetDown => true;
        /// <summary>The prompt while carrying when it cannot be set down.</summary>
        protected virtual string HoldOnText => "Hold on to " + ItemName;
        /// <summary>The prompt's verb for picking it up.</summary>
        protected virtual string CarryText => "Carry " + ItemName + " to the waystone";

        protected abstract void BuildVisual();
        /// <summary>Server: it reached the waystone. Despawn it (or let the subclass do it).</summary>
        protected abstract void ServerDelivered(NetworkObject carrier, DungeonLayout layout);
        protected virtual void ServerCarriedTick(NetworkObject carrier) { }
        /// <summary>Server: the carrier lost <paramref name="amount"/> health; <paramref name="knockedLoose"/> if it fell.</summary>
        protected virtual void ServerCarrierHurt(float amount, bool knockedLoose) { }
        /// <summary>Every peer, each frame: animate the visual.</summary>
        protected virtual void VisualTick(bool carried) { }

        /// <summary>Is this player carrying anything (slowed, hands full)?</summary>
        public static bool IsCarrying(ulong playerId)
        {
            foreach (var c in All) if (c != null && c.Carrier.Value == playerId) return true;
            return false;
        }

        /// <summary>The carrying slow for this player (1 when empty-handed).</summary>
        public static float SpeedFor(ulong playerId)
        {
            float s = 1f;
            foreach (var c in All) if (c != null && c.Carrier.Value == playerId) s = Mathf.Min(s, c.CarrySpeed);
            return s;
        }

        public override string Prompt(PlayerProgress who)
        {
            if (who == null || !CanCarry(who)) return null;
            if (Carrier.Value == who.NetworkObjectId) return CanSetDown ? "Set down " + ItemName : HoldOnText;
            if (Carried || IsCarrying(who.NetworkObjectId)) return null;
            return CarryText;
        }

        public override float Range => 2.4f;

        protected override void ServerUse(PlayerProgress who)
        {
            if (Carrier.Value == who.NetworkObjectId) { if (CanSetDown) ServerDrop(who.transform.position); return; }
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

        /// <summary>Server (tests): hand it straight to a carrier.</summary>
        public void ServerDebugCarry(PlayerProgress who) => ServerUse(who);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);
            if (IsServer) Rest.Value = transform.position;
            BuildVisual();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        protected virtual void Update()
        {
            var carrier = CarrierObject();
            if (carrier != null)
            {
                var o = HoldOffset;
                transform.position = carrier.transform.position + carrier.transform.right * o.x + Vector3.up * o.y + carrier.transform.forward * o.z;
                transform.rotation = carrier.transform.rotation;
            }
            else
            {
                transform.position = Rest.Value;
            }
            VisualTick(carrier != null);
            if (IsServer) ServerTick(carrier);
        }

        protected NetworkObject CarrierObject()
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

            // Hurt: the delivery breaks; a heavy blow knocks it loose.
            float hp = life.Health.Current.Value;
            if (hp < carrierHealthLast - 0.01f)
            {
                float lost = carrierHealthLast - hp;
                Ritual.Value = 0f;
                bool loose = lost >= life.Health.Max.Value * DropFraction;
                ServerCarrierHurt(lost, loose);
                if (loose)
                {
                    ServerDrop(carrier.transform.position + carrier.transform.forward * 0.8f);
                    KnockedLooseRpc(ItemName);
                    return;
                }
            }
            carrierHealthLast = hp;
            ServerCarriedTick(carrier);

            var layout = DungeonDirector.Layout;
            if (layout == null || !DeliversAtWaystone) return;
            var to = layout.WaystonePosition - carrier.transform.position;
            to.y = 0f;
            if (to.magnitude > DeliverRange) { if (Ritual.Value > 0f) Ritual.Value = 0f; return; }
            Ritual.Value = Mathf.Min(DeliverSeconds, Ritual.Value + Time.deltaTime);
            if (Ritual.Value < DeliverSeconds) return;
            ServerDelivered(carrier, layout);
        }

        /// <summary>Yaw that turns the visual toward the local camera.</summary>
        protected float FaceViewer()
        {
            var cam = ViewCamera.Current;
            if (cam == null) return 0f;
            var to = cam.transform.position - transform.position;
            to.y = 0f;
            return to.sqrMagnitude < 0.01f ? 0f : Quaternion.LookRotation(to).eulerAngles.y;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PickupFxRpc() => Sfx.Play(Sound.Whisper, transform.position, 0.8f);

        [Rpc(SendTo.ClientsAndHost)]
        private void KnockedLooseRpc(string what)
        {
            Sfx.Play(Sound.BoneBreak, transform.position, 0.7f);
            UI.GameHud.Feed(char.ToUpper(what[0]) + what.Substring(1) + " is knocked loose!", MarkerColor);
        }
    }
}
