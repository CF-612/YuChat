using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Chat;
using LingChatXR.Core;
using LingChatXR.Speech;
using UniVRM10;
using UniVRM10.FastSpringBones;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 产品原场景、原连接入口、鼠标射线适配器；仅自动输入和取证，不接管求解器。
    public static class R1DefaultFlowProbe
    {
        const string Pending="LingChatXR.R1";
        static readonly string[] Folders={"local-physics-reference","local-smallbluefish-alpha2"};
        [Serializable] class VoiceReport{public bool coldStart,connected,sourcePlayed,textGated,retryConnected;public float maxAmplitude;public List<string> statuses=new List<string>();public string failureStatus;}
        [Serializable] class Clip{public string sha,folder;public int frames;public bool defaultPresentation;public float lateMotionMm;public List<string> phases=new List<string>();}
        [Serializable] class Report{public VoiceReport voice=new VoiceReport();public List<Clip> clips=new List<Clip>();public List<string> transitions=new List<string>();public List<string> centerSampling=new List<string>();public bool naturalnessAccepted=false,questTested=false;}
        static Report report=new Report();static IEnumerator work;static double deadline;static string evidence;static FeiyuPreview preview;static XrChatController chat;static Camera fixedCamera,nearCamera;
        static Clip clip;static int capturedFrames;static string clipDirectory;static Vector3 pointerOffset;
        static bool voiceStarted;static float[] samples=new float[512];static string lastStatus;
        static void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
        public static void Run()
        {
            evidence=Environment.GetEnvironmentVariable("XR_R1_EVIDENCE");Directory.CreateDirectory(evidence);
            report.voice.coldStart=Environment.GetEnvironmentVariable("XR_R1_COLD")=="1";
            Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XR_HOST_CONNECTION")),"Connection override would bypass normal startup");
            LilToonTemplates.Ensure();
            AssetDatabase.ImportAsset("Assets/LingChatXR/Runtime/FeiyuPreview.cs",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            var script=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/LingChatXR/Runtime/FeiyuPreview.cs");
            File.WriteAllText(Path.Combine(evidence,"scene-script.txt"),"guid="+AssetDatabase.AssetPathToGUID("Assets/LingChatXR/Runtime/FeiyuPreview.cs")+"\nclass="+script?.GetClass()+"\neditPreview="+(UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>()!=null));
            if(script?.GetClass()==null){File.WriteAllText(Path.Combine(evidence,"failure.txt"),"Fresh MonoScript import still has no class; compiled type="+typeof(FeiyuPreview).AssemblyQualifiedName);Debug.Log("XR_R1_TECHNICAL_CHECKS_FAILED");EditorApplication.Exit(1);return;}
            Unity.Collections.NativeLeakDetection.Mode=Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SessionState.SetBool(Pending,true);deadline=EditorApplication.timeSinceStartup+600;
            EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.EnterPlaymode();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ConfigureBeforeStart()
        {
            if(!SessionState.GetBool(Pending,false))return;
            var p=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            p.ConfigureCharacter(Folders[0],true);p.ConfigurePreferenceStorage(Path.Combine(evidence,"preferences"));p.ManualInput=true;
        }
        static void Tick()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup<deadline,"R1 timeout: "+(chat==null?"no controller":chat.Status));
                if(!EditorApplication.isPlaying)return;
                if(chat==null){chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();if(chat!=null)chat.Voice.PlaybackEvent+=OnVoice;}
                if(chat!=null&&chat.Status!=lastStatus){lastStatus=chat.Status;report.voice.statuses.Add(lastStatus);Save();}
                if(work==null)work=Check();if(!work.MoveNext())Finish(0);
            }catch(Exception e){File.WriteAllText(Path.Combine(evidence,"failure.txt"),e.ToString());Finish(1);}
        }
        static IEnumerator Frames(int n,Action<int> input=null,bool capture=false)
        {
            for(int i=0;i<n;i++)
            {
                input?.Invoke(i);int last=Time.frameCount;while(Time.frameCount==last)yield return null;
                if(capture)Capture();
            }
        }
        static void OnVoice(VoiceKind kind,VoiceOutcome outcome,long token)
        {
            if(kind!=VoiceKind.Dialogue||outcome!=VoiceOutcome.Started)return;
            voiceStarted=true;report.voice.sourcePlayed=chat.Voice.IsPlaying;
            report.voice.textGated=chat.IsTyping&&!chat.Thinking&&chat.Subtitle.Length>0&&chat.Subtitle.Length<chat.FullSubtitle.Length;
            chat.GetComponent<XrChatView>().CaptureTestUi(Path.Combine(evidence,"tts-start-ui.png"));
        }
        static IEnumerator Check()
        {
            while(preview==null){preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();yield return null;}
            while(!preview.Ready||chat==null||!chat.Connected)yield return null;
            Require(preview.ActivePackageFolder==Folders[0],"Initial package did not follow selected entry");
            bool directed=Environment.GetEnvironmentVariable("XR_R1_MODE")=="Directed";
            var wait=Frames(0);
            if(!directed)
            {
            report.voice.connected=true;chat.AutoAdvance=true;chat.TestVoice();
            while(chat.Busy)
            {
                if(chat.Voice.IsPlaying)report.voice.maxAmplitude=Mathf.Max(report.voice.maxAmplitude,chat.Voice.SampleAmplitude(samples));
                yield return null;
            }
            Require(voiceStarted&&report.voice.sourcePlayed&&report.voice.textGated&&report.voice.maxAmplitude>.0001f&&!chat.CanRetry,"Local playback/gate failed: "+chat.Status);
            chat.GetComponent<XrChatView>().CaptureTestUi(Path.Combine(evidence,"tts-finished-ui.png"));Save();
            // 无效连接配置模拟一次可重试失败；不停止真实宿主、不发云端聊天。
            string invalid=Path.Combine(evidence,"invalid-local-connection.json");File.WriteAllText(invalid,"{\"endpoint\":\"invalid\",\"token\":\"\"}");
            Environment.SetEnvironmentVariable("XR_HOST_CONNECTION",invalid);chat.Connect();
            wait=Frames(3);while(wait.MoveNext())yield return null;
            Require(!chat.Connected,"Invalid config was not rejected");report.voice.failureStatus=chat.Status;
            Require(chat.Status.Contains("配置无效"),"Failure reason was hidden: "+chat.Status);
            chat.GetComponent<XrChatView>().CaptureTestUi(Path.Combine(evidence,"tts-failure-ui.png"));
            Environment.SetEnvironmentVariable("XR_HOST_CONNECTION",null);chat.Connect();while(!chat.Connected)yield return null;
            report.voice.retryConnected=true;Save();
            if(Environment.GetEnvironmentVariable("XR_R1_MODE")=="VoiceRetry")yield break;
            }
            fixedCamera=preview.ViewCamera;fixedCamera.transform.position=new Vector3(.65f,1.55f,2.45f);fixedCamera.transform.LookAt(new Vector3(0,1.24f,0));fixedCamera.fieldOfView=38;
            nearCamera=new GameObject("R1 detail camera").AddComponent<Camera>();nearCamera.CopyFrom(fixedCamera);nearCamera.enabled=false;nearCamera.nearClipPlane=.02f;
            Time.captureFramerate=60;
            foreach(string folder in directed?Array.Empty<string>():Folders)
            {
                if(preview.ActivePackageFolder!=folder){var task=preview.SwitchCharacterAsync(folder);while(!task.IsCompleted)yield return null;Require(task.Result,"Default switch failed");}
                Require(!preview.Avatar.FreezePresentation&&Mathf.Abs(preview.ConfiguredHeight-.24f)<1e-5f,"Presentation or common defaults overridden");
                clip=new Clip{folder=folder,sha=preview.Avatar.Package.model.sha256,defaultPresentation=true};report.clips.Add(clip);
                clipDirectory=Path.Combine(evidence,"clip-"+(report.clips.Count-1));Directory.CreateDirectory(Path.Combine(clipDirectory,"world"));Directory.CreateDirectory(Path.Combine(clipDirectory,"near"));capturedFrames=0;
                Phase("loaded-unplaced");wait=Frames(30,null,true);while(wait.MoveNext())yield return null;
                Require(preview.Place(new Vector3(0,.74f,0)),"Place failed");Phase("placed");wait=Frames(60,null,true);while(wait.MoveNext())yield return null;
                Grab();Phase("lift-low");wait=Frames(30,i=>Move(new Vector3(0,.74f+.15f*(i+1)/30,0)),true);while(wait.MoveNext())yield return null;
                Phase("slow-left-right");wait=Frames(180,i=>Move(new Vector3(.12f*Mathf.Sin(2*Mathf.PI*(i+1)/180),.89f,0)),true);while(wait.MoveNext())yield return null;
                Phase("fast-left-right");wait=Frames(60,i=>Move(new Vector3(.12f*Mathf.Sin(6*Mathf.PI*(i+1)/60),.89f,0)),true);while(wait.MoveNext())yield return null;
                Phase("abrupt-stop");Move(new Vector3(0,.89f,0));wait=Frames(90,null,true);while(wait.MoveNext())yield return null;
                Phase("low-release");Release();wait=Frames(60,null,true);while(wait.MoveNext())yield return null;Require(preview.Interaction.State==PetState.Idle,"Low release did not settle");
                Grab();Phase("lift-one-metre");wait=Frames(60,i=>Move(new Vector3(0,.74f+(i+1)/60f,0)),true);while(wait.MoveNext())yield return null;
                wait=Frames(30,null,true);while(wait.MoveNext())yield return null;Phase("one-metre-descent");Release();
                wait=Frames(150,null,true);while(wait.MoveNext())yield return null;
                Require(preview.Interaction.State==PetState.Idle&&Mathf.Abs(preview.Avatar.transform.position.y-.74f)<.001f,"High release did not settle");
                var joints=preview.Avatar.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>();
                var rest=joints.Select(j=>preview.Avatar.transform.InverseTransformPoint(j.transform.position)).ToArray();
                Phase("late-rest");wait=Frames(30,i=>{for(int j=0;j<joints.Length;j++)clip.lateMotionMm=Mathf.Max(clip.lateMotionMm,1000*Vector3.Distance(rest[j],preview.Avatar.transform.InverseTransformPoint(joints[j].transform.position)));},true);while(wait.MoveNext())yield return null;
                clip.frames=capturedFrames;Save();
            }
            clip=null;
            if(directed){Require(preview.Place(new Vector3(0,.74f,0)),"Directed initial place failed");wait=Frames(20);while(wait.MoveNext())yield return null;}
            foreach(string trigger in new[]{"held","descending"})
            {
                Grab();Move(new Vector3(.1f,1.34f,0));wait=Frames(2);while(wait.MoveNext())yield return null;
                if(trigger=="descending"){Release();wait=Frames(4);while(wait.MoveNext())yield return null;Require(preview.Interaction.State==PetState.Descending,"Not descending before switch");}
                var old=preview.Avatar;var oldPhysics=old.Physics;var state=preview.Interaction;int generation=state.Generation;
                string target=preview.ActivePackageFolder==Folders[0]?Folders[1]:Folders[0];var task=preview.SwitchCharacterAsync(target);
                Require(!state.MoveGrab("editor-mouse",generation,new Vec3(2,3,0))&&!state.StepDescent(.1f,generation),"Old input survived switch");
                while(!task.IsCompleted)yield return null;Require(task.Result,"Directed switch failed");
                Require(!ReferenceEquals(oldPhysics,preview.Avatar.Physics),"Old physics instance reused");CheckCenterSample(trigger+"-loaded");
                wait=Frames(3);while(wait.MoveNext())yield return null;CheckCenterSample(trigger+"-settled");
                Require(old==null&&preview.Avatar.Physics.MotionAirForce==Vector3.zero&&preview.Interaction.State==PetState.Unplaced,"Stale model/force/placement");
                Require(FastSpringBoneService.Instance.BufferCombiner.Combined.Models.Length==1,"Stale native model");
                preview.Place(new Vector3(0,.74f,0));wait=Frames(20);while(wait.MoveNext())yield return null;
                CheckCenterSample(trigger+"-placed",true);
                Grab();Move(new Vector3(0,.89f,0));Release();wait=Frames(60);while(wait.MoveNext())yield return null;
                Require(preview.Interaction.State==PetState.Idle&&preview.Avatar.Physics.MotionAirForce==Vector3.zero,"Recovery grab failed");report.transitions.Add(trigger+"-switch-regrab-release");Save();
            }
            var cancel=preview.SwitchCharacterAsync(Folders[1]);while(preview.LoadingPhase!=FeiyuPreview.LoadPhase.Importing&&!cancel.IsCompleted)yield return null;
            Require(!cancel.IsCompleted,"No import cancellation window");preview.CancelCharacterLoad();while(!cancel.IsCompleted)yield return null;Require(!cancel.Result&&!preview.Ready,"Cancelled load published");
            wait=Frames(3);while(wait.MoveNext())yield return null;Require(FastSpringBoneService.Instance.BufferCombiner.Combined.Models.Length==0,"Cancelled native model survived");
            var recover=preview.SwitchCharacterAsync(Folders[1]);while(!recover.IsCompleted)yield return null;Require(recover.Result,"Reload after cancel failed");
            CheckCenterSample("cancel-reload");
            preview.Place(new Vector3(0,.74f,0));wait=Frames(20);while(wait.MoveNext())yield return null;CheckCenterSample("cancel-placed",true);Grab();Move(new Vector3(0,.94f,0));Release();wait=Frames(60);while(wait.MoveNext())yield return null;
            Require(preview.Interaction.State==PetState.Idle&&preview.Avatar.Physics.MotionAirForce==Vector3.zero,"Post-cancel grab failed");report.transitions.Add("import-cancel-reload-regrab-release");Save();
            Time.captureFramerate=0;
        }
        static Ray BodyRay()=>new Ray(fixedCamera.transform.position,(preview.Avatar.GrabProxy.bounds.center-fixedCamera.transform.position).normalized);
        static void CheckCenterSample(string phase,bool requireSample=false)
        {
            // 只读检查新实例的采样历史，不为通过测试修改运行状态。
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var physics=preview.Avatar.Physics;var type=typeof(PortableSpringPhysics);
            bool has=(bool)type.GetField("hasRootPosition",flags).GetValue(physics);
            var previous=(Vector3)type.GetField("previousRootPosition",flags).GetValue(physics);
            Require(!requireSample||has,"Missing active center sample: "+phase);
            Require(!has||Vector3.Distance(previous,physics.transform.position)<.000001f,"Inherited center displacement: "+phase);
            report.centerSampling.Add(phase+(has?": current root":": no prior sample"));Save();
        }
        static void Grab()
        {
            var ray=BodyRay();var plane=new Plane(Vector3.up,new Vector3(0,.74f,0));Require(plane.Raycast(ray,out float distance),"Grab ray missed plane");
            pointerOffset=ray.GetPoint(distance)-preview.Avatar.transform.position;pointerOffset.y=0;
            preview.HandlePointer(ray,false,false,false,true,false,0);Require(preview.Interaction.State==PetState.Held,"Product pointer grab failed");
        }
        static Ray TargetRay(Vector3 position){Vector3 target=new Vector3(position.x,.74f,position.z)+pointerOffset;return new Ray(fixedCamera.transform.position,(target-fixedCamera.transform.position).normalized);}
        static void Move(Vector3 position)=>preview.HandlePointer(TargetRay(position),false,false,false,false,false,(position.y-preview.Interaction.Position.Y)/.05f);
        static void Release()=>preview.HandlePointer(TargetRay(FeiyuPreview.ToUnity(preview.Interaction.Position)),false,false,false,false,true,0);
        static void Phase(string name){clip.phases.Add(name+" @"+capturedFrames);}
        static void Capture()
        {
            int n=capturedFrames++;if(n%2!=0)return;
            if(preview.Ready&&preview.Avatar.gameObject.activeSelf){foreach(var j in preview.Avatar.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>())Require(float.IsFinite(j.transform.position.sqrMagnitude),"Nonfinite default motion");}
            Alpha2Probe.Capture(fixedCamera,Path.Combine(clipDirectory,"world",(n/2).ToString("D4")+".png"));
            var origin=preview.Avatar.transform.position;nearCamera.transform.position=origin+new Vector3(.24f,.16f,.52f);nearCamera.transform.LookAt(origin+Vector3.up*.12f);
            Alpha2Probe.Capture(nearCamera,Path.Combine(clipDirectory,"near",(n/2).ToString("D4")+".png"));
        }
        static void Save()=>File.WriteAllText(Path.Combine(evidence,"default-flow.json"),JsonUtility.ToJson(report,true));
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;if(chat!=null)chat.Voice.PlaybackEvent-=OnVoice;Save();Debug.Log(code==0?"XR_R1_TECHNICAL_CHECKS_PASSED":"XR_R1_TECHNICAL_CHECKS_FAILED");EditorApplication.Exit(code);}
    }
}
