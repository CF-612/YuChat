using System;
using System.IO;
using System.Linq;
using LingChatXR.Speech;
using UnityEditor;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class ShortVoiceAssets
    {
        const string Folder = "Assets/LingChatXR/Resources/LocalInteractionVoice";
        [MenuItem("YuChat/互动短声/更新本地素材配置")]
        public static void Prepare()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var bank = AssetDatabase.LoadAssetAtPath<ShortVoiceBank>(Folder + "/Bank.asset");
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<ShortVoiceBank>();
                AssetDatabase.CreateAsset(bank, Folder + "/Bank.asset");
            }
            // 已有人工调整的配置只刷新音频引用，不覆盖音量、权重和冷却。
            var existing = bank.entries.ToDictionary(e => e.id);
            var entries = new System.Collections.Generic.List<ShortVoiceEntry>();
            foreach (string group in new[] { "happy", "curious", "surprised", "protest" })
            {
                for (int variant = 1; variant <= 2; variant++)
                {
                    string id = group + "_" + variant;
                    string path = Folder + "/" + id + ".wav";
                    if (!File.Exists(path)) throw new FileNotFoundException("先运行现有 TTS 临时短声导出", path);
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.preloadAudioData = true;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    importer.defaultSampleSettings = settings;
                    importer.forceToMono = true;
                    importer.SaveAndReimport();
                    var entry = existing.TryGetValue(id, out var saved) ? saved : new ShortVoiceEntry { id=id, group=group, cooldown=2 };
                    entry.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    entries.Add(entry);
                }
            }
            bank.entries = entries.ToArray();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }
    }
}
