using UnityEngine;

namespace Deepbound.Art
{
    public class BeamVisual : MonoBehaviour
    {
        private Transform shaft, core;
        private Renderer coreRenderer;
        private Renderer[] bands;
        private Transform[] beads;
        private Light landing;
        private static MaterialPropertyBlock block;
        private const int BeadsPerStrand = 12;

        public static BeamVisual Create()
        {
            var go = new GameObject("MendingBeam");
            var b = go.AddComponent<BeamVisual>();
            b.shaft = new GameObject("Shaft").transform;
            b.shaft.SetParent(go.transform, false);
            b.bands = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                var band = new GameObject("Band");
                band.transform.SetParent(b.shaft, false);

                band.transform.localRotation = Quaternion.Euler(90f, 0f, i * 90f);
                band.AddComponent<MeshFilter>().sharedMesh = Shapes.Bar(1f, 0.32f);
                var r = band.AddComponent<MeshRenderer>();
                r.sharedMaterial = FxShape.SoftMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.bands[i] = r;
            }
            b.core = MeshKit.Part(go.transform, "Core", MeshKit.Box(new Vector3(0.03f, 0.03f, 1f)), Mat.Glow, Vector3.zero).transform;
            b.coreRenderer = b.core.GetComponent<Renderer>();
            b.coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.beads = new Transform[BeadsPerStrand * 2];
            var bead = MeshKit.Ball(0.035f, 6, 4);
            for (int i = 0; i < b.beads.Length; i++)
            {
                var p = MeshKit.Part(go.transform, "Bead", bead, Mat.Glow, Vector3.zero);
                p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(p.GetComponent<Renderer>(), new Color(1f, 0.88f, 0.55f) * 1.7f);
                b.beads[i] = p.transform;
            }
            b.landing = new GameObject("Landing").AddComponent<Light>();
            b.landing.transform.SetParent(go.transform, false);
            b.landing.type = LightType.Point;
            b.landing.color = new Color(1f, 0.85f, 0.45f);
            b.landing.range = 3f;
            b.landing.intensity = 1.4f;
            return b;
        }

        public void Set(Vector3 from, Vector3 to)
        {
            var mid = (from + to) / 2f;
            float len = Vector3.Distance(from, to);
            if (len < 0.01f) return;
            var rot = Quaternion.LookRotation(to - from);
            shaft.SetPositionAndRotation(mid, rot);
            core.SetPositionAndRotation(mid, rot);
            float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 9f);
            shaft.localScale = new Vector3(pulse, pulse, len);
            core.localScale = new Vector3(1f, 1f, len);
            block ??= new MaterialPropertyBlock();
            foreach (var r in bands)
            {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", new Color(1f * 1.6f, 0.8f * 1.6f, 0.4f * 1.6f, 0.55f * pulse));
                r.SetPropertyBlock(block);
            }
            ArtLibrary.SetGlow(coreRenderer, new Color(1f, 0.95f, 0.75f) * 2f);

            var right = rot * Vector3.right;
            var up = rot * Vector3.up;
            var dir = (to - from) / len;
            float flow = Time.time * 1.6f;
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < BeadsPerStrand; i++)
                {
                    float t = Mathf.Repeat(i / (float)BeadsPerStrand + flow / Mathf.Max(1f, len), 1f);
                    float a = t * len * 2.4f - Time.time * 6f + s * Mathf.PI;

                    float r = 0.11f * Mathf.Sin(t * Mathf.PI);
                    var bead = beads[s * BeadsPerStrand + i];
                    bead.position = from + dir * len * t + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;
                    bead.localScale = Vector3.one * (0.6f + 0.6f * Mathf.Sin(t * Mathf.PI));
                }
            landing.transform.position = to;
        }
    }
}
