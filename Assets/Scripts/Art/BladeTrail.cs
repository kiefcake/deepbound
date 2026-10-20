using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// First-person cut feedback (replaced 2026-10-20: the user disliked the swept ribbon). No trail
    /// any more: at the heart of the cut a hard glint of light runs up the blade from the guard to the
    /// tip (steel catching the torchlight), and three faint hairline "air cuts" streak off the tip in
    /// the direction it is travelling and vanish in a tenth of a second. It reads as speed and edge,
    /// not as a shape pasted in front of the camera.
    /// </summary>
    public class BladeTrail
    {
        private readonly Transform space, tip, edge;
        private readonly Color color;
        private readonly Transform glint;
        private readonly Renderer glintA, glintB;
        private readonly List<(Transform t, Renderer r, float born)> streaks = new List<(Transform, Renderer, float)>();
        private float armedAt = -1f, armedUntil = -1f;
        private Vector3 lastTip;
        private bool streaked;
        private const float GlintLife = 0.13f, StreakLife = 0.1f;

        public BladeTrail(Transform space, Transform tip, Transform edge, Color color)
        {
            this.space = space;
            this.tip = tip;
            this.edge = edge;
            this.color = color;
            glint = new GameObject("BladeGlint").transform;
            glint.gameObject.layer = space.gameObject.layer;
            glint.SetParent(space, false);
            glintA = Part(glint, new Vector3(0.11f, 0.008f, 0.004f));
            glintB = Part(glint, new Vector3(0.008f, 0.11f, 0.004f));
            glint.gameObject.SetActive(false);
        }

        private Renderer Part(Transform parent, Vector3 size)
        {
            var p = MeshKit.Part(parent, "Glint", MeshKit.Box(size), Mat.Glow, Vector3.zero);
            p.layer = space.gameObject.layer;
            var r = p.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>The cut is happening for <paramref name="seconds"/>.</summary>
        public void Arm(float seconds)
        {
            armedAt = Time.time;
            armedUntil = Time.time + seconds;
            streaked = false;
            lastTip = space.InverseTransformPoint(tip.position);
        }

        public void Update()
        {
            float now = Time.time;
            var tipNow = space.InverseTransformPoint(tip.position);
            var edgeNow = space.InverseTransformPoint(edge.position);

            // The glint: in the middle of the cut it runs from the edge mark to the tip and flares.
            float mid = armedAt + (armedUntil - armedAt) * 0.35f;
            float g = (now - mid) / GlintLife;
            bool glow = armedAt > 0f && g >= 0f && g <= 1f;
            if (glint.gameObject.activeSelf != glow) glint.gameObject.SetActive(glow);
            if (glow)
            {
                glint.localPosition = Vector3.Lerp(edgeNow, tipNow, Mathf.SmoothStep(0f, 1f, g));
                glint.localRotation = Quaternion.Euler(0f, 0f, 45f + g * 90f);
                float flare = Mathf.Sin(g * Mathf.PI);
                glint.localScale = Vector3.one * (0.5f + 1.1f * flare);
                ArtLibrary.SetGlow(glintA, color * (2.2f * flare));
                ArtLibrary.SetGlow(glintB, color * (2.2f * flare));
            }

            // The air cuts: once per swing, as the tip moves fastest, three hairlines off its path.
            var vel = tipNow - lastTip;
            if (!streaked && now < armedUntil && now > mid && vel.magnitude > 0.012f)
            {
                streaked = true;
                var dir = vel.normalized;
                var side = Vector3.Cross(dir, Vector3.forward).normalized;
                for (int i = -1; i <= 1; i++)
                {
                    float len = 0.16f + 0.05f * (1 - Mathf.Abs(i));
                    var p = MeshKit.Part(space, "AirCut", MeshKit.Box(new Vector3(0.0035f, len, 0.002f)), Mat.Glow, tipNow - dir * len * 0.6f + side * (i * 0.022f));
                    p.layer = space.gameObject.layer;
                    p.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                    var r = p.GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    streaks.Add((p.transform, r, now));
                }
            }
            lastTip = tipNow;
            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                var (t, r, born) = streaks[i];
                float k = (now - born) / StreakLife;
                if (k >= 1f || t == null) { if (t != null) Object.Destroy(t.gameObject); streaks.RemoveAt(i); continue; }
                ArtLibrary.SetGlow(r, color * (0.8f * (1f - k)));
                t.localScale = new Vector3(1f, 1f + k * 0.6f, 1f);
            }
        }
    }
}
