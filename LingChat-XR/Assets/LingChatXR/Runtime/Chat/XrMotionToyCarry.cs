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
        internal void AcceptToyContact()=>carryAcceptedAt=age;
        internal bool CarryReachExpired=>age>1.55f;
        internal bool ReachingToy=>HoldingToyPose && age>=.45f;
        const string ToyCarryOwner="toy-carry";
        const MotionRegion CarryArms=MotionRegion.LeftArm|MotionRegion.RightArm;
        public bool HoldingToyPose=>bodyOwner==ToyCarryOwner && action!=null && !releasing;
        static readonly bool[] CarrySides={true,false};
        static readonly HumanBodyBones[] CarryHandBones={HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
        float toyRadius;
        Vector3 toyPoint;
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
        public Task<bool> BeginToyCarry(Vector3 point,float radius)
        {
            if(Busy)return Task.FromResult(false);
            toyPoint=point;toyRadius=radius;carryAcceptedAt=-1;
            return PlayInternal("ual2_walk_carry_loop",false,ToyCarryOwner);
        }
        public void EndToyCarry()=>CancelOwner(ToyCarryOwner);
        void AimCarryArm(HumanBodyBones armId,HumanBodyBones handId)
        {
            if(!controlBones.TryGetValue(armId,out var arm)||!controlBones.TryGetValue(handId,out var hand))return;
            var forward=instance.transform.forward;
            var outward=Vector3.Project(arm.position-hips.position,instance.transform.right).normalized;
            var elbow=controlBones[handId==HumanBodyBones.LeftHand?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm];
            var extend=Quaternion.FromToRotation(hand.position-elbow.position,forward-Vector3.up*.15f);
            elbow.rotation=Quaternion.Slerp(Quaternion.identity,extend,.62f)*elbow.rotation;
            var toward=forward+outward*.28f-Vector3.up*.54f;
            arm.rotation=Quaternion.FromToRotation(hand.position-arm.position,toward)*arm.rotation;
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
            AimCarryArm(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftHand);
            AimCarryArm(HumanBodyBones.RightUpperArm,HumanBodyBones.RightHand);
            float entryBlend=Smooth(age/.45f);
            for(int i=0;i<bones.Length;i++)
            {
                var held=(boneRegions[i]&CarryArms)!=0?bones[i].localRotation:frameBase[i];
                bones[i].localRotation=Quaternion.Slerp(entry[i],Quaternion.Slerp(source[i],held,lift),entryBlend);
            }
            hips.localPosition=hipsRest+Vector3.up*((pickupHeight-hipsRest.y)*(1-lift)*entryBlend);
            if(pickingUp)hips.position+=Vector3.up*(support-Mathf.Min(leftFoot.position.y,rightFoot.position.y));
        }
    }
}
