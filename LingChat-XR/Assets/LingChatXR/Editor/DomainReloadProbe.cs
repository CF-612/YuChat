using System;
using System.IO;
using LingChatXR.Chat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 保留默认脚本域重载，区别于动作测试的 DisableDomainReload。
    public static class DomainReloadProbe
    {
        const string Key="XR.DomainProbe";
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=false;
            SessionState.SetBool(Key,true);SessionState.SetInt(Key+".cycle",0);SessionState.SetString(Key+".phase","enter");
            SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+180);Resume();
        }
        [InitializeOnLoadMethod]static void Resume(){if(SessionState.GetBool(Key,false)){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}}
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+".deadline",0)){Finish(false);return;}
            string phase=SessionState.GetString(Key+".phase","");
            if(phase=="enter")
            {SessionState.SetString(Key+".phase","running");EditorApplication.EnterPlaymode();return;}
            if(phase=="running")
            {
                if(!EditorApplication.isPlaying)return;
                var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();
                if(chat==null||!chat.Connected)return;
                File.AppendAllText(Path.Combine(Output,"cycles.txt"),"connected cycle "+SessionState.GetInt(Key+".cycle",0)+"\n");
                SessionState.SetString(Key+".phase","leaving");EditorApplication.ExitPlaymode();return;
            }
            if(phase=="leaving"&&!EditorApplication.isPlaying&&!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                int cycle=SessionState.GetInt(Key+".cycle",0)+1;SessionState.SetInt(Key+".cycle",cycle);
                if(cycle>=3){Finish(true);return;}
                SessionState.SetString(Key+".phase","enter");
            }
        }
        static void Finish(bool success)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;
            File.AppendAllText(Path.Combine(Output,"cycles.txt"),success?"completed\n":"timeout\n");
            Debug.Log(success?"XR_M1_PASSED":"XR_DOMAIN_PROBE_FAILED");EditorApplication.Exit(success?0:1);
        }
    }
}
