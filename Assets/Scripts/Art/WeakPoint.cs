using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A glowing weak spot on a creature: the Brute's back rune, a lich's phylactery, the
    /// Gargoyle's ember heart, the Hoarder's sack. Aimed hits that land inside its radius
    /// deal double damage (less on bosses). It pulses so you can find it in a fight.
    /// </summary>
    public class WeakPoint : MonoBehaviour
    {
        public float Radius = 0.12f;
        public Color Glow = new Color(1f, 0.45f, 0.15f);
        private Renderer rend;
        private float phase;

        /// <summary>World radius, following the creature's scale.</summary>
        public float WorldRadius => Radius * transform.lossyScale.y;

        /// <summary>Editor/art time: put a glowing weak point on <paramref name="parent"/>.</summary>
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
