using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Ranger's Snare Shot net (2026-11-01, replacing the bear trap): a weighted rope net that flies open where the
    /// arrow strikes, settles over the floor with stones on its rim, and lies there while what it caught is held, then
    /// sinks away. Local only; the server decides who is caught.
    /// </summary>
    public class SnareNet : MonoBehaviour
    {
        private const int Spokes = 12, Rings = 3;
        private static readonly Color Rope = new Color(0.52f, 0.42f, 0.28f);

        private float radius, seconds, born;
        private Renderer[] parts;

        public static void Spawn(Vector3 at, float radius, float seconds)
        {
            var go = new GameObject("SnareNet");
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.03f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var n = go.AddComponent<SnareNet>();
            n.radius = radius;
            n.seconds = seconds;
            n.born = Time.time;
            n.Build();
            n.Update();
            Destroy(go, seconds + 0.6f);
        }

        private void Build()
        {
            var strand = MeshKit.Box(new Vector3(0.035f, 0.025f, 1f));
            var stone = MeshKit.Ball(0.07f, 6, 4);
            // Spokes from the knot at the middle to the rim; rings of rope across them; a stone weight at each spoke's end.
            for (int s = 0; s < Spokes; s++)
            {
                float a = s * 360f / Spokes;
                var rot = Quaternion.Euler(0f, a, 0f);
                MeshKit.Part(transform, "Spoke", strand, Mat.Leather, rot * Vector3.forward * 0.5f, rot.eulerAngles);
                MeshKit.Part(transform, "Weight", stone, Mat.Rubble, rot * Vector3.forward * 1f);
                for (int r = 1; r <= Rings; r++)
                {
                    float rr = r / (float)(Rings + 1) + 0.12f;
                    float a2 = a + 180f / Spokes;
                    var mid = Quaternion.Euler(0f, a2, 0f) * Vector3.forward * rr * Mathf.Cos(Mathf.PI / Spokes);
                    float chord = 2f * rr * Mathf.Sin(Mathf.PI / Spokes);
                    MeshKit.Part(transform, "Ring", strand, Mat.Leather, mid, new Vector3(0f, a2 + 90f, 0f), new Vector3(1f, 1f, chord));
                }
            }
            MeshKit.Part(transform, "Knot", MeshKit.Ball(0.09f, 6, 4), Mat.Leather, Vector3.zero);
            parts = GetComponentsInChildren<Renderer>();
            foreach (var r in parts)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.Tint(r, r.name == "Weight" ? new Color(0.45f, 0.44f, 0.42f) : Rope);
            }
            Vfx.Smoke(transform.position, 6, radius * 0.7f, 0.8f);
        }

        private void Update()
        {
            float age = Time.time - born;
            // Flies open in a fifth of a second (overshooting a little), drapes, and at the end sinks into the floor.
            float open = age < 0.2f ? Mathf.SmoothStep(0.15f, 1.06f, age / 0.2f) : Mathf.Lerp(1.06f, 1f, Mathf.Clamp01((age - 0.2f) / 0.15f));
            float sink = age > seconds ? Mathf.Clamp01((age - seconds) / 0.5f) : 0f;
            transform.localScale = new Vector3(radius * open, 1f + 2f * Mathf.Max(0f, 0.2f - age), radius * open);
            var p = transform.position;
            p.y += (age < 0.2f ? 0.35f * (1f - age / 0.2f) : 0f) * Time.deltaTime * 6f - sink * 0.15f * Time.deltaTime * 6f;
            transform.position = p;
        }
    }
}
