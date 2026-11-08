using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>Flickers a flame pivot that has no Torch of its own (the bonfire's side licks).</summary>
    public class FlameLick : MonoBehaviour
    {
        private Vector3 rest;
        private Quaternion restRot;
        private float offset;

        private void Start()
        {
            rest = transform.localScale;
            restRot = transform.localRotation;
            offset = Random.value * 100f;
        }

        private void Update()
        {
            float t = Time.time * 8f + offset;
            float n = Mathf.PerlinNoise(t, offset);
            transform.localScale = new Vector3(rest.x * (0.85f + n * 0.25f), rest.y * (0.6f + n * 0.7f), rest.z * (0.85f + n * 0.25f));
            transform.localRotation = restRot * Quaternion.Euler((Mathf.PerlinNoise(t * 0.7f, offset + 1f) - 0.5f) * 18f, 0f, (Mathf.PerlinNoise(offset + 2f, t * 0.7f) - 0.5f) * 18f);
        }
    }
}
