using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Procedural ability effects built from <see cref="Puff"/>s and a few shared shapes:
    /// expanding rings, ember sprays, smoke, ice spikes. Client-side only — call from FX RPCs.
    /// </summary>
    public static class Vfx
    {
        public static Puff Spawn(Mesh mesh, string mat, Vector3 at, Vector3 velocity, float life, float startScale, float endScale, Color? glow = null, float gravity = 0f, float drag = 1f)
        {
            var go = MeshKit.Part(null, "Puff", mesh, mat, at);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var p = go.AddComponent<Puff>();
            p.Velocity = velocity;
            p.Life = life;
            p.StartScale = startScale;
            p.EndScale = endScale;
            p.Gravity = gravity;
            p.Drag = drag;
            if (glow.HasValue)
            {
                p.Glowing = true;
                p.Glow = glow.Value;
                ArtLibrary.SetGlow(r, glow.Value);
            }
            go.transform.localScale = Vector3.one * startScale;
            return p;
        }

        private static Mesh ball, cube, ring, spike;
        private static Mesh Ball => ball ??= MeshKit.Ball(0.5f, 8, 6);
        private static Mesh Cube => cube ??= MeshKit.Box(Vector3.one);
        private static Mesh RingMesh => ring ??= MeshKit.Lathe("vfxRing", new[] { new Vector2(0.92f, 0f), new Vector2(1f, 0.03f), new Vector2(0.92f, 0.06f) }, 32, false);
        private static Mesh Spike => spike ??= MeshKit.Lathe("vfxSpike", new[] { new Vector2(0f, 0f), new Vector2(0.18f, 0.05f), new Vector2(0f, 1f) }, 5);

        /// <summary>A flat ring that races outward from a point and fades: shockwaves, novas, cries.</summary>
        public static void Ring(Vector3 at, float radius, Color color, float life = 0.45f)
        {
            Spawn(RingMesh, Mat.Glow, at + Vector3.up * 0.08f, Vector3.zero, life, radius * 0.15f, radius, color * 2f);
        }

        /// <summary>Glowing sparks thrown up and out, falling back under gravity.</summary>
        public static void Embers(Vector3 at, int count, Color color, float speed = 6f, float size = 0.08f, float life = 0.9f)
        {
            for (int i = 0; i < count; i++)
            {
                var v = (Random.insideUnitSphere + Vector3.up * 0.9f).normalized * speed * Random.Range(0.4f, 1.1f);
                Spawn(Cube, Mat.Glow, at + Random.insideUnitSphere * 0.3f, v, life * Random.Range(0.6f, 1.2f), size, size * 0.2f, color * 2.2f, 9f, 0.6f);
            }
        }

        /// <summary>One soft mote drifting up from somewhere around a body (cosmetic auras).</summary>
        public static void Mote(Vector3 feet, Color color)
        {
            float a = Random.value * Mathf.PI * 2f;
            var p = feet + new Vector3(Mathf.Cos(a) * Random.Range(0.25f, 0.5f), Random.Range(0.1f, 1.5f), Mathf.Sin(a) * Random.Range(0.25f, 0.5f));
            Spawn(Cube, Mat.Glow, p, Vector3.up * Random.Range(0.3f, 0.6f) + Random.insideUnitSphere * 0.12f, Random.Range(1.2f, 2f), 0.06f, 0f, color * 1.7f, -0.1f, 0.4f);
        }

        /// <summary>Dust and grit kicked up and drifting off — small grey motes, rising and dwindling.</summary>
        public static void Smoke(Vector3 at, int count, float spread = 1f, float life = 2.2f)
        {
            count *= 3;
            for (int i = 0; i < count; i++)
            {
                var p = at + new Vector3(Random.Range(-spread, spread), Random.Range(0f, 0.6f), Random.Range(-spread, spread));
                Spawn(Ball, Mat.Rubble, p, Vector3.up * Random.Range(0.5f, 1.4f) + Random.insideUnitSphere * 0.5f, life * Random.Range(0.4f, 0.8f), Random.Range(0.12f, 0.28f), 0f, null, -0.3f, 1.2f);
            }
        }

        /// <summary>A blooming fireball that swells and burns out in a moment.</summary>
        public static void Fireball(Vector3 at, float size, Color color)
        {
            Spawn(Ball, Mat.Glow, at, Vector3.up * 1.5f, 0.45f, size * 0.3f, size, color * 2.4f);
            Spawn(Ball, Mat.Glow, at + Vector3.up * 0.3f, Vector3.up * 2.5f, 0.6f, size * 0.2f, size * 0.7f, color * 1.6f);
        }

        /// <summary>A crown of ice spikes bursting from the ground, then melting away.</summary>
        public static void IceSpikes(Vector3 at, float radius, int count = 14)
        {
            var ice = new Color(0.5f, 0.85f, 1f);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + Random.Range(-0.15f, 0.15f);
                float r = radius * Random.Range(0.45f, 0.95f);
                var p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                var s = Spawn(Spike, Mat.Glow, p, Vector3.zero, 0.9f, Random.Range(0.6f, 1.1f), 0.05f, ice * 1.4f);
                s.transform.rotation = Quaternion.LookRotation(Vector3.up, -new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))) * Quaternion.Euler(-90f + Random.Range(-25f, 10f), 0f, 0f);
            }
        }

        /// <summary>The floor under a point (for rings and spikes under a body), or the point itself.</summary>
        public static Vector3 Ground(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out var hit, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.point : p;

        /// <summary>A short-lived point light that fades out (flashes, explosions).</summary>
        public static void Flash(Vector3 at, Color color, float intensity, float range, float life)
        {
            var go = new GameObject("VfxFlash");
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            go.AddComponent<FadeLight>().Life = life;
        }
    }
}
