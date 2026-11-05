using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A pair of fire vents facing each other across a corridor. They roar on a fixed beat
    /// (a function of the server clock, like the pendulums), so you can watch the rhythm
    /// and slip through between bursts. The dead just walk straight in. The
    /// server (<see cref="DungeonDirector"/>) burns whatever stands in the jet while it is lit.
    /// </summary>
    public class FlameJet : MonoBehaviour
    {
        // 2026-11-05 (the user: less downtime, "the player has to time their movement"): lit 1.2 s, dark 1 s (was 1.3 and 2.7).
        public const float Period = 2.2f, Burn = 1.2f, Warn = 0.4f, HalfWidth = 0.75f;

        private int phase;
        private Transform fire;
        private Transform[] tongues;
        private Light glow;
        private Renderer vents;
        private bool wasLit, wasWarn;

        public static bool Lit(int phase, double time) => Phase(phase, time) < Burn;
        public static bool Warning(int phase, double time) => Phase(phase, time) > Period - Warn;
        private static float Phase(int phase, double time) => (float)((time + phase * 1.37) % Period);

        public static void Build(Transform parent, DungeonLayout.FlameTrap trap, MeshKit.Batcher b)
        {
            var go = new GameObject("FlameJet");
            go.transform.SetParent(parent, false);
            go.transform.position = trap.At;
            var j = go.AddComponent<FlameJet>();
            j.phase = trap.Phase;
            // Iron grates in both walls, and the jet: a long flame across the corridor.
            foreach (float s in new[] { -1f, 1f })
                b.AddBox(trap.At + trap.Across * s * (DungeonLayout.CorridorWidth / 2f - 0.05f) + Vector3.up * 0.5f,
                    trap.Across.x != 0f ? new Vector3(0.12f, 0.5f, 0.9f) : new Vector3(0.9f, 0.5f, 0.12f), Mat.Steel);
            j.vents = MeshKit.Part(go.transform, "VentGlow", MeshKit.Box(trap.Across.x != 0f ? new Vector3(DungeonLayout.CorridorWidth - 0.05f, 0.04f, HalfWidth * 2f) : new Vector3(HalfWidth * 2f, 0.04f, DungeonLayout.CorridorWidth - 0.05f)),
                Mat.Glow, Vector3.up * 0.02f).GetComponent<Renderer>();
            j.fire = MeshKit.Pivot(go.transform, "Fire", Vector3.zero);
            var rng = new System.Random(trap.Phase * 7919 + 3);
            var tongues = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 9; i++)
            {
                // Uneven tongues: orange-red outsides with small hot cores, in a ragged line.
                float t = (i / 8f - 0.5f) * (DungeonLayout.CorridorWidth - 0.6f);
                var along = Vector3.Cross(trap.Across, Vector3.up) * (float)(rng.NextDouble() - 0.5) * 0.35f;
                var pivot = MeshKit.Pivot(j.fire, "Tongue" + i, trap.Across * t + along);
                float hgt = 0.9f + (float)rng.NextDouble() * 0.9f;
                var outer = MeshKit.Part(pivot, "Outer", MeshKit.Lathe("flame", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.07f, 0.05f), new Vector2(0.08f, 0.12f), new Vector2(0.04f, 0.24f), new Vector2(0f, 0.32f),
                }, 6, false), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(2.6f, hgt * 3.2f, 2.6f));
                ArtLibrary.SetGlow(outer.GetComponent<Renderer>(), new Color(1f, 0.32f, 0.06f) * 1.1f);
                outer.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var core = MeshKit.Part(pivot, "Core", MeshKit.Lathe("flameInner", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.04f, 0.04f), new Vector2(0.045f, 0.09f), new Vector2(0f, 0.18f),
                }, 6, false), Mat.Glow, Vector3.up * 0.02f, Vector3.zero, new Vector3(2.2f, hgt * 2.6f, 2.2f));
                ArtLibrary.SetGlow(core.GetComponent<Renderer>(), new Color(1f, 0.7f, 0.3f) * 1.6f);
                core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tongues.Add(pivot);
            }
            j.tongues = tongues.ToArray();
            j.glow = new GameObject("JetLight").AddComponent<Light>();
            j.glow.transform.SetParent(go.transform, false);
            j.glow.transform.localPosition = Vector3.up * 1.2f;
            j.glow.type = LightType.Point;
            j.glow.color = new Color(1f, 0.5f, 0.2f);
            j.glow.range = 7f;
            j.glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            double t = Pendulum.Clock;
            bool lit = Lit(phase, t), warn = Warning(phase, t);
            fire.gameObject.SetActive(lit);
            if (lit)
                for (int i = 0; i < tongues.Length; i++)
                    tongues[i].localScale = new Vector3(1f, 0.6f + 0.6f * Mathf.PerlinNoise(Time.time * 7f + i * 1.3f, phase), 1f);
            glow.intensity = lit ? 5f + Random.Range(-0.6f, 0.6f) : warn ? 1.2f : 0f;
            ArtLibrary.SetGlow(vents, lit ? new Color(0.9f, 0.25f, 0.05f) * 0.8f : warn ? new Color(1f, 0.3f, 0.1f) * (0.6f + 0.6f * Mathf.Sin(Time.time * 30f)) : new Color(0.3f, 0.08f, 0.02f));
            // Heard as well as seen: the grates grind as they warn, then the roar. Sfx rolls off with distance by itself.
            if (warn && !wasWarn) Sfx.Play(Sound.Creak, transform.position + Vector3.up, 0.45f, 1.6f);
            if (lit && !wasLit) Sfx.Play(Sound.Fire, transform.position + Vector3.up, 0.9f, 0.7f);
            wasWarn = warn;
            wasLit = lit;
        }
    }
}
