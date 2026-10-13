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
        public const string Dark = "Dark";
        public const string Glow = "Glow";
        public const string Flame = "Flame";
        public const string Telegraph = "Telegraph";
        public const string Water = "Water";
    }

    /// <summary>
    /// Runtime access to the procedurally baked materials, plus per-renderer tinting.
    ///
    /// Tints go through <see cref="MaterialPropertyBlock"/> rather than material copies,
    /// so four differently coloured knights still share one material and batch together.
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

        public static void Tint(Renderer renderer, Color color)
        {
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Sets the glow colour of a renderer using the Glow material. HDR: values above 1 bloom.</summary>
        public static void SetGlow(Renderer renderer, Color hdrColor)
        {
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(EmissionColorId, hdrColor);
            renderer.SetPropertyBlock(block);
        }
    }
}
