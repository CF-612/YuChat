using System;
using System.IO;
using LingChatXR.Core;
using LingChatXR.Chat;
using LingChatXR.Speech;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LingChatXR.Editor
{
    [InitializeOnLoad]
    public static class Alpha2InteractionCheck
    {
        const string Key="LingChatXR.Alpha2Interaction";static double started;
        static Alpha2InteractionCheck(){if(SessionState.GetBool(Key,false)){started=EditorApplication.timeSinceStartup;EditorApplication.update+=Wait;}}
        public static void Run(){Alpha2Project.CreateScene();EditorSceneManager.OpenScene(Alpha2Project.ScenePath);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void Wait()
        {
            if(EditorApplication.timeSinceStartup-started>240){Finish(1);return;}
            if(!EditorApplication.isPlaying)return;
            var p=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();var c=UnityEngine.Object.FindAnyObjectByType<XrChatController>();
            if(p==null||!p.Ready||c==null||!c.Connected)return;
            EditorApplication.update-=Wait;p.gameObject.AddComponent<Alpha2InteractionDriver>().Initialize(p,c);
        }
        public static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Wait;Debug.Log(code==0?"XR_ALPHA2_INTERACTION_PASSED":"XR_ALPHA2_INTERACTION_FAILED");EditorApplication.Exit(code);}
    }
    [DefaultExecutionOrder(200)]
    public sealed class Alpha2InteractionDriver : MonoBehaviour
    {
        FeiyuPreview preview;XrChatController chat;FeiyuAvatar avatar;int frame,grab,oldTrajectory;Vector3 heldStart;
        string evidence;bool ready,voiceStarted,voiceCompleted,voiceOverlap;float maxMouth;int videoFrame;double started;
        readonly ExpressionKey[] vowels={ExpressionKey.Aa,ExpressionKey.Ih,ExpressionKey.Ou,ExpressionKey.Ee,ExpressionKey.Oh};
        public void Initialize(FeiyuPreview p,XrChatController c)
        {
            preview=p;chat=c;avatar=p.Avatar;preview.ManualInput=true;Time.captureFramerate=60;
            evidence=Environment.GetEnvironmentVariable("XR_EVIDENCE_DIR");Directory.CreateDirectory(Path.Combine(evidence,"landing"));
            preview.Place(new Vector3(0,.74f,0));chat.Voice.PlaybackEvent+=Voice;
            preview.ViewCamera.transform.position=new Vector3(.28f,1.22f,.95f);preview.ViewCamera.transform.LookAt(new Vector3(0,1.05f,0));preview.ViewCamera.fieldOfView=45;
            started=EditorApplication.timeSinceStartup;ready=true;
        }
        void Voice(VoiceKind kind,VoiceOutcome outcome,long generation)
        {
            if(kind!=VoiceKind.Dialogue)return;if(outcome==VoiceOutcome.Started)voiceStarted=true;
            if(outcome==VoiceOutcome.Completed)voiceCompleted=true;
            if(outcome==VoiceOutcome.Cancelled||outcome==VoiceOutcome.Failed){Debug.LogWarning("Voice interrupted during spatial interaction");Alpha2InteractionCheck.Finish(1);}
        }
        void Require(bool ok,string why){if(!ok)throw new Exception(why);}
        void Hold(Vector3 target){Require(preview.Interaction.MoveGrab("integration",grab,FeiyuPreview.ToCore(target)),"active grab rejected");preview.RefreshSpatialState();}
        void Begin(){Require(preview.Interaction.BeginGrab("integration",out grab),"begin grab");heldStart=FeiyuPreview.ToUnity(preview.Interaction.Position);}
        void Release(float x)
        {
            float height=preview.Interaction.Position.Y;Require(preview.Interaction.ReleaseGrabWithDescent("integration",grab,new Vec3(x,.74f,0)),"release");
            Require(preview.Interaction.State==PetState.Descending&&preview.Interaction.Position.Y==height,"release teleported/early idle");oldTrajectory=preview.Interaction.Generation;
        }
        void Update()
        {
            if(!ready)return;
            try
            {
                if(EditorApplication.timeSinceStartup-started>180)throw new Exception("Real audio integration timeout");
                if(frame==540&&!voiceCompleted)return;
                if(frame==30)Begin();
                if(frame>=30&&frame<90)Hold(new Vector3(0,.74f+.12f*Mathf.SmoothStep(0,1,(frame-30)/59f),0));
                if(frame==90)Release(0);
                if(frame==175)Require(preview.Interaction.State==PetState.Idle,"low descent never settled");
                if(frame==180)Begin();
                if(frame>=180&&frame<240)Hold(new Vector3(0,.74f+.4f*Mathf.SmoothStep(0,1,(frame-180)/59f),0));
                if(frame==200)chat.TestVoice();
                if(frame==240)Release(0);
                if(frame==261){Vector3 before=FeiyuPreview.ToUnity(preview.Interaction.Position);Begin();Require(before==FeiyuPreview.ToUnity(preview.Interaction.Position),"regrab jumps");Require(!preview.Interaction.StepDescent(.1f,oldTrajectory),"old descent survives regrab");}
                if(frame>=261&&frame<320)Hold(Vector3.Lerp(heldStart,new Vector3(.12f,1.06f,0),Mathf.SmoothStep(0,1,(frame-261)/58f)));
                if(frame==320)Release(.12f);
                if(frame==420){Require(preview.Interaction.State==PetState.Idle,"high descent never settled");preview.Interaction.BeginTouch("integration-touch");}
                if(frame==450)preview.Interaction.EndTouch("integration-touch");
                if(frame==470){Begin();Hold(new Vector3(.65f,1.0f,0));preview.Interaction.ReleaseGrabWithDescent("integration",grab,new Vec3(.65f,.74f,0));Require(!preview.Interaction.LastReleaseWasValid,"invalid drop accepted");preview.RefreshSpatialState();avatar.ResetSecondaryMotion();}
                Require(preview.Interaction.Position.Y>=.73999f,"feet root penetrated table");
                if(frame==540)
                {
                    avatar.FreezePresentation=true;avatar.Instance.Runtime.Expression.SetWeight(ExpressionKey.Happy,0);avatar.Instance.Runtime.Expression.SetWeight(ExpressionKey.Blink,0);
                    preview.ViewCamera.transform.position=new Vector3(.12f,.98f,.45f);preview.ViewCamera.transform.LookAt(new Vector3(.12f,.9f,0));preview.ViewCamera.fieldOfView=33;
                }
                if(frame>=550&&frame<=650&&frame%20==10)
                {
                    foreach(var key in vowels)avatar.Instance.Runtime.Expression.SetWeight(key,0);
                    int v=(frame-550)/20;if(v<5)avatar.Instance.Runtime.Expression.SetWeight(vowels[v],.85f);else avatar.Instance.Runtime.Expression.SetWeight(ExpressionKey.Blink,1);
                }
                if(frame==665)
                {
                    avatar.Instance.Runtime.Expression.SetWeight(ExpressionKey.Blink,0);
                    avatar.Instance.TryGetBoneTransform(HumanBodyBones.Head,out var head);
                    avatar.Instance.LookAtTarget.position=head.position+new Vector3(-.8f,0,1);
                }
            }
            catch(Exception e){Debug.LogWarning(e);ready=false;Alpha2InteractionCheck.Finish(1);}
        }
        void LateUpdate()
        {
            if(!ready)return;
            try
            {
                maxMouth=Mathf.Max(maxMouth,frame<=540?avatar.Instance.Runtime.Expression.GetWeight(ExpressionKey.Aa):0);
                if(chat.Voice.IsPlaying&&(preview.Interaction.State==PetState.Held||preview.Interaction.State==PetState.Descending||preview.Interaction.State==PetState.Touched))voiceOverlap=true;
                // captureFramerate 的模拟秒数不等于真实 AudioSource 播放时钟。
                if(frame==540&&!voiceCompleted)return;
                if(frame<540&&frame%2==0)Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"landing",(videoFrame++).ToString("D4")+".png"));
                if(frame==545)Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"alpha2-close.png"));
                if(frame>=555&&frame<=655&&frame%20==15)Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"expression-"+((frame-555)/20)+".png"));
                if(frame==675){Require(Mathf.Abs(avatar.Instance.Runtime.LookAt.Yaw)>5,"expression look-at did not turn");Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"alpha2-look-left.png"));}
                if(frame++>=680)
                {
                    Require(voiceStarted&&voiceCompleted&&voiceOverlap,"real voice did not overlap and complete through interaction");Require(maxMouth>.01f,"audio mouth subscriber did not respond");
                    File.WriteAllText(Path.Combine(evidence,"interaction.json"),"{\"result\":\"passed\",\"lowHighDescent\":true,\"regrabCancelsOldTrajectory\":true,\"invalidDropRecovery\":true,\"voiceCompletedDuringInteraction\":true,\"maxAudioMouthWeight\":"+maxMouth.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"fiveVowelAndBlinkSnapshots\":true,\"userAccepted\":false,\"questTested\":false}");
                    ready=false;Time.captureFramerate=0;chat.Voice.PlaybackEvent-=Voice;Alpha2InteractionCheck.Finish(0);
                }
            }
            catch(Exception e){Debug.LogWarning(e);ready=false;Time.captureFramerate=0;Alpha2InteractionCheck.Finish(1);}
        }
    }
}
