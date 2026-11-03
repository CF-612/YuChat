using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR.Features;

namespace LingChatXR.Editor
{
    public static class QuestApkBuild
    {
        [Serializable] sealed class Issue { public string message;public bool error; }
        [Serializable] sealed class Validation { public List<Issue> issues=new List<Issue>(); }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').Contains("/.r1-unity-validation/Assets") || EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
                throw new InvalidOperationException("仅允许在隔离 Android 构建副本中执行。");
            string output=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");Directory.CreateDirectory(output);
            QuestProject.Prepare();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"org.lingchat.smallbluefish.preview");
            PlayerSettings.productName="小肥鱼 MR";PlayerSettings.bundleVersion="0.5.1";PlayerSettings.Android.bundleVersionCode=8;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.Vulkan});
            PlayerSettings.Android.useCustomKeystore=false;
            EditorUserBuildSettings.buildAppBundle=false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject=true;
            AssetDatabase.SaveAssets();
            var rules=new List<OpenXRFeature.ValidationRule>();OpenXRProjectValidation.GetCurrentValidationIssues(rules,BuildTargetGroup.Android);
            var validation=new Validation();foreach(var rule in rules)validation.issues.Add(new Issue{message=rule.message,error=rule.error});
            File.WriteAllText(Path.Combine(output,"android-validation.json"),JsonUtility.ToJson(validation,true));
            if(rules.Any(x=>x.error))throw new InvalidOperationException("Android 官方配置检查有错误，见 android-validation.json。");
            string export=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.cache/quest-gradle-export"));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{QuestProject.ScenePath},target=BuildTarget.Android,locationPathName=export,options=BuildOptions.Development});
            File.WriteAllText(Path.Combine(output,"build-result.txt"),$"stage=GradleExport\nresult={report.summary.result}\nbytes={report.summary.totalSize}\nerrors={report.summary.totalErrors}\nwarnings={report.summary.totalWarnings}\nduration={report.summary.totalTime}\n");
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Quest APK 构建失败，见构建日志。");
            Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);
        }
    }
}
