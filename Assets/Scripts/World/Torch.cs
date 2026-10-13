using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Flickers a point light and its flame mesh.
    ///
    /// Each torch gets its own noise offset so a corridor of them does not pulse in
    /// unison — synchronised flicker is the fastest way to make firelight look fake.
    /// </summary>
    public class Torch : MonoBehaviour
    {
        public Light Light;
        public Transform Flame;
        public float BaseIntensity = 6f;

        private float offset;
        private Vector3 flameScale;

        private void Start()
        {
            offset = Random.value * 100f;
            if (Flame != null) flameScale = Flame.localScale;
        }

        private void Update()
        {
            float t = Time.time * 7f + offset;
            float n = Mathf.PerlinNoise(t, offset) * 0.7f + Mathf.PerlinNoise(t * 2.7f, offset + 5f) * 0.3f;

            if (Light != null) Light.intensity = BaseIntensity * (0.75f + n * 0.45f);
            if (Flame != null)
            {
                Flame.localScale = new Vector3(flameScale.x * (0.9f + n * 0.2f), flameScale.y * (0.8f + n * 0.45f), flameScale.z * (0.9f + n * 0.2f));
            }
        }
    }
}
