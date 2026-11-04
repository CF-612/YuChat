using LingChatXR.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LingChatXR.XR
{
    public sealed class QuestInteractionBridge : MonoBehaviour
    {
        QuestEntry entry;FeiyuAvatar bound;PetInteraction heldCore;Transform heldAnchor;Vector3 grabOffset;int generation;string owner,touchOwner;
        bool contactGrab;
        float releaseAt=-1;
        QuestTrackedHand Hand(string side)=>side=="LeftHand"?entry.LeftBareHand:entry.RightBareHand;
        bool touchExited;Transform touchAnchor;
        float grabAt;Transform gripBone;Vector3 handHeadingAxis;Quaternion handRotationAtGrab;float bodyYawAtGrab;
        public XRSimpleInteractable Body{get;private set;}
        public XRSimpleInteractable Head{get;private set;}
        public XRSimpleInteractable Tail{get;private set;}
        public XRSimpleInteractable Table{get;private set;}
        public bool SelectingWalk{get;private set;}
        bool walkArmed;int walkGeneration,walkRevision;
        public string WalkHint{get;private set;}="指向桌面空白处，右手捏合确认目标 · 翻掌打开菜单可取消";
        public void Configure(QuestEntry value)
        {
            entry=value;
            var table=GameObject.Find("Simulated table (not passthrough)");
            Table=table.AddComponent<XRSimpleInteractable>();Table.interactionManager=entry.Interactions;
            Table.interactionLayers=0;Table.selectEntered.AddListener(SelectWalkTarget);
            table.GetComponent<Renderer>().enabled=false;table.GetComponent<Collider>().enabled=false;
        }
        void Update()
        {
            var live=entry.Preview;
            if(SelectingWalk)
            {
                if(!entry.InputAvailable||!entry.RightTracked||!entry.TableLocked||live.SizeEditing||live.Interaction.Generation!=walkGeneration||entry.Panel.IsExpanded){live.Locomotion?.Cancel();CancelWalkSelection();}
                else if(!entry.RightTrigger.IsPressed())walkArmed=true;
            }
            if(!live.Ready||live.Avatar!=bound){CancelInputs();bound=null;Body=null;Head=null;Tail=null;if(live.Ready)Bind(live.Avatar);}
            if(touchOwner!=null&&touchAnchor!=null&&bound!=null&&live.Interaction.State==PetState.Touched)
            {
                float gap=Vector3.Distance(touchAnchor.position,bound.TouchProxy.ClosestPoint(touchAnchor.position));
                if(touchExited&&gap>.025f+Mathf.Min(.012f,bound.HeightMeters*.04f))
                {live.Interaction.EndTouch(touchOwner);touchOwner=null;live.RefreshSpatialState();}
                else if(!touchExited && Hand(touchOwner).PinchStrength>=.8f)BeginHold(touchOwner,touchAnchor,true);
                else bound.SampleTouch(touchAnchor.position);
            }
            if(heldCore!=null)
            {
                if(heldAnchor==null||heldCore!=live.Interaction){ClearHeld();return;}
                if(contactGrab)
                {
                    if(Hand(owner).PinchStrength>=.55f)releaseAt=-1;
                    else if(releaseAt<0)releaseAt=Time.unscaledTime+.12f;
                }
                if(releaseAt>=0 && Time.unscaledTime>=releaseAt){ReleaseHeld(false);return;}
                ObserveShake(heldAnchor.position);
                var rootToGrip=gripBone!=null?live.Avatar.transform.position-gripBone.position-Vector3.up*bound.HeldGripPadding:grabOffset;
                float blend=Mathf.SmoothStep(0,1,(Time.unscaledTime-grabAt)/.18f);
                var heldPosition=heldAnchor.position+Vector3.Lerp(grabOffset,rootToGrip,blend);
                if(!heldCore.MoveGrab(owner,generation,FeiyuPreview.ToCore(heldPosition))){ClearHeld();return;}
                // 官方 grip 姿态的相对水平转动；抓住瞬间不跳朝向，抬手不把鱼倾倒。
                var from=Vector3.ProjectOnPlane(handRotationAtGrab*handHeadingAxis,Vector3.up);
                var to=Vector3.ProjectOnPlane(heldAnchor.rotation*handHeadingAxis,Vector3.up);
                if(to.sqrMagnitude>.12f)
                {
                    float yaw=Vector3.SignedAngle(from,to,Vector3.up);
                    if(Mathf.Abs(Mathf.DeltaAngle(live.SpatialRotation.eulerAngles.y,bodyYawAtGrab+yaw))>1)
                        live.TryHeldYaw(owner,generation,bodyYawAtGrab+yaw);
                }
                live.RefreshSpatialState();
            }
        }
        public void BeginWalkSelection()
        {
            if(!entry.TableLocked||entry.Preview.SizeEditing||entry.Preview.Interaction.State!=PetState.Idle)return;
            CancelInputs();entry.Preview.Locomotion.Cancel();entry.Panel.Collapse();
            SelectingWalk=true;walkArmed=false;walkGeneration=entry.Preview.Interaction.Generation;Table.interactionLayers=1<<1;WalkHint="指向桌面空白处，右手捏合确认目标 · 翻掌打开菜单可取消";
        }
        public void CancelWalkSelection(){++walkRevision;SelectingWalk=false;walkArmed=false;if(Table!=null)Table.interactionLayers=0;}
        async void SelectWalkTarget(SelectEnterEventArgs args)
        {
            if(!SelectingWalk||!walkArmed||!entry.RightBareHand.CanPinch||args.interactorObject!=entry.RightRay)return;
            if(entry.RightRay.TryGetCurrentUIRaycastResult(out var ui)&&ui.gameObject!=null)return;
            if(!entry.RightRay.TryGetCurrent3DRaycastHit(out var hit)||hit.collider!=Table.GetComponent<Collider>())return;
            int ticket=walkRevision;walkArmed=false;WalkHint="正在确认目标…";
            bool accepted=await entry.Preview.Locomotion.GoTo(hit.point);
            if(this==null||ticket!=walkRevision)return;
            if(accepted)CancelWalkSelection();else WalkHint=entry.Preview.Locomotion.Status+" · 松手后重新选择";
        }
        void Bind(FeiyuAvatar avatar)
        {
            bound=avatar;Body=avatar.GrabProxy.gameObject.AddComponent<XRSimpleInteractable>();Body.interactionManager=entry.Interactions;Body.colliders.Clear();Body.colliders.Add(avatar.GrabProxy);
            Body.selectEntered.AddListener(Grab);Body.selectExited.AddListener(Release);
            Head=avatar.TouchProxy.gameObject.AddComponent<XRSimpleInteractable>();Head.interactionManager=entry.Interactions;Head.colliders.Clear();Head.colliders.Add(avatar.TouchProxy);
            Head.selectEntered.AddListener(Grab);Head.selectExited.AddListener(Release);
            if (avatar.TailGrabProxy != null)
            {
                Tail = avatar.TailGrabProxy.gameObject.AddComponent<XRSimpleInteractable>();
                Tail.interactionManager = entry.Interactions;
                Tail.colliders.Clear();Tail.colliders.Add(avatar.TailGrabProxy);
                Tail.selectEntered.AddListener(Grab);Tail.selectExited.AddListener(Release);
            }
            Head.hoverEntered.AddListener(e=>{if(!entry.Preview.TouchEnabled||entry.Preview.SizeEditing||!(e.interactorObject is XRDirectInteractor))return;string id=Owner(e.interactorObject.transform);if(id!=null&&entry.Preview.Interaction.BeginTouch(id)){touchExited=false;touchOwner=id;touchAnchor=e.interactorObject.transform;entry.Preview.RefreshSpatialState();}});
            Head.hoverExited.AddListener(e=>{if(!(e.interactorObject is XRDirectInteractor))return;string id=Owner(e.interactorObject.transform);if(id==touchOwner)touchExited=true;});
        }
        static string Owner(Transform source)=>source.GetComponentInParent<QuestHand>()?.Side;
        void Grab(SelectEnterEventArgs args)
        {
            BeginHold(Owner(args.interactorObject.transform),args.interactorObject.transform,false,args.interactableObject==Tail);
        }
        void BeginHold(string id,Transform anchor,bool fromContact,bool tail=false)
        {
            var core=entry.Preview.Interaction;
            if(id!=null && heldCore!=null && id==owner && releaseAt>=0){heldAnchor=anchor;contactGrab=fromContact;releaseAt=-1;return;}
            if(id==null||core==null||entry.Preview.SizeEditing||(entry.SizeAdjustment!=null&&entry.SizeAdjustment.BlocksGrab)||!bound.SelectGrabPoint(tail)||!core.BeginGrab(id,out int token))return;
            owner=id;generation=token;heldCore=core;heldAnchor=anchor;contactGrab=fromContact;
            ResetShake(heldAnchor.position);grabAt=Time.unscaledTime;gripBone=bound.HeldGripBone;
            handHeadingAxis=Mathf.Abs(heldAnchor.forward.y)<Mathf.Abs(heldAnchor.right.y)?Vector3.forward:Vector3.right;
            handRotationAtGrab=heldAnchor.rotation;bodyYawAtGrab=entry.Preview.SpatialRotation.eulerAngles.y;
            Debug.Log("XR_MR_GRAB "+id);
            grabOffset=FeiyuPreview.ToUnity(core.Position)-heldAnchor.position;touchOwner=null;entry.Preview.RefreshSpatialState();
        }
        void Release(SelectExitEventArgs args){if(contactGrab||owner==null||args.interactorObject.transform!=heldAnchor)return;
            if(args.isCanceled)ReleaseHeld(true);else releaseAt=Time.unscaledTime+.12f;}
        void ReleaseHeld(bool cancelled)
        {
            if(heldCore!=null&&heldCore==entry.Preview.Interaction)
            {
                var contact=cancelled?heldCore.LastSafePosition:new Vec3(heldCore.Position.X,heldCore.PlaneCenter.Y,heldCore.Position.Z);
                heldCore.ReleaseGrabWithDescent(owner,generation,contact);entry.Preview.RefreshSpatialState();
                Debug.Log("XR_MR_RELEASE cancelled="+cancelled);
            }
            ClearHeld();
        }
        Vector3 shakeOrigin,lastStroke,lastHandSample;
        float strokeStart,lastStrokeAt;
        int reversals;
        void ResetShake(Vector3 position)
        {
            shakeOrigin=lastHandSample=position;lastStroke=Vector3.zero;
            reversals=0;strokeStart=Time.unscaledTime;lastStrokeAt=-10;
        }
        void ObserveShake(Vector3 position)
        {
            float now=Time.unscaledTime;
            if(Vector3.Distance(position,lastHandSample)>.22f){ResetShake(position);return;}
            lastHandSample=position;
            Vector3 stroke=position-shakeOrigin;
            float distance=stroke.magnitude,elapsed=now-strokeStart;
            if(elapsed>.24f){ResetShake(position);return;}
            if(distance<.035f)return;
            Vector3 direction=stroke/distance;
            if(distance/Mathf.Max(.01f,elapsed)>=.32f)
            {
                if(now-lastStrokeAt>.32f)reversals=0;
                if(Vector3.Dot(direction,lastStroke)<-.55f)reversals++;
                lastStroke=direction;lastStrokeAt=now;
                if(reversals>=2){bound.ShakeWhileHeld();reversals=1;}
            }
            else {lastStroke=Vector3.zero;reversals=0;}
            shakeOrigin=position;strokeStart=now;
        }
        void ClearHeld(){releaseAt=-1;heldCore=null;heldAnchor=null;gripBone=null;owner=null;}
        public void LoseHand(string side){if(side==touchOwner)bound?.CancelTouch();if(side==owner)ReleaseHeld(true);if(side==touchOwner){entry.Preview.Interaction?.EndTouch(side);touchOwner=null;entry.Preview.RefreshSpatialState();}}
        public void CancelInputs(){bound?.CancelTouch();CancelWalkSelection();entry.Preview.Locomotion?.Cancel();ReleaseHeld(true);if(touchOwner!=null){entry.Preview.Interaction?.EndTouch(touchOwner);touchOwner=null;entry.Preview.RefreshSpatialState();}}
        void OnDisable(){if(entry!=null)CancelInputs();}
    }
}
