using UnityEditor;
using UnityEngine;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// Kenney's CC0 Particle Pack sprites (2026-10-28, Assets/Textures/Fx, see docs/CREDITS.md) are greyscale, some
    /// white on black with no alpha: their brightness becomes their alpha, so every one can be tinted and blended
    /// the same way. Clamped, mipmapped, 256 px is plenty for a puff.
    /// </summary>
    public class FxImportSettings : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Textures/Fx/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
