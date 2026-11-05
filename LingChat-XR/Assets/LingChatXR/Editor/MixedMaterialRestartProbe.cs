#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LingChatXR.Editor
{
    public static class MixedMaterialRestartProbe
    {
        static bool interrupted, restarting;
        static double deadline;
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SceneManager.sceneLoaded+=SelectTestModel;
            deadline=EditorApplication.timeSinceStartup+240;
            EditorApplication.update+=Tick;
            EditorApplication.EnterPlaymode();
        }
        static void SelectTestModel(Scene scene,LoadSceneMode mode)
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            if(live!=null)live.ConfigureCharacter("local-smallbluefish-alpha2",true);
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("V21 重进 Play 超时");
                if(!EditorApplication.isPlaying)
                {
                    if(interrupted&&!restarting&&!EditorApplication.isPlayingOrWillChangePlaymode)
                    {restarting=true;EditorApplication.EnterPlaymode();}
                    return;
                }
                var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
                if(live==null)return;
                if(!interrupted && live.LoadingPhase==FeiyuPreview.LoadPhase.Importing &&
                    Resources.FindObjectsOfTypeAll<Mesh>().Any(m=>m!=null&&m.name.StartsWith("_RepairStage_",StringComparison.Ordinal)))
                {
                    interrupted=true;
                    File.WriteAllText(Path.Combine(Output,"interruption.txt"),"导入进行中，已出现 V21 网格");
                    EditorApplication.ExitPlaymode();return;
                }
                if(live.LoadingPhase==FeiyuPreview.LoadPhase.Failed)throw new Exception(live.Status);
                if(!restarting||!live.Ready)return;
                var materials=live.Avatar.Instance.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).ToArray();
                foreach(var name in new[]{"TrueMovableMouth_Rose","CircleShockToothIvory"})
                    if(!materials.Any(m=>m.name.StartsWith(name,StringComparison.Ordinal)&&m.shader.name=="Standard"))
                        throw new Exception("原 Standard 材质丢失："+name);
                if(!materials.Any(m=>m.name.EndsWith(" [lilToon]",StringComparison.Ordinal))||!live.Place(Vector3.zero))
                    throw new Exception("MToon 转换或实际放置失败");
                Debug.Log("XR_M1_PASSED V21 重进、混合材质及场景放置");Finish(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(1);}
        }
        static void Finish(int code){SceneManager.sceneLoaded-=SelectTestModel;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
#endif
