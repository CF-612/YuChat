using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LingChatXR.Editor
{
    public sealed class CharacterCatalogBuilder : IPreprocessBuildWithReport
    {
        public int callbackOrder=>0;
        [InitializeOnLoadMethod] static void Schedule()=>EditorApplication.delayCall+=Write;
        public void OnPreprocessBuild(BuildReport report)=>Write();
        [MenuItem("LingChat XR/Refresh Character Package Index")]
        public static void Write()
        {
            string root=Application.streamingAssetsPath+"/Characters";if(!Directory.Exists(root))return;
            var folders=Directory.GetDirectories(root).Where(dir=>File.Exists(Path.Combine(dir,"manifest.json"))).Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            string path=Path.Combine(root,"index.json"),json=JsonUtility.ToJson(new CharacterCatalog{folders=folders},true);
            if(!File.Exists(path)||File.ReadAllText(path)!=json){File.WriteAllText(path,json);AssetDatabase.ImportAsset("Assets/StreamingAssets/Characters/index.json");}
        }
    }
    public sealed class CharacterCatalogImports : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] previous)
        {
            if(imported.Concat(deleted).Concat(moved).Concat(previous).Any(p=>p.StartsWith("Assets/StreamingAssets/Characters/",StringComparison.Ordinal)&&p.EndsWith("/manifest.json",StringComparison.Ordinal)))EditorApplication.delayCall+=CharacterCatalogBuilder.Write;
        }
    }
}
