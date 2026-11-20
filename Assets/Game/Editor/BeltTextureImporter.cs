using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    public sealed class BeltTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Game/Presentation/Resources/PWManagerUI/BeltParts/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.alphaIsTransparency = !assetPath.Contains("/mask-");
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 1024;
            importer.filterMode = assetPath.Contains("/mask-") ? FilterMode.Point : FilterMode.Bilinear;
            importer.sRGBTexture = !assetPath.Contains("/mask-");
        }
    }
}
