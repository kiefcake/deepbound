using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>One die thrown across a table: an arc from the cup, tumbling, landing on the rolled face (local only).</summary>
    public class DieTumble : MonoBehaviour
    {
        private Vector3 from, to;
        private Quaternion end, spinAxis;
        private float start, seconds;

        /// <summary>Throw this die (local space of its parent) from <paramref name="a"/> to <paramref name="b"/>, ending on <paramref name="value"/>.</summary>
        public void Throw(Vector3 a, Vector3 b, int value, float yaw, float delay = 0f, float time = 0.8f)
        {
            from = a; to = b; end = DiceArt.FaceUp(value, yaw);
            start = Time.time + delay; seconds = time;
            spinAxis = Random.rotationUniform;
            transform.localPosition = a;
            enabled = true;
        }

        private void Update()
        {
            float t = (Time.time - start) / seconds;
            if (t < 0f) return;
            if (t >= 1f) { transform.localPosition = to; transform.localRotation = end; enabled = false; return; }
            float e = 1f - (1f - t) * (1f - t);
            // Two little hops on the felt, then it settles.
            float hop = Mathf.Abs(Mathf.Sin(e * Mathf.PI * 2.2f)) * (1f - e) * 0.18f;
            transform.localPosition = Vector3.Lerp(from, to, e) + Vector3.up * hop;
            float spin = (1f - e) * 900f;
            transform.localRotation = Quaternion.Slerp(spinAxis * Quaternion.Euler(spin, spin * 0.7f, spin * 0.4f), end, Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.45f));
        }
    }
}
