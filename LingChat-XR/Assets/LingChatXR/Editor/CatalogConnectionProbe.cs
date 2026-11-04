#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Editor
{
    public static class CatalogConnectionProbe
    {
        static IEnumerator work;static double deadline;static int lastFrame=-1;static string runtimeError;
        static readonly JArray checks=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+150;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Log(string message,string stack,LogType type){if(type==LogType.Exception)runtimeError=message+stack;}
        static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        static void Tick()
        {
            try
            {
                if(runtimeError!=null)throw new Exception(runtimeError);
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("服务接线检查超时");
                if(!EditorApplication.isPlaying||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                work??=Verify();if(work.MoveNext())return;
                File.WriteAllText(Path.Combine(Output,"unity-checks.json"),new JObject{["checks"]=checks,["realHttpTransport"]=true,["realInference"]=false,["questTested"]=false}.ToString());
                Debug.Log("XR_M1_PASSED catalog connection "+checks.Count);Finish(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;EditorApplication.Exit(code);}
        static IEnumerator Verify()
        {
            var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;
            while(!chat.Connected||!live.Ready)yield return null;
            live.Locomotion.ManualStep=true;
            Check(live.ActivePackageFolder=="local-physics-reference","真实占位入口");
            Check(chat.AvailableRoles.Any(r=>r.id=="fixture-third"),"Unity 通过 HTTP 读取新增角色");
            Check(chat.History.Count==50&&chat.HasEarlierHistory,"首次连接只恢复近期历史");
            chat.LoadEarlierHistory();while(chat.History.Count==50)yield return null;
            Check(chat.History.Count==100&&chat.History[0].text=="fixture-history-55","历史分页接入实际控制器");
            var view=chat.GetComponent<XrChatView>();var ui=view.Settings;ui.Open(1);
            var dropdown=ui.Root.Query<DropdownField>().ToList().First(f=>f.label=="对话角色");
            var third=dropdown.choices.First(x=>x.Contains("fixture-third"));dropdown.value=third;ui.Save();
            while(ui.IsSaving||chat.Role!="fixture-third"||!chat.Connected)yield return null;
            Check(!ui.IsOpen&&chat.RoleLabel=="独立测试角色","设置选择新增角色并保存自动关闭");
            Check(chat.History.Count==0&&!chat.Busy,"角色切换隔离旧历史且无永久忙状态");
            string preferences=Path.Combine(Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY"),"xr-dialogue-preferences.json");
            Check((string)JObject.Parse(File.ReadAllText(preferences))["role"]=="fixture-third","新增角色选择持久化");
            chat.Connect();yield return null;while(!chat.Connected||!chat.CanSend)yield return null;
            Check(chat.Role=="fixture-third"&&chat.AvailableRoles.Count==3,"重连保留新增角色");
            chat.SwitchRole("feiyu");yield return null;while(!chat.Connected||chat.Role!="feiyu")yield return null;
            Check(chat.History.Count==50&&chat.History[0].text=="fixture-history-105","切回原角色恢复原历史");
            Check(!chat.Voice.IsPlaying,"重连和切换不自动重播旧音频");
        }
    }
}
#endif
