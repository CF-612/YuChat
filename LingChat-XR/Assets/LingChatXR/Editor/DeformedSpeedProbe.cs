using System;
using System.Collections;
using System.IO;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Editor
{
    public static class DeformedSpeedProbe
    {
        static IEnumerator work;static int last=-1;static double deadline;static string error;
        static readonly JArray checks=new JArray(),measurements=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+600;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Log(string message,string stack,LogType type){if(type==LogType.Exception)error=message+stack;}
        static void Check(bool pass,string name){checks.Add(new JObject{["name"]=name,["passed"]=pass});File.WriteAllText(Path.Combine(Output,"progress.json"),checks.ToString());if(!pass)throw new Exception(name);}
        static void Tick()
        {
            try
            {
                if(error!=null)throw new Exception(error);if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Speed probe timeout");
                if(!EditorApplication.isPlaying||Time.frameCount==last)return;last=Time.frameCount;work??=RunChecks();if(work.MoveNext())return;
                File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["measurements"]=measurements,["questTested"]=false}.ToString());Debug.Log("XR_M1_PASSED");Finish(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){Application.logMessageReceived-=Log;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
        static IEnumerator RunChecks()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;while(!live.Ready)yield return null;
            var walker=live.Locomotion;walker.ManualStep=true;
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;Check(change.Result,"switch "+folder);yield return null;}
                var avatar=live.Avatar;avatar.FreezePresentation=true;var player=avatar.Motions;var load=player.PrepareLocomotion();while(!load.IsCompleted)yield return null;Check(load.Result&&player.DefaultMovement=="skip"&&player.HasRunning,"purchased skip/run loaded "+folder);
                var camera=live.ViewCamera;camera.orthographic=true;camera.orthographicSize=.34f;camera.transform.position=new Vector3(0,1.02f,.95f);camera.transform.LookAt(new Vector3(0,.86f,-.08f));int captureFrame=0;
                Check(File.Exists(Path.Combine(Application.streamingAssetsPath,"Motions/local-locomotion/locomotion_walkcycle.vrma")),"first walk retained as backup");
                Check(live.SaveMovementSpeed(1)==null,"speed preference saved");Check(live.SaveMovementSpeed(float.NaN)!=null&&live.MovementSpeed==1,"invalid speed preserves settings");
                var start=new Vector3(-.43f,.74f,-.08f);var goal=new Vector3(.43f,.74f,-.08f);
                Check(live.PlaceOnPlane(new Vector3(0,.74f,0),.6f,.4f,start),"place");player.SetSpatialAllowed(true);
                live.Interaction.SetAutonomousEnabled(true);live.TryAutonomousPose(start,Quaternion.Euler(0,90,0),live.Interaction.Generation);live.Interaction.SetAutonomousEnabled(false);
                var hips=avatar.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.Hips);player.Step(0);var rest=hips.localPosition;
                var go=walker.GoTo(goal);while(!go.IsCompleted)yield return null;Check(go.Result,"normal movement begins");int generation=live.Interaction.Generation;
                float low=float.PositiveInfinity,high=float.NegativeInfinity,maxNormal=0,maxRun=0,normalRate=0,fastRate=0;bool sawRun=false;
                for(int i=0;i<130;i++)
                {
                    walker.Step(1f/60);player.Step(1f/60);avatar.Instance.Runtime.Process();
                    if(i>30){low=Mathf.Min(low,hips.localPosition.y);high=Mathf.Max(high,hips.localPosition.y);}
                    maxNormal=Mathf.Max(maxNormal,walker.Speed);normalRate=Mathf.Max(normalRate,walker.AnimationPlaybackRate);
                    CheckPosition(live);if(i%2==0)LocomotionProbe.Capture(camera,folder+"-"+(captureFrame++).ToString("D4"));yield return null;
                }
                Check(high-low>.004f,"skip retains real vertical hips movement "+folder);Check(!walker.Running,"default movement remains skip");
                for(int i=0;i<4;i++){var again=walker.GoTo(goal);while(!again.IsCompleted)yield return null;Check(again.Result,"repeat command accepted");}
                Check(walker.SpeedMultiplier>=2.5f&&live.Interaction.Generation==generation&&player.LocomotionOwned,"repeat boosts without restarting ownership");
                float multiplier=walker.SpeedMultiplier;var invalid=walker.GoTo(new Vector3(3,.74f,3));while(!invalid.IsCompleted)yield return null;Check(!invalid.Result&&walker.SpeedMultiplier==multiplier,"outside target cannot boost");
                for(int i=0;i<55;i++){walker.Step(1f/60);player.Step(1f/60);avatar.Instance.Runtime.Process();sawRun|=walker.Running;maxRun=Mathf.Max(maxRun,walker.Speed);fastRate=Mathf.Max(fastRate,walker.AnimationPlaybackRate);CheckPosition(live);if(i%2==0)LocomotionProbe.Capture(camera,folder+"-"+(captureFrame++).ToString("D4"));yield return null;}
                Check(sawRun&&maxRun>maxNormal*1.5f,"repeated points change clip and increase movement speed");
                walker.SetBlocked(true);var stopped=live.Interaction.Position;
                for(int i=0;i<20;i++){walker.Step(1f/60);player.Step(1f/60);avatar.Instance.Runtime.Process();yield return null;}
                Check(walker.Blocked&&Vector3.Distance(FeiyuPreview.ToUnity(stopped),FeiyuPreview.ToUnity(live.Interaction.Position))<.00001f,"palm pause still stops fast movement");walker.SetBlocked(false);
                walker.Cancel(true);Check(Vector3.Distance(hips.localPosition,rest)<.0001f,"cancel restores hips without residual bounce");
                Check(live.SaveMovementSpeed(3)==null,"fast speed can be set directly");live.PlaceOnPlane(new Vector3(0,.74f,0),.6f,.4f,start);player.SetSpatialAllowed(true);
                var fast=walker.GoTo(goal);while(!fast.IsCompleted)yield return null;Check(fast.Result,"settings-based fast movement begins");
                bool settingRun=false;for(int i=0;i<130;i++){walker.Step(1f/60);player.Step(1f/60);avatar.Instance.Runtime.Process();settingRun|=walker.Running;yield return null;}
                Check(settingRun,"fast setting selects purchased run");
                walker.Cancel(true);Check(live.SaveMovementSpeed(1)==null,"restore normal preference");
                var ui=UnityEngine.Object.FindAnyObjectByType<XrChatView>().Settings;
                ui.Open(2);ui.Root.Q<Slider>("settings-movement-speed").value=2;ui.Close();Check(live.MovementSpeed==1,"desktop cancel keeps applied speed");
                ui.Open(2);ui.Root.Q<Slider>("settings-movement-speed").value=2;ui.Save();while(ui.IsSaving)yield return null;
                Check(live.MovementSpeed==2&&!ui.IsOpen,"desktop speed save applies and closes");
                measurements.Add(new JObject{["model"]=folder,["hipsVerticalRange"]=high-low,["normalMaxSpeed"]=maxNormal,["boostMaxSpeed"]=maxRun,["normalMaxPlaybackRate"]=normalRate,["boostMaxPlaybackRate"]=fastRate});
                avatar.FreezePresentation=false;
                var reload=live.SwitchCharacterAsync(folder);while(!reload.IsCompleted)yield return null;yield return null;
                Check(reload.Result&&live.MovementSpeed==2,"model reload restores saved movement speed");Check(live.SaveMovementSpeed(1)==null,"restore fixture preference");
            }
        }
        static void CheckPosition(FeiyuPreview live){if(!live.Interaction.ContainsFootprint(live.Interaction.Position,live.Interaction.RadiusMeters)||Mathf.Abs(live.Interaction.Position.Y-live.Interaction.PlaneCenter.Y)>.0001f)throw new Exception("Motion escaped tabletop owner");}
    }
}
