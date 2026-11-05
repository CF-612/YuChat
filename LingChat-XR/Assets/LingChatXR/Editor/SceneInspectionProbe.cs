#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using LingChatXR.Chat;
using LingChatXR.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace LingChatXR.Editor
{
    public static class SceneInspectionProbe
    {
        static IEnumerator run;
        static double deadline;
        static int frame=-1;
        static bool quest;
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Pc()=>Start(false);
        public static void Quest()=>Start(true);
        static void Start(bool headset)
        {
            quest=headset;
            Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");
            Environment.SetEnvironmentVariable("XR_HOST_CONNECTION",Path.Combine(Output,"no-service.json"));
            EditorSceneManager.OpenScene(quest?QuestProject.ScenePath:PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("检查超时");
                if(!EditorApplication.isPlaying||Time.frameCount==frame)return;
                frame=Time.frameCount;run??=Verify();if(run.MoveNext())return;
                Debug.Log("XR_M1_PASSED scene inspection");EditorApplication.update-=Tick;EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());EditorApplication.update-=Tick;EditorApplication.Exit(1);}
        }
        static void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException(name);
            File.AppendAllText(Path.Combine(Output,"checks.txt"),name+Environment.NewLine);
        }
        static IEnumerator Verify()
        {
            var live=Object.FindAnyObjectByType<FeiyuPreview>();
            while(!live.Ready)yield return null;
            var model=live.Avatar;var instance=model.Instance;
            live.ManualInput=true;
            Check(live.Place(Vector3.zero),"放置实际场景角色");
            model.Motions.EndWelcome();yield return null;
            while(!model.Motions.CatalogReady.IsCompleted)yield return null;
            var prefs=Newtonsoft.Json.Linq.JObject.FromObject(model.Motions.Preferences).ToString();
            int count=Object.FindObjectsByType<FeiyuAvatar>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
            if(quest)
            {
                var entry=Object.FindAnyObjectByType<QuestEntry>();var panel=entry.Panel;
                panel.PalmCanvas.transform.SetParent(entry.TrackingSpace,false);
                panel.PalmCanvas.gameObject.SetActive(true);
                void Click(string label)
                {
                    var button=panel.PalmCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b=>b.name==label);
                    button.onClick.Invoke();
                }
                Click("角色检查");Check(panel.IsExpanded,"掌上入口打开现有控制面板");
                Click("下个动作");Click("播放动作");while(model.Motions.Loading)yield return null;
                Check(model.Motions.ActiveId!=null||model.Motions.HeadId!=null,"手掌按钮在当前角色播放");
                Click("下个");Click("显示表情");Check(model.InspectedExpression!=null,"手掌按钮设置实际模型表情");
                panel.Collapse();Check(!panel.IsExpanded,"收起回到掌上快捷操作");
            }
            else
            {
                var view=Object.FindAnyObjectByType<XrChatView>();
                view.Settings.Open(2);
                var btn=view.Settings.Root.Query<Button>().ToList().First(b=>b.text=="场景角色检查 · 动作与表情");
                using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=btn;btn.SendEvent(evt);}
                while(view.MotionWindow.Preparing)yield return null;
                Check(view.MotionWindow.PreviewAvatar==model,"PC 设置入口绑定场景实例");
                view.MotionWindow.SelectAction("nod",true);
                Check(model.Motions.HeadId=="nod","PC 点头由实际角色播放器执行");
                Check(view.MotionWindow.SelectExpression("happy"),"PC 选择模型实际存在的 happy");
                yield return null;
                Check(model.Instance.Runtime.Expression.GetWeight(UniVRM10.ExpressionKey.Happy)>.99f,"实际 happy 权重为一");
                C0SettingsProbe.CaptureCombined(Path.Combine(Output,"scene-review.png"),view,live);
                Check(live.Interaction.BeginGrab("inspect-test",out int grab),"检查中可提起");
                live.RefreshSpatialState();
                Check(model.InspectedExpression==null && !view.MotionWindow.SelectExpression("happy"),"抓取立即接管面部并拒绝继续试表情");
                Check(live.Interaction.ReleaseGrabWithDescent("inspect-test",grab,live.Interaction.LastSafePosition),"检查后可放回");
                view.MotionWindow.Close();Check(view.Settings.IsOpen,"返回原设置草稿");
            }
            Check(model.InspectedExpression==null,"结束检查释放表情");
            Check(live.Avatar==model&&model.Instance==instance&&count==Object.FindObjectsByType<FeiyuAvatar>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,"实例数量不增加");
            Check(GameObject.Find("Motion review camera")==null&&live.Interaction.PlaneConfirmed,"无预览相机且平台仍有效");
            Check(prefs==Newtonsoft.Json.Linq.JObject.FromObject(model.Motions.Preferences).ToString(),"试播保留动作许可");
        }
    }
}
#endif
