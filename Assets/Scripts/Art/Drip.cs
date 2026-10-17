using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A leak in the vault roof: every few seconds a drop falls from the ceiling and lands in a
    /// tiny ring (and a plink, if you are close). Local only, asleep when the viewer is far.
    /// </summary>
    public class Drip : MonoBehaviour
    {
        public float Floor;
        private float next;
        private static Mesh drop;

        private void Start() => next = Time.time + Random.Range(0.5f, 4f);

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Random.Range(1.4f, 4.2f);
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 22f * 22f) return;
            drop ??= MeshKit.Ball(0.5f, 6, 4);
            float fall = Mathf.Sqrt(2f * Mathf.Max(0.2f, transform.position.y - Floor) / 9.81f);
            Vfx.Spawn(drop, Mat.Glow, transform.position, Vector3.zero, fall, 0.025f, 0.025f, new Color(0.5f, 0.7f, 0.8f) * 0.6f, 9.81f, 0f);
            Invoke(nameof(Land), fall);
        }

        private void Land()
        {
            var at = new Vector3(transform.position.x, Floor + 0.02f, transform.position.z);
            Vfx.Ring(at, 0.25f, new Color(0.6f, 0.8f, 0.9f, 0.5f), 0.6f);
            var cam = ViewCamera.Current;
            if (cam != null && (cam.transform.position - at).sqrMagnitude < 10f * 10f) Sfx.Play(Sound.Splash, at, 0.12f, Random.Range(2.2f, 2.8f));
        }
    }
}
