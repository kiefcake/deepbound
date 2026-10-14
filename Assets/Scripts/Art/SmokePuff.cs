using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A billow of dark smoke: a handful of grey balls that swell, drift up and shrink away.
    /// Used by the Rogue's Shadowstep and Smoke Bomb.
    /// </summary>
    public class SmokePuff : MonoBehaviour
    {
        private Transform[] puffs;
        private Vector3[] drift;
        private float age, life;

        public static void Spawn(Vector3 at, float size)
        {
            var go = new GameObject("SmokePuff");
            go.transform.position = at;
            var p = go.AddComponent<SmokePuff>();
            int n = Mathf.RoundToInt(6 + size * 4);
            p.life = 0.8f + size * 0.6f;
            p.puffs = new Transform[n];
            p.drift = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var ball = MeshKit.Part(go.transform, "Puff", MeshKit.Ball(0.3f, 8, 6), Mat.Cloth,
                    new Vector3(Random.Range(-0.6f, 0.6f) * size, Random.Range(0.2f, 1.6f), Random.Range(-0.6f, 0.6f) * size), Vector3.zero, Vector3.one * 0.1f);
                var r = ball.GetComponent<Renderer>();
                ArtLibrary.Tint(r, new Color(0.16f, 0.14f, 0.18f));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.puffs[i] = ball.transform;
                p.drift[i] = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.3f, 0.8f), Random.Range(-0.3f, 0.3f)) * size;
            }
            Destroy(go, p.life + 0.1f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(age / life);
            float scale = Mathf.Sin(k * Mathf.PI) * 1.4f + 0.1f;
            for (int i = 0; i < puffs.Length; i++)
            {
                puffs[i].localPosition += drift[i] * Time.deltaTime;
                puffs[i].localScale = Vector3.one * scale * (0.8f + 0.4f * Mathf.Sin(i * 1.7f));
            }
        }
    }
}
