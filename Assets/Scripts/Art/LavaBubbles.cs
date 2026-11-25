using UnityEngine;

namespace Deepbound.Art
{
    public class LavaBubbles : MonoBehaviour
    {
        public float Radius = 2f;
        private float next;
        private Light glow;

        private void Start() => glow = GetComponentInChildren<Light>();

        private void Update()
        {
            if (glow != null) glow.intensity = 2.2f + 0.5f * Mathf.Sin(Time.time * 1.7f + transform.position.x);
            if (Time.time < next) return;
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 30f * 30f) { next = Time.time + 0.5f; return; }
            next = Time.time + Random.Range(0.25f, 0.6f);
            var off = Random.insideUnitCircle * Radius * 0.85f;
            var at = transform.position + new Vector3(off.x, 0.1f, off.y);
            Vfx.Embers(at, 2, new Color(1f, 0.5f, 0.15f), 2.2f, 0.05f, 0.9f);
            if (Random.value < 0.15f) Vfx.Smoke(at + Vector3.up * 0.3f, 1, 0.3f, 1.8f);
        }
    }
}
