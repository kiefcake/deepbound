using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// Generates every texture and material the game uses, from code.
    ///
    /// <para>
    /// Deepbound's art is self-authored and procedural (design doc 2.1, revised
    /// 2026-10-13): no downloaded packs. This baker writes tileable albedo and normal maps
    /// to <c>Assets/Art/Generated</c> and URP materials to <c>Assets/Resources/Materials</c>,
    /// where <see cref="Deepbound.Art.ArtLibrary"/> loads them by name at runtime.
    /// </para>
    ///
    /// <para>
    /// Baked to assets rather than generated at runtime for two reasons. First, URP strips
    /// shader variants that no material asset uses, so a material built purely in code
    /// that turns on normal mapping or emission can silently render wrong in a player
    /// build. Materials saved here pin those variants. Second, it keeps startup instant and
    /// lets the textures be looked at and judged as ordinary assets.
    /// </para>
    ///
    /// Deterministic: a fixed seed per texture, so re-baking reproduces identical files and
    /// git sees no churn.
    /// </summary>
    public static class ArtBaker
    {
        private const string TextureDir = "Assets/Art/Generated";
        private const string MaterialDir = "Assets/Resources/Materials";
        private const int Size = 512;

        [MenuItem("Deepbound/Bake Procedural Art")]
        public static void BakeAll()
        {
            EnsureFolder(TextureDir);
            EnsureFolder(MaterialDir);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            // Stone is damp down here: a gloss map makes torchlight catch on wet streaks and puddles.
            BakeSurface("StoneWall", lit, StoneBricks, 0.75f, 0f, 2.2f, StoneWallGloss);
            BakeSurface("Flagstone", lit, Flagstones, 0.95f, 0f, 2.6f, FlagstoneGloss);
            BakeSurface("Wood", lit, WoodPlanks, 0.2f, 0f, 1.6f);
            // Steel: a little less metallic and a little smoother, so torchlight catches it instead of it reading as a dark plank.
            BakeSurface("Steel", lit, BrushedMetal, 0.72f, 0.65f, 0.6f);
            BakeSurface("Gold", lit, (x, y) => Tint(BrushedMetal(x, y), new Color(1f, 0.78f, 0.35f)), 0.7f, 1f, 0.5f);
            BakeSurface("Bone", lit, BoneSurface, 0.25f, 0f, 1.4f);
            BakeSurface("Cloth", lit, ClothWeave, 0.08f, 0f, 0.45f);
            BakeSurface("Leather", lit, LeatherSurface, 0.32f, 0f, 0.4f);
            BakeSurface("Rubble", lit, Rubble, 0.08f, 0f, 3f);
            BakeSurface("Marble", lit, Marble, 0.18f, 0f, 0.5f);

            // Fine grain on top (2026-10-24): a CC0 detail normal from Poly Haven tiled small over the baked surfaces, so
            // stone and wood catch the light grain by grain. Colour and the brick layout stay ours (docs/CREDITS.md).
            ApplyDetail("StoneWall", "Assets/Textures/Detail/rock_grain_normal.jpg", 7f, 0.55f);
            ApplyDetail("Flagstone", "Assets/Textures/Detail/rock_grain_normal.jpg", 5f, 0.45f);
            ApplyDetail("Rubble", "Assets/Textures/Detail/rock_grain_normal.jpg", 4f, 0.7f);
            ApplyDetail("Marble", "Assets/Textures/Detail/rock_grain_normal.jpg", 6f, 0.2f);
            ApplyDetail("Wood", "Assets/Textures/Detail/wood_grain_normal.jpg", 2f, 0.5f);

            BakeFlat("Dark", lit, new Color(0.05f, 0.05f, 0.06f), 0.4f, 0.3f);

            // Emissive surfaces: torch flames, enemy eyes, the boss's crown gems. Colour is
            // set per renderer at runtime through a property block, so one material serves
            // every glow in the game.
            BakeEmissive("Glow", lit, Color.white, 3f);

            BakeUnlit("Flame", unlit, new Color(1f, 0.55f, 0.15f), transparent: false);
            BakeUnlit("Telegraph", unlit, new Color(1f, 0.15f, 0.1f, 0.45f), transparent: true);
            BakeUnlit("Smoke", unlit, new Color(0.17f, 0.15f, 0.2f, 0.32f), transparent: true);
            BakeWater("Water", lit);
            BakeTrail("Trail");

            BakeVolumeProfile();
            ConfigureRenderer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Art] Procedural art baked.");
        }

        // ------------------------------------------------------------------
        // Surfaces
        // ------------------------------------------------------------------

        /// <summary>Albedo at (u, v) in [0,1), with alpha carrying height for the normal map.</summary>
        private delegate Color Surface(float u, float v);

        /// <param name="gloss">Optional smoothness per texel (0-1), stored in albedo alpha; <paramref name="smoothness"/> then scales it.</param>
        private static void BakeSurface(string name, Shader shader, Surface surface,
            float smoothness, float metallic, float normalStrength, System.Func<float, float, float> gloss = null)
        {
            var albedo = new Color[Size * Size];
            var height = new float[Size * Size];

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (float)x / Size, v = (float)y / Size;
                var c = surface(u, v);
                height[y * Size + x] = c.a;
                c.a = gloss != null ? Mathf.Clamp01(gloss(u, v)) : 1f;
                albedo[y * Size + x] = c;
            }

            var albedoTex = SaveTexture(name + "_Albedo", albedo, isNormal: false);
            var normalTex = SaveTexture(name + "_Normal", HeightToNormal(height, normalStrength), isNormal: true);

            var mat = GetOrCreateMaterial(name, shader);
            mat.SetTexture("_BaseMap", albedoTex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", normalTex);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (gloss != null)
            {
                mat.SetFloat("_SmoothnessTextureChannel", 1f);
                mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            else
            {
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            EditorUtility.SetDirty(mat);
        }

        /// <summary>A detail normal map on a baked material (URP Lit's detail inputs; the detail albedo stays neutral grey).</summary>
        private static void ApplyDetail(string material, string normalPath, float tiling, float strength)
        {
            var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType != TextureImporterType.NormalMap || importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + material + ".mat");
            if (tex == null || mat == null) return;
            mat.SetTexture("_DetailNormalMap", tex);
            mat.SetFloat("_DetailNormalMapScale", strength);
            mat.SetFloat("_DetailAlbedoMapScale", 0f);
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(tiling, tiling));
            mat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(mat);
        }

        private static void BakeFlat(string name, Shader shader, Color color, float smoothness, float metallic)
        {
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(mat);
        }

        private static void BakeEmissive(string name, Shader shader, Color color, float intensity)
        {
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", Color.black);
            mat.SetColor("_EmissionColor", color * intensity);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);
        }

        /// <summary>Additive, vertex-coloured (URP particle unlit): weapon swing trails that fade along their length.</summary>
        private static void BakeTrail(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return;
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f); // additive
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
        }

        private static void BakeUnlit(string name, Shader shader, Color color, bool transparent)
        {
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", color);

            if (transparent)
            {
                // Mirrors what URP's material inspector does when Surface Type is switched
                // to Transparent — the shader reads these floats, the keyword selects the
                // variant, and the render queue puts it after opaque geometry.
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// Lit, transparent and almost mirror-smooth, with the flagstone normal map stretched
        /// over it for a gentle ripple: torchlight glints across it, which is most of what
        /// makes standing water read as water.
        /// </summary>
        private static void BakeWater(string name, Shader shader)
        {
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", new Color(0.06f, 0.16f, 0.14f, 0.72f));
            mat.SetFloat("_Smoothness", 0.88f);
            mat.SetFloat("_Metallic", 0f);
            // Ripples: a tileable wave field (whole-number sine waves + soft noise). URP Lit samples the
            // normal with the base UVs and the detail normal with its own: WaterFlow scrolls the two
            // against each other, so the surface moves instead of sliding.
            var h = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size, tau = Mathf.PI * 2f;
                    h[y * Size + x] = Mathf.Sin(tau * (3f * u + 2f * v)) * 0.22f + Mathf.Sin(tau * (-2f * u + 5f * v)) * 0.16f
                        + Mathf.Sin(tau * (7f * u - 1f * v)) * 0.08f + (Fbm(u, v, 6f, 6f, 4, 77) - 0.5f) * 0.9f;
                }
            var ripple = SaveTexture("WaterRipple_Normal", HeightToNormal(h, 5f), true);
            mat.SetTexture("_BumpMap", ripple);
            mat.SetFloat("_BumpScale", 0.32f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_DetailNormalMap", ripple);
            mat.SetFloat("_DetailNormalMapScale", 0.45f);
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(1.7f, 1.7f));
            mat.EnableKeyword("_DETAIL_MULX2");
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
        }

        // ---- Generators. Each returns albedo in rgb and height in a. ----

        private static Color StoneBricks(float u, float v)
        {
            // 4 rows of 2 bricks per tile; alternate rows offset by half a brick.
            const int rows = 4, cols = 2;
            float rowF = v * rows;
            int row = Mathf.FloorToInt(rowF);
            float colF = u * cols + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(colF);

            float bx = colF - col, by = rowF - row;
            float mortar = 0.045f;
            // Bricks are twice as wide as they are tall, so horizontal distance is doubled to
            // measure both edges in the same units.
            float edge = Mathf.Min(Mathf.Min(bx, 1f - bx) * 2f, Mathf.Min(by, 1f - by));
            float inBrick = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mortar * 0.5f, mortar * 1.6f, edge));

            float brickId = Hash(Mod(col, cols), row);
            float tone = 0.32f + brickId * 0.14f;
            float grain = Fbm(u, v, 18f, 18f, 4, 11) * 0.12f;
            float chip = Mathf.Clamp01(Fbm(u, v, 6f, 6f, 3, 12) - 0.62f) * 1.2f;

            // Worn edges catch less light and collect grime, so each brick reads as a
            // rounded block rather than a flat painted rectangle.
            float bevel = Mathf.Clamp01(edge / 0.16f);
            float grime = Fbm(u, v, 4f, 4f, 3, 13) * 0.35f;
            var stone = new Color(tone + 0.02f, tone, tone - 0.03f) * (1f - grain * 0.6f) * (1f - chip * 0.5f)
                        * (0.72f + bevel * 0.28f) * (1f - grime);
            var mortarColor = new Color(0.16f, 0.15f, 0.14f);

            float crack = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u, v, 5f, 5f, 3, 15) - 0.5f) * 90f) * (brickId > 0.88f ? 1f : 0f);
            stone *= 1f - crack * 0.55f;
            var c = Color.Lerp(mortarColor, stone, inBrick);
            c *= 1f - Damp(u, v) * 0.3f;
            c.a = inBrick * (0.6f + bevel * 0.3f + grain) - chip * 0.4f - crack * 0.4f;
            return c;
        }

        private static Color Flagstones(float u, float v)
        {
            // Voronoi cells read as irregular laid stones; the gap between nearest and
            // second-nearest distance is the grout line.
            Voronoi(u * 5f, v * 5f, 5, out float d1, out float d2, out float cellId);
            float gap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.09f, d2 - d1));

            float tone = 0.27f + cellId * 0.1f;
            float grain = Fbm(u, v, 24f, 24f, 4, 21) * 0.15f;
            float moss = Mathf.Clamp01(Fbm(u, v, 3f, 3f, 3, 22) - 0.6f) * (1f - gap);

            // Hairline cracks across some stones.
            float crack = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u, v, 7f, 7f, 4, 25) - 0.5f) * 40f) * (cellId > 0.55f ? 1f : 0f);
            var stone = new Color(tone, tone * 0.97f, tone * 0.92f) * (1f - grain) * (1f - crack * 0.6f);
            stone = Color.Lerp(stone, new Color(0.14f, 0.18f, 0.1f), moss * 1.5f);
            var c = Color.Lerp(new Color(0.08f, 0.075f, 0.07f), stone, gap);
            // Wet stone is darker.
            c *= 1f - Puddle(u, v) * 0.35f;
            c.a = gap * (0.85f + grain) * (1f - d1 * 0.3f) - crack * 0.35f;
            return c;
        }

        /// <summary>Where water stands on the floor: broad, soft-edged patches (0 dry .. 1 pooled).</summary>
        private static float Puddle(float u, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.7f, Fbm(u, v, 2f, 2f, 3, 23)));

        /// <summary>Damp running down a wall: tall thin streaks, strongest low on the wall.</summary>
        private static float Damp(float u, float v) =>
            Mathf.Clamp01((Fbm(u, v, 14f, 1.5f, 3, 14) - 0.55f) * 3.5f) * Mathf.Lerp(1f, 0.35f, v);

        private static float FlagstoneGloss(float u, float v)
        {
            Voronoi(u * 5f, v * 5f, 5, out float d1, out float d2, out float _);
            float grout = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.09f, d2 - d1));
            // Dry stone is matte, grout holds a little water, puddles are mirrors.
            return Mathf.Max(0.18f + Fbm(u, v, 20f, 20f, 2, 24) * 0.1f, grout * 0.45f, Puddle(u, v) * 0.95f);
        }

        private static float StoneWallGloss(float u, float v) => 0.12f + Damp(u, v) * 0.6f;

        private static Color WoodPlanks(float u, float v)
        {
            const int planks = 4;
            float p = u * planks;
            int plank = Mathf.FloorToInt(p);
            float px = p - plank;
            float seam = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.05f, Mathf.Min(px, 1f - px)));

            float id = Hash(plank, 7);
            // Grain runs along the plank: high frequency across it, low along it.
            float grain = Fbm(u, v + id, 48f, 2f, 3, 31);
            float rings = Mathf.Clamp01(grain * 1.4f - 0.2f);
            float knot = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(px, Mathf.Repeat(v + id, 1f)), new Vector2(0.5f, 0.5f)) * 9f);
            float tone = 0.3f + id * 0.1f;

            var wood = new Color(tone, tone * 0.66f, tone * 0.4f) * (0.75f + rings * 0.3f) * (1f - knot * 0.5f);
            var c = Color.Lerp(new Color(0.06f, 0.04f, 0.03f), wood, seam);
            c.a = seam * (0.7f + rings * 0.3f);
            return c;
        }

        private static Color BrushedMetal(float u, float v)
        {
            float streak = Fbm(u, v, 2f, 64f, 3, 41) * 0.25f;
            float scratch = Mathf.Pow(Mathf.Clamp01(Fbm(u, v, 40f, 3f, 2, 42) - 0.55f) * 2.5f, 2f);
            float tone = 0.72f + streak - scratch * 0.15f;
            var c = new Color(tone, tone, tone * 1.02f);
            c.a = 0.5f + streak - scratch * 0.5f;
            return c;
        }

        private static Color BoneSurface(float u, float v)
        {
            float n = Fbm(u, v, 8f, 8f, 5, 51);
            float crack = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u, v, 5f, 5f, 4, 52) - 0.5f) * 30f);
            float tone = 0.72f + n * 0.12f;
            var c = new Color(tone, tone * 0.94f, tone * 0.8f) * (1f - crack * 0.5f);
            c.a = 0.6f + n * 0.3f - crack * 0.5f;
            return c;
        }

        private static Color ClothWeave(float u, float v)
        {
            // A diagonal twill at a frequency the texture can hold (8 px a thread), soft folds over
            // it, and grime gathered in the creases.
            float twill = Mathf.Sin((u + v) * Mathf.PI * 2f * 64f) * 0.5f + 0.5f;
            float thread = Mathf.Sin((u - v) * Mathf.PI * 2f * 64f) * 0.5f + 0.5f;
            float weave = Mathf.Lerp(twill, thread, 0.35f);
            float fold = Fbm(u, v, 3f, 1.5f, 3, 62);
            float dirt = Fbm(u, v, 7f, 7f, 3, 61);
            // The weave is a faint texture in the colour only: as height it lit up as big diagonal
            // ridges on small things (hoods, bedrolls) and read as scuffed. Folds carry the relief.
            float tone = 0.74f + (fold - 0.5f) * 0.18f - dirt * 0.1f + weave * 0.025f;
            var c = new Color(tone, tone, tone);
            c.a = 0.35f + fold * 0.5f + weave * 0.015f;
            return c;
        }

        /// <summary>
        /// Leather (rebaked 2026-10-20: the old 30-cell Voronoi read as cobblestones, so the Rogue and Ranger
        /// looked made of rock): a fine pore grain, long soft creases and a little wear, with gentle relief.
        /// </summary>
        private static Color LeatherSurface(float u, float v)
        {
            Voronoi(u * 90f, v * 90f, 90, out float d1, out float d2, out float id);
            float pores = Mathf.Clamp01((d2 - d1) * 3.5f);
            float grain = Fbm(u, v, 22f, 22f, 3, 71);
            float crease = Fbm(u, v, 3f, 10f, 3, 73);
            float wear = Mathf.Clamp01((Fbm(u, v, 6f, 6f, 3, 74) - 0.55f) * 3f);
            float tone = 0.31f + (grain - 0.5f) * 0.05f + (crease - 0.5f) * 0.07f + wear * 0.05f;
            var c = new Color(tone, tone * 0.62f, tone * 0.38f) * (0.95f + pores * 0.05f);
            c.a = 0.5f + (crease - 0.5f) * 0.6f + pores * 0.08f + (grain - 0.5f) * 0.1f;
            return c;
        }

        private static Color Rubble(float u, float v)
        {
            Voronoi(u * 9f, v * 9f, 9, out float d1, out float d2, out float id);
            float rock = 1f - Mathf.Clamp01(d1 * 1.6f);
            float tone = 0.22f + id * 0.12f;
            var c = new Color(tone, tone * 0.96f, tone * 0.9f) * (0.5f + rock * 0.5f);
            c.a = rock;
            return c;
        }

        /// <summary>Pale funerary marble: cool grey-white, soft clouding, thin dark veins. For statues.</summary>
        private static Color Marble(float u, float v)
        {
            float cloud = Fbm(u, v, 4f, 4f, 4, 31);
            float warp = Fbm(u, v, 3f, 3f, 3, 32) * 7f;
            float vein = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin((u * 5f + v * 2f) * Mathf.PI + warp)) * 9f), 2f);
            float tone = 0.7f + cloud * 0.16f;
            var c = new Color(tone * 0.96f, tone * 0.97f, tone, 1f) * (1f - vein * 0.45f);
            c.a = 0.6f + cloud * 0.3f - vein * 0.2f;
            return c;
        }

        private static Color Tint(Color c, Color tint) => new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);

        // ------------------------------------------------------------------
        // Noise — all tileable over [0,1) via integer periods
        // ------------------------------------------------------------------

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static float Hash(int x, int y, int seed = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Value noise that wraps every <paramref name="px"/> by <paramref name="py"/> units.</summary>
        private static float ValueNoise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float a = Hash(Mod(x0, px), Mod(y0, py), seed);
            float b = Hash(Mod(x0 + 1, px), Mod(y0, py), seed);
            float c = Hash(Mod(x0, px), Mod(y0 + 1, py), seed);
            float d = Hash(Mod(x0 + 1, px), Mod(y0 + 1, py), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>
        /// Fractal noise over (u, v) in [0,1), at a base frequency per axis. Frequencies are
        /// rounded to whole numbers and used as the wrap period, so the result tiles.
        /// </summary>
        private static float Fbm(float u, float v, float freqX, float freqY, int octaves, int seed)
        {
            int px = Mathf.Max(1, Mathf.RoundToInt(freqX));
            int py = Mathf.Max(1, Mathf.RoundToInt(freqY));
            float x = u * px, y = v * py;
            float sum = 0f, amp = 0.5f, norm = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x, y, px, py, seed + i * 17) * amp;
                norm += amp;
                x *= 2f; y *= 2f; px *= 2; py *= 2; amp *= 0.5f;
            }

            return sum / norm;
        }

        private static void Voronoi(float x, float y, int period, out float d1, out float d2, out float cellId)
        {
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            d1 = d2 = 10f;
            cellId = 0f;

            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                int wx = Mod(gx, period), wy = Mod(gy, period);
                float jx = gx + 0.15f + Hash(wx, wy, 101) * 0.7f;
                float jy = gy + 0.15f + Hash(wx, wy, 202) * 0.7f;
                float d = Mathf.Sqrt((x - jx) * (x - jx) + (y - jy) * (y - jy));

                if (d < d1)
                {
                    d2 = d1;
                    d1 = d;
                    cellId = Hash(wx, wy, 303);
                }
                else if (d < d2)
                {
                    d2 = d;
                }
            }
        }

        // ------------------------------------------------------------------
        // Texture + asset plumbing
        // ------------------------------------------------------------------

        private static Color[] HeightToNormal(float[] height, float strength)
        {
            var normals = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float l = height[y * Size + Mod(x - 1, Size)];
                float r = height[y * Size + Mod(x + 1, Size)];
                float d = height[Mod(y - 1, Size) * Size + x];
                float u = height[Mod(y + 1, Size) * Size + x];

                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f / Size * 64f).normalized;
                normals[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            return normals;
        }

        private static Texture2D SaveTexture(string name, Color[] pixels, bool isNormal)
        {
            string path = TextureDir + "/" + name + ".png";

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, linear: isNormal);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material GetOrCreateMaterial(string name, Shader shader)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------
        // Post-processing and renderer
        // ------------------------------------------------------------------

        public const string VolumeProfilePath = "Assets/Settings/DungeonPost.asset";

        /// <summary>
        /// The grade that makes it read as a dungeon rather than a test scene: ACES for
        /// filmic torch highlights, bloom so flames and eyes glow, a heavy vignette for the
        /// claustrophobic Dark-and-Darker edge, and a cool desaturated shadow tone that
        /// makes the warm torchlight pop by contrast.
        /// </summary>
        private static void BakeVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }

            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            // The grade (visual pass 2026-10-16): deeper blacks, warmer firelight, cooler shadows, a
            // little lens character. Readability first: the tells and glows must still pop.
            var bloom = Get<Bloom>();
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.05f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.93f, 0.85f));

            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(new Color(0.03f, 0.02f, 0.02f));

            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.62f);
            color.contrast.Override(22f);
            color.saturation.Override(-4f);

            var split = Get<SplitToning>();
            split.shadows.Override(new Color(0.3f, 0.4f, 0.58f));
            split.highlights.Override(new Color(0.68f, 0.52f, 0.36f));
            split.balance.Override(-10f);

            // Shadows sink a little, highlights keep their warmth.
            var smh = Get<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.92f, 0.95f, 1.05f, -0.06f));
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0.02f));
            smh.highlights.Override(new Vector4(1.04f, 1f, 0.95f, 0.03f));

            var chroma = Get<ChromaticAberration>();
            chroma.intensity.Override(0.08f);

            var grain = Get<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.2f);

            EditorUtility.SetDirty(profile);
        }

        /// <summary>
        /// Screen-space ambient occlusion: contact shadows in corners, under props and round feet —
        /// the single biggest step away from the flat look. Added once to the renderer asset.
        /// </summary>
        private static void AddAmbientOcclusion(UniversalRendererData renderer)
        {
            ScreenSpaceAmbientOcclusion ssao = null;
            foreach (var f in renderer.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion s) ssao = s;
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
                var rso = new SerializedObject(renderer);
                var map = rso.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId))
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            var fso = new SerializedObject(ssao);
            void F(string field, float v) { var p = fso.FindProperty("m_Settings." + field); if (p != null) p.floatValue = v; }
            F("Intensity", 1.7f);
            F("Radius", 0.45f);
            F("DirectLightingStrength", 0.3f);
            F("Falloff", 60f);
            var down = fso.FindProperty("m_Settings.Downsample");
            if (down != null) down.boolValue = false;
            fso.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(renderer);
        }

        /// <summary>
        /// Forward+ lifts URP's 8-lights-per-object cap, which a torch-lit corridor blows
        /// through immediately. Additional-light shadows let torches cast them.
        /// </summary>
        private static void ConfigureRenderer()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/DeepboundRenderer.asset");
            if (renderer != null)
            {
                var so = new SerializedObject(renderer);
                so.FindProperty("m_RenderingMode").intValue = 2; // Forward+
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
                AddAmbientOcclusion(renderer);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/DeepboundURP.asset");
            if (pipeline != null)
            {
                var so = new SerializedObject(pipeline);
                so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1; // per pixel
                so.FindProperty("m_ShadowDistance").floatValue = 40f;
                so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 2048;
                so.ApplyModifiedPropertiesWithoutUndo();
                pipeline.supportsHDR = true;
                EditorUtility.SetDirty(pipeline);
            }
        }
    }
}
