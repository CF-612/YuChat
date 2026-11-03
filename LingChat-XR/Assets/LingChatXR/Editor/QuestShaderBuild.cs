using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LingChatXR.Editor
{
    // VRM 在运行时导入，Unity 无法从场景材质推断这些官方导入器的 Shader.Find 依赖。
    public static class QuestShaderBuild
    {
        public static void PreserveRuntimeImportShaders()
        {
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included=graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach(string name in VrmRuntimeShaders.Required)
            {
                var shader=Shader.Find(name);if(shader==null)throw new InvalidOperationException("VRM 导入着色器缺失："+name);
                bool present=Enumerable.Range(0,included.arraySize).Any(i=>included.GetArrayElementAtIndex(i).objectReferenceValue==shader);
                if(!present){int index=included.arraySize;included.InsertArrayElementAtIndex(index);included.GetArrayElementAtIndex(index).objectReferenceValue=shader;}
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        }
    }
}
