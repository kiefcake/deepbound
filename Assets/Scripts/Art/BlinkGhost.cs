using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The afterimage a Blink leaves behind: a translucent violet silhouette where the mage
    /// stood, rising a little and fading out. Local only.
    /// </summary>
    public class BlinkGhost : MonoBehaviour
    {
        private const float Life = 0.7f;
        private float born;
        private Renderer[] parts;
        private Color color;

        public static void Spawn(Vector3 feet, Quaternion facing, Color color)
        {
            var go = new GameObject("BlinkGhost");
            go.transform.SetPositionAndRotation(feet, facing);
            var g = go.AddComponent<BlinkGhost>();
            g.born = Time.time;
            g.color = color;
            var t = go.transform;
            MeshKit.Part(t, "Robe", MeshKit.Lathe("ghostRobe", new[] { new Vector2(0.34f, 0f), new Vector2(0.24f, 0.9f), new Vector2(0.2f, 1.35f), new Vector2(0.08f, 1.45f) }, 10), Mat.Telegraph, Vector3.zero);
            MeshKit.Part(t, "Head", MeshKit.Ball(0.14f, 8, 6), Mat.Telegraph, Vector3.up * 1.6f);
            MeshKit.Part(t, "ArmL", MeshKit.Cylinder(0.05f, 0.05f, 0.6f, 6), Mat.Telegraph, new Vector3(-0.28f, 0.85f, 0f), new Vector3(0f, 0f, -12f));
            MeshKit.Part(t, "ArmR", MeshKit.Cylinder(0.05f, 0.05f, 0.6f, 6), Mat.Telegraph, new Vector3(0.28f, 0.85f, 0f), new Vector3(0f, 0f, 12f));
            g.parts = go.GetComponentsInChildren<Renderer>();
            foreach (var r in g.parts) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.Tick();
            Destroy(go, Life);
        }

        private void Update() => Tick();

        private void Tick()
        {
            float k = Mathf.Clamp01((Time.time - born) / Life);
            transform.position += Vector3.up * Time.deltaTime * 0.6f;
            transform.localScale = Vector3.one * (1f + k * 0.15f);
            var c = color;
            c.a = (1f - k) * 0.45f;
            if (parts != null) foreach (var r in parts) if (r != null) ArtLibrary.Tint(r, c);
        }
    }
}
