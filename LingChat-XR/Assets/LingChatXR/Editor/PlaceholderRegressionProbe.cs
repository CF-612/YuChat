#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Threading;
using LingChatXR.Core;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class PlaceholderRegressionProbe
    {
        static IEnumerator work;static double deadline;static int lastFrame=-1;static string runtimeError,originalPreferences;
        static readonly JArray checks=new JArray(),fallSamples=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static string PreferenceFile=>Path.Combine(Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY"),"xr-application.json");
        public static void Run()
        {
            originalPreferences="{\"modelFolder\":\"local-smallbluefish-alpha2\",\"futureField\":\"keep\"}";
            File.WriteAllText(PreferenceFile,originalPreferences);
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();if(chat!=null)chat.enabled=false;
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+180;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Log(string message,string stack,LogType type){if(type==LogType.Exception)runtimeError=message+stack;}
        static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        static void Tick()
        {
            try
            {
                if(runtimeError!=null)throw new Exception(runtimeError);
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("占位回归超时");
                if(!EditorApplication.isPlaying||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                work??=Verify();if(work.MoveNext())return;
                File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["fallSamples"]=fallSamples,["questAccepted"]=false,["specialExpressionsAccepted"]=false}.ToString());
                Debug.Log("XR_M1_PASSED placeholder "+checks.Count);Finish(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;EditorApplication.Exit(code);}
        static IEnumerator Verify()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;
            while(!live.Ready){if(live.LoadingPhase==FeiyuPreview.LoadPhase.Failed)throw new Exception(live.Status);yield return null;}
            live.Locomotion.ManualStep=true;
            Check(live.ActivePackageFolder=="local-physics-reference","旧保存选择被临时启动策略覆盖");
            Check(live.Avatar.Package.model.sha256=="733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6","实际加载占位 VRM");
            Check(File.ReadAllText(PreferenceFile)==originalPreferences,"原模型选择和未知设置未改写");
            Check(!live.Avatar.UsesMouth24Face&&live.Avatar.Presentation==null,"缺少专属声明使用通用表情");
            Check(live.Avatar.Physics!=null,"复用通用 UniVRM 物理入口");
            Check(live.Place(Vector3.zero),"统一入口放置");live.Avatar.Motions.EndWelcome();
            var core=live.Interaction;
            Check(core.BeginTouch("fixture"),"进入触摸");live.RefreshSpatialState();yield return null;
            Check(core.EndTouch("fixture"),"结束触摸");live.RefreshSpatialState();
            Check(core.BeginGrab("fixture",out var grab),"触摸后提起");
            var safe=core.LastSafePosition;var raised=new Vec3(safe.X,safe.Y+.4f,safe.Z);
            Check(core.MoveGrab("fixture",grab,raised),"持有位置更新");live.RefreshSpatialState();yield return null;
            Check(!core.MoveGrab("other",grab,raised),"另一只手不能夺取持有权");
            Check(core.ReleaseGrabWithDescent("fixture",grab,safe),"松手进入连续下降");
            Check(Math.Abs(core.Position.Y-raised.Y)<.001f,"松手当帧不瞬移到桌面");
            Check(!core.MoveGrab("fixture",grab,raised),"旧抓取输入失效");
            float prior=core.Position.Y;int changing=0;
            while(core.State==PetState.Descending||core.State==PetState.Settling){yield return null;float y=core.Position.Y;if(y<prior-.00001f)changing++;prior=y;fallSamples.Add(y);}
            Check(changing>=3&&core.State==PetState.Idle,"多帧下降并恢复待机");
            float size=live.Avatar.HeightMeters;var instance=live.Avatar.Instance;
            Check(live.BeginSizeEdit(out var edit)&&live.PreviewSize(edit,.30f),"单手缩放共用接口");
            live.CancelSizeEdit();Check(Math.Abs(live.Avatar.HeightMeters-size)<.0001f&&live.Avatar.Instance==instance,"取消缩放还原且不重载模型");
            Check(!live.PreviewSize(edit,.25f),"旧缩放会话拒绝");
            Check(live.BeginSizeEdit(out edit)&&live.PreviewSize(edit,.26f)&&live.CommitSizeEdit(edit),"确认缩放保存占位模型独立设置");
            Check(File.ReadAllText(PreferenceFile)==originalPreferences,"缩放未覆盖原应用选择");
            live.ResetPlacement();Check(!core.PlaneConfirmed,"重新放置清除平台");Check(live.Place(Vector3.zero),"可重新选择平台");
            for(int i=0;i<20;i++)yield return null;
            Check(float.IsFinite(live.Avatar.HeadGripBone.position.y),"通用待机和物理保持有限值");
            var first=live.SwitchCharacterAsync("local-physics-reference");
            var second=live.SwitchCharacterAsync("local-physics-reference");
            while(!first.IsCompleted||!second.IsCompleted)yield return null;
            Check(!first.Result&&second.Result&&live.Ready,"连续重载只采纳最新请求");
            Check(live.Avatar.Instance!=instance&&!live.Interaction.PlaneConfirmed,"新模型生命周期不继承旧平台和实例");
            Check(live.Place(Vector3.zero),"取消旧加载后仍可正常放置");
            var preservedInstance=live.Avatar.Instance;var preservedCore=live.Interaction;
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();var ignored=live.SwitchCharacterAsync("local-physics-reference",cancelled.Token);
                while(!ignored.IsCompleted)yield return null;
                Check(!ignored.Result&&live.Ready&&live.Avatar.Instance==preservedInstance&&live.Interaction==preservedCore&&preservedCore.PlaneConfirmed,"预先取消的切换不拆掉当前角色和平台");
            }
            string modelPreferences=Path.Combine(Path.GetDirectoryName(PreferenceFile),"xr-"+live.Avatar.Package.model.sha256+".json");
            string previousModelPreferences=File.ReadAllText(modelPreferences);
            File.WriteAllText(modelPreferences,"{interrupted-settings");
            var withBrokenPreferences=live.SwitchCharacterAsync("local-physics-reference");
            while(!withBrokenPreferences.IsCompleted)yield return null;
            Check(withBrokenPreferences.Result&&live.Ready,"设置损坏时仍能载入模型");
            Check(File.ReadAllText(modelPreferences)=="{interrupted-settings","损坏设置原件保留");
            Check(live.SaveMovementSpeed(1.5f)!=null&&File.ReadAllText(modelPreferences)=="{interrupted-settings","不能以默认设置覆盖损坏原件");
            File.WriteAllText(modelPreferences,previousModelPreferences);
            var fixtureFolder="local-load-recovery-"+Guid.NewGuid().ToString("N");
            var missing=live.SwitchCharacterAsync(fixtureFolder);while(!missing.IsCompleted)yield return null;
            Check(!missing.Result&&!live.Ready&&live.LoadingPhase==FeiyuPreview.LoadPhase.Failed,"缺失角色明确失败且不遗留可交互旧模型");
            var retryMissing=live.RetryCharacterLoadAsync();while(!retryMissing.IsCompleted)yield return null;
            Check(!retryMissing.Result&&live.LoadingPhase==FeiyuPreview.LoadPhase.Failed,"重试仍指向失败目标而非悄悄换回上一个角色");
            var fixturePath=Path.Combine(Application.streamingAssetsPath,"Characters",fixtureFolder);Directory.CreateDirectory(fixturePath);
            var sourcePath=Path.Combine(Application.streamingAssetsPath,"Characters","local-physics-reference");
            File.Copy(Path.Combine(sourcePath,"manifest.json"),Path.Combine(fixturePath,"manifest.json"));
            File.Copy(Path.Combine(sourcePath,"model.vrm"),Path.Combine(fixturePath,"model.vrm"));
            var repaired=live.RetryCharacterLoadAsync();while(!repaired.IsCompleted)yield return null;
            Check(repaired.Result&&live.ActivePackageFolder==fixtureFolder,"补齐目标资源后同一重试入口恢复");
            Check(File.ReadAllText(PreferenceFile)==originalPreferences,"失败恢复未改应用选择");
            var bad=Path.Combine(Output,"invalid-catalog");Directory.CreateDirectory(bad);
            File.WriteAllText(Path.Combine(bad,"index.json"),"{\"version\":1,\"folders\":[],\"startupOverride\":\"missing\"}");
            var invalid=CharacterCatalog.Load(bad,default);while(!invalid.IsCompleted)yield return null;
            Check(invalid.IsFaulted&&invalid.Exception.GetBaseException() is InvalidDataException,"无效占位配置明确拒绝");
        }
    }
}
#endif
