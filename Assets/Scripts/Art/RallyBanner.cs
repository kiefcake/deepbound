using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Knight's Rallying Banner as you see it: an iron pole driven into the floor, a
    /// swallow-tailed war banner in the Knight's colour, and a glowing ring on the ground
    /// that marks where its blessing reaches. Purely visual; the server owns the effect.
    /// </summary>
    public class RallyBanner : MonoBehaviour
    {
        private Renderer ring;
        private Transform cloth;
        private Color color;
        private float born, life;

        public static void Spawn(Vector3 at, Color color, float seconds, float radius)
        {
            var go = new GameObject("RallyBanner");
            go.transform.position = at;
            var b = go.AddComponent<RallyBanner>();
            b.color = color;
            b.born = Time.time;
            b.life = seconds;

            MeshKit.Part(go.transform, "Pole", MeshKit.Cylinder(0.04f, 0.035f, 3.2f, 6), Mat.Steel, Vector3.zero);
            MeshKit.Part(go.transform, "Finial", MeshKit.Lathe("bannerFinial", new[] { new Vector2(0.06f, 0f), new Vector2(0f, 0.22f) }, 6), Mat.Gold, Vector3.up * 3.2f);
            MeshKit.Part(go.transform, "Crossbar", MeshKit.Box(new Vector3(1.1f, 0.05f, 0.05f)), Mat.Steel, new Vector3(0.5f, 3f, 0f));
            b.cloth = MeshKit.Pivot(go.transform, "Cloth", new Vector3(0.5f, 3f, 0f));
            var sheet = MeshKit.Part(b.cloth, "Sheet", MeshKit.Box(new Vector3(1f, 1.3f, 0.03f)), Mat.Cloth, new Vector3(0f, -0.65f, 0f));
            ArtLibrary.Tint(sheet.GetComponent<Renderer>(), color);
            for (int s = -1; s <= 1; s += 2)
            {
                var tail = MeshKit.Part(b.cloth, "Tail", MeshKit.Box(new Vector3(0.42f, 0.5f, 0.03f)), Mat.Cloth, new Vector3(s * 0.27f, -1.5f, 0f), new Vector3(0f, 0f, s * 12f));
                ArtLibrary.Tint(tail.GetComponent<Renderer>(), color * 0.85f);
            }
            MeshKit.Part(b.cloth, "Emblem", MeshKit.Box(new Vector3(0.3f, 0.4f, 0.04f)), Mat.Gold, new Vector3(0f, -0.6f, 0f));

            b.ring = MeshKit.Part(go.transform, "Ring", MeshKit.Lathe("bannerRing", new[] { new Vector2(0.96f, 0f), new Vector2(1f, 0.02f), new Vector2(0.96f, 0.04f) }, 40, false),
                Mat.Glow, Vector3.up * 0.05f, Vector3.zero, new Vector3(radius, 1f, radius)).GetComponent<Renderer>();
            b.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = new GameObject("BannerLight").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.transform.localPosition = Vector3.up * 2.5f;
            light.type = LightType.Point;
            light.color = Color.Lerp(color, new Color(1f, 0.8f, 0.5f), 0.5f);
            light.range = radius * 1.3f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;

            Vfx.Smoke(at, 3, 0.8f, 1.2f);
            Vfx.Ring(at, radius, color, 0.5f);
            Destroy(go, seconds);
        }

        private void Update()
        {
            float t = Time.time - born;
            if (cloth != null) cloth.localRotation = Quaternion.Euler(Mathf.Sin(t * 2.3f) * 6f, Mathf.Sin(t * 1.4f) * 10f, 0f);
            float fade = Mathf.Clamp01((life - t) / 1f);
            if (ring != null) ArtLibrary.SetGlow(ring, color * (0.8f + 0.4f * Mathf.Sin(t * 4f)) * fade);
        }
    }
}
