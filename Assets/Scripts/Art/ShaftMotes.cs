using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>Dust turning slowly in a shaft of light. Only while someone is near enough to see it.</summary>
    public class ShaftMotes : MonoBehaviour
    {
        private float radius, height, next;
        private Color color;

        public void Init(float r, float h, Color c)
        {
            radius = r;
            height = h;
            color = c;
        }

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.18f;
            var cam = Camera.main;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 28f * 28f) return;
            var p = transform.position + new Vector3(Random.Range(-radius, radius) * 0.7f, Random.Range(0.3f, height * 0.75f), Random.Range(-radius, radius) * 0.7f);
            Vfx.Speck(p, color);
        }
    }
}
