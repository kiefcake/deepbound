using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Lightless Hall's torch (2026-10-20): the only light in a room built without any. Carry it (F) and
    /// the hall lights up around you, but your hands are full: you cannot fight. With friends, one carries
    /// the light and the others fight in it; alone, set it down and fight inside its glow. Never delivered
    /// anywhere; it burns where it rests too.
    /// </summary>
    public class HandTorch : Carryable
    {
        public const float LightRange = 11f;

        private Light flame;
        private bool torchDeed;
        private Transform flameRoot;

        public override string ItemName => "the hand torch";
        public override float CarrySpeed => 1f;
        public override float DeliverRange => -1f;
        protected override Vector3 HoldOffset => new Vector3(0.35f, 1.55f, 0.35f);
        public override Color MarkerColor => new Color(1f, 0.7f, 0.35f);

        /// <summary>For tests: its light is burning.</summary>
        public bool Lit => flame != null && flame.enabled && flame.intensity > 0.1f;

        public override string Prompt(PlayerProgress who)
        {
            if (who == null) return null;
            if (Carrier.Value == who.NetworkObjectId) return "Set down the torch  (to fight in its light)";
            if (Carried || IsCarrying(who.NetworkObjectId)) return null;
            return "Take up the torch  (whoever carries it cannot fight)";
        }

        protected override void ServerDelivered(NetworkObject carrier, DungeonLayout layout) { }

        protected override void VisualTick(bool carried)
        {
            if (visual != null) visual.localRotation = carried ? Quaternion.Euler(-12f, 0f, 8f) : Quaternion.identity;
            if (carried && !torchDeed && Player.NetworkPlayer.Local != null && Carrier.Value == Player.NetworkPlayer.Local.NetworkObjectId)
            {
                torchDeed = true;
                Progression.Deeds.Earn(Progression.Deeds.Deed.Torchbearer, "Carried the light");
            }
            if (flame != null) flame.intensity = 2.4f + Mathf.Sin(Time.time * 11f) * 0.2f + Mathf.Sin(Time.time * 4.3f) * 0.15f;
        }

        protected override void BuildVisual()
        {
            visual = new GameObject("Torch").transform;
            visual.SetParent(transform, false);
            // Resting, it stands in a little iron tripod; carried, the haft is in the hand.
            MeshKit.Part(visual, "Haft", MeshKit.Cylinder(0.035f, 0.03f, 0.75f, 6), Mat.Wood, Vector3.zero);
            MeshKit.Part(visual, "Wrap", MeshKit.Cylinder(0.055f, 0.06f, 0.14f, 7), Mat.Cloth, new Vector3(0f, 0.68f, 0f));
            foreach (float a in new[] { 0f, 120f, 240f })
                MeshKit.Part(visual, "Leg", MeshKit.Box(new Vector3(0.025f, 0.4f, 0.025f)), Mat.Steel, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.15f, 0.12f), new Vector3(-20f, a, 0f));
            flameRoot = MeshKit.Pivot(visual, "FlameRoot", new Vector3(0f, 0.86f, 0f));
            var core = MeshKit.Part(flameRoot, "Flame", MeshKit.Lathe("handFlame", new[] { new Vector2(0f, -0.04f), new Vector2(0.08f, 0.04f), new Vector2(0.05f, 0.18f), new Vector2(0f, 0.3f) }, 8), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 1.8f);
            core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flame = new GameObject("TorchLight").AddComponent<Light>();
            flame.transform.SetParent(flameRoot, false);
            flame.transform.localPosition = Vector3.up * 0.15f;
            flame.type = LightType.Point;
            flame.color = new Color(1f, 0.68f, 0.38f);
            flame.range = LightRange;
            flame.intensity = 2.4f;
            // The one moving shadow in the room: it is what makes a lightless hall feel lightless.
            flame.shadows = LightShadows.Soft;
        }
    }
}
