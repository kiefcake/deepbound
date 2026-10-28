using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Sprite effects on top of the procedural shapes (2026-10-28, the user: "look up downloadable effects"): Kenney's
    /// CC0 Particle Pack (Assets/Textures/Fx, baked to "Fx_..." materials by ArtBaker; credited in docs/CREDITS.md).
    /// Billboards (camera-facing puffs: flares, sparks, smoke, fire) and decals (flat on the floor: scorch marks, magic
    /// circles, shock rings), both pooled through <see cref="Vfx"/> and fading by <see cref="Puff"/>. Tinted per use,
    /// so the art stays this game's palette; kept faint, so the look does not change much.
    /// </summary>
    public static class FxSprite
    {
        private static Mesh quad;
        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "FxQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                // The particle shader multiplies by vertex colour: without it every sprite drew at zero.
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.RecalculateNormals();
                quad.RecalculateBounds();
                return quad;
            }
        }

        /// <summary>The effects are off when a screen is far from any viewer (no camera yet, or batch mode).</summary>
        private static bool Live => !Application.isBatchMode;

        /// <summary>A camera-facing puff of <paramref name="sprite"/> (e.g. "flare_01") growing from s0 to s1 over its life.</summary>
        public static Puff Billboard(string sprite, Vector3 at, Vector3 velocity, float life, float s0, float s1, Color color, float spin = 0f, float drag = 1f)
        {
            if (!Live) return null;
            var p = Vfx.Spawn(Quad, "Fx_" + sprite, at, velocity, life, s0, s1, null, 0f, drag);
            p.Billboard = true;
            p.Spin = spin;
            p.Fading = true;
            p.Fade = color;
            ArtLibrary.Tint(p.GetComponent<Renderer>(), new Color(color.r, color.g, color.b, 0f));
            return p;
        }

        /// <summary>A flat mark on the floor under <paramref name="at"/> (scorch, a magic circle, a shock ring).</summary>
        public static Puff Decal(string sprite, Vector3 at, float s0, float s1, float life, Color color, float spin = 0f)
        {
            if (!Live) return null;
            var floor = Vfx.Floor(at) + Vector3.up * (0.03f + Random.value * 0.01f);
            var p = Vfx.Spawn(Quad, "Fx_" + sprite, floor, Vector3.zero, life, s0, s1, null, 0f, 1f);
            p.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            p.Spin = spin;
            p.Fading = true;
            p.Fade = color;
            ArtLibrary.Tint(p.GetComponent<Renderer>(), new Color(color.r, color.g, color.b, 0f));
            return p;
        }

        private static readonly string[] Smokes = { "smoke_01", "smoke_04", "smoke_07", "smoke_10" };
        private static readonly string[] Scorches = { "scorch_01", "scorch_02", "scorch_03" };
        private static readonly string[] Stars = { "star_01", "star_06", "star_08" };
        private static readonly string[] Magics = { "magic_01", "magic_03", "magic_05" };
        public static string AnySmoke => Smokes[Random.Range(0, Smokes.Length)];
        public static string AnyScorch => Scorches[Random.Range(0, Scorches.Length)];
        public static string AnyStar => Stars[Random.Range(0, Stars.Length)];
        public static string AnyMagic => Magics[Random.Range(0, Magics.Length)];

        /// <summary>A blast's mark on the floor that cools and fades over several seconds.</summary>
        public static void Scorch(Vector3 at, float radius) => Decal(AnyScorch, at, radius * 1.6f, radius * 1.7f, 8f, new Color(0.08f, 0.06f, 0.05f, 0.8f));

        /// <summary>A turning circle of runes under a caster for <paramref name="seconds"/>.</summary>
        public static void MagicCircle(Vector3 at, float radius, float seconds, Color color) =>
            Decal(AnyMagic, at, radius * 1.6f, radius * 2.1f, seconds + 0.4f, new Color(color.r, color.g, color.b, 0.85f), Random.value < 0.5f ? 40f : -40f);
    }
}
