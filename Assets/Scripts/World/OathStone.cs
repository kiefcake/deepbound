using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Oath Room's weight (2026-10-20): a carved block heavy enough to hold down one of the
    /// <see cref="OathPlates"/>, so a lone delver can keep the oath. Carried like any <see cref="Carryable"/>
    /// (slow, no fighting) but never delivered anywhere: set it down on a plate.
    /// </summary>
    public class OathStone : Carryable
    {
        public override string ItemName => "the Oath Stone";
        public override float CarrySpeed => 0.8f;
        public override float DeliverRange => -1f;
        protected override Vector3 HoldOffset => new Vector3(0f, 0.95f, 0.55f);
        public override Color MarkerColor => new Color(0.55f, 0.75f, 1f);
        protected override bool DeliversAtWaystone => false;
        public override string CarryingLine => "Carrying the Oath Stone: set it down on a plate (F)";

        public override string Prompt(PlayerProgress who)
        {
            if (who == null) return null;
            if (Carrier.Value == who.NetworkObjectId) return "Set down the Oath Stone  (on a plate, it holds it down)";
            if (Carried || IsCarrying(who.NetworkObjectId)) return null;
            return "Lift the Oath Stone";
        }

        protected override void ServerDelivered(NetworkObject carrier, DungeonLayout layout) { }

        protected override void VisualTick(bool carried)
        {
            if (visual != null) visual.localRotation = carried ? Quaternion.identity : Quaternion.Euler(0f, 20f, 0f);
        }

        protected override void BuildVisual()
        {
            visual = new GameObject("Stone").transform;
            visual.SetParent(transform, false);
            // Pale carved stone, so it reads as the plates' twin and not a crate.
            var block = MeshKit.Part(visual, "Block", MeshKit.Box(new Vector3(0.62f, 0.5f, 0.62f)), Mat.Marble, new Vector3(0f, 0.25f, 0f));
            ArtLibrary.Tint(block.GetComponent<Renderer>(), new Color(0.86f, 0.88f, 0.92f));
            MeshKit.Part(visual, "Cap", MeshKit.Box(new Vector3(0.68f, 0.07f, 0.68f)), Mat.StoneWall, new Vector3(0f, 0.53f, 0f));
            MeshKit.Part(visual, "Base", MeshKit.Box(new Vector3(0.68f, 0.06f, 0.68f)), Mat.StoneWall, new Vector3(0f, 0.03f, 0f));
            // A glowing hand carved on every face, matching the plates, and a ring on top.
            for (int f = 0; f < 4; f++)
            {
                var rot = Quaternion.Euler(0f, f * 90f, 0f);
                var palm = MeshKit.Part(visual, "Palm", MeshKit.Box(new Vector3(0.2f, 0.22f, 0.02f)), Mat.Glow, rot * new Vector3(0f, 0.27f, 0.315f), new Vector3(0f, f * 90f, 0f));
                ArtLibrary.SetGlow(palm.GetComponent<Renderer>(), MarkerColor * 1.4f);
                palm.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var ring = MeshKit.Part(visual, "TopRing", MeshKit.Cylinder(0.2f, 0.2f, 0.015f, 14), Mat.Glow, new Vector3(0f, 0.57f, 0f));
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), MarkerColor);
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (float side in new[] { -1f, 1f })
                MeshKit.Part(visual, "Handle", MeshKit.Box(new Vector3(0.05f, 0.18f, 0.05f)), Mat.Steel, new Vector3(side * 0.34f, 0.3f, 0f));
        }
    }
}
