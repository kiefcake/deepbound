using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The ring of a boss's SHARED DOOM (see <c>BossBrain.Doom</c>): it rides under the marked player for the tell, its
    /// rim beating faster and an inner ring swelling out to meet it as the burst nears. Kept faint like every telegraph.
    /// </summary>
    public class DoomMark : MonoBehaviour
    {
        private Transform follow, rim, fill;
        private Renderer rimR, fillR;
        private float born, life, radius;
        private Color color;

        private static Mesh ring;
        private static Mesh Ring => ring ??= MeshKit.Lathe("doomRing", new[] { new Vector2(0.965f, 0f), new Vector2(1f, 0.015f), new Vector2(0.965f, 0.03f) }, 40, false);

        public static void Attach(Transform target, float radius, float seconds, Color color)
        {
            if (target == null) return;
            var go = new GameObject("DoomMark");
            var m = go.AddComponent<DoomMark>();
            m.follow = target;
            m.radius = radius;
            m.life = Mathf.Max(0.1f, seconds);
            m.born = Time.time;
            m.color = color;
            m.rim = MeshKit.Part(go.transform, "Rim", Ring, Mat.Glow, Vector3.up * 0.06f).transform;
            m.fill = MeshKit.Part(go.transform, "Fill", Ring, Mat.Glow, Vector3.up * 0.05f).transform;
            m.rimR = m.rim.GetComponent<Renderer>();
            m.fillR = m.fill.GetComponent<Renderer>();
            m.rimR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m.fillR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m.Update();
        }

        private void Update()
        {
            float k = (Time.time - born) / life;
            if (k >= 1f || follow == null) { Destroy(gameObject); return; }
            transform.position = Vfx.Floor(follow.position);
            rim.localScale = new Vector3(radius, 1f, radius);
            float inner = Mathf.Lerp(0.15f, 1f, k * k);
            fill.localScale = new Vector3(radius * inner, 1f, radius * inner);
            float beat = 0.3f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(4f, 14f, k)));
            ArtLibrary.SetGlow(rimR, color * beat);
            ArtLibrary.SetGlow(fillR, color * (0.15f + 0.25f * k));
            if (Random.value < 0.25f) Vfx.Mote(transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * radius * Random.value, color);
        }
    }
}
