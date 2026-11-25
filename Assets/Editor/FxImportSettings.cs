using UnityEditor;
using UnityEngine;

namespace Deepbound.EditorTools
{
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
