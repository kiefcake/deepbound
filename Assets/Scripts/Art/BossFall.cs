using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A guardian's end, every peer locally: the light goes out of its eyes, it lets go of a column
    /// of pale light and a slow ring that rolls across the arena, the room's fires gutter for a breath
    /// and come back, and soul-motes lift out of the body for a few seconds.
    /// </summary>
    public class BossFall : MonoBehaviour
    {
        private float born;
        private Color color;
        private Transform beam;
        private Light glow;
        private World.Torch[] fires;

        public static void Play(Vector3 at, float size, Color color)
        {
            var go = new GameObject("BossFall");
            go.transform.position = at;
            var f = go.AddComponent<BossFall>();
            f.born = Time.time;
            f.color = color;
            f.fires = Object.FindObjectsByType<World.Torch>(FindObjectsSortMode.None);
            // Its last shockwaves die with it (they no longer hurt; now they stop showing, too).
            foreach (var ring in Object.FindObjectsByType<ShockwaveRing>(FindObjectsSortMode.None)) Destroy(ring.gameObject);
            var pale = Color.Lerp(color, Color.white, 0.6f);
            f.beam = MeshKit.Part(go.transform, "Beam", MeshKit.Cylinder(0.6f, 0.9f, 1f, 16), Mat.Smoke, Vector3.zero, default, new Vector3(size, 30f, size)).transform;
            ArtLibrary.Tint(f.beam.GetComponent<Renderer>(), new Color(pale.r, pale.g, pale.b, 0.22f));
            f.beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f.glow = new GameObject("FallLight").AddComponent<Light>();
            f.glow.transform.SetParent(go.transform, false);
            f.glow.transform.localPosition = Vector3.up * 2.5f;
            f.glow.type = LightType.Point;
            f.glow.color = pale;
            f.glow.range = 22f;
            f.glow.intensity = 9f;
            f.glow.shadows = LightShadows.None;
            Vfx.Flash(at + Vector3.up * 2f, pale, 14f, 18f, 0.6f);
            Vfx.Ring(at + Vector3.up * 0.1f, 3f * size, pale, 0.5f);
            Vfx.Ring(at + Vector3.up * 0.1f, 5.5f * size, pale * 0.7f, 0.9f);
            Vfx.Embers(at + Vector3.up * 1.5f, 40, pale, 6f, 0.07f, 1.4f);
            Sfx.Play(Sound.Relic, at + Vector3.up * 2f, 1f, 0.55f);
            Sfx.Play(Sound.Slam, at, 0.9f, 0.5f);
            Destroy(go, 6f);
        }

        private void Update()
        {
            float t = Time.time - born;
            // The column swells, then thins and fades.
            float k = t < 0.4f ? t / 0.4f : Mathf.Clamp01(1f - (t - 0.4f) / 3.6f);
            if (beam != null)
            {
                beam.localScale = new Vector3(beam.localScale.x * (t < 0.4f ? 1f : 0.995f), 30f, beam.localScale.z * (t < 0.4f ? 1f : 0.995f));
                var r = beam.GetComponent<Renderer>();
                var pale = Color.Lerp(color, Color.white, 0.6f);
                ArtLibrary.Tint(r, new Color(pale.r, pale.g, pale.b, 0.22f * k));
            }
            if (glow != null) glow.intensity = 9f * k;
            // The room's fires gutter low for a moment, then return.
            float gutter = t < 0.25f ? Mathf.Lerp(1f, 0.15f, t / 0.25f) : t < 1.6f ? 0.15f : Mathf.Lerp(0.15f, 1f, Mathf.Clamp01((t - 1.6f) / 1.4f));
            if (fires != null) foreach (var f in fires) if (f != null) f.Dim = gutter;
            // Souls leaving it.
            if (t < 4f && Random.value < 0.5f)
                Vfx.Embers(transform.position + Random.insideUnitSphere * 1.2f + Vector3.up, 1, Color.Lerp(color, Color.white, 0.5f), 1.4f, 0.05f, 1.4f);
        }

        private void OnDestroy()
        {
            if (fires != null) foreach (var f in fires) if (f != null) f.Dim = 1f;
        }
    }
}
