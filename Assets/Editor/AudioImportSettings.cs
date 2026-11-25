using UnityEditor;
using UnityEngine;

namespace Deepbound.EditorTools
{
    public class AudioImportSettings : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            bool longForm = path.Contains("/Audio/Music/") || path.Contains("/Audio/Ambience/");
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = longForm ? 0.45f : 0.6f;
            s.loadType = longForm ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            importer.defaultSampleSettings = s;
            importer.forceToMono = !longForm;
            importer.loadInBackground = longForm;
        }
    }
}
