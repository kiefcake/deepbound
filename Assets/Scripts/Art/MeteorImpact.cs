using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Where a meteor lands: a fireball, a shockwave racing out, rock flung everywhere, then a
    /// smouldering crater — glowing cracks, little flames and smoke — that dies down over a
    /// few seconds, so you can see where it hit after the noise is over.
    /// </summary>
    public class MeteorImpact : MonoBehaviour
    {
        private static readonly Color Fire = new Color(1f, 0.5f, 0.15f);
        private const float Linger = 4.5f;

        private float age, nextSmoke, nextFlame;
        private float radius;
        private Renderer[] cracks;
        private Transform[] flames;
        private Light glow;

        public static void Spawn(Vector3 at, float radius, bool shattered)
        {
            Vfx.Flash(at + Vector3.up * 1.5f, Fire, 16f, 16f, 0.9f);
            Vfx.Fireball(at + Vector3.up * 0.6f, radius * 1.1f, Fire);
            Vfx.Ring(at, radius * 1.5f, Fire, 0.5f);
            Vfx.Ring(at, radius * 0.9f, new Color(1f, 0.85f, 0.5f), 0.3f);
            Vfx.Embers(at, 26, Fire, 8f, 0.1f, 1.1f);
            if (shattered) Vfx.Embers(at, 18, new Color(0.55f, 0.85f, 1f), 7f, 0.09f, 0.9f);
            BoneBurst.Spawn(at + Vector3.up * 0.2f, 1.6f, false, Mat.Rubble);
            Vfx.Smoke(at, 6, radius * 0.5f, 2.6f);
            if (World.DungeonDirector.InLobby) UI.CampChatter.Blast(at);

            var go = new GameObject("MeteorCrater");
            go.transform.position = at;
            var m = go.AddComponent<MeteorImpact>();
            m.radius = radius;
            m.Build();
        }

        private void Build()
        {
            // The scorch: a dark disc, a raised rim of broken stone, and molten cracks.
            MeshKit.Part(transform, "Scorch", MeshKit.Cylinder(radius * 0.85f, radius * 0.85f, 0.02f, 20), Mat.Dark, Vector3.up * 0.015f);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f + Random.Range(-12f, 12f);
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                MeshKit.Part(transform, "Rim" + i, MeshKit.Box(new Vector3(0.6f, 0.22f, 0.4f)), Mat.Rubble, d * radius * 0.85f + Vector3.up * 0.1f, new Vector3(Random.Range(-15f, 15f), a, Random.Range(-10f, 10f)));
            }
            cracks = new Renderer[7];
            for (int i = 0; i < cracks.Length; i++)
            {
                float a = i * (360f / cracks.Length) + Random.Range(-15f, 15f);
                float len = radius * Random.Range(0.4f, 0.8f);
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var c = MeshKit.Part(transform, "Crack" + i, MeshKit.Box(new Vector3(0.09f, 0.03f, len)), Mat.Glow, d * len / 2f + Vector3.up * 0.03f, new Vector3(0f, a, 0f));
                cracks[i] = c.GetComponent<Renderer>();
                ArtLibrary.SetGlow(cracks[i], Fire * 2f);
            }
            flames = new Transform[5];
            for (int i = 0; i < flames.Length; i++)
            {
                var p = Quaternion.Euler(0f, i * 72f + Random.Range(0f, 40f), 0f) * Vector3.forward * radius * Random.Range(0.2f, 0.7f);
                var f = MeshKit.Part(transform, "Flame" + i, MeshKit.Lathe("craterFlame", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.14f, 0.1f), new Vector2(0.08f, 0.35f), new Vector2(0f, 0.55f),
                }, 6), Mat.Flame, p);
                flames[i] = f.transform;
            }
            glow = new GameObject("Embers").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 0.8f;
            glow.type = LightType.Point;
            glow.color = Fire;
            glow.range = radius * 2.2f;
            glow.intensity = 3f;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(1f - age / Linger);
            foreach (var c in cracks) if (c != null) ArtLibrary.SetGlow(c, Fire * 2f * k);
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null) continue;
                float flicker = 0.75f + Mathf.PerlinNoise(Time.time * 7f, i * 3.1f) * 0.6f;
                flames[i].localScale = new Vector3(1f, flicker, 1f) * Mathf.Clamp01(k * 1.6f);
            }
            glow.intensity = 3f * k * (0.8f + Mathf.PerlinNoise(Time.time * 5f, 0f) * 0.4f);
            if (Time.time >= nextSmoke && k > 0.15f)
            {
                nextSmoke = Time.time + 0.35f;
                Vfx.Smoke(transform.position, 1, radius * 0.5f, 2f);
            }
            if (Time.time >= nextFlame && k > 0.3f)
            {
                nextFlame = Time.time + 0.12f;
                Vfx.Embers(transform.position + Random.insideUnitSphere * radius * 0.5f, 1, Fire, 2.5f, 0.06f, 0.8f);
            }
            if (age >= Linger) Destroy(gameObject);
        }
    }
}
