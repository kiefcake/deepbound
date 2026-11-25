using UnityEditor;

namespace Deepbound.EditorTools
{
    public class ModelImportSettings : AssetPostprocessor
    {
        private bool Ours => assetPath.Replace('\\', '/').Contains("/Resources/Models/");

        private void OnPreprocessModel()
        {
            if (!Ours) return;
            var m = (ModelImporter)assetImporter;
            m.importAnimation = false;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            m.addCollider = false;
            m.isReadable = true;
            m.animationType = ModelImporterAnimationType.None;
            m.meshCompression = ModelImporterMeshCompression.Medium;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        private void OnPreprocessTexture()
        {
            if (!Ours) return;
            var t = (TextureImporter)assetImporter;
            t.maxTextureSize = 512;
            t.textureCompression = TextureImporterCompression.Compressed;
            t.mipmapEnabled = true;
            if (assetPath.Contains("_nor_")) t.textureType = TextureImporterType.NormalMap;
        }
    }
}
