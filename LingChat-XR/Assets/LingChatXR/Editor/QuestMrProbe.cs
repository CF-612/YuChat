using System;
using System.Collections;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using LingChatXR.XR;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
namespace LingChatXR.Editor
{
    public static class QuestMrProbe
    {
        static IEnumerator work;static int frame=-1;static double deadline;static JArray checks=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static void Check(bool condition,string name){checks.Add(new JObject{["name"]=name,["passed"]=condition});if(!condition)throw new Exception(name);}
        public static void Run(){QuestProject.Prepare();Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");EditorSceneManager.OpenScene(QuestProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();}
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;work??=RunChecks();if(!work.MoveNext())Finish(0);}catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(1);}}
        static IEnumerator RunChecks()
        {
            var e=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();while(!e.Preview.Ready)yield return null;yield return null;
            Check(e.InputActions!=null&&e.RightTrigger.actionMap.asset==e.InputActions,"official-serialized-action-asset");
            Check(e.HeadCamera.backgroundColor.a==0,"passthrough-transparent-camera-after-model-load");
            Check(!e.Bridge.Table.GetComponent<Renderer>().enabled&&!e.Bridge.Table.GetComponent<Collider>().enabled,"no-fixed-visible-or-collidable-table");
            Check(e.RightHand.GetComponentsInChildren<Renderer>().Length>0,"official-controller-model-present");
            foreach(var group in new[]{BuildTargetGroup.Standalone,BuildTargetGroup.Android})Check(OpenXRSettings.GetSettingsForBuildTargetGroup(group).GetFeatures<OpenXRFeature>().Any(f=>f.GetType().Name=="ARCameraFeature"&&f.enabled),"official-passthrough-enabled-"+group);
            InputSystem.RegisterLayout<XRSimulatedController>();var device=InputSystem.AddDevice<XRSimulatedController>();InputSystem.SetDeviceUsage(device,UnityEngine.InputSystem.CommonUsages.RightHand);
            var state=new XRSimulatedControllerState{isTracked=true,trackingState=3,devicePosition=new Vector3(.6f,1.1f,.5f),deviceRotation=Quaternion.identity};InputSystem.QueueStateEvent(device,state);for(int i=0;i<5;i++)yield return null;
            Check(e.RightTracked,"official-actions-receive-simulated-tracking");Check(Vector3.Distance(e.RightHand.position,state.devicePosition)<.01f,"controller-follows-tracking-pose");Check(Vector3.Distance(e.RightRay.transform.position,state.devicePosition)<.01f,"ray-pose-not-double-transformed");
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(e.Preview.ActivePackageFolder!=folder){var load=e.Preview.SwitchCharacterAsync(folder);while(!load.IsCompleted)yield return null;Check(load.Result,"load-"+folder);yield return null;}
                e.Placement.Begin();Check(!e.Preview.Interaction.PlaneConfirmed,"calibration-hides-unplaced-model-"+folder);
                Check(e.Placement.AcceptPoint(new Vector3(.6f,1.1f,.55f)),"accept-center-"+folder);
                Check(!e.Placement.AcceptPoint(new Vector3(.61f,1.1f,.56f)),"reject-insufficient-footprint-"+folder);
                Check(!e.Placement.AcceptPoint(new Vector3(1,1.5f,.85f)),"reject-wrong-height-"+folder);
                Check(e.Placement.AcceptPoint(new Vector3(1.05f,1.1f,.9f)),"accept-corner-"+folder);
                var core=e.Preview.Interaction;Check(Mathf.Abs(core.PlaneCenter.Y-1.1f)<.001f&&Mathf.Abs(core.PlaneCenter.X-.6f)<.001f,"uses-user-plane-not-fixed-default-"+folder);
                Physics.SyncTransforms();var target=e.Preview.Avatar.GrabProxy.bounds.center;state.devicePosition=target+Vector3.forward*.45f;state.deviceRotation=Quaternion.LookRotation(target-state.devicePosition);InputSystem.QueueStateEvent(device,state);for(int i=0;i<5;i++)yield return null;
                InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.GripButton));for(int i=0;i<4;i++)yield return null;Check(core.State==PetState.Held,"bound-grip-actually-selects-"+folder);
                state.devicePosition+=Vector3.up*.3f;InputSystem.QueueStateEvent(device,state);for(int i=0;i<4;i++)yield return null;Check(core.Position.Y>1.3f,"grab-moves-above-custom-plane-"+folder);
                InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.GripButton,false));for(int i=0;i<2;i++)yield return null;Check(core.State==PetState.Descending,"release-descends-"+folder);while(core.State!=PetState.Idle)yield return null;
                Check(Mathf.Abs(core.Position.Y-1.1f)<.002f,"lands-at-user-height-"+folder);e.Suspend();Check(!core.PlaneConfirmed,"tracking-loss-invalidates-calibration-"+folder);
            }
            InputSystem.RemoveDevice(device);
        }
        static void Finish(int code){EditorApplication.update-=Tick;File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["deviceTested"]=false}.ToString());Debug.Log(code==0?"XR_M1_PASSED":"XR_MR_FAILED");EditorApplication.Exit(code);}
    }
}
