using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Procedural ability effects built from <see cref="Puff"/>s and a few shared shapes:
    /// expanding rings, ember sprays, smoke, ice spikes. Client-side only — call from FX RPCs.
    /// </summary>
    public static class Vfx
    {
        // Spent particles wait here instead of being destroyed: effects fire constantly, and
        // creating and collecting hundreds of GameObjects a second is what makes frames hitch.
        private static readonly System.Collections.Generic.Stack<Puff> pool = new System.Collections.Generic.Stack<Puff>();
        private const int PoolCap = 400;

        public static void Recycle(Puff p)
        {
            if (p == null) return;
            if (pool.Count >= PoolCap) { Object.Destroy(p.gameObject); return; }
            p.gameObject.SetActive(false);
            pool.Push(p);
        }

        public static Puff Spawn(Mesh mesh, string mat, Vector3 at, Vector3 velocity, float life, float startScale, float endScale, Color? glow = null, float gravity = 0f, float drag = 1f)
        {
            Puff p = null;
            while (pool.Count > 0 && p == null) p = pool.Pop();
            GameObject go;
            Renderer r;
            if (p != null)
            {
                go = p.gameObject;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                r = go.GetComponent<Renderer>();
                r.sharedMaterial = ArtLibrary.Get(mat);
                r.SetPropertyBlock(null);
                go.transform.SetPositionAndRotation(at, Quaternion.identity);
                go.SetActive(true);
                p.Restart();
                p.Glowing = false;
                p.Fading = false;
            }
            else
            {
                go = MeshKit.Part(null, "Puff", mesh, mat, at);
                r = go.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p = go.AddComponent<Puff>();
            }
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
            // A soft textured shock ring racing out under it (Kenney's circle sprite).
            FxSprite.Decal("circle_05", at, radius * 0.3f, radius * 2.1f, life * 1.2f, new Color(color.r, color.g, color.b, 0.45f));
        }

        /// <summary>
        /// A hit landing: a spray of sparks and bone chips thrown out of the struck side (toward
        /// <paramref name="toward"/>, the attacker), and a puff of grit. Stronger hits throw more.
        /// </summary>
        public static void HitImpact(Vector3 at, Vector3 toward, float strength, Color spark, string chipMaterial = Mat.Bone)
        {
            strength = Mathf.Clamp01(strength);
            var n = toward - at;
            n.y = 0f;
            n = n.sqrMagnitude > 0.001f ? n.normalized : Vector3.back;
            int sparks = 5 + Mathf.RoundToInt(strength * 9f);
            for (int i = 0; i < sparks; i++)
            {
                var v = (n + Random.insideUnitSphere * 0.7f + Vector3.up * 0.35f).normalized * Random.Range(4f, 9f);
                Spawn(Cube, Mat.Glow, at + Random.insideUnitSphere * 0.08f, v, Random.Range(0.15f, 0.3f), 0.04f, 0f, spark * 2.6f, 12f, 3f);
            }
            int chips = 2 + Mathf.RoundToInt(strength * 4f);
            for (int i = 0; i < chips; i++)
            {
                var v = (n + Random.insideUnitSphere * 0.9f + Vector3.up * 0.6f).normalized * Random.Range(2.5f, 5f);
                var chip = Spawn(Cube, chipMaterial, at + Random.insideUnitSphere * 0.1f, v, Random.Range(0.45f, 0.8f), Random.Range(0.035f, 0.06f), 0.02f, null, 14f, 0.5f);
                chip.transform.rotation = Random.rotation;
            }
            Spawn(Ball, Mat.Rubble, at + n * 0.15f, n * 0.6f + Vector3.up * 0.3f, 0.35f, 0.08f, 0.35f + strength * 0.2f, null, -0.2f, 2f);
            // A star of light where it lands, gone in a blink.
            FxSprite.Billboard(FxSprite.AnyStar, at + n * 0.1f, Vector3.zero, 0.16f, 0.35f, 0.8f + strength * 0.6f, new Color(spark.r, spark.g, spark.b, 1f), Random.Range(-300f, 300f));
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

        /// <summary>A speck of dust turning in a light shaft: slow, faint, drifting.</summary>
        public static void Speck(Vector3 at, Color color) =>
            Spawn(Cube, Mat.Glow, at, Random.insideUnitSphere * 0.12f + Vector3.down * 0.05f, Random.Range(2f, 3.5f), 0.025f, 0f, color * 0.9f, 0f, 0.2f);

        /// <summary>One soft mote drifting up from somewhere around a body (cosmetic auras).</summary>
        public static void Mote(Vector3 feet, Color color)
        {
            float a = Random.value * Mathf.PI * 2f;
            var p = feet + new Vector3(Mathf.Cos(a) * Random.Range(0.25f, 0.5f), Random.Range(0.1f, 1.5f), Mathf.Sin(a) * Random.Range(0.25f, 0.5f));
            Spawn(Cube, Mat.Glow, p, Vector3.up * Random.Range(0.3f, 0.6f) + Random.insideUnitSphere * 0.12f, Random.Range(1.2f, 2f), 0.06f, 0f, color * 1.7f, -0.1f, 0.4f);
        }

        /// <summary>A thread of incense: faint pale wisps that curl up, swell and thin away (not grit: see Smoke).</summary>
        public static void Incense(Vector3 at)
        {
            var drift = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(0.25f, 0.4f), Random.Range(-0.08f, 0.08f));
            var p = Spawn(Ball, Mat.Smoke, at + Random.insideUnitSphere * 0.02f, drift, Random.Range(2.2f, 3f), 0.03f, Random.Range(0.18f, 0.28f), null, -0.05f, 0.5f);
            p.Fading = true;
            p.Fade = new Color(0.82f, 0.8f, 0.74f, 0.32f);
            ArtLibrary.Tint(p.GetComponent<Renderer>(), new Color(p.Fade.r, p.Fade.g, p.Fade.b, 0f));
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
            // Billowing smoke sprites rolling up through the grit.
            int clouds = Mathf.Clamp(count / 3, 1, 4);
            for (int i = 0; i < clouds; i++)
            {
                var p = at + new Vector3(Random.Range(-spread, spread) * 0.6f, Random.Range(0.2f, 0.7f), Random.Range(-spread, spread) * 0.6f);
                FxSprite.Billboard(FxSprite.AnySmoke, p, Vector3.up * Random.Range(0.4f, 0.9f), life * Random.Range(0.7f, 1f), spread * 0.8f + 0.4f, spread * 2f + 1f, new Color(0.22f, 0.2f, 0.22f, 0.5f), Random.Range(-25f, 25f), 0.8f);
            }
        }

        /// <summary>A blooming fireball that swells and burns out in a moment.</summary>
        public static void Fireball(Vector3 at, float size, Color color)
        {
            // A white-hot core that pops and is gone...
            Spawn(Ball, Mat.Glow, at, Vector3.up * 1.2f, 0.25f, size * 0.25f, size * 0.7f, Color.Lerp(color, Color.white, 0.45f) * 2f);
            // ...lumpy billows rolling up and out, cooling from orange to a deep red...
            var deep = new Color(color.r * 0.8f, color.g * 0.35f, color.b * 0.2f);
            for (int i = 0; i < 7; i++)
            {
                var dir = Quaternion.Euler(Random.Range(-20f, 50f), i * 51f + Random.Range(-15f, 15f), 0f) * Vector3.forward;
                var c = Color.Lerp(color, deep, Random.Range(0.1f, 0.7f));
                Spawn(Ball, Mat.Glow, at + dir * size * 0.25f + Vector3.up * 0.2f, dir * size * 1.6f + Vector3.up * 2.2f,
                    Random.Range(0.45f, 0.7f), size * 0.25f, size * Random.Range(0.5f, 0.75f), c * 1.5f, 0f, 2.5f);
            }
            // A licking fire sprite through the billows.
            FxSprite.Billboard(Random.value < 0.5f ? "fire_01" : "fire_02", at + Vector3.up * size * 0.2f, Vector3.up * 1.5f, 0.55f, size * 0.6f, size * 1.7f, new Color(color.r, color.g, color.b, 0.8f), Random.Range(-60f, 60f));
            // ...and a dark cap of smoke climbing after it.
            Smoke(at + Vector3.up * size * 0.6f, 2, size * 0.5f, 1.8f);
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
        /// <summary>
        /// The walkable floor under <paramref name="p"/> (2026-10-20): prefers the level's own floor
        /// (structure) to the tops of props and bodies, so ground circles never sit up on a crate or
        /// a shoulder. Searches from just above down to 3 m below; returns p if nothing is found.
        /// </summary>
        public static Vector3 Floor(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 0.9f, Vector3.down, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return p;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            RaycastHit? best = null;
            foreach (var h in hits)
            {
                if (h.normal.y < 0.55f || h.collider.GetComponentInParent<Combat.Health>() != null) continue;
                var root = h.collider.transform.root;
                if (h.collider.name.Contains("Structure") || root.name.Contains("Structure") || h.collider.transform.parent != null && h.collider.transform.parent.name.Contains("Structure")) return h.point;
                if (best == null) best = h;
            }
            return best.HasValue ? best.Value.point : p;
        }

        public static Vector3 Ground(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out var hit, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.point : p;

        /// <summary>A short-lived point light that fades out (flashes, explosions).</summary>
        public static void Flash(Vector3 at, Color color, float intensity, float range, float life)
        {
            if (UI.Settings.ReduceFlashing) intensity *= 0.35f;
            var go = new GameObject("VfxFlash");
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            go.AddComponent<FadeLight>().Life = life;
            // The flash seen, not only lit: a soft flare at its heart.
            float flare = Mathf.Clamp(range * 0.26f, 0.6f, 3.5f) * (UI.Settings.ReduceFlashing ? 0.6f : 1f);
            FxSprite.Billboard("flare_01", at, Vector3.zero, life * 0.7f, flare * 0.6f, flare, new Color(color.r, color.g, color.b, UI.Settings.ReduceFlashing ? 0.4f : 0.95f), Random.Range(-40f, 40f));
        }
    }
}
