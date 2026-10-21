using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Where a boss's bullet pattern is coming from (2026-10-21, the user: "be more discernable where
    /// they are shooting from"): a burning core with a spoke for every arm, turned to the arms' current
    /// angle so you can read the spin, a pale line down to a mark on the floor so you can find it from
    /// anywhere in the arena, and a swell of the core on every volley. Local to each peer, driven by the volley RPCs
    /// (which carry the emitter's position): it glides between volleys and fades when they stop.
    /// </summary>
    public class PatternEmitter : MonoBehaviour
    {
        private static readonly Dictionary<(Object, byte), PatternEmitter> live = new Dictionary<(Object, byte), PatternEmitter>();

        private Transform core, spokes, floorMark, line;
        private Renderer coreR, lineR, markR;
        private readonly List<Renderer> spokeR = new List<Renderer>();
        private Vector3 target, velocity;
        private float lastVolley, angle, shownAngle;
        private int arms;
        private Color color;
        private float floorY;

        public const float FadeAfter = 1.3f;

        public static void Volley(Object owner, byte id, Vector3 at, float angle, int arms, Color color)
        {
            if (!live.TryGetValue((owner, id), out var e) || e == null)
            {
                e = new GameObject("PatternEmitter").AddComponent<PatternEmitter>();
                e.Build(color);
                e.transform.position = at;
                e.target = at;
                live[(owner, id)] = e;
            }
            e.velocity = Time.time - e.lastVolley < 1.5f ? (at - e.target) / Mathf.Max(0.05f, Time.time - e.lastVolley) : Vector3.zero;
            e.target = at;
            e.lastVolley = Time.time;
            e.angle = angle;
            if (e.arms != arms) e.BuildSpokes(Mathf.Clamp(arms, 1, 16));
            e.floorY = Vfx.Floor(at).y;
            // No light per volley (patterns fire many a second): the core swells instead (Update).
        }

        private void Build(Color c)
        {
            color = c;
            core = MeshKit.Part(transform, "Core", MeshKit.Ball(0.3f, 12, 8), Mat.Glow, Vector3.zero).transform;
            coreR = core.GetComponent<Renderer>();
            ArtLibrary.SetGlow(coreR, c * 1.05f);
            var halo = MeshKit.Part(transform, "Halo", MeshKit.Ball(0.55f, 12, 8), Mat.Smoke, Vector3.zero);
            ArtLibrary.Tint(halo.GetComponent<Renderer>(), new Color(c.r, c.g, c.b, 0.16f));
            spokes = MeshKit.Pivot(transform, "Spokes", Vector3.zero);
            // A thin column of light down to the floor, and a ring there.
            line = MeshKit.Part(transform, "Line", MeshKit.Cylinder(0.025f, 0.025f, 1f, 6), Mat.Glow, Vector3.zero).transform;
            lineR = line.GetComponent<Renderer>();
            ArtLibrary.SetGlow(lineR, c * 0.7f);
            floorMark = new GameObject("FloorMark").transform;
            floorMark.SetParent(transform, false);
            var ring = MeshKit.Part(floorMark, "Ring", MeshKit.Lathe("emitterRing", new[] { new Vector2(0.7f, 0f), new Vector2(0.85f, 0.01f), new Vector2(0.7f, 0.02f) }, 24, false), Mat.Glow, Vector3.zero);
            markR = ring.GetComponent<Renderer>();
            ArtLibrary.SetGlow(markR, c * 0.9f);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lastVolley = Time.time;
        }

        private void BuildSpokes(int n)
        {
            arms = n;
            foreach (Transform c in spokes) Destroy(c.gameObject);
            spokeR.Clear();
            var mesh = MeshKit.Box(new Vector3(0.06f, 0.06f, 0.75f));
            for (int i = 0; i < n; i++)
            {
                var piv = MeshKit.Pivot(spokes, "Spoke" + i, Vector3.zero, new Vector3(0f, i * 360f / n, 0f));
                var s = MeshKit.Part(piv, "Bar", mesh, Mat.Glow, new Vector3(0f, 0f, 0.55f));
                var r = s.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(r, color * 1.2f);
                spokeR.Add(r);
            }
        }

        private void Update()
        {
            float since = Time.time - lastVolley;
            // Glide on toward where the next volley will likely be (emitters orbit and walk).
            var want = target + velocity * Mathf.Min(since, 1f);
            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-Time.deltaTime * 14f));
            shownAngle = Mathf.LerpAngle(shownAngle, angle, 1f - Mathf.Exp(-Time.deltaTime * 20f));
            spokes.localRotation = Quaternion.Euler(0f, shownAngle, 0f);
            float pulse = 1f + 0.25f * Mathf.Max(0f, 1f - since * 6f);
            core.localScale = Vector3.one * pulse;

            float h = Mathf.Max(0.05f, transform.position.y - floorY);
            line.localPosition = Vector3.down * h;
            line.localScale = new Vector3(1f, h, 1f);
            floorMark.position = new Vector3(transform.position.x, floorY + 0.04f, transform.position.z);
            floorMark.rotation = Quaternion.Euler(0f, -Time.time * 40f, 0f);

            float fade = Mathf.Clamp01(1f - (since - FadeAfter) / 0.5f);
            if (fade < 1f)
            {
                ArtLibrary.SetGlow(coreR, color * 1.05f * fade);
                ArtLibrary.SetGlow(lineR, color * 0.7f * fade);
                ArtLibrary.SetGlow(markR, color * 0.9f * fade);
                foreach (var r in spokeR) if (r != null) ArtLibrary.SetGlow(r, color * 1.2f * fade);
                if (fade <= 0f) Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            var dead = new List<(Object, byte)>();
            foreach (var kv in live) if (kv.Value == this || kv.Value == null) dead.Add(kv.Key);
            foreach (var k in dead) live.Remove(k);
        }
    }
}
