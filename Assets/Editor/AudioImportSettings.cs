using UnityEditor;
using UnityEngine;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// Import settings for the recorded audio (2026-10-24, CC0 packs, see docs/CREDITS.md): music and room tone stream
    /// from disk as Vorbis, so a 22 MB boss loop costs a few MB in the build and nothing in memory up front; sound
    /// effects are decompressed on load and mono (they are placed in 3D, where stereo only blurs them).
    /// </summary>
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
