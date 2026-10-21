using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>Names of the baked materials in <c>Resources/Materials</c>. See ArtBaker.</summary>
    public static class Mat
    {
        public const string StoneWall = "StoneWall";
        public const string Flagstone = "Flagstone";
        public const string Wood = "Wood";
        public const string Steel = "Steel";
        public const string Gold = "Gold";
        public const string Bone = "Bone";
        public const string Cloth = "Cloth";
        public const string Leather = "Leather";
        public const string Rubble = "Rubble";
        public const string Marble = "Marble";
        public const string Dark = "Dark";
        public const string Glow = "Glow";
        public const string Flame = "Flame";
        public const string Telegraph = "Telegraph";
        public const string Water = "Water";
        /// <summary>Translucent, unlit haze: smoke clouds.</summary>
        public const string Smoke = "Smoke";
    }

    /// <summary>
    /// Runtime access to the procedurally baked materials, plus per-renderer tinting.
    ///
    /// <para>
    /// Tints (2026-10-21, "smooth as butter"): a renderer tinted once gets a cached copy of its
    /// material in that colour, so it stays on URP's SRP Batcher (a MaterialPropertyBlock takes a
    /// renderer off it, and nearly everything here is tinted: ~1000 draws a frame went the slow way).
    /// A renderer whose colour changes again on a later frame (flames, fades, telegraphs) is
    /// "animated": it goes back to the base material and uses a property block as before.
    /// </para>
    /// </summary>
    public static class ArtLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static MaterialPropertyBlock block;

        public static Material Get(string name)
        {
            if (Cache.TryGetValue(name, out var mat)) return mat;

            mat = Resources.Load<Material>("Materials/" + name);
            if (mat == null)
            {
                Debug.LogError("[Art] Missing material '" + name + "'. Run Deepbound > Bake Procedural Art.");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }

            Cache[name] = mat;
            return mat;
        }

        public static void Tint(Renderer renderer, Color color) => Apply(renderer, color, null);

        /// <summary>Sets the glow colour of a renderer using the Glow material. HDR: values above 1 bloom.</summary>
        public static void SetGlow(Renderer renderer, Color hdrColor) => Apply(renderer, null, hdrColor);

        /// <summary>Is this one of the baked materials (or a tinted copy of it)?</summary>
        public static bool Is(Material m, string name) => m != null && (m == Get(name) || (CloneBase.TryGetValue(m, out var b) && b == Get(name)));

        private class TintState { public Renderer R; public Material Base, Clone; public Color? Tint, Glow; public int Frame; public bool Animated; }
        private static readonly Dictionary<int, TintState> States = new Dictionary<int, TintState>();
        private static readonly Dictionary<(int, int, int, int, int, int, int, int, int), Material> Clones = new Dictionary<(int, int, int, int, int, int, int, int, int), Material>();
        private static readonly Dictionary<Material, Material> CloneBase = new Dictionary<Material, Material>();

        private static int Q(float v) => Mathf.RoundToInt(v * 255f);

        private static void Apply(Renderer r, Color? tint, Color? glow)
        {
            if (r == null) return;
            int id = r.GetInstanceID();
            var mat = r.sharedMaterial;
            if (!States.TryGetValue(id, out var s) || s.R == null)
            {
                if (States.Count > 40000) Prune();
                s = new TintState { R = r, Base = mat, Frame = Time.frameCount };
                States[id] = s;
            }
            else if (mat != s.Base && mat != s.Clone)
            {
                // Someone gave it another material (a pooled puff, a recoloured part): start over.
                s.Base = mat; s.Clone = null; s.Tint = null; s.Glow = null; s.Animated = false; s.Frame = Time.frameCount;
            }
            if (s.Base == null) return;
            bool changed = (tint.HasValue && s.Tint != tint) || (glow.HasValue && s.Glow != glow);
            if (tint.HasValue) s.Tint = tint;
            if (glow.HasValue) s.Glow = glow;
            if (!s.Animated && changed && s.Frame != Time.frameCount && s.Clone != null)
            {
                s.Animated = true;
                r.sharedMaterial = s.Base;
            }
            if (s.Animated)
            {
                block ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                if (s.Tint.HasValue) block.SetColor(BaseColorId, s.Tint.Value);
                if (s.Glow.HasValue) block.SetColor(EmissionColorId, s.Glow.Value);
                r.SetPropertyBlock(block);
                return;
            }
            if (!changed && s.Clone != null) return;
            var t = s.Tint ?? new Color(-1f, -1f, -1f, -1f);
            var g = s.Glow ?? new Color(-1f, -1f, -1f, -1f);
            var key = (s.Base.GetInstanceID(), Q(t.r), Q(t.g), Q(t.b), Q(t.a), Mathf.RoundToInt(g.r * 64f), Mathf.RoundToInt(g.g * 64f), Mathf.RoundToInt(g.b * 64f), Mathf.RoundToInt(g.a * 64f));
            if (!Clones.TryGetValue(key, out var clone) || clone == null)
            {
                clone = new Material(s.Base) { name = s.Base.name };
                if (s.Tint.HasValue && clone.HasProperty(BaseColorId)) clone.SetColor(BaseColorId, s.Tint.Value);
                if (s.Glow.HasValue && clone.HasProperty(EmissionColorId)) clone.SetColor(EmissionColorId, s.Glow.Value);
                Clones[key] = clone;
                CloneBase[clone] = s.Base;
            }
            s.Clone = clone;
            r.sharedMaterial = clone;
        }

        private static void Prune()
        {
            var dead = new List<int>();
            foreach (var kv in States) if (kv.Value.R == null) dead.Add(kv.Key);
            foreach (var k in dead) States.Remove(k);
        }
    }
}
