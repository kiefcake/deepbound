using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A melee swing's trail: a thin glowing crescent sweeping across in front of the swinger,
    /// gone in a fifth of a second. Visual only, seen by everyone.
    /// </summary>
    public class SwingTrail : MonoBehaviour
    {
        private Renderer rend;
        private Color color;
        private float born;
        private const float Life = 0.2f;

        /// <param name="side">+1 swings right-to-left, -1 left-to-right (the arc tilts with it).</param>
        public static void Spawn(Transform who, float radius, float arcDegrees, float side, Color color)
        {
            var go = new GameObject("SwingTrail");
            go.transform.position = who.position + Vector3.up * 1.3f;
            go.transform.rotation = Quaternion.LookRotation(who.forward) * Quaternion.Euler(0f, 0f, side * 18f);
            var t = go.AddComponent<SwingTrail>();
            t.color = color;
            t.born = Time.time;
            var part = MeshKit.Part(go.transform, "Arc", MeshKit.Lathe("swingArc" + Mathf.RoundToInt(arcDegrees), new[]
            {
                new Vector2(0.82f, -0.025f), new Vector2(1f, 0f), new Vector2(0.82f, 0.025f),
            }, 14, false, arcDegrees), Mat.Glow, Vector3.zero, new Vector3(0f, -arcDegrees / 2f, 0f), new Vector3(radius, 1f, radius));
            t.rend = part.GetComponent<Renderer>();
            t.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(t.rend, color);
            Destroy(go, Life);
        }

        private void Update()
        {
            float k = 1f - (Time.time - born) / Life;
            ArtLibrary.SetGlow(rend, color * Mathf.Max(0f, k));
            transform.localScale = Vector3.one * (1f + (1f - k) * 0.15f);
        }
    }
}
