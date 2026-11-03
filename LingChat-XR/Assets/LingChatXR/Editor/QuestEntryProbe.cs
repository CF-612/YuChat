using System;
using System.Collections;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using LingChatXR.Chat;
using LingChatXR.XR;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace LingChatXR.Editor
{
    public static class QuestEntryProbe
    {
        static int lastFrame=-1;static IEnumerator work;static double deadline;static JArray checks=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static void Check(bool condition,string name){checks.Add(new JObject{["name"]=name,["passed"]=condition});if(!condition)throw new Exception(name);}
        public static void Run()
        {
            QuestProject.Prepare();Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");
            EditorSceneManager.OpenScene(QuestProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;work??=Checks();if(!work.MoveNext())Finish(0);}catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(1);}}
        static IEnumerator Frame(){int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;}
        static IEnumerator Checks()
        {
            var entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();var live=entry.Preview;while(!live.Ready)yield return null;
            Check(live.ExternalInput,"desktop-input-disabled-only-in-quest-scene");Check(entry.Controllers.Count==4,"two-hands-near-and-ray");
            Check(UnityEngine.Object.FindObjectsByType<EventSystem>().Length==1,"one-ui-event-system");
            Check(entry.Panel.Canvas.renderMode==RenderMode.WorldSpace,"world-space-chinese-panel");Check(Resources.Load<Font>("Fonts/NotoSansSC").HasCharacter('鱼'),"bundled-chinese-glyph");
            Check(XRGeneralSettings.Instance.Manager.activeLoaders.Any(l=>l.GetType().Name=="OpenXRLoader"),"openxr-loader-configured");
            Check(!XRGeneralSettings.Instance.InitManagerOnStart,"desktop-does-not-auto-start-xr");
            foreach(var group in new[]{BuildTargetGroup.Standalone,BuildTargetGroup.Android})Check(OpenXRSettings.GetSettingsForBuildTargetGroup(group).GetFeatures<OpenXRFeature>().Any(f=>f.enabled&&f.GetType().Name=="OculusTouchControllerProfile"),"touch-profile-"+group);
            Check(live.PackageFolders.Contains("local-smallbluefish-alpha2")&&live.PackageFolders.Contains("local-physics-reference"),"indexed-models-loaded");
            var urlCatalog=CharacterCatalog.Load(new Uri(Application.streamingAssetsPath+"/Characters/").AbsoluteUri,default);while(!urlCatalog.IsCompleted)yield return null;Check(urlCatalog.Result.folders.SequenceEqual(live.PackageFolders),"index-loads-through-url-entry");
            string badFolder=Path.Combine(Output,"invalid-index");Directory.CreateDirectory(badFolder);File.WriteAllText(Path.Combine(badFolder,"index.json"),"{\"version\":1,\"folders\":[\"../outside\"]}");
            var bad=CharacterCatalog.Load(badFolder,default);while(!bad.IsCompleted)yield return null;Check(bad.IsFaulted&&bad.Exception.GetBaseException() is ArgumentException,"index-rejects-path-traversal");
            var left=entry.Controllers.OfType<XRDirectInteractor>().First();var right=entry.Controllers.OfType<XRDirectInteractor>().Last();
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;Check(change.Result,"switch-"+folder);}
                entry.Panel.ConfirmButton.onClick.Invoke();yield return null;
                Check(live.Interaction.PlaneConfirmed,"panel-confirms-table-"+folder);
                var manager=entry.Interactions;var core=live.Interaction;
                manager.HoverEnter((IXRHoverInteractor)left,(IXRHoverInteractable)entry.Bridge.Head);Check(core.State==PetState.Touched,"xri-near-touch-"+folder);manager.HoverExit((IXRHoverInteractor)left,(IXRHoverInteractable)entry.Bridge.Head);Check(core.State==PetState.Idle,"xri-touch-exit-"+folder);
                manager.SelectEnter((IXRSelectInteractor)left,(IXRSelectInteractable)entry.Bridge.Body);
                Check(core.State==PetState.Held&&core.Holder=="LeftHand","xri-grab-owner-"+folder);
                int token=core.Generation;Check(!core.MoveGrab("RightHand",token,new Vec3(0,4,0)),"second-hand-cannot-steal-"+folder);
                left.transform.position+=new Vector3(.1f,.45f,0);yield return null;
                Check(core.Position.Y>.9f,"controller-moves-shared-space-owner-"+folder);
                manager.SelectExit((IXRSelectInteractor)left,(IXRSelectInteractable)entry.Bridge.Body);
                Check(core.State==PetState.Descending,"release-starts-gravity-"+folder);float y=core.Position.Y;yield return null;Check(core.Position.Y<y&&core.Position.Y>.74f,"descent-is-continuous-"+folder);
                while(core.State!=PetState.Idle)yield return null;
                manager.SelectEnter((IXRSelectInteractor)left,(IXRSelectInteractable)entry.Bridge.Body);token=core.Generation;entry.Suspend();Check(core.State==PetState.Suspended&&!core.PlaneConfirmed,"tracking-loss-invalidates-plane-"+folder);Check(!core.MoveGrab("LeftHand",token,new Vec3(0,2,0)),"late-input-rejected-"+folder);
                if(left.hasSelection)manager.SelectExit((IXRSelectInteractor)left,(IXRSelectInteractable)entry.Bridge.Body);entry.Panel.ConfirmButton.onClick.Invoke();
            }
            // 官方模拟设备走真实 InputAction -> XRI 射线 -> 世界 UI / 选择事件，补足直接调用事件的边界。
            InputSystem.RegisterLayout<XRSimulatedController>();var device=InputSystem.AddDevice<XRSimulatedController>();InputSystem.SetDeviceUsage(device,UnityEngine.InputSystem.CommonUsages.LeftHand);
            var ray=entry.Controllers.OfType<XRRayInteractor>().First();left.enabled=false;
            var parent=ray.transform.parent.parent;
            var button=(RectTransform)entry.Panel.ConfirmButton.transform;Vector3 target=button.TransformPoint(button.rect.center),position=target+Vector3.forward*.5f;
            var state=new XRSimulatedControllerState{isTracked=true,trackingState=3,devicePosition=parent.InverseTransformPoint(position),deviceRotation=Quaternion.Inverse(parent.rotation)*Quaternion.LookRotation(target-position)};
            live.ResetPlacement();InputSystem.QueueStateEvent(device,state);for(int frame=0;frame<8;frame++)yield return null;
            Check(ray.TryGetCurrentUIRaycastResult(out var hit)&&hit.gameObject.transform.IsChildOf(button),"input-system-ray-hits-world-button");
            InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.TriggerButton));for(int frame=0;frame<4;frame++)yield return null;
            Check(ray.uiPressInput.ReadIsPerformed(),"trigger-action-is-bound");
            InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.TriggerButton,false));for(int frame=0;frame<4;frame++)yield return null;
            Check(live.Interaction.PlaneConfirmed,"input-system-trigger-click-confirms-plane");
            // 快速批处理可能尚未跨过物理帧；先同步刚由按钮重新放置的碰撞体，再读世界包围盒。
            Physics.SyncTransforms();target=live.Avatar.GrabProxy.bounds.center;position=target+Vector3.forward*.4f;
            state.devicePosition=parent.InverseTransformPoint(position);state.deviceRotation=Quaternion.Inverse(parent.rotation)*Quaternion.LookRotation(target-position);InputSystem.QueueStateEvent(device,state);for(int frame=0;frame<8;frame++)yield return null;
            File.WriteAllText(Path.Combine(Output,"ray-before-grab.json"),new JObject{["rayEnabled"]=ray.enabled,["nearEnabled"]=left.enabled,["hit"]=ray.TryGetCurrent3DRaycastHit(out var bodyHit)?bodyHit.collider.name:"none",["hover"]=string.Join(",",ray.interactablesHovered.Select(x=>x.transform.name)),["ui"]=ray.TryGetCurrentUIRaycastResult(out var uiHit)?uiHit.gameObject.name:"none",["position"]=ray.transform.position.ToString(),["target"]=target.ToString()}.ToString());
            InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.GripButton));for(int frame=0;frame<4;frame++)yield return null;
            File.WriteAllText(Path.Combine(Output,"ray-grab.json"),new JObject{["pressed"]=ray.selectInput.ReadIsPerformed(),["selected"]=string.Join(",",ray.interactablesSelected.Select(x=>x.transform.name)),["state"]=live.Interaction.State.ToString()}.ToString());
            Check(live.Interaction.State==PetState.Held,"input-system-grip-ray-grabs-body");
            state.devicePosition+=parent.InverseTransformVector(Vector3.up*.4f);InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.GripButton));for(int frame=0;frame<4;frame++)yield return null;
            Check(live.Interaction.Position.Y>1,"tracked-pose-moves-grabbed-avatar");
            InputSystem.QueueStateEvent(device,state.WithButton(ControllerButton.GripButton,false));for(int frame=0;frame<2;frame++)yield return null;Check(live.Interaction.State==PetState.Descending,"input-system-grip-release-descends");
            InputSystem.RemoveDevice(device);while(live.Interaction.State!=PetState.Idle)yield return null;
            var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();while(!chat.CanSend)yield return null;Check(chat.Connected,"same-local-dialogue-controller-connected");
            chat.TestVoice();bool heard=false;float until=Time.unscaledTime+45;while(Time.unscaledTime<until){heard|=chat.Voice.IsPlaying;if(heard&&!chat.Busy)break;yield return null;}Check(heard,"world-scene-real-tts-audiosource");
            Capture(entry.HeadCamera);
        }
        static void Capture(Camera camera){camera.transform.position=new Vector3(0,1.2f,1.2f);camera.transform.LookAt(new Vector3(0,1.18f,0));var rt=new RenderTexture(1600,900,24);rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1600,900,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1600,900),0,0);t.Apply();File.WriteAllBytes(Path.Combine(Output,"quest-panel.png"),t.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(t);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        static void Finish(int code){EditorApplication.update-=Tick;File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["deviceTested"]=false}.ToString());Debug.Log(code==0?"XR_M1_PASSED":"XR_QUEST_CHECK_FAILED");EditorApplication.Exit(code);}
    }
}
