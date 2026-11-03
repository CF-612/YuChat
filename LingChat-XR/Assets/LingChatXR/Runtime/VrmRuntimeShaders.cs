using System;
using UnityEngine;
namespace LingChatXR
{
    public static class VrmRuntimeShaders
    {
        public static readonly string[] Required={"VRM10/MToon10","UniGLTF/UniUnlit","Hidden/UniGLTF/StandardMapImporter"};
        public static void Validate()
        {
            foreach(string name in Required)
                if(Shader.Find(name)==null)throw new InvalidOperationException("安装包缺少 VRM 导入着色器："+name+"。请更新修正版安装包。");
        }
    }
}
