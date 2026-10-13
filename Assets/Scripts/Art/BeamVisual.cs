using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Mending Beam: a soft gold shaft of light from the Cleric's hand to its target,
    /// gently pulsing, with a glow where it lands. One per Cleric, toggled on and off.
    /// </summary>
    public class BeamVisual : MonoBehaviour
    {
        private Transform shaft, core;
        private Renderer shaftRenderer, coreRenderer;
        private Light landing;

        public static BeamVisual Create()
        {
            var go = new GameObject("MendingBeam");
            var b = go.AddComponent<BeamVisual>();
            b.shaft = MeshKit.Part(go.transform, "Shaft", MeshKit.Box(new Vector3(0.09f, 0.09f, 1f)), Mat.Glow, Vector3.zero).transform;
            b.core = MeshKit.Part(go.transform, "Core", MeshKit.Box(new Vector3(0.035f, 0.035f, 1f)), Mat.Glow, Vector3.zero).transform;
            b.shaftRenderer = b.shaft.GetComponent<Renderer>();
            b.coreRenderer = b.core.GetComponent<Renderer>();
            b.shaftRenderer.shadowCastingMode = b.coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.landing = new GameObject("Landing").AddComponent<Light>();
            b.landing.transform.SetParent(go.transform, false);
            b.landing.type = LightType.Point;
            b.landing.color = new Color(1f, 0.85f, 0.45f);
            b.landing.range = 3f;
            b.landing.intensity = 2.5f;
            return b;
        }

        public void Set(Vector3 from, Vector3 to)
        {
            var mid = (from + to) / 2f;
            float len = Vector3.Distance(from, to);
            var rot = Quaternion.LookRotation(to - from);
            shaft.SetPositionAndRotation(mid, rot);
            core.SetPositionAndRotation(mid, rot);
            float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 9f);
            shaft.localScale = new Vector3(pulse, pulse, len);
            core.localScale = new Vector3(1f, 1f, len);
            ArtLibrary.SetGlow(shaftRenderer, new Color(1f, 0.8f, 0.35f) * (0.8f * pulse));
            ArtLibrary.SetGlow(coreRenderer, new Color(1f, 0.95f, 0.75f) * 1.8f);
            landing.transform.position = to;
        }
    }
}
