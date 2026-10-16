using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A spell bolt in flight: the core pulses and turns, and it sheds a trail (embers for fire,
    /// glittering motes for frost) when the viewer is close enough to see it. A shared per-frame budget
    /// keeps boss bullet patterns cheap. <see cref="Burst"/> is the little pop where a bolt ends.
    /// </summary>
    public class BoltFx : MonoBehaviour
    {
        public Color Color;
        public Transform Core;
        public bool Frost;
        private float nextShed, spin;
        private static int budgetFrame, budget, flashFrame, flashes;

        private void Start() => spin = Random.Range(0f, 360f);

        private void Update()
        {
            spin += Time.deltaTime * (Frost ? 360f : 200f);
            if (Core != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 30f + spin) * 0.12f;
                Core.localScale = Vector3.one * pulse;
                if (Frost) Core.localRotation = Quaternion.Euler(90f, 0f, spin);
            }
            if (Time.time < nextShed) return;
            nextShed = Time.time + 0.06f;
            if (budgetFrame != Time.frameCount) { budgetFrame = Time.frameCount; budget = 24; }
            if (budget <= 0) return;
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 24f * 24f) return;
            budget--;
            var back = -transform.forward;
            if (Frost)
                Vfx.Embers(transform.position + back * 0.15f + Random.insideUnitSphere * 0.06f, 1, Color * 1.2f, 0.4f, 0.025f, 0.45f);
            else
                Vfx.Embers(transform.position + back * 0.12f + Random.insideUnitSphere * 0.05f, 1, Color, 0.9f, 0.04f, 0.4f);
        }

        /// <summary>The pop where a bolt ends: a quick flash and a spray of sparks in its colour.</summary>
        public static void Burst(Vector3 at, Color color)
        {
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - at).sqrMagnitude > 30f * 30f) return;
            if (flashFrame != Time.frameCount) { flashFrame = Time.frameCount; flashes = 3; }
            if (flashes-- > 0) Vfx.Flash(at, color, 2.5f, 3f, 0.18f);
            Vfx.Embers(at, 6, color, 3.5f, 0.04f, 0.45f);
        }
    }
}
