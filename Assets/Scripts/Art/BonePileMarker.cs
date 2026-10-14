using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A fallen skeleton on the Ossuary that has not finished dying: a heap of bones with a
    /// skull on top whose eyes kindle as it gathers itself, rattling harder near the end.
    /// Visual only; the server owns the timer (and whether someone stamped it out).
    /// </summary>
    public class BonePileMarker : MonoBehaviour
    {
        private float born, life;
        private Transform heap;
        private Renderer eyeL, eyeR;
        private Light glow;

        public static GameObject Spawn(Vector3 at, float seconds)
        {
            var go = new GameObject("BonePile");
            go.transform.position = at;
            var m = go.AddComponent<BonePileMarker>();
            m.born = Time.time;
            m.life = seconds;
            m.heap = MeshKit.Pivot(go.transform, "Heap", Vector3.zero);
            var rng = new System.Random(Mathf.RoundToInt(at.x * 13f + at.z * 7f));
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rng.NextDouble() * 360f;
                var p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (float)rng.NextDouble() * 0.45f + Vector3.up * 0.04f;
                MeshKit.Part(m.heap, "Bone" + i, MeshKit.Box(new Vector3(0.05f, 0.05f, 0.32f + (float)rng.NextDouble() * 0.2f)), Mat.Bone, p,
                    new Vector3(0f, (float)rng.NextDouble() * 180f, (float)rng.NextDouble() * 20f));
            }
            for (int i = 0; i < 3; i++)
                MeshKit.Part(m.heap, "Rib" + i, MeshKit.Lathe("rib" + i, new[] { new Vector2(0.12f + i * 0.012f, 0f), new Vector2(0.135f + i * 0.012f, 0.015f), new Vector2(0.12f + i * 0.012f, 0.03f) }, 8, true, 300f),
                    Mat.Bone, new Vector3(0.1f, 0.05f + i * 0.04f, -0.1f), new Vector3(80f, 30f * i, 0f));
            MeshKit.Part(m.heap, "Skull", MeshKit.Ball(0.11f, 8, 6), Mat.Bone, new Vector3(0f, 0.16f, 0.05f), new Vector3(-20f, 0f, 15f));
            m.eyeL = MeshKit.Part(m.heap, "EyeL", MeshKit.Ball(0.02f, 6, 4), Mat.Glow, new Vector3(-0.04f, 0.18f, 0.15f)).GetComponent<Renderer>();
            m.eyeR = MeshKit.Part(m.heap, "EyeR", MeshKit.Ball(0.02f, 6, 4), Mat.Glow, new Vector3(0.04f, 0.18f, 0.15f)).GetComponent<Renderer>();
            m.glow = new GameObject("PileGlow").AddComponent<Light>();
            m.glow.transform.SetParent(go.transform, false);
            m.glow.transform.localPosition = Vector3.up * 0.5f;
            m.glow.type = LightType.Point;
            m.glow.color = new Color(0.6f, 0.4f, 1f);
            m.glow.range = 2.5f;
            m.glow.shadows = LightShadows.None;
            Destroy(go, seconds + 2f);
            return go;
        }

        private void Update()
        {
            float k = Mathf.Clamp01((Time.time - born) / life);
            var eye = new Color(0.65f, 0.45f, 1f) * (0.3f + k * 2f * (0.7f + 0.3f * Mathf.Sin(Time.time * 9f)));
            ArtLibrary.SetGlow(eyeL, eye);
            ArtLibrary.SetGlow(eyeR, eye);
            glow.intensity = 0.3f + k * 1.4f;
            float shake = k * k * 0.035f;
            heap.localPosition = new Vector3(Random.Range(-shake, shake), Random.Range(0f, shake), Random.Range(-shake, shake));
        }
    }
}
