using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using LingChatXR.Chat;
using LingChatXR.Speech;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 在真实 Preview 自动更新链中对比“只有作者物理”和“相对运动外力”。
    public static class MotionAirProbe
    {
        [Serializable] class Chain {public string name;public float maxAngle,settledAngle,maxOffsetMm;}
        [Serializable] class Row {public string sha;public bool air,worldMotion,voicePlayed,voiceGated;public int fps,descentFrames;public float maxForce;public List<Chain> chains=new List<Chain>();}
        [Serializable] class Report {public List<Row> runs=new List<Row>();public bool visualAccepted=false;}
        static FeiyuPreview preview;static XrChatController chat;static Camera camera;static IEnumerator run;static double deadline;static string evidence;static Report report=new Report();
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        public static void Run()
        {
            LilToonTemplates.Ensure();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            evidence=Environment.GetEnvironmentVariable("XR_NATIVE_EVIDENCE");Directory.CreateDirectory(evidence);
            Unity.Collections.NativeLeakDetection.Mode=Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+480;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.EnterPlaymode();
        }
        static void Tick(){try{Require(EditorApplication.timeSinceStartup<deadline,"Motion/TTS timeout");if(!EditorApplication.isPlaying)return;if(run==null)run=Check();if(!run.MoveNext())Finish(0);}catch(Exception e){File.WriteAllText(Path.Combine(evidence,"failure.txt"),e.ToString());Finish(1);}}
        static IEnumerator Frames(int n){int last=Time.frameCount;for(int i=0;i<n;i++){while(last==Time.frameCount)yield return null;last=Time.frameCount;}}
        static IEnumerator Check()
        {
            camera=new GameObject("Camera").AddComponent<Camera>();camera.gameObject.AddComponent<AudioListener>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.14f,.19f);camera.fieldOfView=38;camera.nearClipPlane=.02f;
            var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,20,0);
            bool centerComparison=Environment.GetEnvironmentVariable("XR_CENTER_MOTION_TEST")=="1";
            if(!centerComparison)chat=new GameObject("XR Dialogue").AddComponent<XrChatController>();
            var folders=Environment.GetEnvironmentVariable("XR_SWITCH_FOLDERS").Split('|');
            preview=new GameObject("Preview").AddComponent<FeiyuPreview>();preview.SetCamera(camera);preview.ConfigureCharacter(folders[0],true);preview.ConfigurePreferenceStorage(Path.Combine(evidence,"preferences"));preview.ManualInput=true;
            while(!preview.Ready)yield return null;
            int asset=0;
            foreach(string folder in folders)
            {
                if(preview.ActivePackageFolder!=folder){var t=preview.SwitchCharacterAsync(folder);while(!t.IsCompleted)yield return null;Require(t.Result,"Load failed");}
                bool played=false,gated=false;
                if(chat!=null)
                {
                while(!chat.Connected||chat.Stopping)yield return null;
                chat.AutoAdvance=true;
                Action<VoiceKind,VoiceOutcome,long> onVoice=(kind,outcome,g)=>{if(kind==VoiceKind.Dialogue&&outcome==VoiceOutcome.Started){played=true;gated=chat.IsTyping&&!chat.Thinking&&chat.Subtitle.Length<chat.FullSubtitle.Length;}};
                chat.Voice.PlaybackEvent+=onVoice;chat.TestVoice();
                while(chat.Busy)yield return null;chat.Voice.PlaybackEvent-=onVoice;Require(played&&gated&&!chat.CanRetry,"Real TTS or text gate failed: "+chat.Status);
                }
                foreach(int fps in new[]{30,60,90})foreach(bool air in new[]{false,true})
                {
                    Time.captureFramerate=fps;var a=preview.Avatar;a.FreezePresentation=true;a.Physics.MotionAirEnabled=centerComparison||air;a.Physics.WorldMotionEnabled=centerComparison?air:true;
                    preview.Place(new Vector3(0,.74f,0));a.ResetSecondaryMotion();
                    var wait=Frames(fps);while(wait.MoveNext())yield return null;
                    var springs=a.Instance.SpringBone.Springs;var joints=springs.SelectMany(s=>s.Joints).Select(j=>j.transform).ToArray();
                    var rest=joints.Select(j=>j.localRotation).ToArray();
                    var restPositions=joints.Select(j=>a.transform.InverseTransformPoint(j.position)).ToArray();
                    var row=new Row{sha=a.Package.model.sha256,air=a.Physics.MotionAirEnabled,worldMotion=a.Physics.WorldMotionEnabled,fps=fps,voicePlayed=played,voiceGated=gated};
                    foreach(var s in springs)row.chains.Add(new Chain{name=s.Name});report.runs.Add(row);
                    var state=preview.Interaction;state.BeginGrab("probe",out int g);state.MoveGrab("probe",g,new Vec3(0,1.74f,0));preview.RefreshSpatialState();a.ResetSecondaryMotion();
                    wait=Frames(fps/2);while(wait.MoveNext())yield return null;
                    state.ReleaseGrabWithDescent("probe",g,new Vec3(0,.74f,0));
                    string frames=Path.Combine(evidence,"asset-"+asset+"-"+(air?"air":"baseline"));if(fps==60)Directory.CreateDirectory(frames);
                    for(int frame=0;frame<fps*4;frame++)
                    {
                        wait=Frames(1);while(wait.MoveNext())yield return null;
                        if(state.State==PetState.Descending)row.descentFrames++;
                        row.maxForce=Mathf.Max(row.maxForce,a.Physics.MotionAirForce.magnitude);
                        int j=0;for(int s=0;s<springs.Count;s++)foreach(var joint in springs[s].Joints)
                        {
                            float offset=Vector3.Distance(restPositions[j],a.transform.InverseTransformPoint(joint.transform.position))*1000;
                            row.chains[s].maxOffsetMm=Mathf.Max(row.chains[s].maxOffsetMm,offset);
                            float angle=Quaternion.Angle(rest[j++],joint.transform.localRotation);Require(float.IsFinite(angle),"Nonfinite joint");
                            row.chains[s].maxAngle=Mathf.Max(row.chains[s].maxAngle,angle);if(frame==fps*4-1)row.chains[s].settledAngle=Mathf.Max(row.chains[s].settledAngle,angle);
                        }
                        if(fps==60&&frame%2==0)
                        {
                            camera.transform.position=a.transform.position+new Vector3(.24f,.16f,.52f);camera.transform.LookAt(a.transform.position+Vector3.up*.12f);
                            Alpha2Probe.Capture(camera,Path.Combine(frames,(frame/2).ToString("D4")+".png"));
                        }
                        if(fps==60&&frame==fps/3)
                        {
                            camera.transform.position=a.transform.position+new Vector3(.52f,.16f,.05f);camera.transform.LookAt(a.transform.position+Vector3.up*.12f);
                            Alpha2Probe.Capture(camera,Path.Combine(evidence,"asset-"+asset+"-"+(air?"after":"before")+"-side.png"));
                            camera.transform.position=a.transform.position+new Vector3(.24f,.16f,-.52f);camera.transform.LookAt(a.transform.position+Vector3.up*.12f);
                            Alpha2Probe.Capture(camera,Path.Combine(evidence,"asset-"+asset+"-"+(air?"after":"before")+"-back.png"));
                        }
                    }
                    Require(state.State==PetState.Idle&&a.Physics.MotionAirForce==Vector3.zero,"Wind or descent survived landing");
                    state.BeginGrab("cleanup",out g);state.MoveGrab("cleanup",g,new Vec3(.1f,1.4f,0));preview.RefreshSpatialState();
                    wait=Frames(1);while(wait.MoveNext())yield return null;preview.SimulateTrackingLoss();
                    Require(a.Physics.MotionAirForce==Vector3.zero,"Paused wind survived");
                    File.WriteAllText(Path.Combine(evidence,"motion-air.json"),JsonUtility.ToJson(report,true));
                }
                asset++;
            }
            UnityEngine.Object.Destroy(preview.gameObject);Time.captureFramerate=0;
            var cleanup=Frames(4);while(cleanup.MoveNext())yield return null;
            Require(UniVRM10.FastSpringBones.FastSpringBoneService.Instance.BufferCombiner.Combined.Models.Length==0,"Native buffer survived");
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Debug.Log(code==0?"XR_NATIVE_PHYSICS_PASSED":"XR_NATIVE_PHYSICS_FAILED");EditorApplication.Exit(code);}
    }
}
