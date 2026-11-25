using UnityEngine;

namespace Deepbound.Art
{
    public class RiseFromGround : MonoBehaviour
    {
        private const float Seconds = 0.85f, Depth = 1.3f;
        private Vector3 rest;
        private float start;

        private void Start()
        {
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 28f * 28f) { Destroy(this); return; }
            rest = transform.localPosition;
            start = Time.time;
            var at = transform.position + Vector3.up * 0.05f;
            var dirt = new Color(0.32f, 0.26f, 0.2f);
            Vfx.Ring(at, 1.1f, dirt, 0.5f);
            for (int i = 0; i < 4; i++) Vfx.Embers(at + Random.insideUnitSphere * 0.4f, 5, dirt, 3.5f, 0.07f, 0.9f);
            transform.localPosition = rest + Vector3.down * Depth;
        }

        private void LateUpdate()
        {
            float t = (Time.time - start) / Seconds;
            if (t >= 1f) { transform.localPosition = rest; Destroy(this); return; }

            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 2.4f);
            k -= Mathf.Sin(t * Mathf.PI * 3f) * 0.05f * (1f - t);
            transform.localPosition = rest + Vector3.down * Depth * (1f - k) + new Vector3(Mathf.Sin(t * 40f) * 0.02f * (1f - t), 0f, 0f);
        }
    }
}
