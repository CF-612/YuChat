using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class QuestAndroidScriptProbe
    {
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");Directory.CreateDirectory(Path.Combine(output,"android-scripts"));
            var result=PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings{group=BuildTargetGroup.Android,target=BuildTarget.Android,options=ScriptCompilationOptions.None},Path.Combine(output,"android-scripts"));
            if(result.assemblies==null||result.assemblies.Count==0)throw new Exception("Android script compilation failed");
            File.WriteAllLines(Path.Combine(output,"android-assemblies.txt"),result.assemblies);
            Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);
        }
    }
}
