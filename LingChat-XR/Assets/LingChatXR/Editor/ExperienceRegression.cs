using System;
using System.IO;
using System.Reflection;
using LingChatXR.Chat;
using LingChatXR.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LingChatXR.Speech;

namespace LingChatXR.Editor
{
    [InitializeOnLoad]
    public static class ExperienceRegression
    {
        const string Key = "LingChatXR.ExperienceRegression";
        static double started;
        static ExperienceRegression() { if(SessionState.GetBool(Key,false)) { started=EditorApplication.timeSinceStartup;EditorApplication.update+=Wait; } }
        public static void Run() { EditorSceneManager.OpenScene(PreviewProject.ScenePath);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode(); }
        public static void Capture() {SessionState.SetBool(Key+".Capture",true);Run();}
        static void Wait()
        {
            if(EditorApplication.timeSinceStartup-started>180) { Finish(1,"load timeout"); return; }
            if(!EditorApplication.isPlaying)return;
            var p=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();var c=UnityEngine.Object.FindAnyObjectByType<XrChatController>();
            if(p==null||!p.Ready||c==null||!c.Connected)return;
            EditorApplication.update-=Wait;
            try {
                if(!p.Avatar.IsAlpha2)throw new Exception("Original entry still loads old model");
                var q=(DialogueQueue)typeof(XrChatController).GetField("queue",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c);
                q.Begin("fixture","feiyu","turn",101);
                string before=c.Subtitle;
                c.Accept(new ChatEvent{sessionId="fixture",roleId="feiyu",turnId="turn",generation=101,kind="text",message="合成未完成时不能提前展示这句话。"});
                if(c.Subtitle!=before)throw new Exception("PREMATURE_TEXT: subtitle revealed before synthesized audio is ready");
                q.Cancel();
                if(!c.Effects.Ready)throw new Exception("Original LingChat effects missing");
                bool capture=SessionState.GetBool(Key+".Capture",false);SessionState.SetBool(Key+".Capture",false);
                p.gameObject.AddComponent<ExperienceDriver>().Begin(p,c,capture);
            } catch(Exception e){ Finish(1,e.Message); }
        }
        public static void Finish(int code,string reason) { SessionState.SetBool(Key,false);EditorApplication.update-=Wait;Debug.Log((code==0?"XR_EXPERIENCE_PASSED ":"XR_EXPERIENCE_FAILED ")+reason);EditorApplication.Exit(code); }
    }
    [DefaultExecutionOrder(200)]
    public sealed class ExperienceDriver : MonoBehaviour
    {
        FeiyuPreview preview;XrChatController chat;IEnumerator work;float deadline;
        int starts;bool startGate;string evidence;readonly List<string> samples=new List<string>();
        bool captureOnly;
        public void Begin(FeiyuPreview p,XrChatController c,bool capture=false)
        {
            captureOnly=capture;
            preview=p;chat=c;preview.ManualInput=true;deadline=Time.realtimeSinceStartup+240;
            evidence=Path.Combine(Environment.GetEnvironmentVariable("XR_EVIDENCE_DIR"),"../experience-repair");Directory.CreateDirectory(evidence);
            chat.Voice.PlaybackEvent+=Voice;work=Run();
        }
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Voice(VoiceKind kind,VoiceOutcome outcome,long token)
        {
            if(kind==VoiceKind.Dialogue&&outcome==VoiceOutcome.Started)
            {starts++;startGate=chat.IsTyping&&chat.Subtitle.Length>0&&chat.Subtitle.Length<chat.FullSubtitle.Length&&!chat.Thinking;}
        }
        void Update()
        {
            if(work==null)return;
            try{Check(Time.realtimeSinceStartup<deadline,"experience timeout");if(!work.MoveNext()){work=null;ExperienceRegression.Finish(0,captureOnly?"capture only, no live LLM":"original entry, gravity, audio gate, manual/auto/stop, live conversation");}}
            catch(Exception e){work=null;ExperienceRegression.Finish(1,e.ToString());}
        }
        Ray BodyRay() => new Ray(preview.ViewCamera.transform.position,(preview.Avatar.GrabProxy.bounds.center-preview.ViewCamera.transform.position).normalized);
        void Shot(string name)
        {
            // 先读取这一帧已经完成的 UI，再额外 Camera.Render；避免手动相机渲染再次清空 UI 目标。
            chat.GetComponent<XrChatView>().CaptureTestUi(Path.Combine(evidence,name+"-ui.png"));
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,name+"-scene.png"));
        }
        IEnumerator Run()
        {
            preview.Place(new Vector3(0,.74f,0));yield return null;
            Time.captureFramerate=60;
            preview.ViewCamera.transform.position=new Vector3(.5f,1.6f,2.5f);preview.ViewCamera.transform.LookAt(new Vector3(0,1.25f,0));
            var ray=BodyRay();preview.HandlePointer(ray,false,false,false,true,false,0);
            Check(preview.Interaction.State==PetState.Held,"original pointer grab failed");
            preview.HandlePointer(ray,false,false,false,false,false,20);
            float height=preview.Interaction.Position.Y;Check(height>1.7f,"mouse wheel did not raise by one metre");
            preview.HandlePointer(ray,false,false,false,false,true,0);
            Check(preview.Interaction.State==PetState.Descending&&preview.Interaction.Position.Y==height,"MOUSE_RELEASE_TELEPORTED");
            var frames=Path.Combine(evidence,"fall-frames");Directory.CreateDirectory(frames);
            float prev=height;int count=0;
            while(preview.Interaction.State==PetState.Descending)
            {
                samples.Add(preview.Interaction.Position.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                Check(preview.Interaction.Position.Y<=prev+.00001f&&preview.Interaction.Position.Y>=.74f,"fall penetrates support");prev=preview.Interaction.Position.Y;
                Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(frames,(count++).ToString("D4")+".png"));yield return null;
            }
            Check(count>=20&&count<=30,"one metre fall duration is not gravity: frames="+count);
            while(preview.Interaction.State!=PetState.Idle)yield return null;
            // 同一鼠标适配器再次提起并在下落中抓住。
            ray=BodyRay();preview.HandlePointer(ray,false,false,false,true,false,0);preview.HandlePointer(ray,false,false,false,false,false,12);
            preview.HandlePointer(ray,false,false,false,false,true,0);yield return null;yield return null;
            var before=preview.Interaction.Position;int old=preview.Interaction.Generation;
            preview.HandlePointer(BodyRay(),false,false,false,true,false,0);
            Check(preview.Interaction.State==PetState.Held&&preview.Interaction.Position.Equals(before),"regrab jumps or fails");
            Check(!preview.Interaction.StepDescent(.1f,old),"old fall survives regrab");
            ray=BodyRay();preview.HandlePointer(ray,false,false,false,false,true,0);
            while(preview.Interaction.State!=PetState.Idle)yield return null;
            Time.captureFramerate=0;
            var anchor=preview.Avatar.transform.position;
            preview.ViewCamera.transform.position=anchor+new Vector3(.14f,.23f,.8f);preview.ViewCamera.transform.LookAt(anchor+Vector3.up*.04f);preview.ViewCamera.fieldOfView=33;
            yield return null;Shot("new-model");
            // 真实本地合成：等待时不出字，开始播放才打字；手动补全不能跳句。
            chat.AutoAdvance=false;int effects=chat.Effects.ThinkingStarts;
            chat.TestVoice();Check(chat.Thinking&&chat.Subtitle=="","thinking did not clear previous line");
            float bubbleTime=Time.realtimeSinceStartup+.25f;while(Time.realtimeSinceStartup<bubbleTime)yield return null;Shot("thinking");
            Check(chat.Effects.ThinkingStarts==effects+1,"thinking effect did not start once");
            while(!chat.Voice.IsPlaying){Check(chat.Subtitle=="","text leaked during synthesis/download");yield return null;}
            Check(startGate,"AudioSource Started did not start partial typewriter");
            yield return null;yield return null;Shot("typing");
            if(captureOnly){chat.Stop();while(chat.Stopping)yield return null;chat.Voice.PlaybackEvent-=Voice;yield break;}
            chat.ContinueDialogue();Check(!chat.IsTyping&&chat.Subtitle==chat.FullSubtitle&&chat.Voice.IsPlaying,"first click must finish text without skipping audio");
            while(chat.Voice.IsPlaying)yield return null;
            for(int i=0;i<10;i++)yield return null;
            Check(chat.Busy,"manual mode advanced without user");chat.ContinueDialogue();
            while(chat.Busy)yield return null;
            // 合成途中停止，迟到回复不可复活。
            chat.TestVoice();chat.Stop();while(chat.Stopping)yield return null;
            for(int i=0;i<20;i++){Check(!chat.Voice.IsPlaying&&!chat.Thinking,"late output revived after stop");yield return null;}
            chat.AutoAdvance=true;int previousStarts=starts;chat.TestVoice();
            while(chat.Busy)yield return null;Check(starts>previousStarts&&startGate,"replay after stop failed");
            foreach(string prompt in new[]{"你好，今天想和你聊聊，请用两句中文分别问候和介绍自己。","你刚才是谁？请用一句话回答。"})
            {
                previousStarts=starts;chat.Send(prompt);while(chat.Busy)yield return null;
                Check(starts>previousStarts&&!chat.CanRetry,"live LLM voice failed: "+chat.Status+" starts="+starts+" previous="+previousStarts);
            }
            Shot("complete");
            File.WriteAllText(Path.Combine(evidence,"runtime.json"),"{\"result\":\"passed\",\"scene\":\"FeiyuPreview\",\"alpha2\":true,\"pointerReleaseContinuous\":true,\"gravityFallFrames60Hz\":"+count+",\"regrabContinuous\":true,\"audioBeforeTypewriter\":true,\"thinkingEffect\":true,\"firstClickOnlyFinishesText\":true,\"manualWait\":true,\"stopLateOutputRejected\":true,\"realLlmRounds\":2,\"voiceStarts\":"+starts+",\"questTested\":false,\"userAccepted\":false,\"fallY\":["+string.Join(",",samples)+"]}");
            chat.Voice.PlaybackEvent-=Voice;
        }
    }
}
