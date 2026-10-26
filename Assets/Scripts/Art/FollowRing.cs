using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A glowing ring that rides under someone for a few seconds and beats faster as it runs out (2026-10-26: the
    /// Cardinal's Excommunication brand shows its blast radius around the branded player, so allies can step clear).
    /// Client-side; <see cref="Attach"/> adds it at runtime.
    /// </summary>
    public class FollowRing : MonoBehaviour
    {
        private Transform target;
        private Renderer rend;
        private float radius, start, seconds;
        private Color color;

        public static void Attach(Transform target, float radius, float seconds, Color color)
        {
            if (target == null) return;
            var go = MeshKit.Part(null, "FollowRing", MeshKit.Lathe("followRing", new[] { new Vector2(0.9f, 0f), new Vector2(1f, 0.02f), new Vector2(0.9f, 0.04f) }, 40, false), Mat.Glow, Vector3.zero);
            var r = go.AddComponent<FollowRing>();
            r.target = target; r.radius = radius; r.seconds = seconds; r.color = color; r.start = Time.time;
            r.rend = go.GetComponent<Renderer>();
            r.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.Update();
        }

        private void Update()
        {
            float t = Time.time - start;
            if (target == null || t >= seconds) { Destroy(gameObject); return; }
            float k = t / seconds;
            // Beats from slow to frantic, and draws in a little at the end.
            float beat = Mathf.Abs(Mathf.Sin(t * Mathf.Lerp(3f, 14f, k * k)));
            transform.position = Vfx.Floor(target.position) + Vector3.up * 0.06f;
            float r = radius * (1f + 0.05f * beat);
            transform.localScale = new Vector3(r, 1f, r);
            ArtLibrary.SetGlow(rend, color * (0.5f + 0.6f * beat));
        }
    }
}
