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
            MeshKit.Part(visual, "Block", MeshKit.Box(new Vector3(0.62f, 0.5f, 0.5f)), Mat.Marble, new Vector3(0f, 0.25f, 0f));
            MeshKit.Part(visual, "Cap", MeshKit.Box(new Vector3(0.68f, 0.07f, 0.56f)), Mat.StoneWall, new Vector3(0f, 0.53f, 0f));
            MeshKit.Part(visual, "Base", MeshKit.Box(new Vector3(0.68f, 0.06f, 0.56f)), Mat.StoneWall, new Vector3(0f, 0.03f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                // A glowing hand carved on each face, matching the plates.
                var palm = MeshKit.Part(visual, "Palm", MeshKit.Box(new Vector3(0.2f, 0.22f, 0.02f)), Mat.Glow, new Vector3(0f, 0.27f, side * 0.255f));
                ArtLibrary.SetGlow(palm.GetComponent<Renderer>(), MarkerColor * 1.1f);
                palm.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                MeshKit.Part(visual, "Ring", MeshKit.Box(new Vector3(0.05f, 0.18f, 0.05f)), Mat.Steel, new Vector3(side * 0.33f, 0.3f, 0f));
            }
        }
    }
}
