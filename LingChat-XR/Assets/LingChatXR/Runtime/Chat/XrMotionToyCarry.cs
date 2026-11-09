using System;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace LingChatXR.Chat
{
    public sealed partial class XrMotionPlayer
    {
        Clip toyPickup;
        float carryAcceptedAt=-1;
        float CarryLift=>carryAcceptedAt<0?0:Smooth((age-carryAcceptedAt)/.58f);
        public Vector2 GripReachDeficit {get;private set;}
        public bool CarryLiftComplete=>HoldingToyPose && CarryLift>=.9999f;
        internal void AcceptToyContact()=>carryAcceptedAt=age;
        internal bool CarryReachExpired=>age>1.55f;
        internal bool ReachingToy=>HoldingToyPose && age>=.45f;
        const string ToyCarryOwner="toy-carry";
        const MotionRegion CarryArms=MotionRegion.LeftArm|MotionRegion.RightArm;
        public bool HoldingToyPose=>bodyOwner==ToyCarryOwner && action!=null && !releasing;
        static readonly bool[] CarrySides={true,false};
        static readonly HumanBodyBones[] CarryHandBones={HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
        PcCoinChase heldToy;
        Vector3 toyPoint=>heldToy.CoinPosition;
        float toyRadius=>heldToy.Radius;
        Task toyWarmup;
        internal Task WarmToyCarry()
        {
            if(toyWarmup==null || toyWarmup.IsCompleted)toyWarmup=WarmToyClips();
            return toyWarmup;
        }
        async Task WarmToyClips()
        {
            try
            {
                await CatalogReady;
                foreach(string id in new[]{"ual2_walk_carry_loop","kay_pickup"})
                {
                    if(disposed || !Preferences.Allowed(id))continue;
                    var asset=Catalog.FirstOrDefault(item=>item.id==id);
                    if(asset!=null && !clips.ContainsKey(id))await Load(asset);
                }
            }
            catch(OperationCanceledException){}
            catch(Exception e){if(!disposed)Debug.LogWarning("玩具动作预载入："+e.Message);}
        }
        public Task<bool> BeginToyCarry(PcCoinChase toy)
        {
            if(Busy)return Task.FromResult(false);
            heldToy=toy;carryAcceptedAt=-1;
            return PlayInternal("ual2_walk_carry_loop",false,ToyCarryOwner);
        }
        public void EndToyCarry()=>CancelOwner(ToyCarryOwner);
        // 在原动作上解两段臂长；币给出握区，手不反过来推动币。
        void FitGrip(bool left,float weight)
        {
            if(weight<=0)return;
            var upper=controlBones[left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm];
            var elbow=controlBones[left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm];
            var wrist=controlBones[left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand];
            var index=controlBones[left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal];
            var pinky=controlBones[left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal];
            var pad=(index.position+pinky.position)*.5f;
            var shoulderLine=controlBones[HumanBodyBones.LeftUpperArm].position-controlBones[HumanBodyBones.RightUpperArm].position;
            var side=Mathf.Sign(Vector3.Dot(shoulderLine,instance.transform.right))*(left?1:-1);
            var frame=heldToy.GripFrame;
            var finger=frame*new Vector3(-side*.88f,.36f,.27f);
            var orientation=Quaternion.LookRotation(finger,frame*Vector3.up)*
                Quaternion.Inverse(Quaternion.LookRotation(pad-wrist.position,index.position-pinky.position));
            var wristRotation=Quaternion.Slerp(wrist.rotation,orientation*wrist.rotation,weight);
            var target=heldToy.GripPoint(side)-orientation*(pad-wrist.position);
            float a=Vector3.Distance(upper.position,elbow.position),b=Vector3.Distance(elbow.position,wrist.position);
            if(controlBones.TryGetValue(left?HumanBodyBones.LeftShoulder:HumanBodyBones.RightShoulder,out var clavicle)){
                var clav=upper.position-clavicle.position;
                var ray=target-clavicle.position;
                if(clav.magnitude>.0001f && ray.magnitude>.0001f){
                    float allowed=Mathf.Acos(Mathf.Clamp((clav.sqrMagnitude+ray.sqrMagnitude-(a+b)*(a+b))/(2*clav.magnitude*ray.magnitude),-1,1))*Mathf.Rad2Deg;
                    float correction=Mathf.Clamp(Vector3.Angle(clav,ray)-allowed+3,0,78)*weight;
                    clavicle.rotation=Quaternion.RotateTowards(Quaternion.identity,Quaternion.FromToRotation(clav,ray),correction)*clavicle.rotation;
                }
            }
            var goal=Vector3.Lerp(wrist.position,target,weight);
            var pole=instance.transform.right*side*1.6f-Vector3.up*.75f-instance.transform.forward*.8f;
            float excess=SolveLimb(upper,elbow,wrist,goal,pole);
            GripReachDeficit=left?new Vector2(excess,GripReachDeficit.y):new Vector2(GripReachDeficit.x,excess);
            wrist.rotation=wristRotation;
            if(CarryLift>.75f)RestFingersOnFace(left,side);
        }
        static float SolveLimb(Transform upper,Transform joint,Transform end,Vector3 goal,Vector3 bend)
        {
            var delta=goal-upper.position;var axis=delta.normalized;
            float a=Vector3.Distance(upper.position,joint.position),b=Vector3.Distance(joint.position,end.position);
            float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.00001f,a+b-.00001f);
            float along=(a*a-b*b+d*d)/(2*d);
            var pole=Vector3.ProjectOnPlane(bend,axis).normalized;
            var mid=upper.position+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(joint.position-upper.position,mid-upper.position)*upper.rotation;
            joint.rotation=Quaternion.FromToRotation(end.position-joint.position,upper.position+axis*d-joint.position)*joint.rotation;
            return Mathf.Max(0,delta.magnitude-a-b);
        }
        static readonly HumanBodyBones[] LeftFingers={HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftMiddleProximal,HumanBodyBones.LeftRingProximal,HumanBodyBones.LeftLittleProximal};
        static readonly HumanBodyBones[] RightFingers={HumanBodyBones.RightIndexProximal,HumanBodyBones.RightMiddleProximal,HumanBodyBones.RightRingProximal,HumanBodyBones.RightLittleProximal};
        void RestFingersOnFace(bool left,float side)
        {
            var frame=heldToy.GripFrame;
            var normal=frame*Vector3.forward;
            var inward=frame*new Vector3(-side,.18f,0).normalized;
            foreach(var id in left?LeftFingers:RightFingers)
            {
                if(!controlBones.TryGetValue(id,out var knuckle)||!controlBones.TryGetValue(id+2,out var tip))continue;
                if(!controlBones.TryGetValue(id+1,out var middle))continue;
                float length=Vector3.Distance(knuckle.position,middle.position)+Vector3.Distance(middle.position,tip.position);
                float depth=Vector3.Dot(knuckle.position-heldToy.CoinPosition,normal);
                float rise=Mathf.Clamp(.0055f-depth,-length*.9f,length*.9f);
                float along=Mathf.Sqrt(Mathf.Max(0,length*length-rise*rise))*.86f;
                var target=knuckle.position+inward*along+normal*rise;
                SolveLimb(knuckle,middle,tip,Vector3.Lerp(tip.position,target,Smooth((CarryLift-.75f)/.25f)),normal);

            }
        }
        void SamplePickup()
        {
            var footL=controlBones[HumanBodyBones.LeftFoot];
            var footR=controlBones[HumanBodyBones.RightFoot];
            float support=Mathf.Min(footL.position.y,footR.position.y);
            var head=controlBones[HumanBodyBones.Head];
            var neutralHead=head.rotation;
            Sample(toyPickup,Mathf.Clamp01(age/.62f)*toyPickup.animation.length*.48f,true);
            float crouch=Smooth(age/.55f);
            var axis=instance.transform.right;
            foreach(bool leftSide in CarrySides)
            {
                var thigh=controlBones[leftSide?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg];
                var shin=controlBones[leftSide?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg];
                var foot=leftSide?footL:footR;
                var footRotation=foot.rotation;
                thigh.rotation=Quaternion.AngleAxis(-39*crouch,axis)*thigh.rotation;
                shin.rotation=Quaternion.AngleAxis(78*crouch,axis)*shin.rotation;
                foot.rotation=footRotation;
            }
            var spine=controlBones[HumanBodyBones.Spine];
            var leftShoulder=controlBones[HumanBodyBones.LeftUpperArm];
            var rightShoulder=controlBones[HumanBodyBones.RightUpperArm];
            var lateral=leftShoulder.position-rightShoulder.position;
            var level=instance.transform.right*Mathf.Sign(Vector3.Dot(lateral,instance.transform.right));
            spine.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(lateral,level),crouch)*spine.rotation;
            var shoulders=(controlBones[HumanBodyBones.LeftUpperArm].position+controlBones[HumanBodyBones.RightUpperArm].position)*.5f;
            var reachDirection=Vector3.ProjectOnPlane(toyPoint-hips.position,axis);
            float pitch=Vector3.SignedAngle(Vector3.ProjectOnPlane(shoulders-hips.position,axis),reachDirection,axis);
            spine.rotation=Quaternion.AngleAxis(Mathf.Clamp(pitch,0,52)*crouch,axis)*spine.rotation;
            hips.position+=Vector3.up*(support-Mathf.Min(footL.position.y,footR.position.y));
            head.rotation=Quaternion.Slerp(head.rotation,Quaternion.AngleAxis(18,axis)*neutralHead,crouch);
            foreach(var handId in CarryHandBones)
            {
                var hand=controlBones[handId];
                var arm=controlBones[handId==HumanBodyBones.LeftHand?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm];
                var forearm=controlBones[handId==HumanBodyBones.LeftHand?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm];
                var target=toyPoint+Vector3.Project(arm.position-hips.position,instance.transform.right).normalized*toyRadius*.60f;
                arm.rotation=Quaternion.FromToRotation(hand.position-arm.position,target-arm.position)*arm.rotation;
                forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,target-forearm.position)*forearm.rotation;
            }
        }

        void StepToyCarry()
        {
            float lift=CarryLift;
            bool pickingUp=toyPickup!=null && Preferences.Allowed("kay_pickup") && lift<1;
            var leftFoot=controlBones[HumanBodyBones.LeftFoot];
            var rightFoot=controlBones[HumanBodyBones.RightFoot];
            float support=Mathf.Min(leftFoot.position.y,rightFoot.position.y);
            if(pickingUp)
            {
                SamplePickup();
                Snapshot(source);
            }
            else Array.Copy(frameBase,source,bones.Length);
            float pickupHeight=hips.localPosition.y;
            Sample(action,Mathf.Repeat(age,action.animation.length));
            float entryBlend=Smooth(age/.45f);
            for(int i=0;i<bones.Length;i++)
            {
                var held=(boneRegions[i]&CarryArms)!=0?bones[i].localRotation:frameBase[i];
                bones[i].localRotation=Quaternion.Slerp(entry[i],Quaternion.Slerp(source[i],held,lift),entryBlend);
            }
            hips.localPosition=hipsRest+Vector3.up*((pickupHeight-hipsRest.y)*(1-lift)*entryBlend);
            if(pickingUp)hips.position+=Vector3.up*(support-Mathf.Min(leftFoot.position.y,rightFoot.position.y));
            var spine=controlBones[HumanBodyBones.Spine];
            var head=controlBones[HumanBodyBones.Head];
            var look=head.rotation;
            var headAnchor=head.position-instance.transform.forward*(lift*toyRadius*.32f);
            // 抱稳时围绕两脚的支点轻微前倾，肩带向前承重，脚底保持原朝向。
            var leftSupport=leftFoot.position;var rightSupport=rightFoot.position;
            var pivot=(leftSupport+rightSupport)*.5f;
            var leftSole=leftFoot.rotation;var rightSole=rightFoot.rotation;
            var lean=Quaternion.AngleAxis(lift*2,instance.transform.right);
            hips.position=pivot+lean*(hips.position-pivot);hips.rotation=lean*hips.rotation;
            float supportHeight=Vector3.Dot(hips.position-pivot,Vector3.up);
            hips.position+=lift*supportHeight*(instance.transform.forward*.13f-Vector3.up*.08f);
            if(lift>0){
                SolveLimb(controlBones[HumanBodyBones.LeftUpperLeg],controlBones[HumanBodyBones.LeftLowerLeg],leftFoot,leftSupport,instance.transform.forward);
                SolveLimb(controlBones[HumanBodyBones.RightUpperLeg],controlBones[HumanBodyBones.RightLowerLeg],rightFoot,rightSupport,instance.transform.forward);
            }
            leftFoot.rotation=leftSole;rightFoot.rotation=rightSole;
            spine.rotation=Quaternion.AngleAxis(lift*11,instance.transform.right)*spine.rotation;
            if(controlBones.TryGetValue(HumanBodyBones.Neck,out var neck)){
                var withdraw=Quaternion.FromToRotation(head.position-neck.position,headAnchor-neck.position);
                neck.rotation=Quaternion.RotateTowards(Quaternion.identity,withdraw,lift*70)*neck.rotation;
            }
            head.rotation=Quaternion.AngleAxis(-lift*2,instance.transform.right)*look;
            FitGrip(true,entryBlend);FitGrip(false,entryBlend);
        }
    }
}
