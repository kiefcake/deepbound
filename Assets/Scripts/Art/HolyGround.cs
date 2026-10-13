using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Consecrated ground: a gold rune circle with slowly rising motes of light and a warm
    /// glow, fading out over its lifetime. Purely visual; the healing is on the server.
    /// </summary>
    public class HolyGround : MonoBehaviour
    {
        private float life, age;
        private Light glow;
        private Transform[] motes;
        private Renderer ring;

        public static void Spawn(Vector3 at, float radius, float seconds)
        {
            var go = new GameObject("HolyGround");
            go.transform.position = at + Vector3.up * 0.03f;
            var h = go.AddComponent<HolyGround>();
            h.life = seconds;

            h.ring = MeshKit.Part(go.transform, "Ring", MeshKit.Lathe("holyRing", new[]
            {
                new Vector2(0.93f, 0f), new Vector2(1f, 0.015f), new Vector2(0.93f, 0.03f),
            }, 40, false), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius)).GetComponent<Renderer>();
            var fill = MeshKit.Part(go.transform, "Fill", MeshKit.Cylinder(1f, 1f, 0.01f, 40), Mat.Telegraph, Vector3.zero, Vector3.zero, new Vector3(radius, 1f, radius));
            ArtLibrary.Tint(fill.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.4f, 0.18f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                var rune = MeshKit.Part(go.transform, "Rune" + i, MeshKit.Box(new Vector3(0.1f, 0.02f, 0.4f)), Mat.Glow,
                    Quaternion.Euler(0f, a, 0f) * Vector3.forward * radius * 0.7f, new Vector3(0f, a, 0f));
                ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.4f) * 1.4f);
            }

            h.motes = new Transform[10];
            for (int i = 0; i < h.motes.Length; i++)
            {
                var m = MeshKit.Part(go.transform, "Mote", MeshKit.Ball(0.04f, 6, 4), Mat.Glow,
                    new Vector3(Random.Range(-radius, radius) * 0.7f, Random.Range(0f, 2f), Random.Range(-radius, radius) * 0.7f));
                ArtLibrary.SetGlow(m.GetComponent<Renderer>(), new Color(1f, 0.9f, 0.55f) * 2f);
                h.motes[i] = m.transform;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            h.glow = new GameObject("Glow").AddComponent<Light>();
            h.glow.transform.SetParent(go.transform, false);
            h.glow.transform.localPosition = Vector3.up * 1.2f;
            h.glow.type = LightType.Point;
            h.glow.color = new Color(1f, 0.85f, 0.45f);
            h.glow.range = radius * 2.2f;
            h.glow.intensity = 3f;
            Destroy(go, seconds + 0.5f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float fade = Mathf.Clamp01((life - age) / 1f);
            glow.intensity = 3f * fade;
            foreach (var m in motes)
            {
                var p = m.localPosition;
                p.y += Time.deltaTime * 0.6f;
                if (p.y > 2.2f) p.y = 0f;
                m.localPosition = p;
                m.localScale = Vector3.one * fade;
            }
            ring.transform.localScale = new Vector3(ring.transform.localScale.x, 1f, ring.transform.localScale.z);
            transform.Rotate(0f, Time.deltaTime * 12f, 0f);
        }
    }
}
