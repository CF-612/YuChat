using System;
using System.IO;
using System.Linq;
using LingChatXR.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.InputSystem;

namespace LingChatXR.Editor
{
    public static class QuestProject
    {
        public const string ScenePath="Assets/Scenes/QuestPreview.unity";
        [MenuItem("LingChat XR/Prepare Quest Entry")]
        public static void Prepare()
        {
            CharacterCatalogBuilder.Write();
            QuestShaderBuild.PreserveRuntimeImportShaders();
            const string folder="Assets/LingChatXR/XR/Settings";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            if(!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey,out var settings))
            {
                settings=ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();AssetDatabase.CreateAsset(settings,folder+"/XRGeneralSettings.asset");EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,settings,true);
            }
            foreach(var group in new[]{BuildTargetGroup.Standalone,BuildTargetGroup.Android})
            {
                if(!settings.HasSettingsForBuildTarget(group))settings.CreateDefaultSettingsForBuildTarget(group);
                if(!settings.HasManagerSettingsForBuildTarget(group))settings.CreateDefaultManagerSettingsForBuildTarget(group);
                var general=settings.SettingsForBuildTarget(group);general.InitManagerOnStart=false;
                if(!XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",group))throw new Exception("OpenXR loader assignment failed "+group);
                FeatureHelpers.RefreshFeatures(group);
                var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(group);if(xr==null)throw new Exception("OpenXR settings missing "+group);
                foreach(var feature in xr.GetFeatures<OpenXRFeature>())if(feature.GetType().Name=="HandTracking"||feature.GetType().Name=="MetaHandTrackingAim"||feature.GetType().Name=="OculusTouchControllerProfile"||feature.GetType().Name=="ARCameraFeature"||feature.GetType().Name=="ARPlaneFeature"||feature.GetType().Name=="ARSessionFeature"||feature.GetType().Name=="OpenXRCompositionLayersFeature"||feature.GetType().Name=="OpenXRLifeCycleFeature"||(group==BuildTargetGroup.Android&&feature.GetType().Name=="MetaQuestFeature")){feature.enabled=true;EditorUtility.SetDirty(feature);}
                // 官方 Hands 示例在 Built-in 下要求 Multi-pass。
                xr.renderMode=OpenXRSettings.RenderMode.MultiPass;
                EditorUtility.SetDirty(xr);EditorUtility.SetDirty(general);EditorUtility.SetDirty(general.Manager);
            }
            EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            bool exists=File.Exists(ScenePath);var scene=EditorSceneManager.OpenScene(exists?ScenePath:PreviewProject.ScenePath);
            var entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();if(entry==null)entry=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>().gameObject.AddComponent<QuestEntry>();
            if(entry.PanelSprite==null)entry.PanelSprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            entry.InputActions=AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/LingChatXR/Resources/QuestInput.inputactions");
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/LingChatXR/Resources/QuestHands"}))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                // Built-in 原生 Fade 材质：中性灰白半透明，不再给真实双手染蓝。
                material.shader=Shader.Find("Standard");material.color=new Color(.86f,.86f,.86f,.42f);
                material.SetFloat("_Metallic",0);material.SetFloat("_Glossiness",0);
                material.SetFloat("_Mode",2);material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);material.SetInt("_ZWrite",0);
                material.SetOverrideTag("RenderType","Transparent");material.DisableKeyword("_ALPHATEST_ON");material.EnableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.DisableKeyword("_EMISSION");material.SetColor("_EmissionColor",Color.black);material.renderQueue=3000;EditorUtility.SetDirty(material);
            }
            // 官方示例为 URP 材质；本项目保留 Built-in，使用相同颜色的 Standard 材质。
            foreach(string name in new[]{"Controller_White","Controller_Grey"}){var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/LingChatXR/Resources/QuestControllers/"+name+".mat");if(material!=null){material.shader=Shader.Find("Standard");material.color=name.EndsWith("White")?new Color(.8f,.86f,.9f):new Color(.12f,.17f,.21f);EditorUtility.SetDirty(material);}}
            const string pointerPath="Assets/LingChatXR/Resources/QuestControllers/Pointer.mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(pointerPath)==null)AssetDatabase.CreateAsset(new Material(Shader.Find("Sprites/Default")){color=new Color(.15f,1f,.65f)},pointerPath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,ScenePath,!exists);
            var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(s=>s.path==ScenePath))scenes.Add(new EditorBuildSettingsScene(ScenePath,false));EditorBuildSettings.scenes=scenes.ToArray();
            Debug.Log("XR_QUEST_PREPARED: 单独场景手动启动 OpenXR，原桌面场景不自动启动 XR。");
        }
    }
}
