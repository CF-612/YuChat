using UnityEditor;
using UnityEngine;
using lilToon;

namespace LingChatXR.Editor
{
    // 使用作者的材质初始化 API，Resources 保留运行时需要的 Shader 变体。
    public static class LilToonTemplates
    {
        const string Folder="Assets/LingChatXR/Resources/Anime";
        [InitializeOnLoadMethod]
        static void Schedule() => EditorApplication.delayCall += Ensure;
        [MenuItem("LingChat XR/Prepare lilToon materials")]
        public static void Ensure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/LingChatXR/Resources","Anime");
            Create("OpaqueOutline",RenderingMode.Opaque,true);
            Create("Opaque",RenderingMode.Opaque,false);
            Create("Cutout",RenderingMode.Cutout,false);
            Create("Transparent",RenderingMode.Transparent,false);
            AssetDatabase.SaveAssets();
        }
        static void Create(string name,RenderingMode mode,bool outline)
        {
            string path=Folder+"/"+name+".mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(path)!=null)return;
            var shader=Shader.Find("lilToon");
            if(shader==null)throw new System.InvalidOperationException("lilToon 2.4.1 尚未导入完成。");
            var material=new Material(shader){name=name};
            lilToonInspector.SetupMaterialWithRenderingMode(material,mode,TransparentMode.Normal,outline,false,false,false);
            material.SetFloat("_UseShadow",1);material.SetFloat("_UseBumpMap",1);
            material.SetFloat("_UseEmission",1);material.SetColor("_EmissionColor",Color.black);
            material.SetFloat("_OutlineWidth",outline?.16f:0);
            AssetDatabase.CreateAsset(material,path);
        }
    }
}
