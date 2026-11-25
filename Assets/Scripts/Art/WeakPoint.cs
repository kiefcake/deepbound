using UnityEngine;

namespace Deepbound.Art
{
    public class WeakPoint : MonoBehaviour
    {
        public float Radius = 0.12f;
        public Color Glow = new Color(1f, 0.45f, 0.15f);
        private Renderer rend;
        private float phase;

        public float WorldRadius => Radius * transform.lossyScale.y;

        public static WeakPoint Add(Transform parent, Vector3 local, float radius, Color? glow = null)
        {
            var c = glow ?? new Color(1f, 0.45f, 0.15f);
            var part = MeshKit.Part(parent, "WeakPoint", MeshKit.Ball(radius, 8, 6), Mat.Glow, local);
            ArtLibrary.SetGlow(part.GetComponent<Renderer>(), c * 1.2f);
            part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var w = part.AddComponent<WeakPoint>();
            w.Radius = radius;
            w.Glow = c;
            return w;
        }

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            phase = Random.value * 6f;
        }

        private void Update()
        {
            if (rend == null || !rend.isVisible) return;
            phase += Time.deltaTime;
            ArtLibrary.SetGlow(rend, Glow * (0.9f + 0.6f * (0.5f + 0.5f * Mathf.Sin(phase * 4f))));
        }
    }
}
