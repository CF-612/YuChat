using System;
using System.Collections;
using System.IO;
using System.Reflection;
using LingChatXR.Chat;
using LingChatXR.Core;
using LingChatXR.XR;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace LingChatXR.Editor
{
    public static class QuestSizeProbe
    {
        static QuestEntry entry;static XRHandDevice left,right;static MetaAimHand leftAim,rightAim;
        static IEnumerator work;static bool started;static double deadline;static JArray checks=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");EditorSceneManager.OpenScene(QuestProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+360;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick(){if(EditorApplication.timeSinceStartup>deadline){Finish(new Exception("timeout"));return;}if(EditorApplication.isPlaying&&!started){started=true;new GameObject("Size probe runner").AddComponent<QuestSizeProbeRunner>();}}
        public static void GameTick(){try{work??=Checks();if(!work.MoveNext())Finish(null);}catch(Exception error){Finish(error);}}
        static void Check(bool passed,string label){checks.Add(new JObject{["name"]=label,["passed"]=passed});if(!passed)throw new Exception(label);}
        static XRHandDevice Create(bool isLeft)
        {
            var device=InputSystem.AddDevice<XRHandDevice>();InputSystem.SetDeviceUsage(device,isLeft?UnityEngine.InputSystem.CommonUsages.LeftHand:UnityEngine.InputSystem.CommonUsages.RightHand);
            typeof(XRHandDevice).GetProperty(isLeft?"leftHand":"rightHand").GetSetMethod(true).Invoke(null,new object[]{device});return device;
        }
        static void Hand(XRHandDevice device,MetaAimHand aim,Vector3 position,bool pinch,bool tracked,bool system,Quaternion? rotationOverride=null)
        {
            var rotation=rotationOverride??Quaternion.LookRotation(Vector3.up,Vector3.forward);
            using(StateEvent.From(device,out var sample))
            {
                device.isTracked.WriteValueIntoEvent(tracked?1f:0f,sample);device.trackingState.WriteValueIntoEvent(tracked?3:0,sample);
                device.devicePosition.WriteValueIntoEvent(position,sample);device.deviceRotation.WriteValueIntoEvent(rotation,sample);
                device.pinchPosition.WriteValueIntoEvent(position,sample);device.gripPosition.WriteValueIntoEvent(position,sample);device.gripRotation.WriteValueIntoEvent(rotation,sample);
                device.pokePosition.WriteValueIntoEvent(position,sample);device.pokeRotation.WriteValueIntoEvent(rotation,sample);InputSystem.QueueEvent(sample);
            }
            var flags=MetaAimFlags.Computed|MetaAimFlags.Valid;if(system)flags|=MetaAimFlags.SystemGesture;
            aim.UpdateHand(tracked,flags,new Pose(position,rotation),pinch?1:0,0,0,0);
        }
        static void Pose(float distance,bool pinch=false,bool leftTracked=true,bool system=false)
        {
            var center=new Vector3(.6f,1.55f,.55f);Hand(left,leftAim,center-Vector3.right*distance*.5f,pinch,leftTracked,system);Hand(right,rightAim,center+Vector3.right*distance*.5f,pinch,true,system);
        }
        static bool BeginStretch(){return entry.SizeAdjustment.Begin();}
        static IEnumerator Frames(){for(int i=0;i<4;i++)yield return null;}
        static IEnumerator Checks()
        {
            entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            left=Create(true);right=Create(false);leftAim=MetaAimHand.CreateHand(InputDeviceCharacteristics.Left);rightAim=MetaAimHand.CreateHand(InputDeviceCharacteristics.Right);MetaAimHand.left=leftAim;MetaAimHand.right=rightAim;
            Pose(.3f);while(!entry.Preview.Ready)yield return null;for(int i=0;i<5;i++)yield return null;
            entry.Panel.ShowSpace();var openedAt=entry.Panel.Canvas.transform.position;
            // 模拟用户转头看菜单后切页；页面导航不应再次把菜单挪到当前视线左侧。
            entry.HeadCamera.transform.LookAt(openedAt);entry.Panel.ShowDialogue();
            Check(Vector3.Distance(openedAt,entry.Panel.Canvas.transform.position)<.001f,"tab-navigation-must-not-reposition-panel");
            // 回归用户实际阻塞：两个组件都收到焦点/暂停事件，不能只测 QuestEntry。
            Check(entry.ConfirmTable(new Vector3(.6f,1.1f,.55f),.6f,.4f,37),"recovery-place");
            var tablePosition=entry.Bridge.Table.transform.position;var tableRotation=entry.Bridge.Table.transform.rotation;
            entry.Panel.Collapse();
            foreach(var callback in new[]{"OnApplicationFocus","OnApplicationPause"})
            {
                bool interrupted=callback=="OnApplicationPause";
                entry.Preview.SendMessage(callback,interrupted);entry.SendMessage(callback,interrupted);
                Check(entry.TableLocked&&!entry.Panel.IsExpanded&&!entry.InputAvailable,"transient-keeps-table-and-collapsed-panel-"+callback);
                Check(!entry.LeftBareHand.CanPinch&&!entry.RightBareHand.CanPinch,"transient-gates-input-"+callback);
                entry.Preview.SendMessage(callback,!interrupted);entry.SendMessage(callback,!interrupted);
                Check(entry.InputAvailable&&entry.TableLocked,"resume-keeps-table-"+callback);
            }
            typeof(QuestEntry).GetMethod("OriginChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(entry,new object[]{null});
            Check(entry.TableLocked&&entry.Bridge.Table.transform.position==tablePosition&&entry.Bridge.Table.transform.rotation==tableRotation&&!entry.Panel.IsExpanded,"origin-notice-keeps-table-pose");
            entry.ConfigureInputModality();var modality=entry.GetComponentInChildren<XRInputModalityManager>();
            foreach(var mode in new[]{XRInputModalityManager.InputMode.TrackedHand,XRInputModalityManager.InputMode.MotionController,XRInputModalityManager.InputMode.None,XRInputModalityManager.InputMode.TrackedHand})
            {
                typeof(XRInputModalityManager).GetMethod("SetLeftMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(modality,new object[]{mode});
                typeof(XRInputModalityManager).GetMethod("SetRightMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(modality,new object[]{mode});
                Check(entry.LeftBareHand.gameObject.activeInHierarchy&&entry.RightBareHand.gameObject.activeInHierarchy,"modality-cannot-disable-live-hands-"+mode);
            }
            entry.LeftBareHand.gameObject.SetActive(false);entry.RecoverHands();Check(entry.LeftBareHand.gameObject.activeInHierarchy,"recovery-reactivates-hand-root");
            Pose(.3f,false,false);for(int i=0;i<4;i++)yield return null;
            Check(!entry.LeftRay.enabled&&entry.TableLocked,"lost-hand-disables-interaction-keeps-table");
            Pose(.3f);for(int i=0;i<4;i++)yield return null;
            Check(entry.LeftRay.enabled&&entry.RightRay.enabled&&entry.TableLocked,"hand-reacquired-without-replacement");
            Check(Vector3.Distance(entry.Panel.PalmMenu.leftPalmAnchor.position,entry.TrackingSpace.TransformPoint(left.gripPosition.ReadValue()))<.001f,"palm-anchor-is-hand-grip-not-controller");
            entry.HeadCamera.transform.SetPositionAndRotation(new Vector3(0,1.6f,0),Quaternion.identity);
            Hand(left,leftAim,new Vector3(-.1f,1.3f,.45f),false,true,false,Quaternion.Euler(135,0,0));
            float palmUntil=Time.unscaledTime+.4f;while(Time.unscaledTime<palmUntil)yield return null;
            Check(entry.Panel.PalmCanvas.gameObject.activeInHierarchy&&Vector3.Distance(entry.Panel.PalmCanvas.transform.position,entry.Panel.PalmMenu.leftPalmAnchor.position)<.08f,"official-palm-menu-shows-at-palm");
            var palmAt=entry.Panel.PalmCanvas.transform.position;
            Hand(left,leftAim,new Vector3(.1f,1.3f,.45f),false,true,false,Quaternion.Euler(135,0,0));
            palmUntil=Time.unscaledTime+.25f;while(Time.unscaledTime<palmUntil)yield return null;
            Check(Vector3.Distance(entry.Panel.PalmCanvas.transform.position,palmAt)>.15f&&Vector3.Distance(entry.Panel.PalmCanvas.transform.position,entry.Panel.PalmMenu.leftPalmAnchor.position)<.08f,"palm-menu-follows-moving-hand-not-world-point");
            Hand(left,leftAim,new Vector3(.1f,1.3f,.45f),false,true,false,Quaternion.identity);
            Hand(right,rightAim,new Vector3(.25f,1.3f,.45f),false,false,false);
            palmUntil=Time.unscaledTime+.3f;while(Time.unscaledTime<palmUntil)yield return null;
            Check(!entry.Panel.PalmCanvas.gameObject.activeInHierarchy,"palm-down-hides-menu");
            Pose(.3f);for(int i=0;i<4;i++)yield return null;
            Hand(left,leftAim,new Vector3(-.1f,1.3f,.45f),false,true,false,Quaternion.Euler(135,0,0));
            palmUntil=Time.unscaledTime+.4f;while(Time.unscaledTime<palmUntil)yield return null;
            entry.Panel.ShowSpace();yield return null;CapturePanel(entry.Panel.Canvas,"space-ui.png");
            var buttonRect=(RectTransform)entry.Panel.ConfirmButton.transform;var target=buttonRect.TransformPoint(buttonRect.rect.center);var pointerOrigin=entry.HeadCamera.transform.position+new Vector3(.15f,-.15f,0);
            Hand(right,rightAim,pointerOrigin,false,true,false,Quaternion.LookRotation(target-pointerOrigin));
            for(int i=0;i<8;i++)yield return null;
            // batchmode 无头显 BeforeRender；显式执行原版 LineVisual 的同一渲染阶段。
            var lineVisual=entry.RightRay.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            typeof(UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual).GetMethod("UpdateLineVisual",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lineVisual,null);
            File.WriteAllText(Path.Combine(Output,"ui-ray.json"),new JObject{["target"]=target.ToString("F4"),["panel"]=entry.Panel.Canvas.transform.position.ToString("F4"),["rayPosition"]=entry.RightRay.transform.position.ToString("F4"),["rayForward"]=entry.RightRay.transform.forward.ToString("F4"),["rayEnabled"]=entry.RightRay.enabled,["canPinch"]=entry.RightBareHand.CanPinch,["hasHit"]=entry.RightRay.TryGetCurrentUIRaycastResult(out var debugHit),["hit"]=debugHit.gameObject?.name,["visual"]=entry.RightBareHand.PointerVisible}.ToString());
            Check(entry.RightRay.TryGetCurrentUIRaycastResult(out var menuHit)&&menuHit.gameObject!=null&&entry.RightBareHand.PointerVisible,"ray-visible-only-when-pointing-at-ui");
            entry.Panel.ShowDialogue();yield return null;CapturePanel(entry.Panel.Canvas,"dialogue-ui.png");
            entry.Panel.Collapse();Pose(.3f);for(int i=0;i<4;i++)yield return null;
            CapturePanel(entry.Panel.PalmCanvas,"palm-ui.png");
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(entry.Preview.ActivePackageFolder!=folder){var load=entry.Preview.SwitchCharacterAsync(folder);while(!load.IsCompleted)yield return null;Check(load.Result,"load-"+folder);}
                Check(entry.ConfirmTable(new Vector3(.6f,1.1f,.55f),.6f,.4f,37),"place-"+folder);
                var avatar=entry.Preview.Avatar;var initial=avatar.HeightMeters;var position=avatar.transform.position;var rotation=avatar.transform.rotation;
                float waitIdle=Time.unscaledTime+12;
                while(avatar.Motions.Busy&&Time.unscaledTime<waitIdle)yield return null;
                var head=avatar.HeadGripBone;
                var upHand=head.position+avatar.transform.forward*.11f+Vector3.up*.16f+avatar.transform.right*.06f;
                var sideHand=head.position+avatar.transform.forward*.13f-avatar.transform.right*.09f;
                Vector3 Aim()=>avatar.transform.InverseTransformDirection((avatar.Instance.LookAtTarget.position-head.position).normalized);
                void Hands(Vector3 l,Vector3 r){Hand(left,leftAim,l,false,true,false);Hand(right,rightAim,r,false,true,false);}
                Hands(sideHand,upHand);
                float settle=Time.unscaledTime+.65f;while(Time.unscaledTime<settle)yield return null;
                Check(avatar.FollowingHand&&Aim().x<-.25f,"两手靠近优先看较近的左手 "+folder+" gaze="+Aim()+" following="+avatar.FollowingHand+" busy="+avatar.Motions.Busy+" state="+entry.Preview.Interaction.State+" distances="+Vector3.Distance(head.position,entry.LeftHand.position)+","+Vector3.Distance(head.position,entry.RightHand.position)+" available="+entry.InputAvailable+" camera="+entry.Preview.ViewCamera.name);
                var almostCloser=head.position+avatar.transform.right*.09f+avatar.transform.forward*.115f;
                Hands(sideHand,almostCloser);
                settle=Time.unscaledTime+.3f;while(Time.unscaledTime<settle)yield return null;
                Check(Aim().x<-.25f,"两手距离接近时保留原目标 "+folder);
                var headBeforeLift=head.rotation;
                Hands(sideHand-avatar.transform.right*.19f,upHand);
                settle=Time.unscaledTime+.65f;while(Time.unscaledTime<settle)yield return null;
                Check(avatar.FollowingHand&&Aim().y>.45f&&Aim().x>0,"更近右手抬起时目光上移 "+folder);
                Check(Quaternion.Angle(headBeforeLift,head.rotation)>8,"抬手实际带动头骨 "+folder);
                Hands(avatar.TouchProxy.bounds.center,upHand);
                settle=Time.unscaledTime+.55f;while(Time.unscaledTime<settle)yield return null;
                Check(entry.Preview.Interaction.State==PetState.Touched&&!avatar.FollowingHand&&Aim().y<-.2f,"摸头时低头忽略上方另一只手 "+folder);
                Hands(sideHand-Vector3.right*.7f,upHand);
                settle=Time.unscaledTime+.24f;while(Time.unscaledTime<settle)yield return null;
                Check(!avatar.FollowingHand&&Aim().y<-.2f,"离开头部后短暂保持低头 "+folder);
                settle=Time.unscaledTime+.85f;while(Time.unscaledTime<settle)yield return null;
                Check(avatar.FollowingHand&&Aim().y>.45f,"结束抚摸后恢复跟随举起的手 "+folder);
                Hands(sideHand+Vector3.up*.6f,upHand+Vector3.up*.6f);
                settle=Time.unscaledTime+.3f;while(Time.unscaledTime<settle)yield return null;
                Check(!avatar.FollowingHand,"双手离开后释放注视 "+folder);
                Pose(.3f);yield return null;
                while(!avatar.Motions.CatalogReady.IsCompleted)yield return null;
                var testPermission=new LingChatXR.Chat.XrMotionPreferences{enabled=true};testPermission.items["wave_normal_right_661"]=true;
                avatar.Motions.ApplyPreferences(testPermission);avatar.Motions.SetSpatialAllowed(true);
                var action=avatar.Motions.Play("wave_normal_right_661");while(!action.IsCompleted)yield return null;
                Check(action.Result,"缩放前启动角色动作 "+folder);
                float beforeDragAge=avatar.Motions.InspectPlayback().age;
                Check(entry.SizeAdjustment.Begin(),"开始右手尺寸编辑 "+folder);
                Hand(left,leftAim,Vector3.zero,false,false,false);
                var corner=entry.Panel.ScaleHandlePosition;
                Hand(right,rightAim,corner,false,true,false);var wait=Frames();while(wait.MoveNext())yield return null;
                Check(entry.Panel.ScaleFrame.gameObject.activeInHierarchy&&entry.Panel.SaveSizeButton.isActiveAndEnabled,"单右手可见尺寸框及确认");
                Hand(right,rightAim,corner,true,true,false);wait=Frames();while(wait.MoveNext())yield return null;
                Check(entry.SizeAdjustment.Dragging,"右手抓住右上角箭头");
                var diagonal=corner-avatar.transform.position;
                Hand(right,rightAim,corner+diagonal*.25f,true,true,false);wait=Frames();while(wait.MoveNext())yield return null;
                Check(Mathf.Abs(avatar.HeightMeters-Mathf.Clamp(initial*1.25f,.12f,.45f))<.003f,"向外拖动按比例放大 "+folder);
                Check(avatar==entry.Preview.Avatar&&Vector3.Distance(position,avatar.transform.position)<.001f&&Quaternion.Angle(rotation,avatar.transform.rotation)<.01f,"缩放保留脚底和朝向");
                Hand(right,rightAim,corner+diagonal*.25f,false,true,false);wait=Frames();while(wait.MoveNext())yield return null;
                Check(!entry.Panel.PalmCanvas.gameObject.activeInHierarchy,"缩放时掌面不会遮住箭头");
                float held=avatar.HeightMeters;corner=entry.Panel.ScaleHandlePosition;
                Hand(right,rightAim,corner,true,true,false);wait=Frames();while(wait.MoveNext())yield return null;
                Check(Mathf.Abs(held-avatar.HeightMeters)<.001f,"重新抓把手无尺寸跳变");
                Hand(right,rightAim,corner-(corner-position)*.25f,true,true,false);wait=Frames();while(wait.MoveNext())yield return null;
                Check(avatar.HeightMeters<held-.01f,"向内拖动缩小");
                Check(avatar.Motions.ActiveId=="wave_normal_right_661"&&avatar.Motions.InspectPlayback().age>beforeDragAge,"右手缩放时原动作连续推进 "+folder);
                var cam=new GameObject("尺寸框整体视图").AddComponent<Camera>();cam.orthographic=true;
                cam.orthographicSize=avatar.HeightMeters*.85f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.09f,.14f,.19f);
                var frame=entry.Panel.ScaleFrame.transform;cam.transform.SetPositionAndRotation(frame.position+Vector3.up*.018f-frame.forward*.6f,frame.rotation);
                cam.nearClipPlane=.01f;Canvas.ForceUpdateCanvases();Alpha2Probe.Capture(cam,Path.Combine(Output,"resize-frame-"+folder+".png"));UnityEngine.Object.Destroy(cam.gameObject);
                entry.Panel.CancelSizeButton.onClick.Invoke();
                Check(Mathf.Abs(initial-avatar.HeightMeters)<.001f&&!entry.Panel.ScaleFrame.gameObject.activeSelf,"取消恢复尺寸并收起框");
                Hand(right,rightAim,corner,false,true,false);wait=Frames();while(wait.MoveNext())yield return null;
                Check(!entry.SizeAdjustment.BlocksGrab,"松手恢复抓取");
                Check(entry.SizeAdjustment.Begin(),"再次开始尺寸事务");
                float chosen=Mathf.Clamp(initial+.012f,.12f,.45f);
                Check(entry.SizeAdjustment.PreviewHeight(chosen)&&entry.SizeAdjustment.Commit()&&Mathf.Abs(entry.Preview.ConfiguredHeight-chosen)<.001f,"确认保留尺寸");
                Check(entry.SizeAdjustment.Begin(),"开始空间丢失场景");entry.Suspend();
                Check(!entry.SizeAdjustment.Active&&!entry.SizeAdjustment.Commit(),"头显失追取消未保存事务");
                // 旋转桌面的边缘放大必须由原空间核心拒绝。
                float radius=avatar.FootprintRadius;var center=new Vector3(.6f,1.1f,.55f);var edge=center+Quaternion.Euler(0,37,0)*new Vector3(.6f-radius-.001f,0,0);
                Check(entry.Preview.PlaceOnPlane(center,.6f,.4f,edge,37),"place-at-rotated-edge-"+folder);
                BeginStretch();wait=Frames();while(wait.MoveNext())yield return null;float edgeHeight=avatar.HeightMeters;
                Check(!entry.SizeAdjustment.PreviewHeight(.45f),"桌边拒绝超出足迹的放大");
                Check(Mathf.Abs(avatar.HeightMeters-edgeHeight)<.001f,"edge-growth-rejected-"+folder);entry.SizeAdjustment.Cancel();
                Pose(.3f);wait=Frames();while(wait.MoveNext())yield return null;
            }
            entry.ConfirmTable(new Vector3(.6f,1.1f,.55f),.6f,.4f);Check(BeginStretch(),"begin-before-model-switch");
            var switching=entry.Preview.SwitchCharacterAsync("local-smallbluefish-alpha2");while(!switching.IsCompleted)yield return null;Check(switching.Result&&!entry.SizeAdjustment.Active&&!entry.SizeAdjustment.Commit(),"model-switch-rejects-old-edit");
        }
        public static void CapturePanel(Canvas canvas,string name)
        {
            // 仅隔离场景的 UI 离屏渲染，不采集用户房间或驱动编辑器。
            var root=canvas.transform;bool active=canvas.gameObject.activeSelf;var scale=root.localScale;
            canvas.gameObject.SetActive(true);root.localScale=Vector3.one*((name=="palm-ui.png"?.00065f:.001f)/(root.parent!=null?root.parent.lossyScale.x:1));
            var obj=new GameObject("UI evidence camera");var camera=obj.AddComponent<Camera>();camera.orthographic=true;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.15f,.20f);
            camera.transform.position=root.position-root.forward*2;camera.transform.rotation=root.rotation;
            var bounds=((RectTransform)root).rect;camera.orthographicSize=Mathf.Max(bounds.height*root.lossyScale.y*.5f,bounds.width*root.lossyScale.x*.5f/(1280f/720f))*1.15f;
            int[] layers=Array.ConvertAll(root.GetComponentsInChildren<Transform>(true),t=>t.gameObject.layer);var children=root.GetComponentsInChildren<Transform>(true);
            for(int i=0;i<children.Length;i++)children[i].gameObject.layer=30;camera.cullingMask=1<<30;
            Canvas.ForceUpdateCanvases();Alpha2Probe.Capture(camera,Path.Combine(Output,name));
            for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layers[i];
            root.localScale=scale;canvas.gameObject.SetActive(active);UnityEngine.Object.Destroy(obj);
        }
        static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;
            if(error!=null)File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());
            if(left!=null){typeof(XRHandDevice).GetProperty("leftHand").GetSetMethod(true).Invoke(null,new object[]{null});InputSystem.RemoveDevice(left);}
            if(right!=null){typeof(XRHandDevice).GetProperty("rightHand").GetSetMethod(true).Invoke(null,new object[]{null});InputSystem.RemoveDevice(right);}
            MetaAimHand.left=null;MetaAimHand.right=null;if(leftAim!=null)InputSystem.RemoveDevice(leftAim);if(rightAim!=null)InputSystem.RemoveDevice(rightAim);
            File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["deviceTested"]=false}.ToString());Debug.Log(error==null?"XR_M1_PASSED":"XR_SIZE_FAILED");EditorApplication.Exit(error==null?0:1);
        }
    }
    public sealed class QuestSizeProbeRunner:MonoBehaviour{void Update()=>QuestSizeProbe.GameTick();}
}
