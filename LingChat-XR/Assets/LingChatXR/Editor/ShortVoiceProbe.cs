using System;
using System.Collections;
using System.IO;
using System.Reflection;
using LingChatXR.Chat;
using LingChatXR.Speech;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace LingChatXR.Editor
{
    public static class ShortVoiceProbe
    {
        static IEnumerator work;
        static double deadline;
        static readonly JArray checks = new JArray();
        static string Output => Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            ShortVoiceAssets.Prepare();
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException();
                if (!EditorApplication.isPlaying) return;
                work ??= Verify();
                if (!work.MoveNext()) Finish(0);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString()); Finish(1); }
        }
        static IEnumerator Verify()
        {
            var live = UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            var chat = UnityEngine.Object.FindAnyObjectByType<XrChatController>();
            live.ManualInput = true;
            while (!live.Ready) yield return null;
            var avatar = live.Avatar;
            live.Place(Vector3.zero);
            avatar.Motions.EndWelcome();
            var ready = avatar.Motions.PrepareGrabbed();
            while (!ready.IsCompleted) yield return null;
            Check(ready.Result && avatar.TailGrabProxy != null,"尾巴抓点与动作就绪");
            live.HandleViewInput(new Vector2(520,0),0,false);
            Check(live.InspectingFromDesktop,"中键环绕进入查看模式");
            var before = live.SpatialRotation;
            var attention = typeof(PetLocomotion).GetMethod("StepAttention", BindingFlags.Instance|BindingFlags.NonPublic);
            for(int i=0;i<80;i++) attention.Invoke(live.Locomotion,new object[]{.05f});
            Check(!live.Locomotion.Active && Quaternion.Angle(before,live.SpatialRotation)<.01f,"查看背面期间不自动转身");
            var clips = chat.ShortVoice;
            var voice = chat.Voice;
            Check(clips.Bank != null && clips.Bank.entries.Length == 8,"八条本地临时短声已加载");
            clips.SetMode(CharacterVoiceMode.Clips);
            Check(clips.Play("happy","happy-test",1),"开心短声开始 "+clips.Status+" "+voice.LastStatus+" "+clips.Bank.entries[0].clip.loadState);
            long first = voice.Generation;
            Check(clips.Play("surprised","shock-test",20),"高优先级惊讶打断开心");
            long shocked = voice.Generation;
            Check(shocked > first && voice.CurrentKind==VoiceKind.Reaction,"单一发声代次接管");
            Check(!clips.Play("surprised","shock-test",20) && voice.Generation==shocked,"重复事件不会停止自身");
            Check(!clips.Play("happy","low-test",1),"低优先级不会打断惊讶");
            Check(!clips.Play("protest","expired-test",25,true),"已放下的持有反应被拒绝");
            Check(!clips.Play("happy","stale-test",30,false,Time.unscaledTime-2),"过期请求被拒绝");
            clips.StopVoice();
            int starts=0;
            voice.PlaybackEvent+=(kind,outcome,gen)=>{if(kind==VoiceKind.Reaction && outcome==VoiceOutcome.Started)starts++;};
            var tail = avatar.TailGrabProxy.transform.position;
            var ray = new Ray(tail+Vector3.back*.3f,Vector3.forward);
            live.HandlePointer(ray,false,false,false,true,false,0);
            Check(live.Interaction.State==Core.PetState.Held && avatar.Motions.TailGrabbed,"电脑尾巴射线走真实抓取入口");
            avatar.SetSpatialFeedback(live.Interaction,.016f);
            for(int i=0;i<25;i++) { avatar.SetSpatialFeedback(live.Interaction,.016f); yield return null; }
            Check(starts==1,"持续抓住只发一次短声");
            clips.SetMode(CharacterVoiceMode.Muted);
            Check(!voice.IsPlaying && live.Interaction.State==Core.PetState.Held,"静音不解除持有");
            Check(!clips.Play("happy","muted-test",50),"静音拒绝新的角色声");
            clips.SetMode(CharacterVoiceMode.Clips);
            live.Interaction.ReleaseGrabWithDescent("editor-mouse",live.Interaction.Generation,live.Interaction.LastSafePosition);
            avatar.SetSpatialFeedback(live.Interaction,.016f);
            Check(!clips.Play("protest","late-held",25,true),"放下后拒绝迟到抗议");
            live.Interaction.StepDescent(1,live.Interaction.Generation);
            live.HandleViewInput(Vector2.zero,0,true);
            Check(!live.InspectingFromDesktop,"F 重置退出查看模式");
        }
        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject {
                ["passed"]=code==0,["checks"]=checks,["questVerified"]=false
            }.ToString());
            Debug.Log(code==0?"XR_M1_PASSED":"XR_SHORT_VOICE_FAILED");
            EditorApplication.Exit(code);
        }
    }
}
