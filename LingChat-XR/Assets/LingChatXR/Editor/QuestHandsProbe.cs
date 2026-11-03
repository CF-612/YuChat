using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using LingChatXR.Core;
using LingChatXR.XR;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace LingChatXR.Editor
{
    public static class QuestHandsProbe
    {
        static IEnumerator work;static int frame=-1;static double deadline;static JArray checks=new JArray();static bool runnerStarted;
        static XRHandDevice hand;static MetaAimHand aim;static QuestEntry entry;
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static void Check(bool condition,string name){checks.Add(new JObject{["name"]=name,["passed"]=condition});if(!condition)throw new Exception(name);}
        public static void Run()
        {
            QuestProject.Prepare();Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");EditorSceneManager.OpenScene(QuestProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+360;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(EditorApplication.isPlaying&&!runnerStarted){runnerStarted=true;new GameObject("Hands validation runner").AddComponent<QuestHandsProbeRunner>();}}
            catch(Exception error){File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());Finish(1);}
        }
        public static void GameTick()
        {
            try{if(frame==Time.frameCount)return;frame=Time.frameCount;work??=Checks();if(!work.MoveNext())Finish(0);}
            catch(Exception error){File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());Finish(1);}
        }
        static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
        // 使用官方 InputDevice 格式喂入样本，不调用业务 Grab/Release，验证完整 XRI 选择链。
        static void Pose(Vector3 point,bool pinch=false,bool tracked=true,bool system=false,Quaternion? aimRotation=null,Quaternion? gripRotation=null)
        {
            using(StateEvent.From(hand,out var sample))
            {
                hand.isTracked.WriteValueIntoEvent(tracked?1f:0f,sample);hand.trackingState.WriteValueIntoEvent(tracked?3:0,sample);
                hand.devicePosition.WriteValueIntoEvent(point,sample);hand.deviceRotation.WriteValueIntoEvent(Quaternion.identity,sample);
                hand.pinchPosition.WriteValueIntoEvent(point,sample);hand.gripRotation.WriteValueIntoEvent(gripRotation??Quaternion.identity,sample);
                hand.pokePosition.WriteValueIntoEvent(point,sample);hand.pokeRotation.WriteValueIntoEvent(Quaternion.identity,sample);
                InputSystem.QueueEvent(sample);
            }
            var flags=MetaAimFlags.Computed|MetaAimFlags.Valid;if(system)flags|=MetaAimFlags.SystemGesture;
            aim.UpdateHand(tracked,flags,new Pose(point,aimRotation??Quaternion.identity),pinch?1:0,0,0,0);
        }
        static IEnumerator Checks()
        {
            entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            entry.Panel.ConfirmButton.onClick.Invoke();
            Check(entry.Placement.Step==1,"calibration-entry-does-not-wait-for-model");
            foreach(var group in new[]{BuildTargetGroup.Standalone,BuildTargetGroup.Android})
            {
                var features=OpenXRSettings.GetSettingsForBuildTargetGroup(group).GetFeatures<OpenXRFeature>();
                foreach(string feature in new[]{"HandTracking","MetaHandTrackingAim","ARCameraFeature","ARPlaneFeature"})Check(features.Any(f=>f.GetType().Name==feature&&f.enabled),feature+"-"+group);
            }
            Check(entry.RightBareHand.GetComponentsInChildren<XRHandSkeletonDriver>(true).Length==1,"official-hand-skeleton-prefab");
            Check(entry.RightBareHand.GetComponentsInChildren<Renderer>(true).Length>0,"official-hand-mesh-present");
            bool sawLoadingIcon=false;
            while(entry.Preview.Loading||entry.Preview.LoadingPhase==FeiyuPreview.LoadPhase.None)
            {
                var indicator=entry.Preview.GetComponent<PetLoadingIndicator>();
                if(!sawLoadingIcon&&indicator!=null&&indicator.Visible)
                {
                    var rect=entry.HeadCamera.transform.Find("角色加载提示");
                    var shot=new GameObject("加载状态特写").AddComponent<Camera>();
                    shot.orthographic=true;shot.orthographicSize=.053f;shot.nearClipPlane=.01f;
                    shot.clearFlags=CameraClearFlags.SolidColor;shot.backgroundColor=new Color(.08f,.12f,.16f);
                    shot.transform.SetPositionAndRotation(rect.position-rect.forward*.28f,rect.rotation);
                    Canvas.ForceUpdateCanvases();Alpha2Probe.Capture(shot,Path.Combine(Output,"loading-badge.png"));
                    UnityEngine.Object.Destroy(shot.gameObject);sawLoadingIcon=true;
                }
                yield return null;
            }
            Check(entry.Preview.Ready,"main-model-import");yield return null;
            Check(sawLoadingIcon&&!entry.Preview.GetComponent<PetLoadingIndicator>().Visible,"导入时可见加载环，就绪后收起");
            // batchmode 编辑器无 GameView 焦点；模拟头显已回到应用前台，不绕过运行时门控。
            entry.SendMessage("OnApplicationPause",false);entry.SendMessage("OnApplicationFocus",true);
            Check(entry.InputAvailable,"fixture-application-focused");
            Check(QualitySettings.antiAliasing==4&&entry.HeadCamera.allowMSAA&&!entry.HeadCamera.allowHDR&&!entry.HeadCamera.allowDynamicResolution,"quest-camera-explicit-msaa4-no-dynamic-downscale");
            // XR_SKIP_LOADER 下没有眼缓冲，不能在此断言真实分辨率；由设备 XR_RENDER_QUALITY 日志验收。
            for(int i=0;i<3;i++)yield return null;
            hand=InputSystem.AddDevice<XRHandDevice>();InputSystem.SetDeviceUsage(hand,UnityEngine.InputSystem.CommonUsages.RightHand);
            // XR Hands 的静态设备引用由原生子系统设置；隔离检查只替换这个设备引用。
            typeof(XRHandDevice).GetProperty("rightHand").GetSetMethod(true).Invoke(null,new object[]{hand});
            aim=MetaAimHand.CreateHand(InputDeviceCharacteristics.Right);MetaAimHand.right=aim;
            Pose(new Vector3(.6f,1.1f,.55f));for(int i=0;i<8;i++)yield return null;
            Debug.Log($"XR_HAND_TEST_DEVICE enabled={hand.enabled} raw={hand.isTracked.ReadValue()} staticSame={XRHandDevice.rightHand==hand} phase={InputSystem.settings.updateMode}");
            Check(entry.RightTracked,"official-hand-tracking-input");Check(Vector3.Distance(entry.RightHand.position,new Vector3(.6f,1.1f,.55f))<.001f,"pinch-anchor-is-tracking-space-not-double-offset");
            foreach(string name in VrmRuntimeShaders.Required)Check(Shader.Find(name)!=null,"import-shader-"+name);
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(entry.Preview.ActivePackageFolder!=folder){var load=entry.Preview.SwitchCharacterAsync(folder);while(!load.IsCompleted)yield return null;Check(load.Result,"load-"+folder);yield return null;}
                entry.Placement.Begin();Pose(new Vector3(.6f,1.1f,.55f));float until=Time.unscaledTime+.4f;while(Time.unscaledTime<until)yield return null;
                Pose(new Vector3(.6f,1.1f,.55f),true);for(int i=0;i<4;i++)yield return null;Check(entry.Placement.Step==2,"pinch-confirms-center-"+folder);
                Pose(new Vector3(1.05f,1.1f,.55f));until=Time.unscaledTime+.3f;while(Time.unscaledTime<until)yield return null;
                Pose(new Vector3(1.05f,1.1f,.55f),true);for(int i=0;i<4;i++)yield return null;Check(entry.Placement.Step==3,"pinch-confirms-right-edge-"+folder);
                Pose(new Vector3(1.05f,1.1f,.9f));until=Time.unscaledTime+.3f;while(Time.unscaledTime<until)yield return null;
                Pose(new Vector3(1.05f,1.1f,.9f),true);for(int i=0;i<4;i++)yield return null;Check(entry.Preview.Interaction.PlaneConfirmed,"pinch-confirms-corner-"+folder);
                Pose(new Vector3(1.05f,1.1f,.9f));for(int i=0;i<4;i++)yield return null;
                Check(!entry.Panel.IsExpanded,"panel-collapses-after-placement-"+folder);
                if(folder=="local-smallbluefish-alpha2")
                {
                    var actor=entry.Preview.Avatar;var motions=actor.Motions;
                    float deadlineWave=Time.unscaledTime+8;
                    while(motions.ActiveId!="wave_normal_right_661"&&Time.unscaledTime<deadlineWave)yield return null;
                    Check(motions.ActiveId=="wave_normal_right_661","首次可见放置实际播放既有挥手动作");
                    var arm=actor.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var start=arm.localRotation;float swing=0;deadlineWave=Time.unscaledTime+12;
                    while(motions.ActiveId=="wave_normal_right_661"&&Time.unscaledTime<deadlineWave)
                    {yield return null;swing=Mathf.Max(swing,Quaternion.Angle(start,arm.localRotation));}
                    Check(swing>5&&motions.ActiveId==null,"挥手实际驱动手臂并自然收势");
                    float pauseUntil=Time.unscaledTime+.5f;
                    while(Time.unscaledTime<pauseUntil)yield return null;
                }
                else Check(entry.Preview.Avatar.Motions.ActiveId!="wave_normal_right_661","切换模型不重复开机问候");
                entry.Panel.MoveFastButton.onClick.Invoke();Check(entry.Preview.MovementSpeed==3,"quest-fast-speed-setting-"+folder);
                entry.Panel.MoveLightButton.onClick.Invoke();Check(entry.Preview.MovementSpeed==1,"quest-normal-speed-setting-"+folder);
                var towardViewer=Vector3.ProjectOnPlane(entry.HeadCamera.transform.position-entry.Preview.Avatar.transform.position,Vector3.up).normalized;
                Check(Vector3.Dot(entry.Preview.Avatar.transform.forward,towardViewer)>.999f,"initial-body-faces-viewer-"+folder);
                var initialFacing=entry.Preview.Avatar.transform.rotation;var oldCameraPosition=entry.HeadCamera.transform.position;
                entry.HeadCamera.transform.position+=Vector3.right*.2f;for(int i=0;i<3;i++)yield return null;
                Check(Quaternion.Angle(entry.Preview.Avatar.transform.rotation,initialFacing)<.01f,"body-not-continuous-camera-billboard-"+folder);entry.HeadCamera.transform.position=oldCameraPosition;
                entry.Panel.Expand();Check(entry.Panel.IsExpanded,"panel-reopens-"+folder);entry.Panel.Collapse();
                if(folder=="local-smallbluefish-alpha2")
                {
                    var bodyRig=entry.Preview.Avatar.Instance.Runtime.ControlRig;
                    var idleChest=bodyRig.GetBoneTransform(HumanBodyBones.Chest);
                    var idleHead=bodyRig.GetBoneTransform(HumanBodyBones.Head);
                    var chestStart=idleChest.localRotation;var headStart=idleHead.localRotation;
                    float chestTravel=0,headTravel=0,idleUntil=Time.unscaledTime+18;
                    while(Time.unscaledTime<idleUntil&&(chestTravel<.22f||headTravel<1.2f))
                    {
                        yield return null;
                        chestTravel=Mathf.Max(chestTravel,Quaternion.Angle(chestStart,idleChest.localRotation));
                        headTravel=Mathf.Max(headTravel,Quaternion.Angle(headStart,idleHead.localRotation));
                    }
                    Check(chestTravel>=.22f&&headTravel>=1.2f,"头显静止时胸部呼吸与头部自然张望");
                }
                var home=entry.TrackingSpace.position;
                var pet=entry.Preview.Avatar.transform;
                var rear=pet.position-pet.forward*.65f;
                rear.y=entry.HeadCamera.transform.position.y;
                entry.TrackingSpace.position+=rear-entry.HeadCamera.transform.position;
                entry.Panel.Expand();
                until=Time.unscaledTime+5;
                while(Time.unscaledTime<until&&Vector3.Dot(pet.forward,Vector3.ProjectOnPlane(rear-pet.position,Vector3.up).normalized)<.98f)yield return null;
                Check(Vector3.Dot(pet.forward,Vector3.ProjectOnPlane(rear-pet.position,Vector3.up).normalized)>.95f,"实际XR入口桌后自动回望-"+folder);
                entry.TrackingSpace.position=home;entry.Panel.Collapse();
                var core=entry.Preview.Interaction;                var fish=entry.Preview.Avatar;Physics.SyncTransforms();var crown=fish.TouchProxy.bounds.center;
                Pose(crown);until=Time.unscaledTime+1;while(!fish.TouchSession.Active&&Time.unscaledTime<until)yield return null;
                Check(fish.TouchSession.Active,"xr-direct-contact-starts-session-"+folder);
                var endCount=fish.TouchSession.CompletedCount;
                for(int stroke=0;stroke<8;stroke++){Pose(crown+(Vector3.right-Vector3.up*.65f)*(fish.HeightMeters*.012f*stroke));yield return null;}
                Check(fish.TouchCompression<.025f,"沿头弧线斜向抚摸保持轻抚-"+folder);
                Pose(crown);yield return null;
                for(int i=0;i<10;i++){Pose(crown-Vector3.up*(fish.HeightMeters*.003f*i));yield return null;}
                until=Time.unscaledTime+.15f;while(Time.unscaledTime<until)yield return null;
                Check(fish.TouchCompression>.001f,"xr-hand-downward-press-"+folder);
                Pose(crown+Vector3.up*.4f);until=Time.unscaledTime+1;while(fish.TouchSession.Active&&Time.unscaledTime<until)yield return null;
                Check(!fish.TouchSession.Active&&fish.TouchSession.CompletedCount==endCount+1,"xr-leave-ends-one-stroke-"+folder);
                var target=crown;
                Pose(target);for(int i=0;i<8;i++)yield return null;
                Pose(target,true);for(int i=0;i<5;i++)yield return null;Check(core.State==PetState.Held,"near-hand-pinch-grabs-through-xri-"+folder);
                Pose(target,false);yield return null;Pose(target,true);yield return null;
                Check(core.State==PetState.Held,"捏合短暂抖动后仍提住-"+folder);
                Pose(target+Vector3.up*.3f,true);for(int i=0;i<5;i++)yield return null;Check(core.Position.Y>1.3f,"hand-lifts-character-"+folder);
                                until=Time.unscaledTime+.5f;while(Time.unscaledTime<until)yield return null;
                var avatar=entry.Preview.Avatar;var handPosition=target+Vector3.up*.3f;
                Check(Vector3.Distance(avatar.HeadGripBone.position+Vector3.up*avatar.HeightMeters*.08f,handPosition)<avatar.HeightMeters*.10f,"提起头部紧贴捏合点-"+folder);
                avatar.Instance.TryGetBoneTransform(HumanBodyBones.LeftFoot,out var lf);avatar.Instance.TryGetBoneTransform(HumanBodyBones.RightFoot,out var rf);
                Check(Vector3.Angle(avatar.HeadGripBone.position-(lf.position+rf.position)*.5f,Vector3.up)<65,"提起时保留斜度且头高于脚-"+folder);
                var beforeTurn=entry.Preview.SpatialRotation;
                Pose(target+Vector3.up*.3f,true,gripRotation:Quaternion.Euler(0,70,0));for(int i=0;i<5;i++)yield return null;
                Check(Quaternion.Angle(entry.Preview.SpatialRotation,Quaternion.Euler(0,70,0)*beforeTurn)<1,"held-wrist-turn-adjusts-direction-"+folder);
                Check(Vector3.Dot(entry.Preview.Avatar.transform.up,Vector3.up)>.999f,"held-yaw-keeps-body-upright-"+folder);
                var spiralKey=UniVRM10.ExpressionKey.CreateCustom("eyeSpiral");
                if(avatar.UsesMouth24Face)
                {
                    var expression=avatar.Instance.Runtime.Expression;
                    var shockKey=UniVRM10.ExpressionKey.CreateCustom("eyeCircleShock");
                    for(int pass=0;pass<2;pass++)
                    {
                        for(int stroke=0;stroke<8;stroke++)
                        {
                            Pose(handPosition+Vector3.right*(stroke%2==0?.065f:-.065f),true,gripRotation:Quaternion.Euler(0,70,0));
                            yield return null;
                            yield return null;
                        }
                        Check(expression.GetWeight(spiralKey)>.99f&&expression.GetWeight(shockKey)<.01f,"shake-replaces-shock-"+pass);
                        if(pass==0)
                        {
                            until=Time.unscaledTime+2f;while(Time.unscaledTime<until)yield return null;
                            Check(expression.GetWeight(spiralKey)>.99f,"still-dizzy-after-two-seconds");
                            until=Time.unscaledTime+1.18f;while(Time.unscaledTime<until)yield return null;
                            Check(expression.GetWeight(UniVRM10.ExpressionKey.CreateCustom("eyeTroubled"))>.99f,"held-scrunch-before-open");
                            until=Time.unscaledTime+.6f;while(Time.unscaledTime<until)yield return null;
                            Check(expression.GetWeight(spiralKey)<.01f&&expression.GetWeight(shockKey)>.99f,"still-held-recovers-from-shake");
                        }
                    }
                }
                var manualRotation=entry.Preview.SpatialRotation;
                Check(!entry.Preview.TryHeldYaw("wrong-owner",core.Generation,0),"wrong-hand-cannot-rotate-held-body-"+folder);
                Pose(target+Vector3.up*.3f,gripRotation:Quaternion.Euler(0,70,0));until=Time.unscaledTime+.23f;while(core.State==PetState.Held&&Time.unscaledTime<until)yield return null;Check(core.State==PetState.Descending,"unpinch-descends-"+folder);
                if(avatar.UsesMouth24Face)Check(avatar.Instance.Runtime.Expression.GetWeight(spiralKey)>.99f,"descent-retains-spiral");
                while(core.State==PetState.Descending)yield return null;
                yield return null;
                if(avatar.UsesMouth24Face)Check(avatar.Instance.Runtime.Expression.GetWeight(spiralKey)<.01f&&avatar.Instance.Runtime.Expression.GetWeight(UniVRM10.ExpressionKey.CreateCustom("eyeTroubled"))>.99f,"contact-switches-to-scrunch");
                while(core.State!=PetState.Idle)yield return null;Check(Mathf.Abs(core.Position.Y-1.1f)<.002f,"lands-on-calibrated-table-"+folder);
                Check(Quaternion.Angle(entry.Preview.SpatialRotation,manualRotation)<1,"release-preserves-manual-heading-"+folder);
                float preserveUntil=Time.unscaledTime+1.2f;Pose(target+Vector3.up*.5f);while(Time.unscaledTime<preserveUntil)yield return null;
                Check(Quaternion.Angle(entry.Preview.SpatialRotation,manualRotation)<1,"idle-does-not-immediately-undo-wrist-heading-"+folder);
                Pose(entry.Preview.Avatar.TouchProxy.bounds.center);float touchDeadline=Time.unscaledTime+.15f;while(Time.unscaledTime<touchDeadline)yield return null;Check(core.State==PetState.Touched,"bare-hand-near-head-touch-"+folder);
                Pose(target,false,false);for(int i=0;i<4;i++)yield return null;Check(core.State!=PetState.Touched&&core.State!=PetState.Held,"hand-tracking-loss-cancels-interaction-"+folder);
                Pose(target,false,true,true);for(int i=0;i<4;i++)yield return null;Check(!entry.RightBareHand.CanPinch,"system-gesture-does-not-select-"+folder);
                Pose(new Vector3(.6f,1.1f,.55f));yield return null;
            }
            var failure=entry.Preview.SwitchCharacterAsync("missing-probe-package");while(!failure.IsCompleted)yield return null;yield return null;
            Check(!failure.Result&&entry.Preview.LoadingPhase==FeiyuPreview.LoadPhase.Failed,"missing-package-fails-explicitly");
            Check(entry.Panel.Canvas.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t=>t.text.Contains("载入失败")),"world-panel-shows-load-error");
            entry.Placement.Begin();Check(entry.Placement.AcceptPoint(new Vector3(.6f,1.1f,.55f))&&entry.Placement.AcceptPoint(new Vector3(1.05f,1.1f,.55f))&&entry.Placement.AcceptPoint(new Vector3(1.05f,1.1f,.9f)),"calibration-works-while-model-failed");
            Check(entry.Placement.HasPendingPlane,"table-kept-pending-until-retry");
            var retry=entry.Preview.RetryCharacterLoadAsync();while(!retry.IsCompleted)yield return null;yield return null;
            Check(retry.Result&&entry.Preview.Interaction.PlaneConfirmed,"retry-places-on-previously-marked-table");
            entry.Placement.Begin();var center=new Vector3(.6f,1.1f,.55f);var rotation=Quaternion.Euler(0,37,0);
            Check(entry.Placement.AcceptPoint(center)&&entry.Placement.AcceptPoint(center+rotation*new Vector3(.45f,0,0))&&entry.Placement.AcceptPoint(center+rotation*new Vector3(.45f,0,.35f)),"rotated-three-point-calibration");
            Check(Mathf.Abs(entry.Preview.Interaction.PlaneYawRadians*Mathf.Rad2Deg-37)<.01f,"core-preserves-table-yaw");
            Check(Quaternion.Angle(entry.Bridge.Table.transform.rotation,rotation)<.01f,"physical-table-matches-bounds-yaw");
            Check(entry.Placement.ChooseSurface(center,.5f,.3f,-28),"scanned-frame-shared-placement-entry");
            Check(Mathf.Abs(entry.Preview.Interaction.PlaneYawRadians*Mathf.Rad2Deg+28)<.01f,"scanned-frame-yaw-preserved");
            var material=Resources.Load<Material>("QuestHands/Materials/HandsDefaultMaterial");var color=material.color;
            Check(Mathf.Abs(color.r-color.b)<.001f&&Mathf.Abs(color.r-color.g)<.001f&&color.a>.1f&&color.a<.6f,"neutral-translucent-hand-material");
            // 官方 ARPlane 数据夹具只验证适配层，不冒充 Quest 原生房间扫描。
            var planeObject=new GameObject("Synthetic room table");var plane=planeObject.AddComponent<ARPlane>();
            var planeRotation=Quaternion.Euler(0,53,0);planeObject.transform.SetPositionAndRotation(center,planeRotation);
            var data=new BoundedPlane(new TrackableId(10,20),TrackableId.invalidId,new Pose(center,planeRotation),new Vector2(.12f,-.08f),new Vector2(1.1f,.7f),PlaneAlignment.HorizontalUp,TrackingState.Tracking,IntPtr.Zero,PlaneClassifications.Table);
            typeof(ARPlane).BaseType.GetMethod("SetSessionRelativeData",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(plane,new object[]{data});
            entry.EnvironmentSurfaces.BeginSelection();typeof(QuestEnvironmentSurfaces).GetMethod("Upsert",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(entry.EnvironmentSurfaces,new object[]{plane});
            Check(entry.EnvironmentSurfaces.SurfaceCount==1,"ar-plane-table-becomes-selectable");
            var selectable=plane.GetComponentInChildren<BoxCollider>();
            Check(Vector3.Distance(selectable.transform.position,plane.center)<.001f,"ar-plane-center-offset-transformed-once");
            float dwellEnd=Time.unscaledTime+2;
            while(entry.EnvironmentSurfaces.Selecting && Time.unscaledTime<dwellEnd)
            {WalkHandFixture.Feed(entry,plane.center+Vector3.up*.02f,Quaternion.identity,true);yield return null;}
            Check(Mathf.Abs(entry.Preview.Interaction.PlaneYawRadians*Mathf.Rad2Deg-53)<.01f,"selected-ar-plane-yaw-preserved");
            Check(!entry.EnvironmentSurfaces.Selecting&&!selectable.gameObject.activeSelf,"selected-room-surfaces-stop-intercepting-hands");
            UnityEngine.Object.Destroy(planeObject);
            // 标准 Meta Aim 输入 → XRI 平台射线 → 同一行走入口；不直接调用目标回调。
            var walk=entry.Preview.Locomotion;var prepared=entry.Preview.Avatar.Motions.PrepareLocomotion();while(!prepared.IsCompleted)yield return null;Check(prepared.Result,"walk-assets-ready");
            var walkPoint=FeiyuPreview.ToUnity(entry.Preview.Interaction.PlaneCenter)+Quaternion.Euler(0,53,0)*new Vector3(.16f,0,.12f);
            var rayOrigin=walkPoint+Vector3.up*.4f;var rayRotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
            Pose(rayOrigin,false,true,false,rayRotation);for(int i=0;i<8;i++)yield return null;
            entry.Bridge.BeginWalkSelection();for(int i=0;i<8;i++)yield return null;
            Check(entry.Bridge.SelectingWalk&&!entry.Panel.IsExpanded,"walk-target-mode-hides-panel");
            Check(entry.RightRay.hasHover,"walk-target-ray-hits-confirmed-platform");
            Pose(rayOrigin,true,true,false,rayRotation);for(int i=0;i<10;i++)yield return null;
            Check(walk.Active&&!entry.Bridge.SelectingWalk,"single-hand-pinch-starts-walking-through-xri");
            Check((entry.Bridge.Table.interactionLayers.value&entry.RightRay.interactionLayers.value)==0,"walk-ray-target-disabled-after-confirm");
            Pose(rayOrigin,false,true,false,rayRotation);for(int i=0;i<4;i++)yield return null;
            entry.SendMessage("OnApplicationFocus",false);for(int i=0;i<3;i++)yield return null;Check(!walk.Active&&entry.TableLocked,"pause-cancels-walk-without-erasing-table");entry.SendMessage("OnApplicationFocus",true);
            entry.Bridge.BeginWalkSelection();for(int i=0;i<4;i++)yield return null;entry.Panel.ShowSpace();for(int i=0;i<4;i++)yield return null;Check(!entry.Bridge.SelectingWalk&&!walk.Active,"opening-menu-cancels-target-selection");
            entry.Panel.Collapse();entry.Preview.Avatar.Motions.EndLocomotion(true);entry.Preview.Avatar.Motions.SetSpatialAllowed(true);
            Pose(rayOrigin,false,true,false,rayRotation);for(int i=0;i<5;i++)yield return null;
            float gestureUntil=Time.unscaledTime+.4f;while(Time.unscaledTime<gestureUntil){WalkHandFixture.Feed(entry,rayOrigin,rayRotation,true);yield return null;}
            Check(!walk.Active&&entry.WalkGestures.PointProgress>0,"official-point-shape-starts-dwell-without-button");
            gestureUntil=Time.unscaledTime+.45f;while(Time.unscaledTime<gestureUntil){WalkHandFixture.Feed(entry,rayOrigin,rayRotation,true);yield return null;}
            Check(walk.Active&&!entry.Bridge.SelectingWalk,"stable-index-point-starts-walk-without-menu-or-pinch");
            var forward=Vector3.ProjectOnPlane(walk.Target-FeiyuPreview.ToUnity(entry.Preview.Interaction.Position),Vector3.up).normalized;
            var palm=FeiyuPreview.ToUnity(entry.Preview.Interaction.Position)+forward*.07f+Vector3.up*entry.Preview.Avatar.HeightMeters*.45f;
            var palmRotation=Quaternion.FromToRotation(Vector3.up,forward);Pose(palm,false,true,false,palmRotation);
            gestureUntil=Time.unscaledTime+.2f;while(Time.unscaledTime<gestureUntil){WalkHandFixture.Feed(entry,palm,palmRotation,false);yield return null;}
            Check(walk.Blocked,"open-palm-in-route-pauses-walking");var blockedPosition=entry.Preview.Interaction.Position;
            gestureUntil=Time.unscaledTime+.25f;while(Time.unscaledTime<gestureUntil){WalkHandFixture.Feed(entry,palm,palmRotation,false);yield return null;}
            Check(Vector3.Distance(FeiyuPreview.ToUnity(blockedPosition),FeiyuPreview.ToUnity(entry.Preview.Interaction.Position))<.001f,"palm-block-keeps-root-still");
            Pose(rayOrigin,false,true,false,palmRotation);gestureUntil=Time.unscaledTime+.4f;while(Time.unscaledTime<gestureUntil){WalkHandFixture.Feed(entry,rayOrigin,palmRotation,false);yield return null;}
            Check(walk.Active&&!walk.Blocked,"moving-palm-away-resumes-same-goal");
            palm=FeiyuPreview.ToUnity(entry.Preview.Interaction.Position)+forward*.07f+Vector3.up*entry.Preview.Avatar.HeightMeters*.45f;Pose(palm,false,true,false,palmRotation);
            gestureUntil=Time.unscaledTime+.2f;while(Time.unscaledTime<gestureUntil){WalkHandFixture.Feed(entry,palm,palmRotation,false);yield return null;}
            Check(walk.Blocked,"palm-can-block-again");Pose(palm,false,false);WalkHandFixture.Feed(entry,palm,palmRotation,false,false);for(int i=0;i<6;i++)yield return null;
            Check(!walk.Active&&entry.TableLocked,"lost-blocking-hand-cancels-goal-without-unlocking-table");
            walk.Cancel(true);entry.Panel.Collapse();Pose(rayOrigin);
            var tappedFish=entry.Preview.Avatar;
            float tapReadyUntil=Time.unscaledTime+2;
            while((tappedFish.Motions.Busy||!entry.RightBareHand.CanPinch)&&Time.unscaledTime<tapReadyUntil)yield return null;
            Check(!tappedFish.Motions.Busy&&entry.RightBareHand.CanPinch,"轻点前模型与右手就绪");
            var ahead=tappedFish.transform.forward;
            var forehead=tappedFish.TouchProxy.bounds.center+Vector3.up*tappedFish.HeightMeters*.012f+ahead*tappedFish.HeightMeters*.14f;
            var towardFace=Quaternion.LookRotation(-ahead,Vector3.up);
            var pokePalm=forehead+ahead*.103f;
            int tapsBefore=tappedFish.ForeheadTapCount;
            for(int sample=0;sample<4;sample++){WalkHandFixture.Feed(entry,pokePalm+ahead*.04f,towardFace,true);yield return null;}
            for(int sample=0;sample<4;sample++){WalkHandFixture.Feed(entry,pokePalm-ahead*.035f,towardFace,true);yield return null;}
            Check(tappedFish.ForeheadTapCount==tapsBefore+1,"正面食指点额头只后仰一次 "+entry.Preview.Interaction.State+" "+entry.Panel.IsExpanded+" "+tappedFish.Motions.Status);
            var downward=Quaternion.LookRotation(Vector3.down,Vector3.forward);
            var aimPoint=FeiyuPreview.ToUnity(entry.Preview.Interaction.Position)+Vector3.right*.14f;
            foreach(float surfaceError in new[]{.03f,-.03f})
            {
                walk.Cancel(true);entry.Preview.Avatar.Motions.EndLocomotion(true);entry.Preview.Avatar.Motions.SetSpatialAllowed(true);
                var tapPalm=aimPoint+Vector3.up*(surfaceError+.103f);
                for(int beat=0;beat<2;beat++)
                {
                    float end=Time.unscaledTime+.10f;
                    while(Time.unscaledTime<end){WalkHandFixture.Feed(entry,tapPalm+Vector3.up*.035f,downward,true);yield return null;}
                    end=Time.unscaledTime+.13f;
                    while(Time.unscaledTime<end){WalkHandFixture.Feed(entry,tapPalm,downward,true);yield return null;}
                }
                Check(walk.Active&&walk.SpeedMultiplier>1,"连续敲击桌面误差带触发提速 "+surfaceError);
                Check(Mathf.Abs(walk.Target.y-entry.Preview.Interaction.PlaneCenter.Y)<.0001f,"敲击点投影到锁定桌面");
                var water=walk.transform.Find("已确认桌面落点");
                Check(water!=null&&Vector3.Distance(water.position,walk.Target+Vector3.up*.005f)<.001f,"水纹位于已接受落点");
                float waterDeadline=Time.unscaledTime+.4f;bool twoRipples=false;
                while(Time.unscaledTime<waterDeadline&&!twoRipples)
                {
                    var lines=water.GetComponentsInChildren<LineRenderer>();
                    twoRipples=lines.Length==2&&lines[0].enabled&&lines[1].enabled;
                    if(!twoRipples)yield return null;
                }
                Check(twoRipples,"连续敲击出现双层水纹");
                if(surfaceError>0)
                {
                    var cam=new GameObject("桌面敲击近景").AddComponent<Camera>();
                    cam.orthographic=true;cam.orthographicSize=.07f;cam.nearClipPlane=.01f;
                    cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.09f,.13f,.17f);
                    cam.transform.position=water.position+new Vector3(.04f,.22f,.12f);cam.transform.LookAt(water.position);
                    Alpha2Probe.Capture(cam,Path.Combine(Output,"accepted-ripple.png"));UnityEngine.Object.Destroy(cam.gameObject);
                }
                WalkHandFixture.Feed(entry,tapPalm,downward,true,false);yield return null;
            }
            walk.Cancel(true);
            QuestQualityCapture.Run(entry,Output);
            Check(entry.TableLocked,"table-locked-before-tracking-events");
            var trackingFlags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(QuestEntry).GetMethod("OriginChanged",trackingFlags).Invoke(entry,new object[]{null});
            Check(entry.TableLocked,"origin-update-keeps-table");
            var lostHead=new UnityEngine.XR.XRNodeState {nodeType=UnityEngine.XR.XRNode.Head};
            typeof(QuestEntry).GetMethod("TrackingLost",trackingFlags).Invoke(entry,new object[]{lostHead});
            Check(entry.TableLocked,"head-tracking-loss-keeps-table");
            entry.Suspend();Check(!entry.Preview.Interaction.PlaneConfirmed,"explicit-suspend-invalidates-table");
        }
        static void Finish(int code)
        {
            EditorApplication.update-=Tick;if(hand!=null){typeof(XRHandDevice).GetProperty("rightHand").GetSetMethod(true).Invoke(null,new object[]{null});InputSystem.RemoveDevice(hand);}if(aim!=null){MetaAimHand.right=null;InputSystem.RemoveDevice(aim);}
            File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["deviceTested"]=false}.ToString());Debug.Log(code==0?"XR_M1_PASSED":"XR_HANDS_FAILED");EditorApplication.Exit(code);
        }
    }
    // 在 PlayerLoop 读取动态输入缓冲，避免 EditorApplication.update 读取 Editor 缓冲。
    public sealed class QuestHandsProbeRunner:MonoBehaviour{void Update()=>QuestHandsProbe.GameTick();}
}
