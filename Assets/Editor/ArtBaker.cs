using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Deepbound.EditorTools
{
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

            BakeSurface("StoneWall", lit, StoneBricks, 0.36f, 0f, 2.2f, StoneWallGloss);
            BakeSurface("Flagstone", lit, Flagstones, 0.45f, 0f, 2.6f, FlagstoneGloss);
            BakeSurface("Wood", lit, WoodPlanks, 0.2f, 0f, 1.6f);

            BakeSurface("Steel", lit, BrushedMetal, 0.48f, 0.6f, 0.6f);
            BakeSurface("Gold", lit, (x, y) => Tint(BrushedMetal(x, y), new Color(1f, 0.78f, 0.35f)), 0.45f, 0.9f, 0.5f);
            BakeSurface("Bone", lit, BoneSurface, 0.12f, 0f, 1.4f);
            BakeSurface("Cloth", lit, ClothWeave, 0.05f, 0f, 0.45f);
            BakeSurface("Leather", lit, LeatherSurface, 0.18f, 0f, 0.4f);
            BakeSurface("Rubble", lit, Rubble, 0.08f, 0f, 3f);
            BakeSurface("Marble", lit, Marble, 0.1f, 0f, 0.5f);

            ApplyDetail("StoneWall", "Assets/Textures/Detail/rock_grain_normal.jpg", 7f, 0.55f);
            ApplyDetail("Flagstone", "Assets/Textures/Detail/rock_grain_normal.jpg", 5f, 0.45f);
            ApplyDetail("Rubble", "Assets/Textures/Detail/rock_grain_normal.jpg", 4f, 0.7f);
            ApplyDetail("Marble", "Assets/Textures/Detail/rock_grain_normal.jpg", 6f, 0.2f);
            ApplyDetail("Wood", "Assets/Textures/Detail/wood_grain_normal.jpg", 2f, 0.5f);
            ApplyDetail("Cloth", "Assets/Textures/Detail/cloth_weave_normal.jpg", 6f, 0.35f);
            ApplyDetail("Leather", "Assets/Textures/Detail/leather_grain_normal.jpg", 4f, 0.4f);
            ApplyDetail("Steel", "Assets/Textures/Detail/metal_wear_normal.jpg", 3f, 0.25f);
            ApplyDetail("Gold", "Assets/Textures/Detail/metal_wear_normal.jpg", 3f, 0.2f);
            ApplyDetail("Bone", "Assets/Textures/Detail/bone_pores_normal.jpg", 6f, 0.3f);

            BakeModels(lit);

            BakeFlat("Dark", lit, new Color(0.05f, 0.05f, 0.06f), 0.2f, 0.2f);

            BakeEmissive("Glow", lit, Color.white, 3f);

            BakeUnlit("Flame", unlit, new Color(1f, 0.55f, 0.15f), transparent: false);
            BakeUnlit("Telegraph", unlit, new Color(1f, 0.15f, 0.1f, 0.45f), transparent: true);
            BakeUnlit("Smoke", unlit, new Color(0.17f, 0.15f, 0.2f, 0.32f), transparent: true);
            BakeWater("Water", lit);
            BakeTrail("Trail");
            BakeFxSprites();

            BakeVolumeProfile();
            ConfigureRenderer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Art] Procedural art baked.");
        }

        private delegate Color Surface(float u, float v);

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

        private const string ModelDir = "Assets/Resources/Models";

        private static void BakeModels(Shader lit)
        {
            if (!AssetDatabase.IsValidFolder(ModelDir)) return;
            EnsureFolder(MaterialDir + "/Models");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelDir }))
            {
                var diff = AssetDatabase.GUIDToAssetPath(guid);
                var file = Path.GetFileNameWithoutExtension(diff);
                int at = file.IndexOf("_diff_", StringComparison.Ordinal);
                if (at < 0) continue;
                string stem = file.Substring(0, at);
                string dir = Path.GetDirectoryName(diff).Replace('\\', '/');
                string nor = null;
                foreach (var ext in new[] { ".png", ".jpg" })
                {
                    var p = dir + "/" + stem + "_nor_gl_1k" + ext;
                    if (File.Exists(p)) { nor = p; break; }
                }
                if (nor != null && AssetImporter.GetAtPath(nor) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
                {
                    ni.textureType = TextureImporterType.NormalMap;
                    ni.SaveAndReimport();
                }
                var matPath = MaterialDir + "/Models/" + stem + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = lit;
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diff));
                var nt = nor != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(nor) : null;
                if (nt != null) { mat.SetTexture("_BumpMap", nt); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
                string s = stem.ToLowerInvariant();
                bool glass = s.EndsWith("_glass");
                bool metal = s.Contains("brass") || s.Contains("trim") || s.Contains("clamp") || s.Contains("sledgehammer") || s.Contains("chandelier");
                mat.SetColor("_BaseColor", glass ? new Color(1f, 0.78f, 0.45f, 0.45f) : new Color(0.86f, 0.82f, 0.78f, 1f));
                mat.SetFloat("_Metallic", metal ? 0.55f : 0f);
                mat.SetFloat("_Smoothness", glass ? 0.7f : metal ? 0.3f : 0.08f);
                if (glass)
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_Blend", 0f);
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.25f) * 0.6f);
                }
                EditorUtility.SetDirty(mat);
            }
        }

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

        private static void BakeTrail(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return;
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
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

        private static void BakeFxSprites()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures/Fx" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                string name = "Fx_" + System.IO.Path.GetFileNameWithoutExtension(path);
                bool darkens = name.Contains("smoke") || name.Contains("scorch") || name.Contains("dirt");
                var mat = GetOrCreateMaterial(name, shader);
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", darkens ? 0f : 2f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)(darkens ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
                mat.SetFloat("_SrcBlendAlpha", (float)(darkens ? BlendMode.One : BlendMode.Zero));
                mat.SetFloat("_DstBlendAlpha", (float)(darkens ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
            }

            BakeUnlit("Cards", shader, Color.white, false);
        }

        private static void BakeUnlit(string name, Shader shader, Color color, bool transparent)
        {
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", color);

            if (transparent)
            {
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

        private static void BakeWater(string name, Shader shader)
        {
            var mat = GetOrCreateMaterial(name, shader);
            mat.SetColor("_BaseColor", new Color(0.06f, 0.16f, 0.14f, 0.72f));
            mat.SetFloat("_Smoothness", 0.6f);
            mat.SetFloat("_Metallic", 0f);

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

        private static Color StoneBricks(float u, float v)
        {
            const int rows = 4, cols = 2;
            float rowF = v * rows;
            int row = Mathf.FloorToInt(rowF);
            float colF = u * cols + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(colF);

            float bx = colF - col, by = rowF - row;
            float mortar = 0.045f;

            float edge = Mathf.Min(Mathf.Min(bx, 1f - bx) * 2f, Mathf.Min(by, 1f - by));
            float inBrick = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mortar * 0.5f, mortar * 1.6f, edge));

            float brickId = Hash(Mod(col, cols), row);
            float tone = 0.32f + brickId * 0.14f;
            float grain = Fbm(u, v, 18f, 18f, 4, 11) * 0.12f;
            float chip = Mathf.Clamp01(Fbm(u, v, 6f, 6f, 3, 12) - 0.62f) * 1.2f;

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
            Voronoi(u * 5f, v * 5f, 5, out float d1, out float d2, out float cellId);
            float gap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.09f, d2 - d1));

            float tone = 0.27f + cellId * 0.1f;
            float grain = Fbm(u, v, 24f, 24f, 4, 21) * 0.15f;
            float moss = Mathf.Clamp01(Fbm(u, v, 3f, 3f, 3, 22) - 0.6f) * (1f - gap);

            float crack = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u, v, 7f, 7f, 4, 25) - 0.5f) * 40f) * (cellId > 0.55f ? 1f : 0f);
            var stone = new Color(tone, tone * 0.97f, tone * 0.92f) * (1f - grain) * (1f - crack * 0.6f);
            stone = Color.Lerp(stone, new Color(0.14f, 0.18f, 0.1f), moss * 1.5f);
            var c = Color.Lerp(new Color(0.08f, 0.075f, 0.07f), stone, gap);

            c *= 1f - Puddle(u, v) * 0.35f;
            c.a = gap * (0.85f + grain) * (1f - d1 * 0.3f) - crack * 0.35f;
            return c;
        }

        private static float Puddle(float u, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.7f, Fbm(u, v, 2f, 2f, 3, 23)));

        private static float Damp(float u, float v) =>
            Mathf.Clamp01((Fbm(u, v, 14f, 1.5f, 3, 14) - 0.55f) * 3.5f) * Mathf.Lerp(1f, 0.35f, v);

        private static float FlagstoneGloss(float u, float v)
        {
            Voronoi(u * 5f, v * 5f, 5, out float d1, out float d2, out float _);
            float grout = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.09f, d2 - d1));

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
            float twill = Mathf.Sin((u + v) * Mathf.PI * 2f * 64f) * 0.5f + 0.5f;
            float thread = Mathf.Sin((u - v) * Mathf.PI * 2f * 64f) * 0.5f + 0.5f;
            float weave = Mathf.Lerp(twill, thread, 0.35f);
            float fold = Fbm(u, v, 3f, 1.5f, 3, 62);
            float dirt = Fbm(u, v, 7f, 7f, 3, 61);

            float tone = 0.74f + (fold - 0.5f) * 0.18f - dirt * 0.1f + weave * 0.025f;
            var c = new Color(tone, tone, tone);
            c.a = 0.35f + fold * 0.5f + weave * 0.015f;
            return c;
        }

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

        public const string VolumeProfilePath = "Assets/Settings/DungeonPost.asset";

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

        private static void AddRetroPass(UniversalRendererData renderer)
        {
            var shader = Shader.Find("Deepbound/Retro");
            if (shader == null) { Debug.LogError("[ArtBaker] Deepbound/Retro shader missing"); return; }
            var mat = GetOrCreateMaterial("Retro", shader);
            EditorUtility.SetDirty(mat);
            FullScreenPassRendererFeature pass = null;
            foreach (var f in renderer.rendererFeatures) if (f is FullScreenPassRendererFeature fp && fp.name == "Retro") pass = fp;
            if (pass == null)
            {
                pass = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                pass.name = "Retro";
                AssetDatabase.AddObjectToAsset(pass, renderer);
                renderer.rendererFeatures.Add(pass);
                var rso = new SerializedObject(renderer);
                var map = rso.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pass, out string _, out long localId))
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            pass.passMaterial = mat;
            pass.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            pass.fetchColorBuffer = true;
            pass.requirements = ScriptableRenderPassInput.Color;
            pass.passIndex = 0;
            pass.SetActive(true);
            EditorUtility.SetDirty(pass);
            EditorUtility.SetDirty(renderer);
        }

        private static void ConfigureRenderer()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/DeepboundRenderer.asset");
            if (renderer != null)
            {
                var so = new SerializedObject(renderer);
                so.FindProperty("m_RenderingMode").intValue = 2;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
                AddAmbientOcclusion(renderer);
                AddRetroPass(renderer);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/DeepboundURP.asset");
            if (pipeline != null)
            {
                var so = new SerializedObject(pipeline);
                so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
                so.FindProperty("m_ShadowDistance").floatValue = 40f;
                so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 2048;
                so.ApplyModifiedPropertiesWithoutUndo();
                pipeline.supportsHDR = true;
                EditorUtility.SetDirty(pipeline);
            }
        }
    }
}
