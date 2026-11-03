using System;
using System.Collections;
using System.Collections.Generic;
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
    public static class M1CompletionProbe
    {
        static IEnumerator work;
        static double deadline;
        static readonly List<string> checks = new List<string>();
        static string Evidence => Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            Directory.CreateDirectory(Evidence);
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
            EditorApplication.EnterPlaymode();
        }
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("completion timeout");
                if (!EditorApplication.isPlaying) return;
                work ??= RunChecks();
                if (!work.MoveNext()) Finish(0);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Evidence,"failure.txt"), e.ToString()); Finish(1); }
        }
        static IEnumerator RunChecks()
        {
            var live = UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            live.ManualInput = true;
            while (!live.Ready) yield return null;
            var view = UnityEngine.Object.FindAnyObjectByType<XrChatView>();
            view.Settings.Open(2);
            for (int i=0;i<20;i++) yield return null;
            var button = view.Settings.Root.Query<Button>().ToList().First(b=>b.text=="打开动作预览与素材库");
            double start = EditorApplication.timeSinceStartup;
            // 通过真实 UI Toolkit 按钮事件进入，区别于上批直接调用 Open。
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target=button; button.SendEvent(evt); }
            Check(view.MotionWindow.IsOpen,"settings-button-opens-preview");
            while(view.MotionWindow.Preparing && EditorApplication.timeSinceStartup-start<40) yield return null;
            File.WriteAllText(Path.Combine(Evidence,"preview-load.json"),new JObject{
                ["seconds"]=EditorApplication.timeSinceStartup-start,["ready"]=view.MotionWindow.PreviewAvatar!=null,
                ["labels"]=new JArray(view.MotionWindow.Root.Query<Label>().ToList().Select(x=>x.text))}.ToString());
            Check(view.MotionWindow.PreviewAvatar!=null,"ui-event-preview-ready-within-40s");
            var cached=view.MotionWindow.PreviewAvatar;view.MotionWindow.Close();
            Check(cached!=null&&!cached.gameObject.activeInHierarchy,"closed-preview-pauses-cached-avatar");
            double reopen=EditorApplication.timeSinceStartup;view.MotionWindow.Open();
            Check(view.MotionWindow.PreviewAvatar==cached&&!view.MotionWindow.Preparing,"reopen-reuses-model-without-import");
            File.WriteAllText(Path.Combine(Evidence,"preview-reopen.json"),new JObject{["seconds"]=EditorApplication.timeSinceStartup-reopen,["sameAvatar"]=view.MotionWindow.PreviewAvatar==cached}.ToString());
            view.MotionWindow.Close();
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;Check(change.Result,"switch-"+folder);}
                Check(live.Place(new Vector3(0,.74f,0)),"place-"+folder);
                var avatar=live.Avatar;var player=avatar.Motions;while(!player.CatalogReady.IsCompleted)yield return null;
                var prefs=new XrMotionPreferences{enabled=true,director=true,conversation=true};foreach(var item in player.Catalog)prefs.items[item.id]=true;player.ApplyPreferences(prefs);
                avatar.FreezePresentation=true;player.SetSpatialAllowed(true);player.Stop(true);
                var head=avatar.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.Head);
                var arm=avatar.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                player.BeginTurn("turn-a");var nod=player.Decide("turn-a","request-a","nod");while(!nod.IsCompleted)yield return null;
                Check(nod.Result&&player.HeadId=="nod","structured-nod-"+folder);
                var initial=head.localRotation;player.Step(.26f);Check(Quaternion.Angle(initial,head.localRotation)>5,"source-nod-curve-moves-head-"+folder);
                var duplicate=player.Decide("turn-a","request-a","nod");Check(duplicate.IsCompleted&&!duplicate.Result,"dedup-request-"+folder);
                var stale=player.Decide("old-turn","old","shake");Check(!stale.Result,"old-turn-rejected-"+folder);
                player.EndTurn();Check(player.HeadId==null,"turn-cancels-owned-head-"+folder);
                var manual=player.Play("shake");Check(manual.Result,"manual-head-"+folder);player.EndTurn();Check(player.HeadId=="shake","turn-end-preserves-manual-"+folder);
                player.Stop(true);player.BeginTurn("speech");
                var pcm=new float[24000*4];for(int i=0;i<pcm.Length;i++){float t=i/24000f;pcm[i]=t<.6f||t>1.0f&&t<2.8f?.08f*Mathf.Sin(i*.1f):0;}
                var rhythm=XrVoiceRhythm.Build(pcm,24000,1);Check(rhythm.Cues.Length>0,"voice-pause-produces-cue-"+folder);
                var armStart=arm.localRotation;
                for(int i=0;i<70;i++){player.UpdateConversation(true,false,100,i/60f,rhythm,"开心");player.Step(1f/60);}
                Check(Quaternion.Angle(armStart,arm.localRotation)>1,"speech-current-pose-arm-motion-"+folder);
                manual=player.Play("nod");Check(manual.Result,"head-over-speech-"+folder);player.Step(.2f);
                Check(player.HeadId=="nod"&&Quaternion.Angle(armStart,arm.localRotation)>1,"head-layer-keeps-speaking-arms-"+folder);
                player.SetSpatialAllowed(false);Check(player.HeadId==null&&!player.Busy,"spatial-cancels-all-"+folder);player.SetSpatialAllowed(true);
                player.Stop(true);player.BeginTurn("load");var pending=player.Decide("load","pending","wave_normal_right_661");player.EndTurn();while(!pending.IsCompleted)yield return null;Check(!pending.Result&&player.ActiveId==null,"late-director-load-cancelled-"+folder);
                // 只在测试内给现有中立片声明合法区间，检验契约；不写入产品目录或模型批准表。
                var pose=player.Catalog.First(a=>a.loop);bool oldLoop=pose.loop;var oldApproved=pose.approvedModels;
                pose.loop=false;pose.poseLoop=new XrPoseLoop{from=.2f,to=.6f,enterEnd=.2f,exitFrom=.8f,overlapSeconds=.1f};pose.approvedModels=new[]{avatar.Package.model.sha256};
                player.BeginTurn("pose-a");var play=player.Decide("pose-a","pose-request",pose.id);while(!play.IsCompleted)yield return null;Check(play.Result,"declared-pose-enter-"+folder);
                for(int i=0;i<600;i++)player.Step(1f/60);
                Check(player.PoseId==pose.id,"declared-pose-holds-without-last-frame-freeze-"+folder);
                var gate=(System.Threading.SemaphoreSlim)typeof(XrMotionPlayer)
                    .GetField("loadGate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(player);
                var lockTask=gate.WaitAsync();while(!lockTask.IsCompleted)yield return null;
                System.Threading.Tasks.Task<bool> late;
                try
                {
                    late=player.Decide("pose-a","delayed-action","wave_normal_right_661");
                    Check(!late.IsCompleted,"director-load-blocked-with-pose-"+folder);
                    player.BeginTurn("pose-b");
                }
                finally{gate.Release();}
                while(!late.IsCompleted)yield return null;
                Check(!late.Result&&player.PoseId==pose.id,"late-director-cannot-replace-retained-pose-"+folder);
                Check(player.PoseId==pose.id,"new-turn-keeps-current-director-pose-"+folder);
                var keep=player.Decide("pose-b","keep",pose.id);Check(keep.Result&&player.PoseId==pose.id,"repeated-pose-continues-"+folder);
                var end=player.Decide("pose-b","exit","none");Check(end.Result,"explicit-none-exits-own-pose-"+folder);
                player.Step(100);Check(player.PoseId==null&&player.ActiveId==null,"declared-pose-exits-"+folder);
                pose.poseLoop=null;pose.loop=oldLoop;pose.approvedModels=oldApproved;
                player.ApplyPreferences(new XrMotionPreferences());Check(!player.Busy,"permissions-off-"+folder);
                avatar.FreezePresentation=false;
            }
            if(Environment.GetEnvironmentVariable("XR_DIRECTOR_LIVE")=="1")
            {
                var change=live.SwitchCharacterAsync("local-smallbluefish-alpha2");while(!change.IsCompleted)yield return null;
                live.Place(Vector3.zero);yield return null;yield return null;
                var motions=live.Avatar.Motions;
                var prefs=new XrMotionPreferences{enabled=true,director=true};
                foreach(var item in motions.Catalog)prefs.items[item.id]=true;
                motions.ApplyPreferences(prefs);
                var window=ScriptableObject.CreateInstance<DirectorPlaytestWindow>();
                var results=new JArray();
                try{
                    foreach(string text in new[]{"我要走啦，下次见。","陪我安静待一会儿，先别动。","请蹲下，双手假装捡起一个大箱子，再慢慢站起来。"}){
                        window.ProbeInput=text;var job=window.Run(live,0);while(!job.IsCompleted)yield return null;job.GetAwaiter().GetResult();
                        results.Add(new JObject{["input"]=text,["selected"]=window.Selected,["executed"]=window.Executed,["cache"]=window.CachedPackage});
                        File.WriteAllText(Path.Combine(Evidence,"director-live.json"),results.ToString());
                    }
                    Check(window.Selected=="generate"&&motions.PlayingPackage,"real-director-to-t2m-playback");
                    var cachedPackage=window.CachedPackage;
                    var replay=window.Run(live,1);while(!replay.IsCompleted)yield return null;
                    Check(window.CachedPackage==cachedPackage&&window.Executed.StartsWith("缓存重播"),"real-cache-replay");
                    var reroll=window.Run(live,2);while(!reroll.IsCompleted)yield return null;
                    Check(window.CachedPackage!=cachedPackage&&motions.PlayingPackage,"same-intent-new-generated-package");
                    var late=window.Run(live,0);
                    live.Interaction.BeginGrab("playtest-hand",out var holdToken);live.RefreshSpatialState();
                    typeof(DirectorPlaytestWindow).GetMethod("OnInspectorUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(window,null);
                    while(!late.IsCompleted)yield return null;
                    Check(!motions.PlayingPackage&&window.Executed.Contains("过期"),"late-live-director-yields-to-held");
                    live.Interaction.ReleaseGrabWithDescent("playtest-hand",holdToken,live.Interaction.LastSafePosition);
                    live.RefreshSpatialState();
                }finally{UnityEngine.Object.DestroyImmediate(window);}
            }
            var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();
            var queue=(LingChatXR.Core.DialogueQueue)typeof(XrChatController).GetField("queue",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(chat);
            foreach(var failure in new[]{"translation","synthesis"})
            {
                queue.Begin("fixture","feiyu",failure,10);
                var evt=new ChatEvent{sessionId="fixture",roleId="feiyu",turnId=failure,generation=10,kind="text",message="还在这里。"};
                chat.Accept(evt);
                evt.kind="sentenceError";evt.message=failure;evt.emotion="开心";evt.motion="点头";
                chat.Accept(evt);chat.Accept(evt);
                var cue=queue.Take();
                Check(cue!=null&&cue.Failed&&cue.Text=="还在这里。"&&cue.Emotion=="开心"&&cue.Motion=="点头","silent-cue-keeps-presentation-"+failure);
                queue.Complete();Check(queue.Empty,"duplicate-error-does-not-repeat-"+failure);
                queue.Cancel();
            }
        }
        static void Log(string message,string stack,LogType type)
        { if(type==LogType.Exception || type==LogType.Error) File.AppendAllText(Path.Combine(Evidence,"runtime-errors.txt"),message+"\n"+stack+"\n"); }
        static void Finish(int code)
        {
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            File.WriteAllText(Path.Combine(Evidence,"checks.json"),new JObject{["technicalPassed"]=code==0,["checks"]=JArray.FromObject(checks),["visualAccepted"]=false,["questTested"]=false}.ToString());
            if(code==0)Debug.Log("XR_M1_PASSED");EditorApplication.Exit(code);
        }
    }
}
