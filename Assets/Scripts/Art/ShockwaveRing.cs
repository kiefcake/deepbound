using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A boss shockwave as you see it: a low, glowing wall of force racing outward from a
    /// point, kicking up grit as it goes. Jump it, or dodge through it.
    /// </summary>
    public class ShockwaveRing : MonoBehaviour
    {
        private float speed, maxRadius, age, nextDust;
        private Transform ring;
        private Renderer rend;
        private Color color;

        private static Mesh wall;

        public static void Spawn(Vector3 at, float speed, float maxRadius, Color color)
        {
            var go = new GameObject("Shockwave");
            go.transform.position = at;
            var s = go.AddComponent<ShockwaveRing>();
            s.speed = speed;
            s.maxRadius = maxRadius;
            s.color = color;
            wall ??= MeshKit.Lathe("shockWall", new[] { new Vector2(0.94f, 0f), new Vector2(1f, 0.05f), new Vector2(0.99f, 0.45f), new Vector2(0.96f, 0.5f) }, 40, false);
            s.ring = MeshKit.Part(go.transform, "Wall", wall, Mat.Glow, Vector3.up * 0.02f).transform;
            s.rend = s.ring.GetComponent<Renderer>();
            s.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(s.rend, color * 1.3f);
            s.ring.localScale = new Vector3(0.1f, 1f, 0.1f);
            Destroy(go, maxRadius / speed + 0.1f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float r = Mathf.Max(0.1f, age * speed);
            float k = Mathf.Clamp01(1f - r / maxRadius);
            ring.localScale = new Vector3(r, 0.6f + 0.6f * k, r);
            ArtLibrary.SetGlow(rend, color * (0.45f + 1.0f * k));
            if (Time.time >= nextDust)
            {
                nextDust = Time.time + 0.06f;
                float a = Random.value * Mathf.PI * 2f;
                Vfx.Embers(transform.position + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * r, 2, color, 2f, 0.05f, 0.4f);
            }
        }
    }
}
