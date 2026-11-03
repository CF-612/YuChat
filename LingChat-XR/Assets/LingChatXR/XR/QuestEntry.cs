using System;
using System.Collections;
using System.Collections.Generic;
using LingChatXR.Chat;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace LingChatXR.XR
{
    // 仅新场景拥有 XR rig。输入/射线/近触复用 XRI，空间根继续只由 PetInteraction 写入。
    [DefaultExecutionOrder(-100)]
    public sealed class QuestEntry : MonoBehaviour
    {
        public Sprite PanelSprite;
        public InputActionAsset InputActions;
        public FeiyuPreview Preview{get;private set;}
        public XRInteractionManager Interactions{get;private set;}
        public Camera HeadCamera{get;private set;}
        public XrWorldPanel Panel{get;private set;}
        public QuestInteractionBridge Bridge{get;private set;}
        public string ConnectionStatus{get;private set;}="正在准备头显连接";
        public readonly List<XRBaseInteractor> Controllers=new List<XRBaseInteractor>();
        readonly List<InputActionReference> references=new List<InputActionReference>();
        readonly List<InputAction> palmActions=new List<InputAction>();
        readonly List<XRInputSubsystem> inputSubsystems=new List<XRInputSubsystem>();
        InputAction headTracked,leftTracked,rightTracked;
        XRManagerSettings manager;bool ownsLoader,headWasTracked,leftWasTracked,rightWasTracked;
        public QuestTablePlacement Placement{get;private set;}
        public QuestWalkGestures WalkGestures{get;private set;}
        public QuestSizeAdjustment SizeAdjustment{get;private set;}
        public QuestEnvironmentSurfaces EnvironmentSurfaces{get;private set;}
        public Transform TrackingSpace{get;private set;}
        public Transform RightHand{get;private set;}
        public Transform LeftHand{get;private set;}
        public XRRayInteractor LeftRay{get;private set;}
        public XRRayInteractor RightRay{get;private set;}
        public QuestTrackedHand LeftBareHand{get;private set;}
        public QuestTrackedHand RightBareHand{get;private set;}
        public bool RightTracked=>RightBareHand!=null&&RightBareHand.Tracked;
        public InputAction RightTrigger=>InputActions.FindAction("XRI Right Interaction/UI Press",true);
        float nextDiagnostic;
        ARCameraManager arCamera;
        bool focusLost,applicationPaused,headLost;float nextRecovery;XRInputModalityManager modality;
        readonly List<XRHandSubsystem> handSubsystems=new List<XRHandSubsystem>();
        public bool InputAvailable=>!focusLost&&!applicationPaused&&!headLost;
        public bool TableLocked=>Preview.Ready&&Preview.Interaction.PlaneConfirmed;
        void Awake()
        {
            Preview=FindAnyObjectByType<FeiyuPreview>();Preview.ExternalInput=true;HeadCamera=Preview.ViewCamera;
            Interactions=new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            if(InputActions==null)InputActions=Resources.Load<InputActionAsset>("QuestInput");
            if(InputActions==null)throw new InvalidOperationException("缺少官方 XRI 输入资产。");
            var origin=new GameObject("XR Origin");
            var offset=new GameObject("Camera Offset");offset.transform.SetParent(origin.transform,false);
            TrackingSpace=offset.transform;
            HeadCamera.transform.SetParent(offset.transform,false);HeadCamera.transform.localPosition=new Vector3(0,1.6f,0);HeadCamera.transform.localRotation=Quaternion.identity;
            Pose(HeadCamera.gameObject,"XRI Head");headTracked=InputActions.FindAction("XRI Head/Is Tracked",true);
            HeadCamera.clearFlags=CameraClearFlags.SolidColor;HeadCamera.backgroundColor=Color.clear;
            QuestRenderQuality.Configure(HeadCamera);
            var xr=origin.AddComponent<XROrigin>();xr.Camera=HeadCamera;xr.CameraFloorOffsetObject=offset;xr.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            var chat=FindAnyObjectByType<XrChatController>();if(chat==null)chat=new GameObject("XR Dialogue").AddComponent<XrChatController>();
            Bridge=gameObject.AddComponent<QuestInteractionBridge>();Bridge.Configure(this);
            Panel=gameObject.AddComponent<XrWorldPanel>();Panel.Configure(this,chat);
            // 先建 UI 入口，再启用射线，避免 XRI 的延迟查找另建第二个 EventSystem。
            Hand(offset.transform,"LeftHand",out leftTracked);Hand(offset.transform,"RightHand",out rightTracked);
            Placement=gameObject.AddComponent<QuestTablePlacement>();Placement.Configure(this);
            SizeAdjustment=gameObject.AddComponent<QuestSizeAdjustment>();SizeAdjustment.Configure(this);
            EnvironmentSurfaces=gameObject.AddComponent<QuestEnvironmentSurfaces>();EnvironmentSurfaces.Configure(this,origin);
            WalkGestures=gameObject.AddComponent<QuestWalkGestures>();WalkGestures.Configure(this);
            Panel.ConfigureHandMenu();
            InputActions.Enable();
        }
        IEnumerator Start()
        {
            if(Environment.GetEnvironmentVariable("XR_SKIP_LOADER")=="1"){ConnectionStatus="隔离检查 · 未连接头显";yield break;}
            manager=XRGeneralSettings.Instance?.Manager;
            if(manager==null){ConnectionStatus="XR 配置未就绪，请先执行 Prepare Quest Entry";yield break;}
            if(manager.activeLoader==null){ownsLoader=true;yield return manager.InitializeLoader();}
            if(!isActiveAndEnabled){CleanupLoader();yield break;}
            if(manager.activeLoader==null){ConnectionStatus="未检测到 OpenXR 头显 · 连接 Quest Link 后重新运行此场景";yield break;}
            manager.StartSubsystems();
            QuestRenderQuality.ConfigureDisplays();
            StartCoroutine(QuestRenderQuality.ReportActualBuffers());
            // 官方 AR Foundation 相机子系统负责透视合成，不读取或上传摄像头像素。
            var session=gameObject.AddComponent<ARSession>();gameObject.AddComponent<ARInputManager>();arCamera=HeadCamera.gameObject.AddComponent<ARCameraManager>();
            EnvironmentSurfaces.Connect(session);
            ConfigureInputModality();
            ConnectionStatus="等待头显与双手追踪 · 请选择真实平台";
            InputTracking.trackingAcquired+=TrackingAcquired;InputTracking.trackingLost+=TrackingLost;
            SubsystemManager.GetSubsystems(inputSubsystems);foreach(var input in inputSubsystems)input.trackingOriginUpdated+=OriginChanged;
        }
        public void ConfigureInputModality()
        {
            if(modality!=null)return;
            var obj=new GameObject("XR Input Modality");obj.SetActive(false);obj.transform.SetParent(transform,false);modality=obj.AddComponent<XRInputModalityManager>();
            // 官方模态管理器优先手柄，不能让它关掉负责手追恢复的整棵手部层级。
            // 仅让它发布 HandMenu 所需的模态；实际输入由 XRHandDevice/MetaAim 每帧判定。
            modality.leftHand=new GameObject("Left modality marker");modality.leftHand.transform.SetParent(obj.transform,false);
            modality.rightHand=new GameObject("Right modality marker");modality.rightHand.transform.SetParent(obj.transform,false);obj.SetActive(true);
        }
        void TrackingAcquired(XRNodeState state){if(state.nodeType==XRNode.Head){headLost=false;RecoverHands();}}
        void TrackingLost(XRNodeState state){if(state.nodeType==XRNode.Head){headLost=true;PauseInteraction();}}
        void OriginChanged(XRInputSubsystem input){PauseInteraction();ConnectionStatus="追踪原点已更新 · 保留锁定桌面，偏移时可重新定位";Debug.Log("XR_SPACE_ORIGIN_NOTICE tableKept="+TableLocked);}
        void Update()
        {
            bool tracked=headTracked.IsPressed(),left=LeftBareHand.Tracked,right=RightTracked;
            if(!headWasTracked&&tracked){headLost=false;RecoverHands();ConnectionStatus=TableLocked?"桌面已锁定":"头显追踪正常 · 请选择真实平台";}
            if(headWasTracked&&!tracked){headLost=true;PauseInteraction();}
            if(leftWasTracked&&!left)LoseHand("LeftHand");if(rightWasTracked&&!right)LoseHand("RightHand");
            if(Preview.Ready)
            {
                bool available=InputAvailable&&TableLocked&&!Preview.SizeEditing&&!Panel.ComposerVisible;
                Preview.Avatar.SetNearbyHands(available&&left?LeftHand.position:(Vector3?)null,available&&right?RightHand.position:(Vector3?)null);
            }
            headWasTracked=tracked;leftWasTracked=left;rightWasTracked=right;
            if(InputAvailable&&(!left||!right)&&Time.unscaledTime>=nextRecovery){nextRecovery=Time.unscaledTime+2;RecoverHands();}
            if(Time.unscaledTime>=nextDiagnostic){nextDiagnostic=Time.unscaledTime+5;handSubsystems.Clear();SubsystemManager.GetSubsystems(handSubsystems);string native="";foreach(var subsystem in handSubsystems)native+=$" running={subsystem.running} nativeLeft={subsystem.leftHand.isTracked} nativeRight={subsystem.rightHand.isTracked}";Debug.Log($"XR_MR_INPUT head={tracked} leftHand={left} rightHand={right} leftActive={LeftBareHand.gameObject.activeInHierarchy} rightActive={RightBareHand.gameObject.activeInHierarchy} inputAvailable={InputAvailable} tableLocked={TableLocked} subsystems={handSubsystems.Count}{native} phase={Preview.LoadingPhase} ready={Preview.Ready} passthroughRunning={arCamera!=null&&arCamera.subsystem!=null&&arCamera.subsystem.running}");}
        }
        public void RecoverHands()
        {
            InputActions?.Enable();
            foreach(var hand in new[]{LeftBareHand,RightBareHand})if(hand!=null){hand.gameObject.SetActive(true);hand.enabled=true;}
            handSubsystems.Clear();SubsystemManager.GetSubsystems(handSubsystems);
            if(InputAvailable)foreach(var subsystem in handSubsystems)if(!subsystem.running)subsystem.Start();
            ConnectionStatus=TableLocked?"桌面已锁定 · 双手伸到头显前即可继续":"请放下手柄，将双手伸到头显前";
        }
        public void PauseInteraction()
        {
            foreach(var interactor in Controllers)Interactions.CancelInteractorSelection((IXRSelectInteractor)interactor);
            Bridge.CancelInputs();SizeAdjustment?.Cancel();
            ConnectionStatus=TableLocked?"交互暂歇 · 桌面保持锁定":"交互暂歇 · 等待双手恢复";
        }
        void EnableHand(string side){foreach(var controller in Controllers)if(controller.GetComponentInParent<QuestHand>().Side==side)controller.enabled=true;}
        public bool ConfirmTable(Vector3 position)=>ConfirmTable(position,.45f,.3f);
        public bool ConfirmTable(Vector3 center,float halfWidth,float halfDepth,float yawDegrees=0){bool placed=Preview.PlaceOnPlane(center,halfWidth,halfDepth,center,yawDegrees);if(placed){ConnectionStatus="平台已确认 · 裸手捏合抓放，靠近头部抚摸";var table=Bridge.Table;table.transform.position=center-Vector3.up*.02f;table.transform.rotation=Quaternion.Euler(0,yawDegrees,0);table.transform.localScale=new Vector3(halfWidth*2,.04f,halfDepth*2);table.GetComponent<Collider>().enabled=true;EnvironmentSurfaces?.StopSelection();Panel.Collapse();Debug.Log($"XR_MR_PLACED center={center} width={halfWidth*2} depth={halfDepth*2} yaw={yawDegrees}");}return placed;}
        void LoseHand(string side){foreach(var controller in Controllers)if(controller.GetComponentInParent<QuestHand>().Side==side){Interactions.CancelInteractorSelection((IXRSelectInteractor)controller);controller.enabled=false;}Bridge.LoseHand(side);}
        public void Suspend(){foreach(var controller in Controllers)Interactions.CancelInteractorSelection((IXRSelectInteractor)controller);Bridge.CancelInputs();Preview.SimulateTrackingLoss();Placement?.Invalidate();Panel.Expand();ConnectionStatus="追踪中断 · 请重新选择平台";}
        void OnApplicationPause(bool paused){applicationPaused=paused;if(Bridge==null)return;if(paused)PauseInteraction();else RecoverHands();}
        void OnApplicationFocus(bool focused){focusLost=!focused;if(Bridge==null)return;if(!focused)PauseInteraction();else RecoverHands();}
        void Hand(Transform parent,string side,out InputAction tracked)
        {
            var hand=new GameObject(side);hand.SetActive(false);hand.transform.SetParent(parent,false);
            string map=side=="LeftHand"?"XRI Left":"XRI Right";tracked=InputActions.FindAction(map+"/Is Tracked",true);
            var model=Resources.Load<GameObject>("QuestHands/"+(side=="LeftHand"?"Left":"Right")+" Hand Tracking");
            if(model==null)throw new InvalidOperationException("缺少官方 XR Hands 手部预制体。");
            Instantiate(model,hand.transform,false);
            var group=hand.AddComponent<XRInteractionGroup>();group.interactionManager=Interactions;
            var directObject=new GameObject("Near touch");directObject.transform.SetParent(hand.transform,false);
            HandPose(directObject,map,"Pinch Position","Grip Rotation");
            var body=directObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var sphere=directObject.AddComponent<SphereCollider>();sphere.isTrigger=true;sphere.radius=.025f;
            var direct=directObject.AddComponent<XRDirectInteractor>();direct.interactionManager=Interactions;direct.selectInput=Button(side,"gripPressed");
            var rayObject=new GameObject("Hand pointer");rayObject.transform.SetParent(hand.transform,false);
            Pose(rayObject,map,true);
            var ray=rayObject.AddComponent<XRRayInteractor>();ray.interactionManager=Interactions;ray.maxRaycastDistance=3;ray.raycastTriggerInteraction=QueryTriggerInteraction.Collide;ray.selectInput=Button(side,"gripPressed");ray.uiPressInput=Button(side,"triggerPressed");ray.enableUIInteraction=true;
            // 远射线仅服务 UI 和待选择平台；角色（默认第 0 层）仅近手抓放。
            ray.interactionLayers=1<<1;direct.interactionLayers=(1<<0)|(1<<1);
            var line=rayObject.AddComponent<LineRenderer>();line.sharedMaterial=Resources.Load<Material>("QuestControllers/Pointer");line.widthMultiplier=.0015f;rayObject.AddComponent<XRInteractorLineVisual>();
            var pokeObject=new GameObject("Finger UI");pokeObject.transform.SetParent(hand.transform,false);HandPose(pokeObject,map,"Poke Position","Poke Rotation");
            var poke=pokeObject.AddComponent<XRPokeInteractor>();poke.interactionManager=Interactions;poke.enableUIInteraction=true;
            var marker=hand.AddComponent<QuestHand>();marker.Side=side;
            Controllers.Add(direct);Controllers.Add(ray);Controllers.Add(poke);group.AddGroupMember(direct);group.AddGroupMember(poke);group.AddGroupMember(ray);
            var tracking=hand.AddComponent<QuestTrackedHand>();tracking.Configure(side=="LeftHand",direct,ray,poke,this);
            if(side=="RightHand"){RightHand=directObject.transform;RightRay=ray;RightBareHand=tracking;}else {LeftHand=directObject.transform;LeftRay=ray;LeftBareHand=tracking;}
            hand.SetActive(true);
        }
        void HandPose(GameObject target,string map,string position,string rotation)
        {
            var pose=target.AddComponent<TrackedPoseDriver>();pose.positionInput=new InputActionProperty(Reference(map+"/"+position));pose.rotationInput=new InputActionProperty(Reference(map+"/"+rotation));pose.trackingStateInput=new InputActionProperty(Reference(map+"/Tracking State"));
        }
        public void BindPalm(Transform anchor,bool left)
        {
            // Grip 是 XR Hands 的掌心姿态；原示例 Position 包含手腕及手柄回退。
            var pose=anchor.GetComponent<TrackedPoseDriver>();if(pose==null)pose=anchor.gameObject.AddComponent<TrackedPoseDriver>();
            string map=left?"XRI Left":"XRI Right";
            pose.positionInput=new InputActionProperty(Reference(map+"/Grip Position"));pose.rotationInput=new InputActionProperty(Reference(map+"/Grip Rotation"));
            var state=new InputAction("Palm tracking",InputActionType.Value,"<XRHandDevice>{"+(left?"LeftHand":"RightHand")+"}/trackingState",expectedControlType:"Integer");
            palmActions.Add(state);pose.trackingStateInput=new InputActionProperty(state);
        }
        InputActionReference Reference(string path){var reference=InputActionReference.Create(InputActions.FindAction(path,true));references.Add(reference);return reference;}
        void Pose(GameObject target,string map,bool aim=false)
        {
            var pose=target.AddComponent<TrackedPoseDriver>();pose.positionInput=new InputActionProperty(Reference(map+"/"+(aim?"Aim Position":"Position")));pose.rotationInput=new InputActionProperty(Reference(map+"/"+(aim?"Aim Rotation":"Rotation")));pose.trackingStateInput=new InputActionProperty(Reference(map+"/Tracking State"));
        }
        XRInputButtonReader Button(string side,string control)=>new XRInputButtonReader(side+control,inputSourceMode:XRInputButtonReader.InputSourceMode.InputActionReference){inputActionReferencePerformed=Reference((side=="LeftHand"?"XRI Left":"XRI Right")+" Interaction/"+(control=="gripPressed"?"Select":"UI Press"))};
        void CleanupLoader(){if(ownsLoader&&manager!=null){manager.StopSubsystems();manager.DeinitializeLoader();ownsLoader=false;}}
        void OnDisable(){InputTracking.trackingAcquired-=TrackingAcquired;InputTracking.trackingLost-=TrackingLost;foreach(var input in inputSubsystems)input.trackingOriginUpdated-=OriginChanged;inputSubsystems.Clear();InputActions?.Disable();CleanupLoader();}
        void OnDestroy(){foreach(var reference in references)Destroy(reference);foreach(var action in palmActions)action.Dispose();}
    }
    public sealed class QuestHand : MonoBehaviour { public string Side; }
}
