using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Each class's abilities as their own SILHOUETTES (2026-11-21, the user: "every ability should be discernable in battle,
    /// and look distinct"; the first pass, one shared burst recoloured per class, was "not what i meant"). Every class has a
    /// shape language, and no two abilities share a shape:
    /// <list type="bullet">
    /// <item><b>Knight</b>, gold and steel, the SHIELD: a kite ward of light while blocking; Shield Bash hurls the kite
    ///   forward and it shatters; War Cry is walls of sound and a chevron over every foe it taunts; the Banner raises a
    ///   wall round its ground. Swings: a broad steel crescent, a cross-glint where steel bites.</item>
    /// <item><b>Cleric</b>, white gold, the CROSS and light from above: smites are spinning crosses that stand up where they
    ///   land; the beam lifts little crosses off the mended; Consecrate drops a shaft of light and etches a cross in
    ///   the ground; Aegis spins a gyroscope of rings round each ally; the Hymn lowers a halo onto every head.</item>
    /// <item><b>Flagellant</b>, crimson and iron, BLOOD and BROKEN GROUND: the Scourge is a lash drawn round the body; the
    ///   chain ends in a barbed hook; the Leap trails blood and cracks the floor open; the Red Litany whirls three
    ///   spiked flails round him; the flail's crash splits the stone.</item>
    /// <item><b>Rogue</b>, violet and black, CUTS and AFTERIMAGES: stabs are thin thrusts and an X nicked where they land;
    ///   knives trail violet; Shadowstep leaves a ghost of the body behind and tears in where it lands; the Smoke Bomb
    ///   pops a dome of black; the Opener and Death From Shadow are crossed and starred cuts.</item>
    /// <item><b>Plague Doctor</b>, sick green and glass, FLASKS, FLIES and VEINS: vials and tonics fly as tumbling flasks;
    ///   a vial bursts in glass and leaves a bubbling pool, a tonic in a rose mist; flies swarm in the Miasma; the Mask
    ///   rises as a ghostly beak; the Pandemic sends black veins across the floor to every foe and the flies to them.</item>
    /// </list>
    /// Client only: called from the kits' FX RPCs. Abilities fire rarely enough that nothing here is pooled.
    /// </summary>
    public static class ClassFx
    {
        public static readonly Color Gold = new Color(1f, 0.8f, 0.42f);
        public static readonly Color Steel = new Color(0.85f, 0.9f, 1f);
        public static readonly Color Roar = new Color(1f, 0.38f, 0.12f);
        public static readonly Color Holy = new Color(1f, 0.88f, 0.55f);
        public static readonly Color Crimson = new Color(0.95f, 0.1f, 0.07f);
        public static readonly Color Violet = new Color(0.62f, 0.36f, 1f);
        public static readonly Color Plague = new Color(0.55f, 1f, 0.3f);
        public static readonly Color Rose = new Color(1f, 0.5f, 0.62f);
        public static readonly Color Pale = new Color(0.78f, 1f, 0.72f);

        private static bool Live => !Application.isBatchMode;

        /// <summary>HDR colour for additive parts: <paramref name="i"/> x brightness (above 1 blooms), <paramref name="a"/> opacity.</summary>
        private static Color H(Color c, float i, float a) => new Color(c.r * i, c.g * i, c.b * i, a);

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }

        private static Vector3 CamPos => ViewCamera.Current != null ? ViewCamera.Current.transform.position : Vector3.zero;

        /// <summary>Turns a flat (XY) part's face to the camera, keeping it upright.</summary>
        private static Quaternion FaceCam(Vector3 at)
        {
            var d = at - CamPos;
            return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d, Vector3.up) : Quaternion.identity;
        }

        /// <summary>Is the camera standing in this body (your own cast seen from your own eyes)?</summary>
        public static bool OwnBody(Vector3 feet)
        {
            var d = CamPos - feet;
            return ViewCamera.Current != null && d.y > 0.4f && d.y < 2.4f && new Vector2(d.x, d.z).sqrMagnitude < 0.8f * 0.8f;
        }

        private static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

        /// <summary>Fade in over <paramref name="inn"/> and out over the last <paramref name="outt"/> of k.</summary>
        private static float Env(float k, float inn, float outt) => Mathf.Clamp01(k / Mathf.Max(0.001f, inn)) * Mathf.Clamp01((1f - k) / Mathf.Max(0.001f, outt));

        /// <summary>A straight band of light from a to b, built twice crosswise so it reads from any side.</summary>
        public static FxShape Tether(Vector3 a, Vector3 b, Color c, float width, float life)
        {
            if (!Live) return null;
            var d = b - a;
            if (d.sqrMagnitude < 0.01f) return null;
            var s = FxShape.Make("Tether", a, Quaternion.LookRotation(d), life);
            float len = d.magnitude;
            var path = new[] { Vector3.zero, Vector3.forward * len * 0.5f, Vector3.forward * len };
            var w = new[] { width * 0.6f, width, width * 0.4f };
            s.Light(Shapes.Ribbon(path, new[] { Vector3.right, Vector3.right, Vector3.right }, w), c, Vector3.zero, Quaternion.identity);
            s.Light(Shapes.Ribbon(path, new[] { Vector3.up, Vector3.up, Vector3.up }, w), c, Vector3.zero, Quaternion.identity);
            s.Tick = (sh, k) => sh.Bright = 1f - k * k;
            return s;
        }

        /// <summary>A ribbon lying on the floor from <paramref name="from"/> along <paramref name="dir"/> (dust furrows, smears, veins).</summary>
        private static Transform FloorBand(FxShape s, Vector3 from, Vector3 dir, float length, float width, Color c, bool shade, float jitter = 0f, int seed = 0)
        {
            dir = Flat(dir);
            var side = Vector3.Cross(Vector3.up, dir);
            var rnd = new System.Random(seed);
            int n = Mathf.Clamp(Mathf.RoundToInt(length / 0.35f), 2, 40);
            var path = new Vector3[n];
            var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                var p = from + dir * length * t + side * (jitter * ((float)rnd.NextDouble() - 0.5f) * (i > 0 ? 1f : 0f));
                path[i] = Vfx.Floor(p) + Vector3.up * 0.04f - s.transform.position;
                w[i] = width * Mathf.Lerp(1f, 0.35f, t);
            }
            var mesh = Shapes.Ribbon(path, Shapes.FlatSides(path), w);
            return shade ? s.Shade(mesh, c, Vector3.zero, Quaternion.identity) : s.Light(mesh, c, Vector3.zero, Quaternion.identity);
        }

        /// <summary>Chunks of floor thrown up and falling back (landings, slams).</summary>
        public static void Rubble(Vector3 at, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                var p = Vfx.Spawn(MeshKit.Box(Vector3.one), Mat.Rubble, at + dir * Random.Range(0.2f, 0.8f) + Vector3.up * 0.1f,
                    dir * speed * Random.Range(0.4f, 1f) + Vector3.up * speed * Random.Range(0.9f, 1.6f), Random.Range(0.6f, 0.9f), Random.Range(0.1f, 0.2f), 0.04f, null, 16f, 0.3f);
                p.transform.rotation = Random.rotation;
            }
        }

        /// <summary>Little shards of something (shield light, glass) thrown out and tumbling.</summary>
        public static void Shards(Vector3 at, Vector3 dir, int count, Color glow, float speed, float size = 0.12f)
        {
            var mesh = MeshKit.Box(new Vector3(1f, 0.55f, 0.08f));
            for (int i = 0; i < count; i++)
            {
                var v = (dir + Random.insideUnitSphere * 0.9f + Vector3.up * 0.3f).normalized * speed * Random.Range(0.5f, 1f);
                var p = Vfx.Spawn(mesh, Mat.Glow, at + Random.insideUnitSphere * 0.25f, v, Random.Range(0.35f, 0.6f), size * Random.Range(0.6f, 1.2f), 0f, glow, 9f, 1.2f);
                p.transform.rotation = Random.rotation;
            }
        }

        /// <summary>Cracks split across the floor from a point: dark rifts with a glowing seam, fading.</summary>
        public static void Cracks(Vector3 at, float radius, int count, Color seam, float life = 4f)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at) + Vector3.up * 0.035f;
            var s = FxShape.Make("Cracks", floor, Quaternion.identity, life);
            var seams = new List<Transform>();
            float turn = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                int seed = Random.Range(0, 99999);
                float len = radius * Random.Range(0.55f, 1.15f);
                var rot = Quaternion.Euler(0f, turn + i * 360f / count + Random.Range(-14f, 14f), 0f);
                var start = rot * Vector3.forward * radius * 0.12f;
                s.Shade(Shapes.Crack(len, 0.26f, seed), new Color(0.05f, 0.03f, 0.03f, 0.85f), start, rot);
                seams.Add(s.Light(Shapes.Crack(len, 0.08f, seed), H(seam, 2.2f, 1f), start + Vector3.up * 0.005f, rot));
            }
            s.Tick = (sh, k) =>
            {
                sh.Bright = Mathf.Clamp01((1f - k) / 0.3f);
                // The seams cool long before the rifts close.
                float glow = Mathf.Clamp01(1f - sh.Age / 1.4f);
                foreach (var t in seams) sh.Dim(t, glow);
            };
        }

        /// <summary>A low wall of force racing out across the floor from a point (a landing, a slam).</summary>
        public static void Shock(Vector3 at, float radius, Color c, float height, float life, float delay = 0f)
        {
            if (!Live) return;
            var s = FxShape.Make("Shock", Vfx.Floor(at), Quaternion.identity, life + delay);
            var wall = s.Light(Shapes.Wall(), H(c, 2f, 0.85f), Vector3.zero, Quaternion.identity, Vector3.zero);
            s.Tick = (sh, k) =>
            {
                float kk = Mathf.Clamp01((sh.Age - delay) / life);
                if (sh.Age < delay) { wall.localScale = Vector3.zero; return; }
                float r = Mathf.Lerp(0.4f, radius, EaseOut(kk));
                wall.localScale = new Vector3(r, Mathf.Lerp(height, height * 0.2f, kk), r);
                sh.Bright = (1f - kk) * (1f - kk);
            };
        }

        // ==================================================================
        // KNIGHT: the shield
        // ==================================================================

        /// <summary>The ward of a raised shield, seen by everyone else: a kite of gold light before the Knight. Flash() on a block.</summary>
        public class Ward
        {
            public FxShape Shape;
            public float Flash;
            public void Hit() => Flash = 1f;
        }

        public static Ward KnightWard(Transform knight, System.Func<bool> raised, System.Func<bool> hidden)
        {
            if (!Live) return null;
            var ward = new Ward();
            var s = FxShape.Make("KnightWard", knight.position, knight.rotation, 1e9f).Ride(knight, new Vector3(0.05f, 1.2f, 0.9f), true);
            var face = s.Pivot(Vector3.zero, Quaternion.identity);
            s.Light(Shapes.Fill("kite", Shapes.Kite), H(Gold, 1.3f, 0.16f), Vector3.zero, Quaternion.identity, Vector3.one * 1.05f, true, face);
            s.Light(Shapes.Outline("kite", Shapes.Kite, 0.07f), H(Gold, 2.6f, 0.95f), Vector3.zero, Quaternion.identity, Vector3.one * 1.05f, true, face);
            s.Light(Shapes.Bar(0.62f, 0.06f), H(Gold, 2f, 0.6f), new Vector3(0f, -0.05f, 0f), Quaternion.identity, null, true, face);
            s.Light(Shapes.Bar(0.44f, 0.06f), H(Gold, 2f, 0.6f), new Vector3(0f, 0.12f, 0f), Quaternion.Euler(0f, 0f, 90f), null, true, face);
            float level = 0f;
            s.Tick = (sh, k) =>
            {
                bool up = raised() && !hidden();
                level = Mathf.MoveTowards(level, up ? 1f : 0f, Time.deltaTime * 8f);
                ward.Flash = Mathf.MoveTowards(ward.Flash, 0f, Time.deltaTime * 3.5f);
                sh.Bright = level * (0.55f + 0.1f * Mathf.Sin(Time.time * 3f) + 0.9f * ward.Flash);
                face.localScale = Vector3.one * (0.8f + 0.2f * level + 0.18f * ward.Flash);
            };
            ward.Shape = s;
            return ward;
        }

        /// <summary>Shield Bash: the kite of light hurled forward off the shield face; it shatters at the end of its reach.</summary>
        public static void KnightBash(Transform knight, float reach)
        {
            if (!Live) return;
            var fwd = Flat(knight.forward);
            var start = knight.position + Vector3.up * 1.15f + fwd * 0.6f;
            var s = FxShape.Make("Bash", start, Quaternion.LookRotation(fwd), 0.42f);
            var face = s.Pivot(Vector3.zero, Quaternion.identity);
            s.Light(Shapes.Fill("kite", Shapes.Kite), H(Gold, 1.6f, 0.4f), Vector3.zero, Quaternion.identity, null, true, face);
            s.Light(Shapes.Outline("kite", Shapes.Kite, 0.07f), H(Gold, 3f, 1f), Vector3.zero, Quaternion.identity, null, true, face);
            // A second, larger kite trailing behind: the push of air it drives.
            var echo = s.Light(Shapes.Outline("kite", Shapes.Kite, 0.07f), H(Steel, 1.6f, 0.5f), Vector3.zero, Quaternion.identity, null, true, face);
            bool shattered = false;
            s.Tick = (sh, k) =>
            {
                float e = EaseOut(Mathf.Clamp01(k / 0.6f));
                face.localPosition = Vector3.forward * e * reach * 0.85f;
                face.localScale = Vector3.one * (1.15f + 0.55f * e);
                echo.localPosition = Vector3.back * 0.35f * e;
                echo.localScale = Vector3.one * (1f + 0.5f * e);
                sh.Bright = k < 0.6f ? 1f : Mathf.Clamp01(1f - (k - 0.6f) / 0.2f);
                if (!shattered && k >= 0.6f)
                {
                    shattered = true;
                    Shards(face.position, fwd, 12, Gold * 2.6f, 6f, 0.13f);
                    FxSprite.Billboard(FxSprite.AnyStar, face.position, Vector3.zero, 0.2f, 0.6f, 1.8f, new Color(1f, 0.9f, 0.6f, 1f), 200f);
                }
            };
            // The Knight's boots plough two furrows in the dust through the lunge.
            var dust = FxShape.Make("BashFurrow", knight.position, Quaternion.identity, 2.4f);
            var side = Vector3.Cross(Vector3.up, fwd);
            foreach (float sx in new[] { -0.22f, 0.22f })
                FloorBand(dust, knight.position + side * sx - fwd * 0.3f, fwd, 2.2f, 0.22f, new Color(0.12f, 0.09f, 0.07f, 0.6f), true, 0.08f, Random.Range(0, 9999));
            dust.Tick = (sh, k) => sh.Bright = 1f - k;
        }

        /// <summary>War Cry: three walls of sound racing out to the taunt radius, the roar spiking from the mouth.</summary>
        public static void KnightWarCry(Transform knight, float radius)
        {
            if (!Live) return;
            var feet = Vfx.Floor(knight.position);
            for (int i = 0; i < 3; i++)
            {
                float delay = i * 0.1f;
                var s = FxShape.Make("WarCry", feet, Quaternion.identity, 0.6f + delay);
                var wall = s.Light(Shapes.Wall(), H(i == 0 ? Roar : Gold, 1.7f, i == 0 ? 0.4f : 0.25f), Vector3.zero, Quaternion.identity, Vector3.zero);
                var rim = s.Light(Shapes.Hoop(0.08f), H(Roar, 2.5f, 0.9f), Vector3.up * 0.06f, Quaternion.identity, Vector3.zero);
                s.Tick = (sh, k) =>
                {
                    float kk = Mathf.Clamp01((sh.Age - delay) / 0.6f);
                    if (sh.Age < delay) { wall.localScale = rim.localScale = Vector3.zero; return; }
                    float r = Mathf.Lerp(1f, radius, EaseOut(kk));
                    wall.localScale = new Vector3(r, Mathf.Lerp(1.8f, 0.35f, kk), r);
                    rim.localScale = new Vector3(r, 1f, r);
                    sh.Bright = Mathf.Pow(1f - kk, 1.4f);
                };
            }
            // The roar itself: jagged spikes of sound thrown out of the Knight's mouth.
            var mouth = knight.position + Vector3.up * 1.6f + Flat(knight.forward) * 0.25f;
            var roar = FxShape.Make("Roar", mouth, Quaternion.identity, 0.35f);
            for (int i = 0; i < 14; i++)
            {
                var dir = Quaternion.Euler(Random.Range(-35f, 25f), i * 360f / 14f + Random.Range(-8f, 8f), 0f) * Vector3.forward;
                float len = Random.Range(1f, 1.8f);
                roar.Light(Shapes.Spindle(len, 0.1f), H(i % 2 == 0 ? Roar : Gold, 2.6f, 0.95f), dir * (0.5f + len * 0.5f), Quaternion.FromToRotation(Vector3.up, dir));
            }
            roar.Tick = (sh, k) => { sh.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.4f, EaseOut(k)); sh.Bright = 1f - k * k; };
        }

        /// <summary>The taunt you can see: a burning chevron over a foe the War Cry pulled, and the snap of a tether back to the Knight.</summary>
        public static void TauntMark(Transform foe, float height, float seconds, Vector3 knightChest, Vector3 foeChest)
        {
            if (!Live || foe == null) return;
            var s = FxShape.Make("Taunt", foe.position, Quaternion.identity, seconds).Ride(foe, Vector3.up * (height + 0.45f));
            var face = s.Pivot(Vector3.zero, Quaternion.identity);
            foreach (float sx in new[] { -1f, 1f })
                s.Light(Shapes.Spindle(0.5f, 0.12f), H(Roar, 2.8f, 1f), new Vector3(sx * 0.14f, 0f, 0f), Quaternion.Euler(0f, 0f, sx * 50f), null, true, face);
            s.Light(Shapes.Spindle(0.34f, 0.08f), H(Gold, 2.4f, 0.8f), new Vector3(0f, 0.22f, 0f), Quaternion.Euler(0f, 0f, 90f), null, true, face);
            s.Tick = (sh, k) =>
            {
                face.rotation = FaceCam(face.position);
                face.localPosition = Vector3.up * (0.08f * Mathf.Sin(sh.Age * 5f));
                sh.Bright = Env(k, 0.04f, 0.12f) * (0.75f + 0.25f * Mathf.Sin(sh.Age * 9f));
            };
            Tether(foeChest, knightChest, H(Roar, 2f, 0.7f), 0.14f, 0.35f);
        }

        /// <summary>Rallying Banner: a wall of gold light round its ground for as long as it stands, pulsing, so the party sees where to stand.</summary>
        public static void KnightBanner(Vector3 at, float radius, float seconds)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at);
            var s = FxShape.Make("BannerZone", floor, Quaternion.identity, seconds);
            var wall = s.Light(Shapes.Wall(48), H(Gold, 1.5f, 0.22f), Vector3.zero, Quaternion.identity, new Vector3(radius, 0f, radius));
            s.Light(Shapes.Hoop(0.06f, 48), H(Gold, 2.2f, 0.7f), Vector3.up * 0.05f, Quaternion.identity, new Vector3(radius, 1f, radius));
            s.Tick = (sh, k) =>
            {
                float rise = EaseOut(Mathf.Clamp01(sh.Age / 0.4f));
                wall.localScale = new Vector3(radius, 1.4f * rise, radius);
                float beat = Mathf.Repeat(sh.Age, 1.6f) / 1.6f;
                sh.Bright = Env(k, 0.02f, 0.08f) * (0.6f + 0.4f * Mathf.Exp(-beat * 5f));
            };
            Shock(floor, radius, Gold, 1.4f, 0.45f);
            Cracks(floor, 1.6f, 6, Gold, 3f);
            Rubble(floor, 8, 3.5f);
        }

        /// <summary>A sword swing as others see it: a broad crescent of steel light sweeping across the Knight's front.</summary>
        public static void KnightCut(Transform knight, float side, float reach, float arc, Color c)
        {
            if (!Live) return;
            var s = FxShape.Make("KnightCut", knight.position + Vector3.up * 1.15f, Quaternion.LookRotation(Flat(knight.forward)) * Quaternion.Euler(0f, 0f, side * 34f), 0.22f);
            var sweep = s.Pivot(Vector3.zero, Quaternion.identity);
            s.Light(Shapes.Crescent(Mathf.Min(arc, 150f) * 0.8f, 0.45f), H(c, 2.4f, 0.95f), Vector3.zero, Quaternion.identity, Vector3.one * reach * 0.9f, true, sweep);
            s.Tick = (sh, k) =>
            {
                sweep.localRotation = Quaternion.Euler(0f, side * Mathf.Lerp(arc * 0.3f, -arc * 0.3f, EaseOut(k)), 0f);
                sh.Bright = Env(k, 0.1f, 0.6f);
            };
        }

        /// <summary>Where the Knight's steel bites: a white cross-glint and steel sparks.</summary>
        public static void SteelBite(Vector3 at, Vector3 from)
        {
            if (!Live) return;
            Vfx.HitImpact(at, from, 0.7f, Gold, Mat.Steel);
            Glint(at, Steel, 1.1f, 0.16f, 45f);
        }

        /// <summary>A four-pointed glint facing the camera (two crossed spindles), popping and gone.</summary>
        public static void Glint(Vector3 at, Color c, float size, float life, float roll)
        {
            var s = FxShape.Make("Glint", at, FaceCam(at), life);
            s.Light(Shapes.Spindle(size, size * 0.12f), H(c, 3f, 1f), Vector3.zero, Quaternion.Euler(0f, 0f, roll));
            s.Light(Shapes.Spindle(size * 0.7f, size * 0.1f), H(c, 3f, 1f), Vector3.zero, Quaternion.Euler(0f, 0f, roll + 90f));
            s.Tick = (sh, k) => { sh.transform.rotation = FaceCam(sh.transform.position); sh.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.2f, EaseOut(k)); sh.Bright = 1f - k; };
        }

        // ==================================================================
        // CLERIC: the cross, light from above
        // ==================================================================

        /// <summary>An upright cross of light facing the camera (a smite landing, a mending mote).</summary>
        public static FxShape HolyCross(Vector3 at, float size, Color c, float life, Vector3 rise)
        {
            if (!Live) return null;
            var s = FxShape.Make("HolyCross", at, FaceCam(at), life);
            s.Light(Shapes.Bar(size, size * 0.16f), c, Vector3.zero, Quaternion.identity);
            s.Light(Shapes.Bar(size * 0.6f, size * 0.16f), c, new Vector3(0f, size * 0.18f, 0f), Quaternion.Euler(0f, 0f, 90f));
            var start = at;
            s.Tick = (sh, k) =>
            {
                sh.transform.position = start + rise * EaseOut(k);
                sh.transform.rotation = FaceCam(sh.transform.position);
                sh.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.1f, EaseOut(Mathf.Clamp01(k * 3f)));
                sh.Bright = Env(k, 0.12f, 0.6f);
            };
            return s;
        }

        /// <summary>A smite lands: a cross of light stands up where it struck.</summary>
        public static void SmiteLanded(Vector3 at)
        {
            if (!Live) return;
            HolyCross(at, 1.1f, H(Holy, 2.8f, 1f), 0.4f, Vector3.up * 0.25f);
            FxSprite.Billboard("light_01", at, Vector3.zero, 0.3f, 0.6f, 1.6f, new Color(1f, 0.9f, 0.6f, 0.7f));
        }

        /// <summary>The beam's mending, worn by the one mended: small crosses lifting off them.</summary>
        public static void MendMote(Transform who)
        {
            if (!Live || who == null) return;
            if (OwnBody(who.position)) return;
            var p = who.position + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.6f, 1.6f), Random.Range(-0.35f, 0.35f));
            HolyCross(p, 0.22f, H(Holy, 2.4f, 0.95f), 0.8f, Vector3.up * 0.7f);
        }

        /// <summary>Consecrate: a shaft of light comes down onto the spot, and a cross is etched into the ground for as long as it lasts.</summary>
        public static void ClericConsecrate(Vector3 at, float radius, float seconds, Vector3 forward, Transform follow)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at);
            var shaft = FxShape.Make("Shaft", floor, Quaternion.identity, 0.7f);
            var col = shaft.Light(Shapes.Wall(), H(Holy, 2.4f, 0.9f), Vector3.zero, Quaternion.identity, Vector3.zero);
            var core = shaft.Light(Shapes.Wall(), H(Color.white, 2f, 0.7f), Vector3.zero, Quaternion.identity, Vector3.zero);
            bool landed = false;
            shaft.Tick = (sh, k) =>
            {
                float t = sh.Age;
                float drop = EaseOut(Mathf.Clamp01(t / 0.16f));
                float w = t < 0.16f ? 0.7f : Mathf.Lerp(0.7f, radius * 0.6f, EaseOut(Mathf.Clamp01((t - 0.16f) / 0.5f)));
                col.localPosition = Vector3.up * 10f * (1f - drop);
                col.localScale = new Vector3(w, 10f, w);
                core.localPosition = col.localPosition;
                core.localScale = new Vector3(w * 0.35f, 10f, w * 0.35f);
                sh.Bright = t < 0.16f ? 1f : Mathf.Clamp01(1f - (t - 0.16f) / 0.5f);
                if (!landed && t >= 0.16f)
                {
                    landed = true;
                    Shock(floor, radius, Holy, 0.5f, 0.4f);
                    Vfx.Flash(floor + Vector3.up, Holy, 5f, radius * 2.5f, 0.4f);
                }
            };
            // The cross in the ground, long arm along the Cleric's facing.
            var fwd = Flat(forward);
            var g = FxShape.Make("ConsecrateCross", floor + Vector3.up * 0.045f, Quaternion.LookRotation(fwd), seconds);
            if (follow != null) g.Ride(follow, Vector3.up * 0.045f, true);
            var flatRot = Quaternion.Euler(90f, 0f, 0f);
            var arms = new[]
            {
                g.Light(Shapes.Bar(radius * 1.75f, 0.42f), H(Holy, 2f, 0.6f), Vector3.zero, flatRot),
                g.Light(Shapes.Bar(radius * 1.1f, 0.42f), H(Holy, 2f, 0.6f), new Vector3(0f, 0f, radius * 0.38f), flatRot * Quaternion.Euler(0f, 0f, 90f)),
            };
            g.Light(Shapes.Hoop(0.05f, 48), H(Holy, 1.8f, 0.45f), Vector3.zero, Quaternion.identity, new Vector3(radius, 1f, radius));
            g.Tick = (sh, k) =>
            {
                // The cross burns in from the middle out.
                float grow = EaseOut(Mathf.Clamp01(sh.Age / 0.35f));
                foreach (var a in arms) a.localScale = new Vector3(1f, grow, 1f);
                sh.Bright = Env(k, 0.02f, 0.12f) * (0.8f + 0.2f * Mathf.Sin(sh.Age * 3.5f));
            };
        }

        /// <summary>Divine Aegis on one ally: three rings of gold close in round them and spin like a gyroscope, then settle into the shield.</summary>
        public static void AegisGyro(Transform ally, Vector3 clericChest, bool isCleric)
        {
            if (!Live || ally == null) return;
            bool own = OwnBody(ally.position);
            if (own)
            {
                // From your own eyes: one gold ring closing in round your feet (rings at head height white out the view).
                var f = FxShape.Make("AegisOwn", ally.position, Quaternion.identity, 1.2f).Ride(ally, Vector3.up * 0.08f);
                var hoop = f.Light(Shapes.Hoop(0.18f), H(Holy, 2.6f, 0.9f), Vector3.zero, Quaternion.identity);
                f.Tick = (sh, k) => { hoop.localScale = Vector3.one * Mathf.Lerp(3f, 1.1f, EaseOut(Mathf.Clamp01(sh.Age / 0.35f))); sh.Bright = Env(k, 0.05f, 0.4f); };
                return;
            }
            var s = FxShape.Make("Aegis", ally.position, Quaternion.identity, 1.4f).Ride(ally, Vector3.up * 1.1f);
            var rings = new Transform[3];
            var tilts = new[] { Quaternion.identity, Quaternion.Euler(70f, 0f, 0f), Quaternion.Euler(0f, 0f, 70f) };
            for (int i = 0; i < 3; i++)
            {
                rings[i] = s.Pivot(Vector3.zero, tilts[i]);
                s.Light(Shapes.Hoop(0.2f), H(i == 0 ? Holy : Gold, 3f, 1f), Vector3.zero, Quaternion.identity, null, true, rings[i]);
            }
            float baseR = own ? 1.3f : 0.85f;
            s.Tick = (sh, k) =>
            {
                float close = EaseOut(Mathf.Clamp01(sh.Age / 0.35f));
                float r = Mathf.Lerp(baseR * 2.4f, baseR, close);
                for (int i = 0; i < 3; i++)
                {
                    rings[i].localScale = Vector3.one * r;
                    rings[i].localRotation = tilts[i] * Quaternion.Euler(0f, sh.Age * (240f + i * 90f) * (i == 1 ? -1f : 1f), 0f);
                }
                sh.Bright = Env(k, 0.05f, 0.35f);
            };
            if (!isCleric) Tether(clericChest, ally.position + Vector3.up * 1.2f, H(Holy, 2f, 0.7f), 0.12f, 0.35f);
        }

        /// <summary>Hymn of Return on one ally: a halo comes down out of the dark onto their head; the fallen get a pillar of light to rise in.</summary>
        public static void HymnHalo(Transform ally, bool downed)
        {
            if (!Live || ally == null) return;
            bool own = OwnBody(ally.position);
            float head = own ? 1.05f : 2.05f;
            var s = FxShape.Make("Halo", ally.position, Quaternion.identity, 2.2f).Ride(ally, Vector3.zero);
            var halo = s.Light(Shapes.Hoop(0.16f), H(Holy, 2.8f, 1f), Vector3.up * 8f, Quaternion.identity, Vector3.one * (own ? 0.9f : 0.38f));
            Transform pillar = null;
            if (downed) pillar = s.Light(Shapes.Wall(), H(Holy, 2.2f, 0.7f), Vector3.zero, Quaternion.identity, new Vector3(0.8f, 10f, 0.8f));
            s.Tick = (sh, k) =>
            {
                float down = EaseOut(Mathf.Clamp01(sh.Age / 0.7f));
                halo.localPosition = Vector3.up * Mathf.Lerp(8f, head, down);
                halo.localRotation = Quaternion.Euler(0f, sh.Age * 60f, 0f);
                if (pillar != null) pillar.localScale = new Vector3(0.8f * (1f + 0.2f * Mathf.Sin(sh.Age * 6f)), 10f, 0.8f * (1f + 0.2f * Mathf.Sin(sh.Age * 6f)));
                sh.Bright = Env(k, 0.05f, 0.3f);
            };
        }

        // ==================================================================
        // FLAGELLANT: blood and broken ground
        // ==================================================================

        /// <summary>Scourge: a lash of red light drawn round the body over the shoulder, a crack at its tip, blood flung off.</summary>
        public static void FlagScourge(Transform who, bool mine)
        {
            if (!Live) return;
            var s = FxShape.Make("Scourge", who.position, who.rotation, 0.55f).Ride(who, Vector3.zero, true);
            const int segs = 7, per = 4;
            var parts = new Transform[segs];
            // A helix from behind the right shoulder, over and round the back, down to the left hip.
            Vector3 P(float t)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(150f, -170f, t);
                float r = Mathf.Lerp(0.5f, 0.75f, Mathf.Sin(t * Mathf.PI));
                return new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(2.05f, 0.75f, t), Mathf.Cos(a) * r - (mine ? 0f : 0f));
            }
            for (int i = 0; i < segs; i++)
            {
                var path = new Vector3[per]; var side = new Vector3[per]; var w = new float[per];
                for (int j = 0; j < per; j++)
                {
                    float t = (i + j / (float)(per - 1)) / segs;
                    path[j] = P(t);
                    var tangent = P(Mathf.Min(1f, t + 0.01f)) - P(Mathf.Max(0f, t - 0.01f));
                    side[j] = Vector3.Cross(tangent, new Vector3(path[j].x, 0f, path[j].z)).normalized;
                    w[j] = Mathf.Lerp(0.16f, 0.04f, t);
                }
                parts[i] = s.Light(Shapes.Ribbon(path, side, w), H(Crimson, 2.6f, mine ? 0.5f : 1f), Vector3.zero, Quaternion.identity);
            }
            bool cracked = false;
            s.Tick = (sh, k) =>
            {
                // Drawn from the shoulder to the tip in a tenth of a second, then it burns out from the root.
                float drawn = Mathf.Clamp01(sh.Age / 0.11f) * segs;
                for (int i = 0; i < segs; i++)
                {
                    float on = Mathf.Clamp01(drawn - i);
                    float off = Mathf.Clamp01((sh.Age - 0.12f - i * 0.03f) / 0.25f);
                    sh.Dim(parts[i], on * (1f - off));
                }
                if (!cracked && sh.Age >= 0.11f)
                {
                    cracked = true;
                    var tip = sh.transform.TransformPoint(P(1f));
                    Glint(tip, new Color(1f, 0.6f, 0.5f), 0.9f, 0.14f, 20f);
                    var back = who.position + Vector3.up * 1.3f - Flat(who.forward) * 0.3f;
                    for (int d = 0; d < 14; d++)
                        Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, back + Random.insideUnitSphere * 0.2f,
                            (-Flat(who.forward) + Random.insideUnitSphere * 0.8f + Vector3.up * 0.4f) * Random.Range(1.5f, 3.5f),
                            Random.Range(0.5f, 0.8f), Random.Range(0.04f, 0.08f), 0.02f, new Color(0.5f, 0.02f, 0.02f), 9.8f, 0.3f);
                    BloodSpatter.Splash(who.position - Flat(who.forward) * 0.5f, 0.2f);
                }
            };
        }

        /// <summary>Chains of Penance: a barbed hook bites where the chain lands, and the chain flashes red as it snaps taut.</summary>
        public static void FlagHook(Vector3 from, Vector3 to)
        {
            if (!Live) return;
            var d = to - from;
            if (d.sqrMagnitude < 0.01f) return;
            var s = FxShape.Make("Hook", to, Quaternion.LookRotation(d), 0.6f);
            var hook = s.Pivot(Vector3.zero, Quaternion.identity);
            s.Solid(MeshKit.Lathe("hookTip", new[] { new Vector2(0.07f, 0f), new Vector2(0f, 0.32f) }, 5), Mat.Steel, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), null, hook);
            foreach (float sx in new[] { -1f, 1f })
                s.Solid(MeshKit.Lathe("hookBarb", new[] { new Vector2(0.04f, 0f), new Vector2(0f, 0.2f) }, 4), Mat.Steel, new Vector3(sx * 0.05f, 0f, 0.08f), Quaternion.Euler(-150f, 0f, sx * 40f), null, hook);
            s.Light(Shapes.Hoop(0.2f), H(Crimson, 2.8f, 1f), Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one * 0.3f);
            s.Tick = (sh, k) =>
            {
                hook.localScale = Vector3.one * (k < 0.75f ? 1f : Mathf.Clamp01((1f - k) / 0.25f));
                sh.Bright = 1f - k;
            };
            Tether(from, to, H(Crimson, 2.4f, 0.9f), 0.1f, 0.3f);
        }

        /// <summary>The Penitent Leap's flight: a trail of blood-red light behind the body all the way up and down.</summary>
        public static void FlagLeapTrail(Transform who, float seconds)
        {
            if (!Live || who == null) return;
            var s = FxShape.Make("LeapTrail", who.position, Quaternion.identity, seconds);
            float next = 0f;
            s.Tick = (sh, k) =>
            {
                if (who == null || sh.Age < next) return;
                next = sh.Age + 0.025f;
                var p = who.position + Vector3.up * 1f;
                Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, p + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 0.4f, 0.45f, 0.32f, 0f, Crimson * 2.2f, 0f, 1f);
                if (Random.value < 0.5f) Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, p, Random.insideUnitSphere * 1.5f, 0.6f, 0.06f, 0.02f, new Color(0.5f, 0.02f, 0.02f), 9.8f, 0.3f);
            };
            Cracks(who.position, 1.2f, 4, Crimson, 2f);
        }

        /// <summary>A landing that breaks the floor: a crater of cracks, rubble thrown up, a low ring of force. <paramref name="power"/> 1 = normal.</summary>
        public static void FlagCrater(Vector3 at, float radius, float power)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at);
            Cracks(floor, radius, Mathf.RoundToInt(Mathf.Lerp(5f, 11f, Mathf.Clamp01(power - 0.5f))), Crimson, 3.5f + power);
            Rubble(floor, Mathf.RoundToInt(6 + 6 * power), 3f + 1.5f * power);
            Shock(floor, radius, Crimson, 0.8f, 0.35f);
            FxSprite.Decal("dirt_02", floor, radius * 1.1f, radius * 1.2f, 6f, new Color(0.25f, 0.03f, 0.02f, 0.6f));
        }

        /// <summary>The Red Litany: three spiked flails whirl round him on their chains, a smear of red where they pass.</summary>
        public static void FlagLitany(Transform who, float radius, float seconds)
        {
            if (!Live || who == null) return;
            var s = FxShape.Make("Litany", who.position, Quaternion.identity, seconds).Ride(who, Vector3.zero);
            var whirl = s.Pivot(Vector3.up * 1.05f, Quaternion.identity);
            float reach = radius * 0.78f;
            var spike = MeshKit.Lathe("litanySpike", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.16f) }, 4);
            var ball = MeshKit.Ball(0.17f, 8, 6);
            var link = MeshKit.Box(new Vector3(0.05f, 0.05f, 0.12f));
            for (int a = 0; a < 3; a++)
            {
                var arm = s.Pivot(Vector3.zero, Quaternion.Euler(0f, a * 120f, 0f), whirl);
                int links = Mathf.RoundToInt(reach / 0.16f);
                for (int i = 1; i < links; i++)
                    s.Solid(link, Mat.Steel, Vector3.forward * (i * 0.16f), Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f), null, arm);
                var head = s.Pivot(Vector3.forward * reach, Quaternion.identity, arm);
                s.Solid(ball, Mat.Steel, Vector3.zero, Quaternion.identity, null, head);
                for (int i = 0; i < 6; i++)
                {
                    var dir = Quaternion.Euler(i < 4 ? 0f : (i == 4 ? 90f : -90f), i < 4 ? i * 90f : 0f, 0f) * Vector3.forward;
                    s.Solid(spike, Mat.Steel, dir * 0.15f, Quaternion.FromToRotation(Vector3.up, dir), null, head);
                }
                // Red-hot heads.
                s.Light(MeshKit.Ball(0.22f, 8, 6), H(Crimson, 2f, 0.35f), Vector3.zero, Quaternion.identity, null, false, head);
            }
            var smear = s.Light(Shapes.Hoop(0.32f, 48), H(Crimson, 2.2f, 0.5f), Vector3.up * 1.05f, Quaternion.identity, new Vector3(reach, 1f, reach));
            s.Tick = (sh, k) =>
            {
                float spin = 520f * Mathf.Min(1f, sh.Age / 0.4f);
                whirl.localRotation = Quaternion.Euler(Mathf.Sin(sh.Age * 2.3f) * 8f, sh.Age * spin, 0f);
                whirl.localPosition = Vector3.up * (1.05f + 0.12f * Mathf.Sin(sh.Age * 4f));
                float grow = EaseOut(Mathf.Clamp01(sh.Age / 0.3f)) * Mathf.Clamp01((seconds - sh.Age) / 0.25f);
                whirl.localScale = Vector3.one * Mathf.Max(0.001f, grow);
                smear.localPosition = whirl.localPosition;
                sh.Bright = grow * (0.7f + 0.3f * Mathf.Sin(sh.Age * 20f));
            };
        }

        /// <summary>Flail swing as others see it: a heavy, low red crescent.</summary>
        public static void FlagCut(Transform who, float side, float reach, float arc, Color c)
        {
            if (!Live) return;
            var s = FxShape.Make("FlagCut", who.position + Vector3.up * 0.95f, Quaternion.LookRotation(Flat(who.forward)) * Quaternion.Euler(0f, 0f, side * 30f), 0.26f);
            var sweep = s.Pivot(Vector3.zero, Quaternion.identity);
            s.Light(Shapes.Crescent(arc * 0.8f, 0.55f), H(c, 2.2f, 0.95f), Vector3.zero, Quaternion.identity, Vector3.one * reach * 0.9f, true, sweep);
            s.Tick = (sh, k) =>
            {
                sweep.localRotation = Quaternion.Euler(0f, side * Mathf.Lerp(arc * 0.35f, -arc * 0.35f, k * k), 0f);
                sh.Bright = Env(k, 0.15f, 0.5f);
            };
        }

        /// <summary>Where the flail lands on a body: blood flung and a dull iron flash.</summary>
        public static void FlailBite(Vector3 at, Vector3 from)
        {
            if (!Live) return;
            Vfx.HitImpact(at, from, 1f, Crimson, Mat.Steel);
            for (int d = 0; d < 8; d++)
                Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, at, (Flat(at - from) + Random.insideUnitSphere * 0.9f + Vector3.up * 0.5f) * Random.Range(2f, 4f),
                    Random.Range(0.4f, 0.7f), Random.Range(0.04f, 0.08f), 0.02f, new Color(0.5f, 0.02f, 0.02f), 9.8f, 0.3f);
        }

        // ==================================================================
        // ROGUE: cuts and afterimages
        // ==================================================================

        /// <summary>A violet streak behind a thrown knife (a part of the knife's own visual).</summary>
        public static void KnifeTrail(Transform knife)
        {
            if (!Live) return;
            foreach (float roll in new[] { 0f, 90f })
            {
                var go = new GameObject("KnifeTrail");
                go.transform.SetParent(knife, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.4f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, roll);
                go.AddComponent<MeshFilter>().sharedMesh = Shapes.Spindle(1.1f, 0.1f);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = FxShape.SoftMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var b = new MaterialPropertyBlock();
                b.SetColor("_BaseColor", H(Violet, 2.8f, 0.95f));
                r.SetPropertyBlock(b);
            }
        }

        /// <summary>A dagger thrust as others see it: a thin spike of violet light driven straight out.</summary>
        public static void RogueThrust(Transform who, float hand)
        {
            if (!Live) return;
            var fwd = Flat(who.forward);
            var side = Vector3.Cross(Vector3.up, fwd);
            var s = FxShape.Make("Thrust", who.position + Vector3.up * 1.2f + side * hand * 0.22f, Quaternion.LookRotation(fwd), 0.14f);
            var spike = s.Light(Shapes.Spindle(1.5f, 0.13f), H(Violet, 2.8f, 1f), Vector3.forward * 1.1f, Quaternion.Euler(90f, 0f, 0f));
            s.Light(Shapes.Spindle(1.5f, 0.13f), H(Violet, 2.8f, 1f), Vector3.forward * 1.1f, Quaternion.Euler(90f, 0f, 90f));
            s.Tick = (sh, k) => { sh.transform.localScale = new Vector3(1f, 1f, Mathf.Lerp(0.5f, 1.15f, EaseOut(k))); sh.Bright = 1f - k; };
        }

        /// <summary>Where a dagger bites: a small violet X nicked in the air.</summary>
        public static void RogueNick(Vector3 at, float size = 0.55f)
        {
            if (!Live) return;
            var s = FxShape.Make("Nick", at, FaceCam(at), 0.2f);
            float roll = Random.Range(-20f, 20f);
            s.Light(Shapes.Spindle(size, size * 0.14f), H(Violet, 2.8f, 1f), Vector3.zero, Quaternion.Euler(0f, 0f, roll + 42f));
            s.Light(Shapes.Spindle(size, size * 0.14f), H(Color.white, 2f, 0.9f), Vector3.zero, Quaternion.Euler(0f, 0f, roll - 42f));
            s.Tick = (sh, k) => { sh.transform.rotation = FaceCam(sh.transform.position); sh.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.25f, EaseOut(k)); sh.Bright = 1f - k; };
        }

        /// <summary>Shadowstep's departure: a ghost of the Rogue's body, caught in its pose, left standing where it was and dissolving.</summary>
        public static void Afterimage(Transform body, Transform rigRoot, Vector3 at, float life = 1f)
        {
            if (!Live || body == null) return;
            var rot = Quaternion.Euler(0f, body.eulerAngles.y, 0f);
            var s = FxShape.Make("Afterimage", at, rot, life);
            var toLocal = Matrix4x4.TRS(body.position, rot, Vector3.one).inverse;
            int n = 0;
            if (rigRoot != null)
                foreach (var mf in rigRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || n++ > 60) continue;
                    var r = mf.GetComponent<Renderer>();
                    if (r == null || r is SkinnedMeshRenderer) continue;
                    var m = toLocal * mf.transform.localToWorldMatrix;
                    var pos = (Vector3)m.GetColumn(3);
                    var q = m.rotation;
                    var sc = m.lossyScale;
                    s.Solid(mf.sharedMesh, Mat.Glow, pos, q, sc, null, Violet * 0.3f);
                }
            if (n == 0)
            {
                // No rig to copy (a hidden body): a plain shadow figure.
                s.Solid(MeshKit.Cylinder(0.25f, 0.2f, 1.3f, 8), Mat.Dark, Vector3.up * 0.2f, Quaternion.identity);
                s.Solid(MeshKit.Ball(0.17f, 8, 6), Mat.Dark, Vector3.up * 1.65f, Quaternion.identity);
            }
            // It holds the pose a breath, then sinks into the floor and shrinks away; the violet rim burns out first.
            var dust = Vfx.Floor(at);
            s.Tick = (sh, k) =>
            {
                sh.Bright = Mathf.Clamp01(1f - k * 1.4f);
                float sink = Mathf.Clamp01((k - 0.25f) / 0.75f);
                sh.transform.position = at + Vector3.down * 1.9f * sink * sink;
                sh.transform.localScale = new Vector3(1f - 0.3f * sink, 1f, 1f - 0.3f * sink);
                if (Random.value < 0.35f && sink > 0f) Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, dust + new Vector3(Random.Range(-0.3f, 0.3f), 0.05f, Random.Range(-0.3f, 0.3f)), Vector3.up * 0.6f, 0.5f, 0.08f, 0f, Violet * 1.4f, -0.2f, 1f);
            };
        }

        /// <summary>Shadowstep's arrival: a tall violet tear opens in the air where the Rogue steps out, and a smear of shadow along the floor between.</summary>
        public static void ShadowTear(Vector3 from, Vector3 to)
        {
            if (!Live) return;
            var at = to + Vector3.up * 1f;
            var s = FxShape.Make("Tear", at, FaceCam(at), 0.32f);
            var slit = s.Light(Shapes.Spindle(2.4f, 0.5f), H(Violet, 2.6f, 1f), Vector3.zero, Quaternion.identity);
            s.Shade(Shapes.Spindle(2.0f, 0.3f), new Color(0.03f, 0.01f, 0.06f, 0.9f), Vector3.back * 0.02f, Quaternion.identity);
            s.Tick = (sh, k) =>
            {
                sh.transform.rotation = FaceCam(sh.transform.position);
                float open = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                sh.transform.localScale = new Vector3(Mathf.Max(0.02f, open), 0.8f + 0.2f * open, 1f);
                sh.Bright = open;
            };
            var smear = FxShape.Make("Smear", from, Quaternion.identity, 1.4f);
            var d = to - from;
            if (Flat(d).sqrMagnitude > 0f && new Vector2(d.x, d.z).magnitude > 0.5f)
                FloorBand(smear, from, d, new Vector2(d.x, d.z).magnitude, 0.6f, new Color(0.08f, 0.03f, 0.13f, 0.6f), true);
            smear.Tick = (sh, k) => sh.Bright = 1f - k;
        }

        /// <summary>Smoke Bomb: a dome of black smoke pops out at once (the cloud then hangs), violet sparks spitting from it.</summary>
        public static void SmokeDome(Vector3 at, float radius)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at);
            var s = FxShape.Make("SmokeDome", floor, Quaternion.identity, 0.9f);
            var dome = s.Shade(MeshKit.Ball(1f, 14, 8), new Color(0.07f, 0.05f, 0.1f, 0.75f), Vector3.zero, Quaternion.identity, Vector3.zero);
            var rim = s.Light(Shapes.Hoop(0.12f, 48), H(Violet, 1.5f, 0.6f), Vector3.up * 0.06f, Quaternion.identity, Vector3.zero);
            s.Tick = (sh, k) =>
            {
                float e = EaseOut(Mathf.Clamp01(k / 0.35f));
                dome.localScale = new Vector3(radius, radius * 0.8f, radius) * Mathf.Lerp(0.15f, 1f, e);
                rim.localScale = new Vector3(radius * e * 1.1f, 1f, radius * e * 1.1f);
                sh.Bright = 1f - k * k;
            };
            Vfx.Embers(floor + Vector3.up * 0.6f, 18, Violet, 5f, 0.05f, 0.6f);
        }

        /// <summary>The Opener (first strike out of the smoke): a great violet X cut across the target, black behind it.</summary>
        public static void CrossCut(Vector3 at, float size, int cuts = 2)
        {
            if (!Live) return;
            var s = FxShape.Make("CrossCut", at, FaceCam(at), 0.42f);
            float roll = Random.Range(-15f, 15f);
            for (int i = 0; i < cuts; i++)
            {
                float a = roll + (cuts == 2 ? (i == 0 ? 40f : -40f) : i * 180f / cuts);
                s.Shade(Shapes.Spindle(size * 1.08f, size * 0.2f), new Color(0.03f, 0.01f, 0.05f, 0.85f), Vector3.back * 0.03f, Quaternion.Euler(0f, 0f, a));
                s.Light(Shapes.Spindle(size, size * 0.12f), H(Violet, 3f, 1f), Vector3.zero, Quaternion.Euler(0f, 0f, a));
                s.Light(Shapes.Spindle(size * 0.8f, size * 0.035f), H(Color.white, 2.5f, 1f), Vector3.forward * -0.01f, Quaternion.Euler(0f, 0f, a));
            }
            s.Tick = (sh, k) =>
            {
                sh.transform.rotation = FaceCam(sh.transform.position);
                sh.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.15f, EaseOut(Mathf.Clamp01(k * 4f)));
                sh.Bright = k < 0.25f ? 1f : 1f - (k - 0.25f) / 0.75f;
            };
        }

        /// <summary>Death From Shadow: a star of three cuts and a pit of black closing on the mark.</summary>
        public static void DeathMark(Vector3 at)
        {
            if (!Live) return;
            CrossCut(at, 2.8f, 3);
            var s = FxShape.Make("Pit", at, Quaternion.identity, 0.5f);
            var pit = s.Shade(MeshKit.Ball(1f, 12, 8), new Color(0.04f, 0.01f, 0.07f, 0.8f), Vector3.zero, Quaternion.identity, Vector3.one * 2.4f);
            s.Tick = (sh, k) => { pit.localScale = Vector3.one * Mathf.Lerp(2.4f, 0.1f, EaseOut(k)); sh.Bright = 1f - k * 0.5f; };
            Vfx.Embers(at, 20, Violet, 6f, 0.05f, 0.6f);
        }

        // ==================================================================
        // PLAGUE DOCTOR: flasks, flies and veins
        // ==================================================================

        /// <summary>A thrown flask (the vial's own visual): a glowing round belly, a glass neck, a cork, tumbling end over end.</summary>
        public static Transform Flask(Vector3 origin, Vector3 direction, Color liquid, bool tonic)
        {
            var root = new GameObject(tonic ? "Tonic" : "Vial").transform;
            root.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            var tumble = new GameObject("Tumble").transform;
            tumble.SetParent(root, false);
            var spin = tumble.gameObject.AddComponent<Spin>();
            spin.DegreesPerSecond = 720f;
            tumble.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var belly = MeshKit.Part(tumble, "Belly", MeshKit.Ball(0.1f, 8, 6), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(belly.GetComponent<Renderer>(), liquid * 1.8f);
            var neck = MeshKit.Part(tumble, "Neck", MeshKit.Cylinder(0.04f, 0.032f, 0.11f, 6), Mat.Glow, new Vector3(0f, 0f, 0.07f), new Vector3(90f, 0f, 0f));
            ArtLibrary.SetGlow(neck.GetComponent<Renderer>(), Color.Lerp(liquid, Color.white, 0.6f) * 0.7f);
            MeshKit.Part(tumble, "Cork", MeshKit.Cylinder(0.036f, 0.04f, 0.05f, 6), tonic ? Mat.Cloth : Mat.Wood, new Vector3(0f, 0f, 0.17f), new Vector3(90f, 0f, 0f));
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // The flask turns about a sideways axis: Spin turns about local up, so tip the pivot over.
            tumble.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return root;
        }

        /// <summary>A vial bursts: glass flies, a pool of sick green is left bubbling for a few seconds. A tonic bursts into a rose mist that lifts.</summary>
        public static void FlaskBurst(Vector3 at, float radius, bool tonic, float poolSeconds)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at);
            Shards(at + Vector3.up * 0.2f, Vector3.up, 9, new Color(0.9f, 1f, 0.95f) * 1.4f, 4.5f, 0.07f);
            if (tonic)
            {
                var s = FxShape.Make("RoseMist", floor, Quaternion.identity, 1.1f);
                var mist = s.Light(Shapes.Wall(), H(Rose, 1.6f, 0.5f), Vector3.zero, Quaternion.identity, Vector3.zero);
                s.Tick = (sh, k) =>
                {
                    float e = EaseOut(k);
                    mist.localScale = new Vector3(radius * (0.5f + 0.5f * e), 1.8f * e, radius * (0.5f + 0.5f * e));
                    mist.localPosition = Vector3.up * 0.3f * e;
                    sh.Bright = Env(k, 0.1f, 0.6f);
                };
                for (int i = 0; i < 14; i++)
                {
                    var p = floor + new Vector3(Random.Range(-radius, radius) * 0.6f, Random.Range(0.1f, 0.6f), Random.Range(-radius, radius) * 0.6f);
                    Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, p, Vector3.up * Random.Range(0.6f, 1.3f) + Random.insideUnitSphere * 0.3f, Random.Range(0.9f, 1.4f), Random.Range(0.05f, 0.09f), 0f, Rose * 1.8f, -0.3f, 0.6f);
                }
                return;
            }
            FxSprite.Decal("dirt_01", floor, radius * 1.3f, radius * 1.45f, poolSeconds + 1f, new Color(0.3f, 0.65f, 0.1f, 0.85f));
            FxSprite.Decal("circle_02", floor, radius * 1.1f, radius * 1.2f, poolSeconds, new Color(0.5f, 1f, 0.3f, 0.35f));
            var pool = FxShape.Make("Pool", floor, Quaternion.identity, poolSeconds);
            float next = 0f;
            pool.Tick = (sh, k) =>
            {
                if (sh.Age < next) return;
                next = sh.Age + 0.14f;
                var off = Random.insideUnitCircle * radius * 0.6f;
                var p = floor + new Vector3(off.x, 0.04f, off.y);
                // A bubble swells and pops.
                Vfx.Spawn(MeshKit.Ball(0.5f, 8, 6), Mat.Glow, p, Vector3.up * 0.15f, Random.Range(0.4f, 0.7f), 0.03f, Random.Range(0.16f, 0.3f), Plague * 1.6f, 0f, 1f);
            };
        }

        /// <summary>
        /// Plague flies: dark specks with a sick glint, buzzing in tight, jittering orbits round a spot or riding a body.
        /// The Miasma's mark, and every plagued foe's.
        /// </summary>
        public static FxShape Flies(Transform ride, Vector3 at, float radius, float height, float seconds, int count, bool dark = false)
        {
            if (!Live) return null;
            var s = FxShape.Make("Flies", at, Quaternion.identity, seconds);
            if (ride != null) s.Ride(ride, Vector3.zero);
            var fly = MeshKit.Box(new Vector3(0.08f, 0.05f, 0.09f));
            var flies = new Transform[count];
            var phase = new float[count]; var speed = new float[count]; var rr = new float[count]; var hh = new float[count];
            for (int i = 0; i < count; i++)
            {
                // Dark specks against a green cloud; a sick glint where there is only the dark behind them.
                flies[i] = s.Solid(fly, Mat.Glow, Vector3.zero, Quaternion.identity, null, null, dark ? new Color(0.02f, 0.03f, 0.01f) : new Color(0.35f, 0.5f, 0.08f));
                phase[i] = Random.value * 10f;
                speed[i] = Random.Range(2.5f, 5f) * (Random.value < 0.5f ? -1f : 1f);
                rr[i] = radius * Random.Range(0.3f, 1f);
                hh[i] = height * Random.Range(0.3f, 1f);
            }
            s.Tick = (sh, k) =>
            {
                float t = sh.Age;
                float life = Env(k, 0.08f, 0.1f);
                for (int i = 0; i < count; i++)
                {
                    float a = phase[i] + t * speed[i];
                    float r = rr[i] * (0.8f + 0.2f * Mathf.Sin(t * 7f + phase[i])) * life;
                    var p = new Vector3(Mathf.Cos(a) * r, hh[i] + 0.25f * Mathf.Sin(t * 5.3f + phase[i] * 3f), Mathf.Sin(a) * r);
                    p += new Vector3(Mathf.Sin(t * 31f + phase[i]), Mathf.Sin(t * 27f + phase[i] * 2f), Mathf.Sin(t * 29f + phase[i] * 5f)) * 0.04f;
                    flies[i].localPosition = p;
                    flies[i].localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    flies[i].localScale = Vector3.one * life;
                }
            };
            return s;
        }

        /// <summary>The Plague Mask: a ghostly beaked mask rises over the Doctor and looks out, its lenses burning; a ring of smoke lifts round every ally it wards.</summary>
        public static void PlagueMask(Transform doctor, List<Transform> allies)
        {
            if (!Live || doctor == null) return;
            var fwd = Flat(doctor.forward);
            var s = FxShape.Make("Mask", doctor.position + Vector3.up * 2.1f + fwd * 0.15f, Quaternion.LookRotation(fwd), 1.5f);
            var mask = s.Pivot(Vector3.zero, Quaternion.identity);
            var pale = H(Pale, 1.3f, 0.5f);
            s.Light(MeshKit.Ball(0.22f, 10, 8), pale, Vector3.zero, Quaternion.identity, new Vector3(1f, 1.15f, 0.9f), false, mask);
            s.Light(MeshKit.Cylinder(0.12f, 0.01f, 0.62f, 8), H(Pale, 1.5f, 0.6f), new Vector3(0f, -0.06f, 0.12f), Quaternion.Euler(105f, 0f, 0f), null, false, mask);
            foreach (float sx in new[] { -1f, 1f })
                s.Light(MeshKit.Ball(0.065f, 8, 6), H(Plague, 3.2f, 1f), new Vector3(sx * 0.09f, 0.05f, 0.17f), Quaternion.identity, null, false, mask);
            s.Light(Shapes.Hoop(0.5f), H(Pale, 1.2f, 0.35f), new Vector3(0f, 0.2f, 0f), Quaternion.identity, Vector3.one * 0.36f, true, mask);
            s.Light(MeshKit.Cylinder(0.19f, 0.17f, 0.26f, 10), H(Pale, 1f, 0.3f), new Vector3(0f, 0.2f, 0f), Quaternion.identity, null, false, mask);
            var start = s.transform.position;
            s.Tick = (sh, k) =>
            {
                sh.transform.position = start + Vector3.up * 0.7f * EaseOut(k);
                mask.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.35f, EaseOut(k));
                mask.localRotation = Quaternion.Euler(Mathf.Sin(sh.Age * 2f) * 6f, Mathf.Sin(sh.Age * 1.3f) * 12f, 0f);
                sh.Bright = Env(k, 0.12f, 0.45f);
            };
            if (allies == null) return;
            foreach (var a in allies)
            {
                if (a == null) continue;
                bool own = OwnBody(a.position);
                var ring = FxShape.Make("WardRing", a.position, Quaternion.identity, 1f).Ride(a, Vector3.zero);
                var hoop = ring.Light(Shapes.Hoop(0.3f), H(Pale, 1.8f, 0.7f), Vector3.up * 0.1f, Quaternion.identity, Vector3.one * (own ? 1.3f : 0.9f));
                ring.Tick = (sh, k) =>
                {
                    float e = EaseOut(k);
                    hoop.localPosition = Vector3.up * Mathf.Lerp(0.1f, own ? 1f : 2f, e);
                    hoop.localScale = Vector3.one * Mathf.Lerp(own ? 1.3f : 0.9f, own ? 0.9f : 0.25f, e);
                    sh.Bright = Env(k, 0.1f, 0.5f);
                };
            }
        }

        /// <summary>
        /// The Pandemic: black veins creep over the floor from the Doctor to every foe it takes, each with a sick green seam; the
        /// flies go to them.
        /// </summary>
        public static void Veins(Vector3 from, IList<Vector3> to, float life = 3.2f, float creep = 0.5f)
        {
            if (!Live || to == null) return;
            var floor = Vfx.Floor(from);
            var s = FxShape.Make("Veins", floor, Quaternion.identity, life);
            var segs = new List<(Transform t, float at)>();
            foreach (var target in to)
            {
                var d = target - from; d.y = 0f;
                float len = d.magnitude;
                if (len < 0.5f) continue;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.5f));
                var dir = d / len;
                var side = Vector3.Cross(Vector3.up, dir);
                var off = 0f;
                var a = from;
                for (int i = 0; i < n; i++)
                {
                    float t1 = (i + 1f) / n;
                    float off1 = i == n - 1 ? 0f : Random.Range(-0.6f, 0.6f);
                    var b = from + dir * len * t1 + side * off1;
                    var mid = Vector3.Lerp(a, b, 0.5f) + side * Random.Range(-0.3f, 0.3f);
                    var path = new[] { a, mid, b };
                    var local = new Vector3[3];
                    for (int j = 0; j < 3; j++) local[j] = Vfx.Floor(path[j]) + Vector3.up * 0.045f - floor;
                    var w0 = Mathf.Lerp(0.4f, 0.18f, (float)i / n);
                    segs.Add((s.Shade(Shapes.Ribbon(local, Shapes.FlatSides(local), new[] { w0, w0 * 0.9f, w0 * 0.8f }), new Color(0.04f, 0.06f, 0.02f, 0.8f), Vector3.zero, Quaternion.identity), (float)i / n));
                    segs.Add((s.Light(Shapes.Ribbon(local, Shapes.FlatSides(local), new[] { w0 * 0.3f, w0 * 0.25f, w0 * 0.2f }), H(Plague, 2.2f, 0.95f), Vector3.up * 0.005f, Quaternion.identity), (float)i / n));
                    a = b; off = off1;
                }
            }
            s.Tick = (sh, k) =>
            {
                float reach = sh.Age / creep;
                foreach (var (t, at) in segs) sh.Dim(t, Mathf.Clamp01((reach - at) * 4f));
                sh.Bright = Mathf.Clamp01((1f - k) / 0.35f);
            };
        }

        // ==================================================================
        // MAGE: fire and ice, a sky that answers (2026-11-22)
        // ==================================================================

        public static readonly Color Fire = new Color(1f, 0.48f, 0.14f);
        public static readonly Color Ice = new Color(0.55f, 0.88f, 1f);
        public static readonly Color Hunter = new Color(0.55f, 0.95f, 0.45f);

        /// <summary>A soft streak trailing a projectile (part of its visual): a flame tail, a frost wake, a knife's violet.</summary>
        public static void BoltTrail(Transform bolt, Color c, float length, float width)
        {
            if (!Live) return;
            foreach (float roll in new[] { 0f, 90f })
            {
                var go = new GameObject("BoltTrail");
                go.transform.SetParent(bolt, false);
                go.transform.localPosition = new Vector3(0f, 0f, -length * 0.5f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, roll);
                go.AddComponent<MeshFilter>().sharedMesh = Shapes.Spindle(length, width);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = FxShape.SoftMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var b = new MaterialPropertyBlock();
                b.SetColor("_BaseColor", c);
                r.SetPropertyBlock(b);
            }
        }

        /// <summary>A six-pointed snowflake glint where frost lands (three crossed spindles).</summary>
        public static void Snowflake(Vector3 at, float size, float life = 0.35f)
        {
            if (!Live) return;
            var s = FxShape.Make("Snowflake", at, FaceCam(at), life);
            for (int i = 0; i < 3; i++)
            {
                s.Light(Shapes.Spindle(size, size * 0.1f), H(Ice, 2.8f, 1f), Vector3.zero, Quaternion.Euler(0f, 0f, i * 60f));
                // Little barbs half way out on each arm.
                foreach (float end in new[] { -1f, 1f })
                {
                    var arm = Quaternion.Euler(0f, 0f, i * 60f) * Vector3.up * size * 0.28f * end;
                    s.Light(Shapes.Spindle(size * 0.3f, size * 0.06f), H(Color.white, 2.2f, 0.9f), arm, Quaternion.Euler(0f, 0f, i * 60f + 90f));
                }
            }
            s.Tick = (sh, k) =>
            {
                sh.transform.rotation = FaceCam(sh.transform.position) * Quaternion.Euler(0f, 0f, k * 40f);
                sh.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.1f, EaseOut(Mathf.Clamp01(k * 3f)));
                sh.Bright = 1f - k * k;
            };
        }

        /// <summary>SHATTER: the chill breaks like glass: a big snowflake, ice shards flung out, a cold ring.</summary>
        public static void ShatterBurst(Vector3 at)
        {
            if (!Live) return;
            Snowflake(at, 2.2f, 0.45f);
            Shards(at, Vector3.up, 16, Ice * 2.6f, 7f, 0.14f);
            Shock(at, 2.2f, Ice, 0.6f, 0.35f);
        }

        /// <summary>
        /// The Meteor's warning: a ring of fire round the landing that CLOSES on the blast radius as the rock falls, so
        /// everyone reads when it lands, not only where. Runes turn inside it.
        /// </summary>
        public static void MeteorRing(Vector3 at, float radius, float delay)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at) + Vector3.up * 0.06f;
            var s = FxShape.Make("MeteorRing", floor, Quaternion.identity, delay + 0.05f);
            var closing = s.Light(Shapes.Hoop(0.1f, 48), H(Fire, 2.6f, 1f), Vector3.zero, Quaternion.identity, Vector3.one * radius * 2.2f);
            s.Light(Shapes.Hoop(0.04f, 48), H(Fire, 1.8f, 0.55f), Vector3.zero, Quaternion.identity, Vector3.one * radius);
            var spokes = s.Pivot(Vector3.zero, Quaternion.identity);
            for (int i = 0; i < 6; i++)
                s.Light(Shapes.Spindle(radius * 0.5f, 0.12f), H(Fire, 1.8f, 0.6f), Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * radius * 0.7f,
                    Quaternion.Euler(0f, i * 60f, 0f) * Quaternion.Euler(90f, 0f, 0f), null, true, spokes);
            s.Tick = (sh, k) =>
            {
                closing.localScale = Vector3.one * Mathf.Lerp(radius * 2.2f, radius, k * k);
                spokes.localRotation = Quaternion.Euler(0f, sh.Age * 90f, 0f);
                sh.Bright = Mathf.Clamp01(k * 6f) * (0.75f + 0.25f * Mathf.Sin(sh.Age * (10f + 20f * k)));
            };
        }

        // ==================================================================
        // RANGER: the hunt, green and feathered (2026-11-22)
        // ==================================================================

        /// <summary>A full draw: the shot leaves a green streak through everything in line, a glint at the bow.</summary>
        public static void PiercingShot(Vector3 from, Vector3 dir, float length)
        {
            if (!Live) return;
            dir = dir.normalized;
            Tether(from + dir * 0.8f, from + dir * length, H(Hunter, 2.4f, 0.9f), 0.12f, 0.35f);
            Glint(from + dir * 0.8f, Hunter, 0.9f, 0.16f, 0f);
        }

        /// <summary>Hunter's Mark: a reticle snaps onto the quarry (a closing ring and four ticks), then a red diamond holds.</summary>
        public static void HuntersMark(Transform quarry, Vector3 at)
        {
            if (!Live) return;
            var s = FxShape.Make("Reticle", at, FaceCam(at), 0.55f);
            if (quarry != null) s.Ride(quarry, at - quarry.position);
            var face = s.Pivot(Vector3.zero, Quaternion.identity);
            var red = new Color(1f, 0.3f, 0.25f);
            s.Light(Shapes.Hoop(0.12f), H(red, 2.6f, 1f), Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one * 0.6f, true, face);
            for (int i = 0; i < 4; i++)
            {
                var d = Quaternion.Euler(0f, 0f, i * 90f) * Vector3.up;
                s.Light(Shapes.Spindle(0.35f, 0.07f), H(red, 2.6f, 1f), d * 0.85f, Quaternion.Euler(0f, 0f, i * 90f), null, true, face);
            }
            s.Tick = (sh, k) =>
            {
                face.rotation = FaceCam(face.position) * Quaternion.Euler(0f, 0f, 45f * (1f - EaseOut(Mathf.Clamp01(k * 2.5f))));
                face.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, EaseOut(Mathf.Clamp01(k * 2.5f)));
                sh.Bright = Env(k, 0.05f, 0.3f);
            };
        }

        /// <summary>Disengage: a spray of green leaves where the Ranger kicked off, and a short arc of light along the leap.</summary>
        public static void Disengage(Transform who)
        {
            if (!Live || who == null) return;
            var feet = Vfx.Floor(who.position);
            Shock(feet, 1.6f, Hunter, 0.4f, 0.3f);
            var leaf = MeshKit.Box(new Vector3(1f, 0.08f, 0.55f));
            for (int i = 0; i < 14; i++)
            {
                var v = (Flat(who.forward) * 2f + Random.insideUnitSphere * 1.6f + Vector3.up * 1.5f).normalized * Random.Range(2f, 4.5f);
                var p = Vfx.Spawn(leaf, Mat.Glow, feet + Vector3.up * 0.3f + Random.insideUnitSphere * 0.3f, v, Random.Range(0.8f, 1.3f), Random.Range(0.07f, 0.12f), 0.03f,
                    Color.Lerp(Hunter, new Color(0.9f, 0.8f, 0.3f), Random.value) * 1.2f, 3f, 1.6f);
                p.transform.rotation = Random.rotation;
                p.Spin = Random.Range(-400f, 400f);
            }
            var s = FxShape.Make("DisengageTrail", who.position, Quaternion.identity, 0.5f);
            float next = 0f;
            s.Tick = (sh, k) =>
            {
                if (who == null || sh.Age < next) return;
                next = sh.Age + 0.03f;
                Vfx.Spawn(MeshKit.Ball(0.5f, 6, 4), Mat.Glow, who.position + Vector3.up * 0.9f, Vector3.zero, 0.35f, 0.18f, 0f, Hunter * 1.6f, 0f, 1f);
            };
        }

        /// <summary>A Volley arrow coming down out of the dark: a green streak from the sky to where it lands.</summary>
        public static void FallingStreak(Vector3 from, Vector3 to)
        {
            if (!Live) return;
            Tether(from, to, H(Hunter, 2f, 0.6f), 0.06f, 0.3f);
        }

        /// <summary>The Volley's ground: a green ring and arrowheads pointing in at its edge while the sky fills.</summary>
        public static void VolleyRing(Vector3 at, float radius, float seconds)
        {
            if (!Live) return;
            var floor = Vfx.Floor(at) + Vector3.up * 0.06f;
            var s = FxShape.Make("VolleyRing", floor, Quaternion.identity, seconds);
            s.Light(Shapes.Hoop(0.07f, 48), H(Hunter, 2.2f, 0.85f), Vector3.zero, Quaternion.identity, Vector3.one * radius);
            var heads = s.Pivot(Vector3.zero, Quaternion.identity);
            for (int i = 0; i < 8; i++)
            {
                var rot = Quaternion.Euler(0f, i * 45f, 0f);
                foreach (float sx in new[] { -1f, 1f })
                    s.Light(Shapes.Spindle(0.5f, 0.09f), H(Hunter, 2.4f, 0.9f), rot * new Vector3(sx * 0.13f, 0f, radius - 0.25f),
                        rot * Quaternion.Euler(90f, 0f, sx * 35f), null, true, heads);
            }
            s.Tick = (sh, k) =>
            {
                heads.localRotation = Quaternion.Euler(0f, sh.Age * 25f, 0f);
                sh.Bright = Env(k, 0.05f, 0.15f);
            };
        }
    }
}
