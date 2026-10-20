using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A boss gathering itself before a blow (2026-10-20). Added at runtime to the boss on every
    /// peer: during a wind-up the body crouches and draws in (squash), a core of light swells at
    /// its chest and embers stream into it; on release it stretches up and snaps back, throwing
    /// dust. Purely visual, driven by <see cref="Anticipate"/> and <see cref="Release"/>.
    /// </summary>
    public class BossWindup : MonoBehaviour
    {
        private Transform body;
        private Vector3 baseScale;
        private float start, end = -1f, releaseAt = -1f, strength = 1f;
        private Color color = new Color(1f, 0.4f, 0.2f);
        private Transform core;
        private Renderer coreRenderer;
        private Light coreLight;
        private float height = 2f, nextEmber;

        public static BossWindup For(GameObject boss, HumanoidRig rig, float height)
        {
            var w = boss.GetComponent<BossWindup>();
            if (w == null) w = boss.AddComponent<BossWindup>();
            if (rig != null && w.body != rig.transform)
            {
                w.body = rig.transform;
                w.baseScale = rig.transform.localScale;
                w.color = rig.GlowColor;
            }
            w.height = height;
            return w;
        }

        /// <summary>Gather for <paramref name="seconds"/>; <paramref name="k"/> 0..1 scales how big the tell is.</summary>
        public void Anticipate(float seconds, float k)
        {
            start = Time.time;
            end = Time.time + Mathf.Max(0.15f, seconds);
            strength = Mathf.Clamp(k, 0.2f, 1.5f);
            if (core == null)
            {
                core = MeshKit.Part(transform, "WindupCore", MeshKit.Ball(0.25f, 10, 8), Mat.Glow, Vector3.up * height * 0.62f).transform;
                coreRenderer = core.GetComponent<Renderer>();
                coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                coreLight = core.gameObject.AddComponent<Light>();
                coreLight.type = LightType.Point;
                coreLight.shadows = LightShadows.None;
                coreLight.range = 6f;
            }
        }

        public void Release()
        {
            end = -1f;
            releaseAt = Time.time;
            var at = transform.position;
            Vfx.Ring(at + Vector3.up * 0.06f, 2.2f + height * 0.4f, new Color(0.55f, 0.5f, 0.45f), 0.4f);
            Vfx.Embers(at + Vector3.up * 0.3f, 12, new Color(0.6f, 0.55f, 0.48f), 5f, 0.07f, 0.6f);
        }

        private void Update()
        {
            float squash = 0f, stretch = 0f, glow = 0f;
            if (end > 0f)
            {
                float t = Mathf.Clamp01((Time.time - start) / Mathf.Max(0.01f, end - start));
                // Ease in: most of the gathering comes late, so the last instant reads as "now".
                float e = t * t;
                squash = e * 0.07f * strength;
                glow = e;
                if (Time.time >= nextEmber)
                {
                    nextEmber = Time.time + Mathf.Lerp(0.14f, 0.04f, e);
                    // Embers drawn in from a wide ring towards the chest.
                    var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                    Vfx.Mote(transform.position + dir * Random.Range(1.5f, 3.2f) + Vector3.up * Random.Range(0.2f, height), color);
                }
                if (t >= 1f) Release();
            }
            if (releaseAt > 0f)
            {
                float r = (Time.time - releaseAt) / 0.28f;
                if (r >= 1f) releaseAt = -1f;
                else stretch = Mathf.Sin(r * Mathf.PI) * 0.06f * strength;
            }
            if (body != null)
                body.localScale = new Vector3(baseScale.x * (1f + squash * 0.6f - stretch * 0.4f), baseScale.y * (1f - squash + stretch), baseScale.z * (1f + squash * 0.6f - stretch * 0.4f));
            if (core != null)
            {
                bool on = glow > 0.01f;
                if (core.gameObject.activeSelf != on) core.gameObject.SetActive(on);
                if (on)
                {
                    core.localScale = Vector3.one * (0.4f + glow * 1.4f * strength);
                    ArtLibrary.SetGlow(coreRenderer, color * (0.6f + glow * 1.6f));
                    coreLight.color = color;
                    coreLight.intensity = glow * 4f * strength;
                }
            }
        }
    }
}
