using UnityEngine;

namespace Deepbound.World
{
    public class BannerSway : MonoBehaviour
    {
        private float seed;

        private void Start() => seed = Random.value * 100f;

        private void Update()
        {
            float t = Time.time * 0.35f + seed;
            float swing = (Mathf.PerlinNoise(t, seed) - 0.5f) * 7f;
            float twist = (Mathf.PerlinNoise(seed, t * 0.7f) - 0.5f) * 3f;
            transform.localRotation = Quaternion.Euler(swing, twist, twist * 0.5f);
        }
    }
}
