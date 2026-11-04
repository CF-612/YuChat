using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VRM10.MToon10;

namespace LingChatXR
{
    // lilToon 原版 Shader；这里只做 alpha.2 材质映射，不实现或修改描边算法。
    public static class AnimePresentation
    {
        public static int Apply(FeiyuAvatar avatar)
        {
            var roles=avatar.Presentation?.animeMaterials;
            if(roles==null)return 0;
            var bridge=avatar.gameObject.GetComponent<LilToonExpressionBridge>();
            if(bridge!=null)return bridge.MaterialCount;
            // UniVRM 的表情系统先绑定原始材质，颜色动画仍由它唯一驱动。
            var expression=avatar.Instance.Runtime.Expression;
            bridge=avatar.gameObject.AddComponent<LilToonExpressionBridge>();
            var converted=new Dictionary<Material,Material>();
            foreach(var renderer in avatar.Instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                bool hairSheets=Array.Exists(materials,m=>m!=null&&roles.hairSheets.Matches(m.name));
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i];if(source==null)continue;
                    if(!converted.TryGetValue(source,out var target))
                    {
                        var context=new MToon10Context(source);
                        string name=source.name.Replace(" (Instance)","");
                        bool skin=roles.skin.Matches(name),detail=roles.detail.Matches(name);
                        bool cloth=roles.cloth.Matches(name);
                        bool gold=roles.gold.Matches(name);
                        bool outline=context.AlphaMode==MToon10AlphaMode.Opaque&&!detail&&!roles.outlineExcluded.Matches(name);
                        string variant=context.AlphaMode==MToon10AlphaMode.Opaque?(outline?"OpaqueOutline":"Opaque"):
                            context.AlphaMode==MToon10AlphaMode.Cutout?"Cutout":"Transparent";
                        var template=Resources.Load<Material>("Anime/"+variant);
                        if(template==null)throw new InvalidOperationException("缺少 lilToon 材质模板："+variant);
                        target=new Material(template){name=source.name+" [lilToon]"};
                        target.SetColor("_Color",source.GetColor("_Color"));
                        target.SetTexture("_MainTex",source.GetTexture("_MainTex"));
                        target.SetTextureScale("_MainTex",source.GetTextureScale("_MainTex"));
                        target.SetTextureOffset("_MainTex",source.GetTextureOffset("_MainTex"));
                        target.SetFloat("_Cull",source.GetFloat("_M_CullMode"));
                        target.SetFloat("_Cutoff",context.AlphaMode==MToon10AlphaMode.Transparent?-.001f:source.GetFloat("_Cutoff"));
                        target.SetFloat("_ZWrite",source.GetFloat("_M_ZWrite"));
                        target.renderQueue=source.renderQueue;
                        target.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));
                        target.SetFloat("_UseBumpMap",0);target.SetFloat("_BumpScale",0);
                        target.SetTexture("_EmissionMap",source.GetTexture("_EmissionMap"));
                        target.SetColor("_EmissionColor",source.GetColor("_EmissionColor"));
                        // 固定明部颜色，只保留一级赛璐璐阴影；不叠加写实凹凸、反射或环境渐变。
                        target.SetFloat("_UseReflection",0);target.SetFloat("_Smoothness",0);target.SetFloat("_Metallic",0);
                        target.SetFloat("_UseMatCap",0);target.SetFloat("_UseMatCap2nd",0);
                        target.SetFloat("_UseRim",0);target.SetFloat("_UseRimShade",0);target.SetFloat("_UseEmission",0);
                        target.SetFloat("_UseShadow",skin||detail?0:1);
                        Color shade=skin?new Color(.95f,.80f,.78f):cloth?new Color(.86f,.83f,.90f):new Color(.70f,.75f,.90f);
                        target.SetColor("_ShadowColor",shade);
                        target.SetFloat("_ShadowBorder",.45f);target.SetFloat("_ShadowBlur",.012f);
                        target.SetFloat("_ShadowNormalStrength",0);
                        target.SetFloat("_ShadowBorderRange",0);
                        target.SetFloat("_ShadowReceive",0);
                        target.SetFloat("_ShadowEnvStrength",0);
                        target.SetFloat("_LightMinLimit",1);target.SetFloat("_LightMaxLimit",1);
                        target.SetFloat("_MonochromeLighting",1);target.SetFloat("_AsUnlit",1);
                        Color line=skin?new Color(.46f,.21f,.20f):cloth&&!hairSheets?new Color(.12f,.10f,.16f):gold?new Color(.16f,.10f,.08f):new Color(.075f,.10f,.24f);
                        target.SetColor("_OutlineColor",line);
                        // lilToon 在模型空间按厘米挤出；深度偏移压低内部交叠线的权重。
                        target.SetFloat("_OutlineWidth",outline?(skin?.10f:hairSheets?.22f:cloth?.28f:gold?.06f:.22f):0);
                        target.SetFloat("_OutlineFixWidth",0);
                        if(outline)
                        {
                            target.SetFloat("_OutlineCull",(float)CullMode.Off);
                            target.SetFloat("_OutlineZBias",target.GetFloat("_OutlineWidth")*.01f+.0002f);
                        }
                        target.SetFloat("_OutlineEnableLighting",0);
                        if(hairSheets)ConfigureHairOutline(target);
                        converted.Add(source,target);bridge.Add(source,target,shade);
                    }
                    materials[i]=target;
                }
                renderer.sharedMaterials=materials;
            }
            return converted.Count;
        }
        // 薄发片需要双面描边。壳后移略超过挤出宽度，由深度测试保留完整发束边缘。
        // 不用整片头发共用 Stencil，否则内部发束线也会被抹掉。
        public static void ConfigureHairOutline(Material material)
        {
            material.SetFloat("_StencilPass",(float)StencilOp.Keep);
            material.SetFloat("_OutlineStencilComp",(float)CompareFunction.Always);
            material.SetFloat("_OutlineCull",(float)CullMode.Off);
            material.SetFloat("_OutlineZBias",material.GetFloat("_OutlineWidth")*.01f+.0002f);
            material.SetFloat("_UseShadow",0);
        }
        public static void ConfigureStage(Camera camera)
        {
            camera.allowHDR=false;camera.allowMSAA=true;QualitySettings.antiAliasing=4;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.58f,.60f,.66f);
            RenderSettings.ambientEquatorColor=new Color(.45f,.46f,.53f);
            RenderSettings.ambientGroundColor=new Color(.32f,.33f,.39f);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(light.type==LightType.Directional)
                {
                    light.intensity=.9f;light.color=new Color(1,.97f,.95f);light.transform.rotation=Quaternion.Euler(35,-35,0);
                    light.shadows=LightShadows.Soft;light.shadowStrength=.45f;light.shadowBias=.02f;light.shadowNormalBias=.05f;
                }
        }
    }
}
