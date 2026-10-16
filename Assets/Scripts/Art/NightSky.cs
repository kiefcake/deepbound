using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>The camp's sky, alive: stars twinkle a few at a time, clouds drift round the castle on a slow wind.</summary>
    public class NightSky : MonoBehaviour
    {
        public Renderer[] Stars;
        public Transform[] Clouds;
        public Vector3 Center;
        private float[] starBase;
        private int cursor;

        private void Start()
        {
            if (Stars == null) return;
            starBase = new float[Stars.Length];
            for (int i = 0; i < Stars.Length; i++) starBase[i] = 0.6f + Mathf.Abs(Mathf.Sin(i * 12.9898f)) * 0.9f;
        }

        private void Update()
        {
            // Twinkle: refresh a handful of stars a frame (cheap), each on its own flicker.
            if (Stars != null && Stars.Length > 0)
                for (int n = 0; n < 12; n++)
                {
                    cursor = (cursor + 1) % Stars.Length;
                    var r = Stars[cursor];
                    if (r == null) continue;
                    float tw = Mathf.PerlinNoise(Time.time * 1.7f, cursor * 0.37f);
                    ArtLibrary.SetGlow(r, new Color(0.8f, 0.85f, 1f) * (starBase[cursor] * (0.45f + tw * 0.9f)));
                }
            if (Clouds == null) return;
            for (int i = 0; i < Clouds.Length; i++)
            {
                var c = Clouds[i];
                if (c == null) continue;
                // Each cloud on its own ring and height, all going the same way.
                float radius = 34f + (i % 3) * 9f;
                float a = Time.time * (0.012f + i * 0.0015f) + i * 0.9f;
                c.position = Center + new Vector3(Mathf.Cos(a) * radius, 44f + (i % 4) * 3f, Mathf.Sin(a) * radius);
                c.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            }
        }
    }
}
