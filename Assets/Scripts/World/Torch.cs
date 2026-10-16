using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Flickers a point light and its flame mesh.
    ///
    /// Each torch gets its own noise offset so a corridor of them does not pulse in
    /// unison — synchronised flicker is the fastest way to make firelight look fake.
    /// </summary>
    public class Torch : MonoBehaviour
    {
        public Light Light;
        public Transform Flame;
        public float BaseIntensity = 6f;
        private static Mesh emberMesh;
        private static Mesh EmberMesh => emberMesh ??= Art.MeshKit.Box(Vector3.one);

        private float offset;
        private Vector3 flameScale;
        private Vector3 lightRest;
        private Quaternion flameRest;
        private float nextEmber;
        private Transform[] tongues;

        private void Start()
        {
            offset = Random.value * 100f;
            if (Flame != null)
            {
                flameScale = Flame.localScale; flameRest = Flame.localRotation;
                var list = new System.Collections.Generic.List<Transform>();
                for (int i = 0; i < 3; i++) { var tg = Flame.Find("Tongue" + i); if (tg != null) list.Add(tg); }
                tongues = list.ToArray();
            }
            if (Light != null) lightRest = Light.transform.localPosition;
            nextEmber = Time.time + Random.value * 2f;
        }

        private void Update()
        {
            float t = Time.time * 7f + offset;
            float n = Mathf.PerlinNoise(t, offset) * 0.7f + Mathf.PerlinNoise(t * 2.7f, offset + 5f) * 0.3f;

            if (Light != null)
            {
                Light.intensity = BaseIntensity * (0.75f + n * 0.45f);
                // The flame wanders, so its shadows breathe a little.
                Light.transform.localPosition = lightRest + new Vector3(Mathf.PerlinNoise(t * 0.6f, offset + 9f) - 0.5f, 0f, Mathf.PerlinNoise(offset + 3f, t * 0.6f) - 0.5f) * 0.06f;
            }
            if (Flame != null)
            {
                Flame.localScale = new Vector3(flameScale.x * (0.9f + n * 0.2f), flameScale.y * (0.8f + n * 0.45f), flameScale.z * (0.9f + n * 0.2f));
                Flame.localRotation = flameRest * Quaternion.Euler((Mathf.PerlinNoise(t * 0.8f, offset + 1f) - 0.5f) * 14f, 0f, (Mathf.PerlinNoise(offset + 2f, t * 0.8f) - 0.5f) * 14f);
                // Each tongue leaps and sinks on its own rhythm.
                if (tongues != null)
                    for (int i = 0; i < tongues.Length; i++)
                    {
                        float k = Mathf.PerlinNoise(t * 1.3f + i * 3.1f, offset + i);
                        tongues[i].localScale = new Vector3(1f, 0.5f + k * 0.9f, 1f);
                    }
                // Now and then a spark lifts off (only near the viewer: cheap).
                if (Time.time >= nextEmber)
                {
                    nextEmber = Time.time + Random.Range(0.35f, 1.2f);
                    var cam = Art.ViewCamera.Current;
                    if (cam != null && (cam.transform.position - Flame.position).sqrMagnitude < 14f * 14f)
                        Art.Vfx.Spawn(EmberMesh, Art.Mat.Glow, Flame.position + Random.insideUnitSphere * 0.05f, Vector3.up * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * 0.25f,
                            Random.Range(0.8f, 1.6f), 0.025f * Mathf.Max(1f, flameScale.x * 1.6f), 0f, (Light != null ? Light.color : new Color(1f, 0.6f, 0.3f)) * 2.5f, -0.4f, 0.8f);
                }
            }
        }
    }
}
