using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A jagged bolt from the ceiling into the first target, then arcing target to target.
    /// Built from thin glowing boxes between jittered points; lives a quarter of a second.
    /// </summary>
    public class LightningBolt : MonoBehaviour
    {
        private Light flash;
        private readonly List<Renderer> segments = new List<Renderer>();
        private float age;
        private static readonly Color Bolt = new Color(0.65f, 0.8f, 1f);

        public static void Spawn(Vector3 sky, Vector3[] targets)
        {
            if (targets == null || targets.Length == 0) return;
            var go = new GameObject("Lightning");
            var bolt = go.AddComponent<LightningBolt>();

            var path = new List<Vector3> { targets[0] + Vector3.up * 7f };
            path.AddRange(targets);
            for (int i = 0; i < path.Count - 1; i++) bolt.Arc(go.transform, path[i], path[i + 1]);

            bolt.flash = new GameObject("Flash").AddComponent<Light>();
            bolt.flash.transform.SetParent(go.transform, false);
            bolt.flash.transform.position = targets[0] + Vector3.up;
            bolt.flash.type = LightType.Point;
            bolt.flash.color = Bolt;
            bolt.flash.range = 12f;
            bolt.flash.intensity = 10f;
            Destroy(go, 0.3f);
        }

        private void Arc(Transform parent, Vector3 from, Vector3 to)
        {
            const int kinks = 5;
            var prev = from;
            for (int i = 1; i <= kinks; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)kinks);
                if (i < kinks) p += Random.insideUnitSphere * 0.35f;
                var mid = (prev + p) / 2f;
                var len = Vector3.Distance(prev, p);
                var seg = MeshKit.Part(parent, "Seg", MeshKit.Box(new Vector3(0.04f, 0.04f, 1f)), Mat.Glow, mid, Vector3.zero, new Vector3(1f, 1f, len));
                seg.transform.rotation = Quaternion.LookRotation(p - prev);
                var r = seg.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(r, Bolt * 4f);
                segments.Add(r);
                prev = p;
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(1f - age / 0.3f);
            flash.intensity = 10f * k;
            // Flicker: lightning strobes rather than fading smoothly.
            bool on = (int)(age * 40f) % 2 == 0 || age < 0.06f;
            foreach (var s in segments) s.enabled = on;
        }
    }
}
