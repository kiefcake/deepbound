using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>Fades a light to nothing over its life, then removes it.</summary>
    public class FadeLight : MonoBehaviour
    {
        public float Life = 0.5f;
        private Light l;
        private float start, age;

        private void Start()
        {
            l = GetComponent<Light>();
            start = l.intensity;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(1f - age / Life);
            l.intensity = start * k * k;
            if (age >= Life) Destroy(gameObject);
        }
    }
}
