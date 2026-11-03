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
    public static class M1MotionProbe
    {
        static IEnumerator work;static double deadline;static FeiyuPreview preview;static XrChatView view;
        static readonly List<string> checks=new List<string>();
        static string Evidence=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            Directory.CreateDirectory(Evidence);EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            Unity.Collections.NativeLeakDetection.Mode=Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SessionState.SetBool("XR.M1",true);deadline=EditorApplication.timeSinceStartup+720;
            EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.EnterPlaymode();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Configure()
        {if(SessionState.GetBool("XR.M1",false))UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>().ManualInput=true;}
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        static IEnumerator Frames(int count){for(int i=0;i<count;i++){int f=Time.frameCount;while(Time.frameCount==f)yield return null;}}
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("M1 timeout");if(!EditorApplication.isPlaying)return;if(work==null)work=RunChecks();if(!work.MoveNext())Finish(0);}catch(Exception e){File.WriteAllText(Path.Combine(Evidence,"failure.txt"),e.ToString());Finish(1);}}
        static IEnumerator RunChecks()
        {
            preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();while(!preview.Ready)yield return null;
            view=UnityEngine.Object.FindAnyObjectByType<XrChatView>();
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(preview.ActivePackageFolder!=folder){var change=preview.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;Check(change.Result,"load-"+folder);}
                var avatar=preview.Avatar;var player=avatar.Motions;while(!player.CatalogReady.IsCompleted)yield return null;
                Check(player.Catalog.Count(a=>!a.builtin)==23&&player.Catalog.Count(a=>a.builtin)==2,"catalog-23-vrma-plus-2-head-"+folder);Check(preview.Place(new Vector3(0,.74f,0)),"place-"+folder);
                var frames=Frames(6);while(frames.MoveNext())yield return null;
                var denied=player.Play("wave_normal_right_661");while(!denied.IsCompleted)yield return null;Check(!denied.Result,"permission-off-blocks-"+folder);
                var preferences=new XrMotionPreferences{enabled=true,automatic=false};foreach(var asset in player.Catalog)preferences.items[asset.id]=true;player.ApplyPreferences(preferences);
                foreach(var asset in player.Catalog)
                {
                    var play=player.Play(asset.id);while(!play.IsCompleted)yield return null;Check(play.Result,"import-play-"+folder+"-"+asset.id);
                    frames=Frames(5);while(frames.MoveNext())yield return null;
                    foreach(var bone in avatar.Instance.Runtime.ControlRig.Bones.Values)Check(float.IsFinite(bone.ControlTarget.position.y),"finite-"+folder+"-"+asset.id+"-"+bone.BoneType);
                    player.Stop(true);
                }
                var idle=player.SetIdle("idle_normal_both_550");while(!idle.IsCompleted)yield return null;Check(idle.Result,"loop-idle-enter-"+folder);
                frames=Frames(25);while(frames.MoveNext())yield return null;
                var wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;Check(wave.Result,"wave-start-"+folder);
                avatar.Instance.TryGetBoneTransform(HumanBodyBones.RightUpperArm,out var arm);var start=arm.rotation;float maxAngle=0;var root=avatar.transform.position;
                // 批处理帧率不固定；按真实动作时间观察，不能把快速的 180 帧当作三秒。
                float motionStarted=Time.unscaledTime;
                while(Time.unscaledTime-motionStarted<3){maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(start,arm.rotation));frames=Frames(1);while(frames.MoveNext())yield return null;}
                File.WriteAllText(Path.Combine(Evidence,"motion-time-"+folder+".json"),new JObject{["seconds"]=Time.unscaledTime-motionStarted,["maxArmAngle"]=maxAngle}.ToString());
                Check(maxAngle>8,"actual-bone-motion-"+folder);Check(avatar.transform.position==root,"no-root-motion-"+folder);
                player.Stop();while(player.Busy)yield return null;Check(player.IdleId=="idle_normal_both_550","stop-recovers-current-idle-"+folder);
                wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;
                Check(preview.Interaction.BeginGrab("m1-check",out int grab),"grab-owner-"+folder);
                frames=Frames(3);while(frames.MoveNext())yield return null;Check(!player.Busy,"real-grab-interrupts-"+folder);
                Check(preview.Interaction.ReleaseGrab("m1-check",grab,preview.Interaction.Position),"put-back-"+folder);
                frames=Frames(3);while(frames.MoveNext())yield return null;
                var late=player.Play("think_reserved_both_643");player.Stop(true);while(!late.IsCompleted)yield return null;
                frames=Frames(3);while(frames.MoveNext())yield return null;Check(!player.Busy,"stop-invalidates-late-play-"+folder);
                wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;
                player.SetSpatialAllowed(false);Check(!player.Busy,"spatial-loss-interrupts-"+folder);player.SetSpatialAllowed(true);
                wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;
                preferences.enabled=false;player.ApplyPreferences(preferences);Check(!player.Busy,"permission-off-stops-active-motion-"+folder);
                preferences.enabled=true;player.ApplyPreferences(preferences);
                var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();while(!chat.CanSend)yield return null;
                chat.AutoAdvance=true;chat.TestVoice();float sound=0,mouth=0;var samples=new float[256];bool movingWithVoice=false;
                Check(preview.BeginSizeEdit(out int sizeGeneration),"motion-size-start-"+folder);var beforeHeight=avatar.HeightMeters;
                while(chat.Busy)
                {
                    if(chat.Voice.IsPlaying)
                    {
                        if(!movingWithVoice){wave=player.Play("wave_normal_right_661");movingWithVoice=true;}
                        sound=Mathf.Max(sound,chat.Voice.SampleAmplitude(samples));mouth=Mathf.Max(mouth,avatar.Instance.Runtime.Expression.GetWeight(UniVRM10.ExpressionKey.Aa));
                        Check(preview.PreviewSize(sizeGeneration,beforeHeight+Mathf.Sin(Time.unscaledTime*2)*.01f),"speech-motion-resize-"+folder);
                    }
                    yield return null;
                }
                preview.CancelSizeEdit();Check(movingWithVoice&&sound>.0001f&&mouth>.001f,"actual-tts-mouth-with-motion-"+folder);
                while(player.Busy)yield return null;Check(player.ActiveId==null,"full-clip-completes-"+folder);
                var automatic=player.Catalog.First(a=>a.Speaking&&a.MatchesStyle(preferences.style));
                // 前面刚逐个试播过：仅测试实例缩短该条冷却，文件与产品默认保持原值。
                automatic.daily["minimumRepeatMs"]=0;
                foreach(var key in preferences.items.Keys.ToArray())preferences.items[key]=key==automatic.id;
                preferences.automatic=true;player.ApplyPreferences(preferences);player.BeginTurn("rhythm-fixture");
                var pcm=new float[48000];for(int n=24000;n<48000;n++)pcm[n]=.1f*Mathf.Sin(n*.1f);
                var rhythm=XrVoiceRhythm.Build(pcm,24000,1);string allowedEmotion=automatic.daily["emotions"][0].ToString();
                for(int tick=0;tick<=54;tick++)player.UpdateConversation(true,false,123,tick/60f,rhythm,allowedEmotion);
                frames=Frames(3);while(frames.MoveNext())yield return null;Check(player.ActiveId==automatic.id,"phrase-boundary-selects-permitted-motion-"+folder);
                player.CancelSpeech();while(player.Busy)yield return null;Check(player.ActiveId==null,"cancel-speech-owned-motion-"+folder);
                preferences.items["wave_normal_right_661"]=true;wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;player.CancelSpeech();
                Check(player.ActiveId=="wave_normal_right_661","speech-cancel-preserves-manual-owner-"+folder);
                preferences.Save(avatar.Package.model.sha256);Check(XrMotionPreferences.Read(avatar.Package.model.sha256).Allowed("wave_normal_right_661"),"model-bound-motion-preferences-save-"+folder);
                player.ApplyPreferences(new XrMotionPreferences{enabled=false});
                // 独立窗口：原角色实例、空间、许可均不能因试播改变。
                view.MotionWindow.Open();while(view.MotionWindow.PreviewAvatar==null)yield return null;
                var clone=view.MotionWindow.PreviewAvatar;Check(clone!=avatar&&preview.Avatar==avatar,"isolated-preview-avatar-"+folder);
                view.MotionWindow.SelectAction("wave_normal_right_661",true);while(clone.Motions.Loading)yield return null;
                frames=Frames(90);while(frames.MoveNext())yield return null;
                view.CaptureTestUi(Path.Combine(Evidence,"motion-preview-"+folder+".png"));
                if(folder=="local-smallbluefish-alpha2")
                {
                    var video=Path.Combine(Evidence,"preview-frames");Directory.CreateDirectory(video);double startTime=EditorApplication.timeSinceStartup;
                    var times=new List<double>();for(int f=0;f<50;f++){while(EditorApplication.timeSinceStartup<startTime+f*.1)yield return null;view.CaptureTestUi(Path.Combine(video,f.ToString("D4")+".png"));times.Add(EditorApplication.timeSinceStartup-startTime);}
                    File.WriteAllText(Path.Combine(Evidence,"preview-frame-times.json"),new JArray(times).ToString());
                    view.MotionWindow.ShowLibrary();frames=Frames(12);while(frames.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"motion-library.png"));
                }
                Check(!player.Preferences.enabled&&preview.Interaction.PlaneConfirmed,"preview-no-permission-or-placement-change-"+folder);
                view.MotionWindow.Close();frames=Frames(10);while(frames.MoveNext())yield return null;Check(clone!=null&&!clone.gameObject.activeInHierarchy,"preview-pauses-bounded-cache-"+folder);
            }
            view.Settings.Open(2);var wait=Frames(15);while(wait.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"settings-interaction.png"));view.Settings.Close();
            view.Settings.Open(2);wait=Frames(15);while(wait.MoveNext())yield return null;C0SettingsProbe.CaptureCombined(Path.Combine(Evidence,"settings-combined.png"),view,preview);view.Settings.Close();
            view.SetDialogueCollapsed(false);wait=Frames(15);while(wait.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"main-ui.png"));
            C0SettingsProbe.CaptureCombined(Path.Combine(Evidence,"main-combined.png"),view,preview);
            view.MotionWindow.Open();view.MotionWindow.Close();wait=Frames(15);while(wait.MoveNext())yield return null;Check(!view.MotionWindow.IsOpen,"close-during-async-load");
        }
        static void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(Evidence,"error.txt"),message+"\n"+trace);Finish(1);}}
        static void Finish(int code){SessionState.SetBool("XR.M1",false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;File.WriteAllText(Path.Combine(Evidence,"checks.json"),new JObject{{"checks",new JArray(checks)},{"technicalPassed",code==0},{"visualAccepted",false},{"questTested",false}}.ToString());Debug.Log(code==0?"XR_M1_PASSED":"XR_M1_FAILED");EditorApplication.Exit(code);}
    }
}
