using UnityEditor;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 思考 WebP 的有效画幅为 405×314；默认 NPOT ToNearest 会把它拉成方形。
    public sealed class DialogueTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/LingChatXR/Resources/LocalLingChatEffects/Thinking/"))return;
            var importer=(TextureImporter)assetImporter;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
            importer.maxTextureSize=512;importer.wrapMode=TextureWrapMode.Clamp;
        }
    }
}
