using UnityEditor;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// Downloaded CC0 models (2026-11-20, Poly Haven, Assets/Resources/Models, see docs/CREDITS.md): meshes only (no animation,
    /// cameras, lights or colliders; Art.ModelKit sizes and places them and swaps in the materials ArtBaker bakes). Textures are
    /// capped at 512 px (the retro filter never shows more) and normal maps typed as such.
    /// </summary>
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
            m.isReadable = true; // static dressing merges them (MeshKit.Batcher.AddModel)
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
