using UnityEngine;

namespace Deepbound.Art
{
    public class LanternFlicker : MonoBehaviour
    {
        private Light l;
        private float seed;
        private void Awake() { l = GetComponent<Light>(); seed = Random.value * 10f; }
        private void Update()
        {
            if (l == null) return;
            float n = Mathf.PerlinNoise(seed, Time.time * 2.2f);
            l.intensity = CarriedLantern.Intensity * (0.85f + 0.3f * n);
        }
    }
}
