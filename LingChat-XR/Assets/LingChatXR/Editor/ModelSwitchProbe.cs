using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LingChatXR.Core;
using LingChatXR.Speech;
using UniVRM10;
using UniVRM10.FastSpringBones;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 走产品的同一 Preview：不手动执行 VRM 或 SpringBone，不按模型名分支。
    public static class ModelSwitchProbe
    {
        [Serializable] class Row{public string from,to,trigger;public int fps,chains,colliders;public double loadSeconds;public bool clothNetworkAvailable=false;}
        [Serializable] class Report{public List<Row> switches=new List<Row>();public List<string> checks=new List<string>();public bool automaticSpringService=true,userAccepted=false,questTested=false;}
        static FeiyuPreview preview;static Camera camera;static IEnumerator run;static double deadline;static string evidence,prefs;static string[] folders;static Report report=new Report();
        public static void Run()
        {
            Unity.Collections.NativeLeakDetection.Mode=Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            LilToonTemplates.Ensure();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            evidence=Environment.GetEnvironmentVariable("XR_NATIVE_EVIDENCE");Directory.CreateDirectory(evidence);prefs=Path.Combine(evidence,"preferences");Directory.CreateDirectory(prefs);
            folders=Environment.GetEnvironmentVariable("XR_SWITCH_FOLDERS").Split('|');if(folders.Length<2)throw new Exception("Provide two packages");
            foreach(string folder in folders){var p=Package(folder);File.WriteAllText(Path.Combine(prefs,"xr-"+p.model.sha256+".json"),JsonUtility.ToJson(new XrPreferences{modelSha256=p.model.sha256},true));}
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+520;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.EnterPlaymode();
        }
        static void CreatePreview()
        {
            camera=new GameObject("Switch evidence camera").AddComponent<Camera>();camera.transform.position=new Vector3(.65f,1.4f,2.3f);camera.transform.LookAt(new Vector3(0,1.1f,0));camera.fieldOfView=38;camera.nearClipPlane=.02f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.14f,.19f);camera.gameObject.AddComponent<AudioListener>();
            var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,20,0);
            var table=GameObject.CreatePrimitive(PrimitiveType.Cube);table.transform.position=new Vector3(0,.72f,0);table.transform.localScale=new Vector3(1.4f,.04f,1f);
            preview=new GameObject("Shared Preview").AddComponent<FeiyuPreview>();preview.SetCamera(camera);preview.ConfigureCharacter(folders[0],true);preview.ConfigureAuthorPhysics(false);preview.ConfigurePreferenceStorage(prefs);preview.ManualInput=true;
        }
        static CharacterPackage Package(string folder)=>JsonUtility.FromJson<CharacterPackage>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Characters",folder,"manifest.json")));
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("S4 timeout");if(!EditorApplication.isPlaying)return;if(run==null)run=Check();if(!run.MoveNext())Finish(0);}catch(Exception e){Debug.LogWarning(e);Finish(1);}}
        static void Require(bool value,string why){if(!value)throw new Exception(why);}
        static IEnumerator Frames(int count){int last=Time.frameCount;for(int i=0;i<count;i++){while(last==Time.frameCount)yield return null;last=Time.frameCount;}}
        static void CheckReady(string folder)
        {
            Require(preview.Ready&&preview.ActivePackageFolder==folder,"Wrong committed selection");
            Require(preview.Avatar.Package.model.sha256==Package(folder).model.sha256,"Wrong committed SHA");
            Require(preview.Avatar.Physics!=null&&!preview.Avatar.UsesAuthorPhysics,"Different physics path");
            Require(preview.Interaction.State==PetState.Unplaced&&!preview.Interaction.PlaneConfirmed,"Old placement leaked");
            Require(Mathf.Abs(preview.ConfiguredHeight-.24f)<1e-5f,"Different common calibration");
            var instances=UnityEngine.Object.FindObjectsByType<Vrm10Instance>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Require(instances.Length==1&&instances[0]==preview.Avatar.Instance,"Stale VRM instance");
            Require(UnityEngine.Object.FindObjectsByType<PortableSpringPhysics>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"Duplicate physics owner");
            Require(UnityEngine.Object.FindObjectsByType<ContactShadow>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"Stale shadow");
            Require(FastSpringBoneService.Instance.BufferCombiner.Combined.Models.Length==1,"Stale native model buffer");
        }
        static IEnumerator Check()
        {
            CreatePreview();
            while(!preview.Ready){Require(preview.LoadingPhase!=FeiyuPreview.LoadPhase.Failed,"Initial load failed");yield return null;}
            var wait=Frames(3);while(wait.MoveNext())yield return null;CheckReady(folders[0]);
            var voice=preview.GetComponent<XrSpeechController>();voice.Volume=0;
            var silent=AudioClip.Create("S4 silent cancellation probe",240000,1,48000,false);
            int current=0;
            foreach(int fps in new[]{30,60,90})
            {
                Time.captureFramerate=fps;
                foreach(string trigger in new[]{"touch","held","descending","suspended"})for(int direction=0;direction<2;direction++)
                {
                    Require(preview.Place(new Vector3(0,.74f,0)),"Placement rejected");
                    wait=Frames(3);while(wait.MoveNext())yield return null;
                    var old=preview.Avatar;var state=preview.Interaction;int grab=0;
                    if(trigger=="touch")Require(state.BeginTouch("old-pointer"),"Touch rejected");
                    if(trigger=="held"||trigger=="descending")
                    {
                        Require(state.BeginGrab("old-pointer",out grab),"Grab rejected");state.MoveGrab("old-pointer",grab,new Vec3(0,1.54f,0));preview.RefreshSpatialState();
                        if(trigger=="descending")state.ReleaseGrabWithDescent("old-pointer",grab,new Vec3(0,.74f,0));
                    }
                    if(trigger=="suspended")preview.SimulateTrackingLoss();
                    wait=Frames(4);while(wait.MoveNext())yield return null;
                    int oldGeneration=state.Generation;
                    Require(voice.PlayReaction(silent),"Silent playback could not start");long voiceGeneration=voice.Generation;
                    string from=folders[current];current=1-current;string to=folders[current];
                    double start=EditorApplication.timeSinceStartup;var task=preview.SwitchCharacterAsync(to);
                    Require(!preview.Ready,"Old Ready during import");
                    Require(!state.MoveGrab("old-pointer",grab,new Vec3(1,2,0))&&!state.StepDescent(.1f,oldGeneration),"Old input accepted");
                    while(!task.IsCompleted)yield return null;Require(task.GetAwaiter().GetResult(),"Switch failed");
                    wait=Frames(3);while(wait.MoveNext())yield return null;CheckReady(to);
                    Require(old==null,"Old adapter not destroyed");Require(voice.Generation>voiceGeneration&&voice.CurrentKind==VoiceKind.None&&!voice.IsWaiting&&!voice.IsPlaying,"Old playback survived");
                    var a=preview.Avatar;
                    report.switches.Add(new Row{from=Package(from).model.sha256,to=a.Package.model.sha256,trigger=trigger,fps=fps,
                        loadSeconds=EditorApplication.timeSinceStartup-start,chains=a.Instance.SpringBone.Springs.Count,colliders=a.Instance.GetComponentsInChildren<VRM10SpringBoneCollider>().Length});
                    File.WriteAllText(Path.Combine(evidence,"switches.json"),JsonUtility.ToJson(report,true));
                    // 同一设置/真实自动 LateUpdate 下采集下落；不手动推进求解器。
                    if(fps==60&&trigger=="held")
                    {
                        preview.Place(new Vector3(0,.74f,0));var s=preview.Interaction;s.BeginGrab("capture",out int g);s.MoveGrab("capture",g,new Vec3(0,1.54f,0));preview.RefreshSpatialState();a.ResetSecondaryMotion();
                        wait=Frames(30);while(wait.MoveNext())yield return null;s.ReleaseGrabWithDescent("capture",g,new Vec3(0,.74f,0));
                        string dir=Path.Combine(evidence,"frames-"+current);Directory.CreateDirectory(dir);
                        for(int n=0;n<90;n++){wait=Frames(1);while(wait.MoveNext())yield return null;if(n%2==0)Alpha2Probe.Capture(camera,Path.Combine(dir,(n/2).ToString("D4")+".png"));}
                        var pos=camera.transform.position;var rot=camera.transform.rotation;camera.transform.position=new Vector3(.24f,.91f,.48f);camera.transform.LookAt(new Vector3(0,.86f,0));
                        Alpha2Probe.Capture(camera,Path.Combine(evidence,"model-"+current+"-close.png"));camera.transform.SetPositionAndRotation(pos,rot);
                        Require(s.State==PetState.Idle&&Mathf.Abs(a.transform.position.y-.74f)<.001f,"Automatic descent failed");
                    }
                }
            }
            // 真正进入导入阶段后取消；后来的请求获胜，旧完成回调不可覆盖。
            var cancelled=preview.SwitchCharacterAsync(folders[1]);while(preview.LoadingPhase!=FeiyuPreview.LoadPhase.Importing&&!cancelled.IsCompleted)yield return null;
            Require(!cancelled.IsCompleted,"Could not observe import cancellation");preview.CancelCharacterLoad();while(!cancelled.IsCompleted)yield return null;Require(!cancelled.Result&&!preview.Ready,"Cancelled import committed");
            wait=Frames(3);while(wait.MoveNext())yield return null;
            Require(UnityEngine.Object.FindObjectsByType<Vrm10Instance>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0,"Cancelled VRM survived");report.checks.Add("cancel-during-import");
            var superseded=preview.SwitchCharacterAsync(folders[1]);var winner=preview.SwitchCharacterAsync(folders[0]);while(!superseded.IsCompleted||!winner.IsCompleted)yield return null;
            Require(!superseded.Result&&winner.Result,"Latest selection did not win");wait=Frames(3);while(wait.MoveNext())yield return null;CheckReady(folders[0]);report.checks.Add("latest-selection-wins");
            var missing=preview.SwitchCharacterAsync("local-s4-missing");while(!missing.IsCompleted)yield return null;Require(!missing.Result&&!preview.Ready,"Missing package accepted");report.checks.Add("missing-package-rejected");
            // 只在隔离验证工程创建坏包；不修改原资产或作者清单。
            string badFolder="local-s4-invalid-hash",badPath=Path.Combine(Application.streamingAssetsPath,"Characters",badFolder);
            Directory.CreateDirectory(badPath);var bad=Package(folders[0]);
            string badModel=Path.Combine(badPath,bad.model.path);Directory.CreateDirectory(Path.GetDirectoryName(badModel));
            File.Copy(Path.Combine(Application.streamingAssetsPath,"Characters",folders[0],bad.model.path),badModel,true);
            bad.model.sha256=new string('0',64);File.WriteAllText(Path.Combine(badPath,"manifest.json"),JsonUtility.ToJson(bad,true));
            var corrupt=preview.SwitchCharacterAsync(badFolder);while(!corrupt.IsCompleted)yield return null;
            Require(!corrupt.Result&&!preview.Ready&&preview.Status.Contains("SHA256"),"Corrupted model accepted");report.checks.Add("model-hash-mismatch-rejected");
            bool invalid=false;try{preview.SwitchCharacterAsync("../escape").GetAwaiter().GetResult();}catch(ArgumentException){invalid=true;}Require(invalid,"Traversal accepted");report.checks.Add("unsafe-path-rejected");
            // 同样的保存格式，故意不同高度：另一模型不能继承前一个实例的内存设置。
            var p=Package(folders[0]);File.WriteAllText(Path.Combine(prefs,"xr-"+p.model.sha256+".json"),JsonUtility.ToJson(new XrPreferences{modelSha256=p.model.sha256,heightMeters=.30f},true));
            foreach(var target in new[]{folders[0],folders[1],folders[0]})
            {
                var t=preview.SwitchCharacterAsync(target);while(!t.IsCompleted)yield return null;Require(t.Result,"Calibration reload failed");Require(Mathf.Abs(preview.ConfiguredHeight-(target==folders[0]?.30f:.24f))<1e-5f,"Saved calibration leaked across models");
            }
            report.checks.Add("per-asset-preferences-isolated");
            var disabled=preview.SwitchCharacterAsync(folders[1]);while(preview.LoadingPhase!=FeiyuPreview.LoadPhase.Importing&&!disabled.IsCompleted)yield return null;preview.enabled=false;
            while(!disabled.IsCompleted)yield return null;Require(!disabled.Result&&!preview.Ready,"Disabled preview published model");preview.enabled=true;report.checks.Add("disable-cancels-import");
            var final=preview.SwitchCharacterAsync(folders[1]);while(!final.IsCompleted)yield return null;Require(final.Result,"Recovery after disable failed");
            report.checks.Add("recovery-after-disable");
            var destroyed=preview.SwitchCharacterAsync(folders[0]);while(preview.LoadingPhase!=FeiyuPreview.LoadPhase.Importing&&!destroyed.IsCompleted)yield return null;
            Require(!destroyed.IsCompleted,"Could not observe destroy during import");
            UnityEngine.Object.Destroy(silent);UnityEngine.Object.Destroy(preview.gameObject);
            while(!destroyed.IsCompleted)yield return null;Require(!destroyed.Result,"Destroyed preview published model");report.checks.Add("destroy-during-import-cancels");
            wait=Frames(4);while(wait.MoveNext())yield return null;
            Require(UnityEngine.Object.FindObjectsByType<Vrm10Instance>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0,"Shutdown retained VRM");
            Require(FastSpringBoneService.Instance.BufferCombiner.Combined.Models.Length==0,"Shutdown retained native model");report.checks.Add("destroy-cleans-native-buffers");
            Time.captureFramerate=0;File.WriteAllText(Path.Combine(evidence,"switches.json"),JsonUtility.ToJson(report,true));
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Debug.Log(code==0?"XR_NATIVE_PHYSICS_PASSED":"XR_NATIVE_PHYSICS_FAILED");EditorApplication.Exit(code);}
    }
}
