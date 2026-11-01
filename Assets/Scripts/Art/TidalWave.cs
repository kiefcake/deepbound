using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Mage's Tidal Wave (2026-11-01, replacing Blink): a curling crest of water that rolls out from the caster's
    /// feet across a wedge, rising as it leaves and breaking into foam at its reach, with spray and wet rings on the
    /// floor behind it. Heavier and greener-dark on a flooded floor. Local only; the hits are resolved by the server.
    /// </summary>
    public class TidalWave : MonoBehaviour
    {
        private const int Segments = 17;
        private static readonly Color Water = new Color(0.1f, 0.3f, 0.45f, 0.42f), Murk = new Color(0.08f, 0.28f, 0.24f, 0.48f);
        private static readonly Color Foam = new Color(0.62f, 0.78f, 0.85f, 0.6f);

        private Vector3 from, dir;
        private float length, arc, seconds, born, nextSpray;
        private bool wet;
        private Transform[] crest;
        private Renderer[] parts;
        private Color tint;

        public static void Spawn(Vector3 from, Vector3 dir, float length, float arcDegrees, float seconds, bool wet)
        {
            var go = new GameObject("TidalWave");
            var w = go.AddComponent<TidalWave>();
            w.from = from;
            w.dir = dir;
            w.length = length;
            w.arc = arcDegrees;
            w.seconds = seconds;
            w.wet = wet;
            w.born = Time.time;
            w.tint = wet ? Murk : Water;
            w.Build();
            w.Tick();
            Destroy(go, seconds + 0.45f);
        }

        private void Build()
        {
            transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir, Vector3.up));
            crest = new Transform[Segments];
            parts = new Renderer[Segments * 2];
            var slab = MeshKit.Box(Vector3.one);
            var lip = MeshKit.Ball(0.5f, 8, 5);
            for (int i = 0; i < Segments; i++)
            {
                crest[i] = MeshKit.Pivot(transform, "Crest" + i, Vector3.zero);
                // A sloping wall of water, and a rolling white lip along its top.
                var body = MeshKit.Part(crest[i], "Body", slab, Mat.Telegraph, new Vector3(0f, 0.5f, 0f), new Vector3(-26f, 0f, 0f));
                var top = MeshKit.Part(crest[i], "Lip", lip, Mat.Telegraph, new Vector3(0f, 1f, 0.12f), default, new Vector3(1f, 0.45f, 0.6f));
                parts[i * 2] = body.GetComponent<Renderer>();
                parts[i * 2 + 1] = top.GetComponent<Renderer>();
                foreach (var r in new[] { parts[i * 2], parts[i * 2 + 1] }) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // The first heave of water at the Mage's feet.
            Vfx.Ring(from + Vector3.up * 0.05f, 1.4f, Foam, 0.4f);
            FxSprite.Decal("circle_05", from + Vector3.up * 0.04f, 1.2f, 3.2f, 0.6f, new Color(tint.r, tint.g, tint.b, 0.5f));
        }

        private void Update() => Tick();

        private void Tick()
        {
            float age = Time.time - born;
            float k = Mathf.Clamp01(age / seconds);
            // Out fast, easing as it spends itself; after its reach it collapses into foam.
            float r = Mathf.Lerp(0.6f, length, 1f - (1f - k) * (1f - k));
            float rise = Mathf.Sin(Mathf.Clamp01(k * 1.15f) * Mathf.PI * 0.85f) * (wet ? 1.5f : 1.25f);
            float fall = age > seconds ? Mathf.Clamp01(1f - (age - seconds) / 0.4f) : 1f;
            float half = arc * 0.5f;
            float chord = 2f * r * Mathf.Sin(Mathf.Deg2Rad * arc / (2f * Segments)) * 1.6f;
            for (int i = 0; i < Segments; i++)
            {
                float t = Segments == 1 ? 0.5f : i / (float)(Segments - 1);
                float a = Mathf.Lerp(-half, half, t);
                // The middle runs a little ahead and stands taller: a crest, not a wall.
                float lead = 1f + 0.06f * (1f - Mathf.Abs(t * 2f - 1f));
                var local = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (r * lead);
                crest[i].localPosition = local;
                crest[i].localRotation = Quaternion.Euler(0f, a, 0f);
                float h = Mathf.Max(0.05f, rise * fall * (0.75f + 0.25f * (1f - Mathf.Abs(t * 2f - 1f))) + 0.15f * Mathf.Sin(age * 14f + i));
                crest[i].localScale = new Vector3(Mathf.Max(0.3f, chord), h, 0.35f + 0.2f * fall);
            }
            var c = tint;
            c.a *= fall;
            var f = Foam;
            f.a *= fall;
            for (int i = 0; i < parts.Length; i++) if (parts[i] != null) ArtLibrary.Tint(parts[i], i % 2 == 0 ? c : f);

            // Spray off the crest, and wet rings where it has passed.
            if (age < seconds && Time.time >= nextSpray)
            {
                nextSpray = Time.time + 0.04f;
                for (int n = 0; n < 2; n++)
                {
                    float a = Random.Range(-half, half);
                    var at = from + Quaternion.LookRotation(dir) * (Quaternion.Euler(0f, a, 0f) * Vector3.forward * r);
                    Vfx.Embers(at + Vector3.up * (0.4f + rise * 0.8f), 2, Foam, 3.5f, 0.05f, 0.45f);
                    FxSprite.Billboard("smoke_04", at + Vector3.up * (0.3f + rise * 0.6f), dir * 2f + Vector3.up * 1.5f, 0.5f, 0.4f, 1.1f, new Color(f.r, f.g, f.b, 0.45f));
                    if (n == 0) FxSprite.Decal("circle_02", at + Vector3.up * 0.03f, 0.5f, 1.4f, 0.7f, new Color(tint.r, tint.g, tint.b, 0.35f));
                }
            }
        }
    }
}
