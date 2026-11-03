using System.Collections.Generic;
using LingChatXR.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

namespace LingChatXR.XR
{
    [DefaultExecutionOrder(-45)]
    public sealed class QuestWalkGestures:MonoBehaviour
    {
        struct HandFrame {public bool tracked,point,open;public Vector3 tip,direction,palm,normal;public float radius,time;}
        QuestEntry entry;XRHandShape pointShape,openShape;HandFrame left,right;
        readonly PointDwell dwell=new PointDwell();LineRenderer cursor;readonly Vector3[] points=new Vector3[40];
        float obstructionAge,clearAge;bool blockerLeft,blockerRight;
        InputAction leftPinch,rightPinch;
        public float PointProgress=>dwell.Progress;
        public bool TargetVisible=>cursor!=null&&cursor.enabled;
        public void Configure(QuestEntry value)
        {
            entry=value;pointShape=ScriptableObject.CreateInstance<XRHandShape>();openShape=ScriptableObject.CreateInstance<XRHandShape>();
            leftPinch=entry.InputActions.FindAction("XRI Left Interaction/UI Press",true);rightPinch=entry.RightTrigger;
            foreach(var finger in new[]{XRHandFingerID.Index,XRHandFingerID.Middle,XRHandFingerID.Ring,XRHandFingerID.Little})
            {
                pointShape.fingerShapeConditions.Add(Condition(finger,finger==XRHandFingerID.Index?0:1,finger==XRHandFingerID.Index?.25f:.45f));
                openShape.fingerShapeConditions.Add(Condition(finger,0,.25f));
            }
            foreach(bool isLeft in new[]{true,false})
            {
                var obj=new GameObject(isLeft?"Left walk gesture events":"Right walk gesture events");obj.transform.SetParent(transform,false);
                var events=obj.AddComponent<XRHandTrackingEvents>();events.handedness=isLeft?Handedness.Left:Handedness.Right;events.updateType=XRHandTrackingEvents.UpdateTypes.Dynamic;
                events.jointsUpdated.AddListener(args=>ReadHand(args,isLeft));events.trackingLost.AddListener(()=>{if(isLeft)left=default;else right=default;});
            }
            cursor=new GameObject("Point to walk confirmation").AddComponent<LineRenderer>();cursor.useWorldSpace=true;cursor.widthMultiplier=.0018f;cursor.sharedMaterial=Resources.Load<Material>("QuestControllers/Water");cursor.enabled=false;
        }
        static XRFingerShapeCondition Condition(XRHandFingerID finger,float value,float tolerance)=>new XRFingerShapeCondition{fingerID=finger,targets=new[]{new XRFingerShapeCondition.Target{shapeType=XRFingerShapeType.FullCurl,desired=value,upperTolerance=tolerance,lowerTolerance=tolerance}}};
        // 只订阅官方关节和形状计算，无摄像头图像、骨名表或独立手追。
        public void ReadHand(XRHandJointsUpdatedEventArgs args,bool isLeft)
        {
            var sample=new HandFrame();var hand=args.hand;
            if(hand.isTracked&&hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm)&&hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip)&&hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out var root))
            {
                sample.tracked=true;sample.time=Time.unscaledTime;sample.point=pointShape.CheckConditions(args);sample.open=openShape.CheckConditions(args);
                sample.tip=entry.TrackingSpace.TransformPoint(tip.position);sample.direction=entry.TrackingSpace.TransformDirection(tip.position-root.position).normalized;
                sample.palm=entry.TrackingSpace.TransformPoint(palm.position);sample.normal=entry.TrackingSpace.TransformDirection(palm.rotation*Vector3.up);sample.radius=.06f;
                if(hand.GetJoint(XRHandJointID.IndexMetacarpal).TryGetPose(out var a)&&hand.GetJoint(XRHandJointID.LittleMetacarpal).TryGetPose(out var b))sample.radius=Mathf.Clamp(Vector3.Distance(a.position,b.position)*.5f+.025f,.04f,.085f);
            }
            if(isLeft)left=sample;else right=sample;
        }
        struct DeskTap
        {
            public bool valid,descending,latched;
            public float time,lowAt;
            public Vector3 peak,low;
            public bool Sample(Vector3 tip,float now,float desk,out Vector3 goal)
            {
                goal=default;
                if(valid&&now==time)return false;
                if(!valid||now-time>.14f||Mathf.Abs(tip.y-desk)>.12f)
                {this=default;valid=true;peak=low=tip;time=now;return false;}
                time=now;
                if(latched)
                {
                    if(tip.y-low.y<.015f)return false;
                    latched=false;descending=false;peak=low=tip;
                }
                if(tip.y>peak.y||Vector3.ProjectOnPlane(tip-peak,Vector3.up).magnitude>.04f)
                {peak=low=tip;descending=false;}
                if(tip.y<low.y-.001f){low=tip;lowAt=now;}
                if(peak.y-low.y>=.015f)descending=true;
                if(!descending)return false;
                if(tip.y-low.y<.003f&&now-lowAt<.055f)return false;
                descending=false;latched=true;
                if(Mathf.Abs(low.y-desk)>.045f)return false;
                goal=new Vector3(low.x,desk,low.z);return true;
            }
        }
        DeskTap leftTap,rightTap;
        float lastDeskTap=-1;
        bool CheckDeskTap(HandFrame hand,bool eligible,ref DeskTap tap)
        {
            var live=entry.Preview;
            if(!eligible||hand.direction.y>-.22f||(hand.open&&Mathf.Abs(hand.palm.y-live.Interaction.PlaneCenter.Y)<.035f)||live.Interaction.State!=PetState.Idle)
            {tap=default;return false;}
            if(!tap.Sample(hand.tip,hand.time,live.Interaction.PlaneCenter.Y,out var goal))return false;
            if(!live.Interaction.ContainsFootprint(FeiyuPreview.ToCore(goal),live.Interaction.RadiusMeters))return false;
            if(Time.unscaledTime-lastDeskTap<.09f)return false;
            lastDeskTap=Time.unscaledTime;dwell.Reset();_=live.Locomotion.GoTo(goal);return true;
        }
        struct TipHistory { public FeiyuAvatar avatar;public float time; public Vector3 point; }
        TipHistory leftTip,rightTip;
        void CheckForehead(HandFrame hand,bool pinching,ref TipHistory history)
        {
            var live=entry.Preview;
            if(!live.TouchEnabled||!Fresh(hand)||!hand.point||pinching||!entry.InputAvailable||!entry.TableLocked||live.SizeEditing||
                (live.Interaction.State!=PetState.Idle&&live.Interaction.State!=PetState.Touched))
            {history=default;return;}
            if(history.avatar==live.Avatar&&history.time==hand.time)return;
            var avatar=live.Avatar;float height=avatar.HeightMeters;
            var forward=avatar.transform.forward;
            var center=avatar.TouchProxy.bounds.center+Vector3.up*height*.012f+forward*height*.14f;
            float dt=hand.time-history.time;
            if(history.avatar==avatar&&dt>.001f&&dt<.12f)
            {
                float before=Vector3.Dot(history.point-center,forward),after=Vector3.Dot(hand.tip-center,forward);
                if(before>0&&after<=0&&(before-after)/dt>.16f&&Vector3.Dot(hand.direction,forward)<-.35f)
                {
                    var hit=Vector3.Lerp(history.point,hand.tip,before/(before-after));
                    if((hit-center).sqrMagnitude<height*height*.009f)avatar.TryForeheadTap();
                }
            }
            history.avatar=avatar;history.time=hand.time;history.point=hand.tip;
        }
        public float SurfaceDistance(Collider surface)
        {
            float Distance(HandFrame hand)
            {
                if(!Fresh(hand))return float.PositiveInfinity;
                return Mathf.Min(Vector3.Distance(hand.tip,surface.ClosestPoint(hand.tip)),Vector3.Distance(hand.palm,surface.ClosestPoint(hand.palm)));
            }
            return Mathf.Min(Distance(left),Distance(right));
        }
        bool Fresh(HandFrame hand)=>hand.tracked&&Time.unscaledTime-hand.time<.15f;
        void Update()
        {
            if(entry==null)return;float dt=Mathf.Min(Time.deltaTime,.05f);var live=entry.Preview;var walk=live.Locomotion;
            if(walk==null)return;
            bool available=entry.InputAvailable&&entry.TableLocked&&!live.SizeEditing&&!entry.Panel.IsExpanded&&!entry.Panel.ComposerVisible&&!entry.Bridge.SelectingWalk&&entry.Placement.Step==0&&!entry.EnvironmentSurfaces.Selecting;
            walk.IdleFacingAllowed=entry.InputAvailable&&entry.TableLocked&&!live.SizeEditing&&entry.Placement.Step==0;
            if(available){CheckForehead(left,leftPinch.IsPressed(),ref leftTip);CheckForehead(right,rightPinch.IsPressed(),ref rightTip);}else{leftTip=rightTip=default;}
            if(!available){leftTap=rightTap=default;dwell.Reset();cursor.enabled=false;obstructionAge=clearAge=0;if(walk.Blocked)walk.Cancel();return;}
            bool leftFresh=Fresh(left)&&entry.LeftBareHand.CanPinch,rightFresh=Fresh(right)&&entry.RightBareHand.CanPinch;
            bool leftBlock=leftFresh&&BlocksPath(left,live),rightBlock=rightFresh&&BlocksPath(right,live);
            if(walk.Blocked&&((blockerLeft&&!leftFresh)||(blockerRight&&!rightFresh))){walk.Cancel();blockerLeft=blockerRight=false;}
            if(leftBlock||rightBlock)
            {
                obstructionAge+=dt;clearAge=0;
                if(obstructionAge>=.08f){walk.SetBlocked(true);blockerLeft=leftBlock;blockerRight=rightBlock;}
            }
            else{obstructionAge=0;clearAge+=dt;if(clearAge>=.3f){walk.SetBlocked(false);blockerLeft=blockerRight=false;}}
            bool tapped=CheckDeskTap(right,rightFresh&&!rightPinch.IsPressed(),ref rightTap);
            tapped|=CheckDeskTap(left,leftFresh&&!leftPinch.IsPressed(),ref leftTap);
            if(tapped||Time.unscaledTime-lastDeskTap<.75f||leftTap.latched||rightTap.latched){cursor.enabled=false;dwell.Reset();return;}
            var candidate=rightFresh&&right.point&&!rightPinch.IsPressed()?right:leftFresh&&left.point&&!leftPinch.IsPressed()?left:default;
            var ray=new Ray(candidate.tip,candidate.direction);var plane=new Plane(Vector3.up,FeiyuPreview.ToUnity(live.Interaction.PlaneCenter));
            bool valid=candidate.tracked&&candidate.direction.y<-.08f&&live.Interaction.State==PetState.Idle&&!walk.Blocked;
            if((entry.RightRay.TryGetCurrentUIRaycastResult(out var rightUi)&&rightUi.gameObject!=null&&rightUi.gameObject.activeInHierarchy)||(entry.LeftRay.TryGetCurrentUIRaycastResult(out var leftUi)&&leftUi.gameObject!=null&&leftUi.gameObject.activeInHierarchy))valid=false;
            Vector3 point=default;
            if(valid&&plane.Raycast(ray,out float distance)&&distance>.02f&&distance<1.5f)
            {
                point=ray.GetPoint(distance);valid=live.Interaction.ContainsFootprint(FeiyuPreview.ToCore(point),live.Interaction.RadiusMeters)&&Vector3.Distance(point,FeiyuPreview.ToUnity(live.Interaction.Position))>.04f;
            }
            else valid=false;
            // UI 和已有选择不会透传；Point 手型也不能与捏合、近触同时发令。
            if(valid){var near=entry.Preview.Avatar;valid=!near.GrabProxy.Raycast(ray,out _,1.5f)&&!near.TouchProxy.Raycast(ray,out _,1.5f);}
            if(dwell.Step(valid,FeiyuPreview.ToCore(point),dt,out var goal))_=walk.GoTo(FeiyuPreview.ToUnity(goal));
            cursor.enabled=valid&&!dwell.Latched;
            if(cursor.enabled)
            {
                int count=Mathf.Max(3,Mathf.CeilToInt(dwell.Progress*(points.Length-1))+1);cursor.positionCount=count;
                for(int i=0;i<count;i++){float angle=i*Mathf.PI*2/(points.Length-1);cursor.SetPosition(i,point+new Vector3(Mathf.Cos(angle)*.018f,.004f,Mathf.Sin(angle)*.018f));}
            }
        }
        static bool BlocksPath(HandFrame hand,FeiyuPreview preview)
        {
            var walk=preview.Locomotion;if(!hand.open||!walk.Active||walk.State==PetLocomotion.Phase.Preparing)return false;
            Vector3 origin=FeiyuPreview.ToUnity(preview.Interaction.Position),direction=Vector3.ProjectOnPlane(walk.Target-origin,Vector3.up).normalized,offset=hand.palm-origin;
            float along=Vector3.Dot(offset,direction),side=Vector3.ProjectOnPlane(offset-direction*along,Vector3.up).magnitude;
            return Mathf.Abs(Vector3.Dot(hand.normal,direction))>.45f&&along>=-hand.radius*.2f&&along<=hand.radius+preview.Interaction.RadiusMeters+walk.NominalSpeed*.45f&&side<=hand.radius+preview.Interaction.RadiusMeters&&offset.y>-.01f&&offset.y<preview.Avatar.HeightMeters+hand.radius;
        }
        void OnDisable(){dwell.Reset();if(cursor!=null)cursor.enabled=false;if(entry!=null&&entry.Preview.Locomotion!=null&&entry.Preview.Locomotion.Blocked)entry.Preview.Locomotion.Cancel();}
        void OnDestroy(){if(cursor!=null)Destroy(cursor.gameObject);if(pointShape!=null)Destroy(pointShape);if(openShape!=null)Destroy(openShape);}
    }
}
