using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A WEIGHT in the Scales room (2026-11-08, the activities plan's weight puzzle; <see cref="PuzzleRoom"/>, kind Scales):
    /// an iron-bound clay urn worth <see cref="Weight"/> (1-7), shown by its size and by that many gold studs round its
    /// neck. Carried like any <see cref="Carryable"/> (the heavier, the slower) and never delivered: set it down by the
    /// scale's empty pan and it settles onto the pan (<see cref="PuzzleRoom"/> moves it).
    /// </summary>
    public class ScaleWeight : Carryable
    {
        public readonly NetworkVariable<byte> Weight = new NetworkVariable<byte>(1);
        public readonly NetworkVariable<int> Group = new NetworkVariable<int>(-1);

        public override string ItemName => "a weight of " + Weight.Value;
        public override float CarrySpeed => 0.94f - 0.04f * Weight.Value;
        public override float DeliverRange => -1f;
        protected override bool DeliversAtWaystone => false;
        protected override Vector3 HoldOffset => new Vector3(0f, 0.8f, 0.55f);
        public override Color MarkerColor => new Color(0.95f, 0.75f, 0.4f);
        public override string CarryingLine => "Carrying " + ItemName + ": set it down by the scale's empty pan (F)";

        private byte builtWeight;

        public override string Prompt(PlayerProgress who)
        {
            if (who == null) return null;
            if (Carrier.Value == who.NetworkObjectId) return "Set down the weight (" + Weight.Value + ")  -  by the empty pan, it goes on the scale";
            if (Carried || IsCarrying(who.NetworkObjectId)) return null;
            return "Lift the weight  (" + Weight.Value + ")";
        }

        protected override void ServerDelivered(NetworkObject carrier, DungeonLayout layout) { }

        protected override void VisualTick(bool carried)
        {
            // The weight is set after spawn: build again once it arrives.
            if (Weight.Value != builtWeight) { if (visual != null) Destroy(visual.gameObject); BuildVisual(); }
        }

        protected override void BuildVisual()
        {
            builtWeight = Weight.Value;
            int w = Mathf.Clamp(builtWeight, 1, 9);
            visual = new GameObject("Weight").transform;
            visual.SetParent(transform, false);
            float r = 0.13f + 0.022f * w, hgt = 0.3f + 0.045f * w;
            // A squat clay urn on a foot, an iron band at the belly and the lip, and the studs at the neck.
            var body = MeshKit.Part(visual, "Urn", MeshKit.Lathe("scaleUrn", new[] { new Vector2(0.55f, 0f), new Vector2(0.8f, 0.12f), new Vector2(1f, 0.45f), new Vector2(0.85f, 0.8f), new Vector2(0.5f, 0.92f), new Vector2(0.55f, 1f), new Vector2(0f, 1f) }, 12),
                Mat.Rubble, Vector3.zero, default, new Vector3(r, hgt, r));
            ArtLibrary.Tint(body.GetComponent<Renderer>(), new Color(0.52f, 0.33f, 0.22f));
            MeshKit.Part(visual, "Band", MeshKit.Cylinder(r * 1.01f, r * 1.01f, 0.035f, 12), Mat.Steel, new Vector3(0f, hgt * 0.43f, 0f));
            MeshKit.Part(visual, "Lip", MeshKit.Cylinder(r * 0.58f, r * 0.58f, 0.03f, 12), Mat.Steel, new Vector3(0f, hgt * 0.97f, 0f));
            float neckR = r * 0.9f, neckY = hgt * 0.72f;
            for (int i = 0; i < w; i++)
            {
                float a = (i - (w - 1) * 0.5f) * 0.42f; // a row of studs across the front face
                var stud = MeshKit.Part(visual, "Stud", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(Mathf.Sin(a) * neckR, neckY, Mathf.Cos(a) * neckR));
                stud.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(stud.GetComponent<Renderer>(), MarkerColor * 0.7f);
                // And the same on the back, so it reads from either side.
                var back = MeshKit.Part(visual, "Stud", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(Mathf.Sin(a) * neckR, neckY, -Mathf.Cos(a) * neckR));
                back.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(back.GetComponent<Renderer>(), MarkerColor * 0.7f);
            }
        }
    }
}
