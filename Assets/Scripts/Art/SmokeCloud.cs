using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Rogue's smoke bomb, as a place: a thick, slowly churning cloud that hangs where it
    /// burst for the whole duration (it is also the Smoke Screen allies can hide in), then
    /// thins out and drifts away.
    /// </summary>
    public class SmokeCloud : MonoBehaviour
    {
        private Transform[] puffs;
        private Vector3[] home;
        private float[] phase, size;
        private float age, life, radius;

        public static void Spawn(Vector3 at, float radius, float seconds)
        {
            var go = new GameObject("SmokeCloud");
            go.transform.position = at;
            var c = go.AddComponent<SmokeCloud>();
            c.life = seconds;
            c.radius = radius;
            int n = 70;
            c.puffs = new Transform[n];
            c.home = new Vector3[n];
            c.phase = new float[n];
            c.size = new float[n];
            var mesh = MeshKit.Ball(0.5f, 8, 6);
            for (int i = 0; i < n; i++)
            {
                // Denser and taller in the middle, low and wispy at the edge.
                float r = Mathf.Sqrt(Random.value) * radius;
                float a = Random.value * Mathf.PI * 2f;
                float edge = r / radius;
                c.home[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Lerp(1.4f, 0.4f, edge) + Random.Range(-0.3f, 0.6f), Mathf.Sin(a) * r);
                c.size[i] = Mathf.Lerp(1.6f, 0.8f, edge) * Random.Range(0.7f, 1.25f);
                c.phase[i] = Random.value * 10f;
                var ball = MeshKit.Part(go.transform, "Smoke", mesh, Mat.Smoke, c.home[i]);
                var rend = ball.GetComponent<Renderer>();
                // Translucent layers: overlapping puffs build the cloud's density in the middle.
                float shade = Random.Range(0.1f, 0.2f);
                ArtLibrary.Tint(rend, new Color(shade, shade * 0.9f, shade * 1.2f, Random.Range(0.22f, 0.38f)));
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ball.transform.localScale = Vector3.zero;
                c.puffs[i] = ball.transform;
            }
            Destroy(go, seconds + 1.2f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            var lp = Player.NetworkPlayer.Local;
            var cam = lp != null && lp.Camera != null ? lp.Camera.transform : (Camera.main != null ? Camera.main.transform : null);
            // Billows out fast, holds, then thins over the last second.
            float grow = Mathf.Clamp01(age / 0.35f);
            float fade = Mathf.Clamp01((life + 1f - age) / 1.2f);
            float k = grow * fade;
            for (int i = 0; i < puffs.Length; i++)
            {
                float t = Time.time * 0.6f + phase[i];
                var churn = new Vector3(Mathf.Sin(t) * 0.35f, Mathf.Sin(t * 0.7f) * 0.2f + (age > life ? (age - life) * 0.8f : 0f), Mathf.Cos(t * 0.9f) * 0.35f);
                puffs[i].localPosition = home[i] * (0.6f + 0.4f * grow) + churn;
                float s = size[i] * k * (0.9f + 0.15f * Mathf.Sin(t * 1.3f));
                // Never smother the camera: puffs right in your face thin away.
                if (cam != null)
                {
                    float d = Vector3.Distance(cam.position, puffs[i].position);
                    if (d < 2.2f) s *= Mathf.Clamp01((d - 0.6f) / 1.6f);
                }
                puffs[i].localScale = Vector3.one * s;
            }
        }
    }
}
