using UnityEditor;
using UnityEngine;

// Music stems (Assets/Resources/Music) import compressed in memory rather than decompressed on load: three
// 3-minute stereo tracks would take a few hundred MB as raw samples. Not streamed either, so the stems can be
// scheduled to start on the exact same sample (see MusicPlayer).
// Ambience loops (Assets/Resources/Ambience) decompress on load, which keeps their loop points exact (the web
// build's compressed-in-memory playback is less precise), at a lower quality: they're background noise, and the
// WAVs would otherwise be most of the web download.
class MusicImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        bool music = assetPath.StartsWith("Assets/Resources/Music/");
        bool ambience = assetPath.StartsWith("Assets/Resources/Ambience/");
        if (!music && !ambience) return;
        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;
        settings.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = music ? 0.7f : 0.5f;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = false;
    }
}
