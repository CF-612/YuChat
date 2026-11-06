using System;
using System.Threading.Tasks;
using LingChatXR.Core;
using UnityEngine;

namespace LingChatXR
{
    // 单个平台内直达目标。路径是凸矩形内的线段，边界和空间写入仍由 PetInteraction 裁定。
    // 不生成随机目标，不修改动作偏好，不添加另一个 Animator 或物理引擎。
    [DefaultExecutionOrder(-40)]
    public sealed class PetLocomotion:MonoBehaviour
    {
        FeiyuPreview preview;FeiyuAvatar bound;PetInteraction core;int generation,request;
        bool previousAutonomy,restoring;float speed,walkTime,turnTime,nominalSpeed;
        Vector3 target;Quaternion heading,turnStart;
        float turnElapsed,turnDuration,turnAngle;
        float attentionAge;bool manualFacing,applicationPaused,focusLost;Vector3 manualViewer;
        float clickBoost,lastCommandAt=float.NegativeInfinity,boostUntil,runSpeed;
        bool running;
        public bool Running=>running&&State==Phase.Walking;
        public float SpeedMultiplier=>Mathf.Clamp((preview!=null?preview.MovementSpeed:1)+clickBoost,.75f,3f);
        public float AnimationPlaybackRate {get;private set;}
        LineRenderer marker;readonly Vector3[] markerPoints=new Vector3[40];
        public enum Phase { None, Preparing, Turning, Walking, Blocked, Facing }
        Phase resumePhase;float blockedAge;
        public Phase State {get;private set;}
        public bool Active=>State!=Phase.None;
        public bool ManualStep {get;set;}
        public bool IdleFacingAllowed {get;set;}=true;
        public string Status {get;private set;}="点桌面空白处，让角色走过去";
        public Vector3 Target=>target;
        public float Speed=>speed;
        public float NominalSpeed=>nominalSpeed;
        public bool Blocked=>State==Phase.Blocked;
        public void SetBlocked(bool blocked)
        {
            if(blocked&&(State==Phase.Walking||State==Phase.Turning)){resumePhase=State;State=Phase.Blocked;speed=0;blockedAge=0;Status="手掌挡住了去路 · 移开后继续";}
            else if(!blocked&&State==Phase.Blocked){State=resumePhase;Status="继续走向目标";}
        }
        public bool Ready=>bound!=null&&bound.Motions!=null&&bound.Motions.PrepareLocomotion().IsCompletedSuccessfully&&bound.Motions.PrepareLocomotion().Result;
        public void Configure(FeiyuPreview value)=>preview=value;
        void Update()
        {
            if(preview==null)return;
            if(!ReferenceEquals(bound,preview.Avatar))
            {
                Cancel(true);bound=preview.Avatar;manualFacing=false;attentionAge=0;
                if(bound!=null)_=bound.Motions.PrepareLocomotion();
            }
            if(!ManualStep){Step(Time.deltaTime);StepAttention(Time.deltaTime);}
            if(marker==null&&preview.Ready)
            {
                marker=new GameObject("Walk target").AddComponent<LineRenderer>();marker.useWorldSpace=true;marker.loop=true;marker.positionCount=markerPoints.Length;marker.widthMultiplier=.0015f;marker.sharedMaterial=Resources.Load<Material>("QuestControllers/Water");marker.enabled=false;
            }
            if(marker!=null)
            {
                marker.enabled=preview.Ready&&(State==Phase.Turning||State==Phase.Walking);
                if(marker.enabled){float radius=Mathf.Clamp(preview.Avatar.HeightMeters*.08f,.008f,.03f);for(int i=0;i<markerPoints.Length;i++){float angle=i*Mathf.PI*2/markerPoints.Length;markerPoints[i]=target+new Vector3(Mathf.Cos(angle)*radius,.004f,Mathf.Sin(angle)*radius);}marker.SetPositions(markerPoints);}
            }
        }
        TableWaterRings acceptedRipple;
        public async Task<bool> GoTo(Vector3 destination)
        {
            var actor=preview!=null?preview.Avatar:null;
            if(!await Begin(destination,false))return false;
            if(this==null||actor==null||preview.Avatar!=actor||!preview.Interaction.PlaneConfirmed)return true;
            if(acceptedRipple==null)
            {
                var effect=new GameObject("已确认桌面落点");effect.transform.SetParent(transform,false);
                acceptedRipple=effect.AddComponent<TableWaterRings>();
            }
            destination.y=preview.Interaction.PlaneCenter.Y+.005f;
            acceptedRipple.Show(destination,Mathf.Clamp(actor.HeightMeters*.2f,.025f,.07f),clickBoost>0);
            return true;
        }
        public Task<bool> FaceViewer()=>Begin(preview!=null&&preview.Ready?FeiyuPreview.ToUnity(preview.Interaction.Position):Vector3.zero,true);
        public void PreserveManualFacing(){manualFacing=true;attentionAge=0;if(preview.ViewCamera!=null)manualViewer=preview.ViewCamera.transform.position;}
        void StepAttention(float dt)
        {
            if(Active||applicationPaused||focusLost||!IdleFacingAllowed||preview.InspectingFromDesktop||!preview.Ready||preview.SizeEditing||!preview.Avatar.GazeEnabled||preview.Avatar.FollowingHand||preview.Interaction.State!=PetState.Idle||!preview.Interaction.PlaneConfirmed||preview.Avatar.Motions.Busy){attentionAge=0;return;}
            if(manualFacing)
            {
                if(Vector3.ProjectOnPlane(preview.ViewCamera.transform.position-manualViewer,Vector3.up).magnitude<.35f)return;
                manualFacing=false;
            }
            var direction=Vector3.ProjectOnPlane(preview.ViewCamera.transform.position-FeiyuPreview.ToUnity(preview.Interaction.Position),Vector3.up);
            float angle=direction.sqrMagnitude>.01f?Vector3.Angle(preview.SpatialRotation*Vector3.forward,direction):0;
            attentionAge=angle>75?attentionAge+Mathf.Min(dt,.05f):0;
            if(attentionAge>=.65f){attentionAge=0;_=FaceViewer();}
        }
        internal Task<bool> FacePoint(Vector3 point)=>Begin(FeiyuPreview.ToUnity(preview.Interaction.Position),true,point);
        async Task<bool> Begin(Vector3 destination,bool faceOnly,Vector3? facePoint=null)
        {
            if(preview==null||!preview.Ready||preview.SizeEditing||preview.Interaction.State!=PetState.Idle||!preview.Interaction.PlaneConfirmed||!Finite(destination))return false;
            var selectedCore=preview.Interaction;var avatar=preview.Avatar;
            destination.y=selectedCore.PlaneCenter.Y;
            if(!selectedCore.ContainsFootprint(FeiyuPreview.ToCore(destination),selectedCore.RadiusMeters)){Status="目标太靠近桌边，请往内选一点";return false;}
            if(!faceOnly&&Vector3.Distance(destination,FeiyuPreview.ToUnity(selectedCore.Position))<.005f){Status="已经在这里了";return false;}
            if(!faceOnly)avatar.Motions.EndWelcome();
            // 同一行程的再次点选只更新目标和速度，不停止播放器、清相位或重新抢身体。
            if(!faceOnly&&Active&&State!=Phase.Preparing&&core==selectedCore&&bound==avatar&&generation==core.Generation&&bound.Motions.LocomotionOwned)
            {
                if(State==Phase.Blocked)return false;
                RegisterCommand();target=destination;
                var nextHeading=Quaternion.LookRotation((destination-FeiyuPreview.ToUnity(core.Position)).normalized,Vector3.up);
                float difference=Quaternion.Angle(preview.SpatialRotation,nextHeading);
                if(State==Phase.Facing||State==Phase.Turning||difference>75)
                {if(Quaternion.Angle(heading,nextHeading)>2||State==Phase.Facing){ConfigureTurn(nextHeading);State=Phase.Turning;speed=0;}}
                else heading=nextHeading;
                return true;
            }
            Cancel(true);int ticket=++request;int expected=selectedCore.Generation;State=Phase.Preparing;Status="准备行走动作…";
            await avatar.Motions.CatalogReady;bool prepared=await avatar.Motions.PrepareLocomotion();
            if(this==null||ticket!=request)return false;
            if(!prepared||!preview.Ready||preview.Avatar!=avatar||preview.Interaction!=selectedCore||preview.SizeEditing||selectedCore.Generation!=expected||selectedCore.State!=PetState.Idle||!selectedCore.PlaneConfirmed)
            {State=Phase.None;Status=prepared?"操作已变化，请重新选择目标":avatar.Motions.LocomotionStatus;return false;}
            // 尺度只取官方 Humanoid 腿链，避免 Q 版头部、发饰与衣裙改变步幅。
            avatar.Instance.TryGetBoneTransform(HumanBodyBones.LeftUpperLeg,out var hip);avatar.Instance.TryGetBoneTransform(HumanBodyBones.LeftLowerLeg,out var knee);avatar.Instance.TryGetBoneTransform(HumanBodyBones.LeftFoot,out var foot);
            if(hip==null||knee==null||foot==null){State=Phase.None;Status="模型缺少标准腿部骨骼，暂不能行走";return false;}
            float leg=Vector3.Distance(hip.position,knee.position)+Vector3.Distance(knee.position,foot.position);
            nominalSpeed=avatar.Motions.LocomotionSourceSpeed*leg/avatar.Motions.LocomotionSourceLeg;
            runSpeed=avatar.Motions.HasRunning?avatar.Motions.MovementSpeedPerLeg("run")*leg:nominalSpeed;
            if(!float.IsFinite(nominalSpeed)||nominalSpeed<=0||!avatar.Motions.BeginLocomotion()){State=Phase.None;Status="角色正在执行动作，请结束后再选择目标";return false;}
            bound=avatar;core=selectedCore;previousAutonomy=core.AutonomousEnabled;core.SetAutonomousEnabled(true);restoring=true;generation=core.Generation;
            target=destination;speed=walkTime=turnTime=0;manualFacing=false;attentionAge=0;running=false;
            if(!faceOnly)RegisterCommand();
            var direction=Vector3.ProjectOnPlane((faceOnly?(facePoint??preview.ViewCamera.transform.position):destination)-FeiyuPreview.ToUnity(core.Position),Vector3.up);
            if(direction.sqrMagnitude<.0001f){bound.Motions.EndLocomotion();Finish();return true;}
            ConfigureTurn(Quaternion.LookRotation(direction.normalized,Vector3.up));
            if(faceOnly){State=Phase.Facing;Status="转身看向你";return true;}
            // 小修正直接随步行转向，避免先停下来播放一个几乎看不到的转身。
            State=Mathf.Abs(turnAngle)<=12?Phase.Walking:Phase.Turning;Status=State==Phase.Turning?"转向目标":"正在走向目标";return true;
        }
        void ConfigureTurn(Quaternion rotation)
        {
            heading=rotation;
            turnStart=preview.SpatialRotation;turnAngle=Vector3.SignedAngle(turnStart*Vector3.forward,heading*Vector3.forward,Vector3.up);
            turnElapsed=0;turnDuration=Mathf.Max(.18f,Mathf.Abs(turnAngle)/200f+.08f);
        }
        void RegisterCommand()
        {
            float now=Time.unscaledTime;
            if(now-lastCommandAt<=1.2f)clickBoost=Mathf.Min(2,clickBoost+.5f);
            lastCommandAt=now;boostUntil=now+1.8f;
        }
        public void Step(float dt)
        {
            if(State==Phase.None||State==Phase.Preparing)return;
            if(!float.IsFinite(dt)||dt<=0)return;
            if(preview==null||!preview.Ready||preview.Avatar!=bound||preview.Interaction!=core||preview.SizeEditing||core.Generation!=generation||core.State!=PetState.Idle||!core.PlaneConfirmed||!bound.Motions.LocomotionOwned)
            {Cancel();return;}
            dt=Mathf.Min(dt,.05f);
            if(Time.unscaledTime>boostUntil)clickBoost=Mathf.MoveTowards(clickBoost,0,dt*.6f);
            if(State==Phase.Blocked){blockedAge+=dt;bound.Motions.SampleLocomotion("idle",blockedAge);return;}
            if(State==Phase.Turning||State==Phase.Facing)
            {
                turnElapsed+=dt;float progress=Mathf.Clamp01(turnElapsed/turnDuration);
                float eased=progress*progress*(3-2*progress);
                // 源素材每个循环约 90 度；相位跟随实际转角，不让加速后的空间根与脚步脱节。
                turnTime=Mathf.Abs(turnAngle)*eased/90;
                bound.Motions.SampleLocomotion(turnAngle<0?"left":"right",turnTime);
                if(!preview.TryAutonomousPose(FeiyuPreview.ToUnity(core.Position),Quaternion.Slerp(turnStart,heading,eased),generation)){Cancel();return;}
                if(progress>=1)
                {
                    if(State==Phase.Facing){bound.Motions.EndLocomotion();Finish();Status="已到达 · 看向你";}
                    else {State=Phase.Walking;Status="正在走向目标";}
                }
                return;
            }
            Vector3 current=FeiyuPreview.ToUnity(core.Position);float remaining=Vector3.Distance(current,target);
            float pace=SpeedMultiplier;
            float fastBoost=Mathf.Max(0,pace-1);
            float acceleration=nominalSpeed*(4+fastBoost*5);
            float desired=Mathf.Min(nominalSpeed*(pace+fastBoost*.65f),Mathf.Sqrt(2*acceleration*remaining));speed=Mathf.MoveTowards(speed,desired,acceleration*dt);
            if(bound.Motions.HasRunning)running=running?speed>nominalSpeed*1.25f:speed>nominalSpeed*1.45f;
            string gait=running?"run":bound.Motions.DefaultMovement;float gaitSpeed=running?runSpeed:nominalSpeed;
            float distance=Mathf.Min(remaining,speed*dt),length=bound.Motions.MovementLength(gait);
            walkTime+=distance/(gaitSpeed*length);AnimationPlaybackRate=distance/(dt*gaitSpeed);
            bound.Motions.SampleLocomotion(gait,walkTime*length);
            Status=running?"加速跑向目标":"蹦跳着走向目标";
            if(!preview.TryAutonomousPose(Vector3.MoveTowards(current,target,distance),Quaternion.RotateTowards(preview.SpatialRotation,heading,240*dt),generation)){Cancel();return;}
            if(remaining-distance<=.0001f)
            {
                speed=0;var towardViewer=Vector3.ProjectOnPlane(preview.ViewCamera.transform.position-target,Vector3.up);
                if(towardViewer.sqrMagnitude>.01f&&Vector3.Angle(heading*Vector3.forward,towardViewer)>12)
                {ConfigureTurn(Quaternion.LookRotation(towardViewer,Vector3.up));State=Phase.Facing;Status="已到达 · 转身看向你";}
                else {bound.Motions.EndLocomotion();Finish();Status="已到达 · 点桌面可再选目标";}
            }
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        void Finish()
        {
            State=Phase.None;speed=0;running=false;AnimationPlaybackRate=0;clickBoost=0;lastCommandAt=float.NegativeInfinity;
            if(restoring&&core!=null){core.SetAutonomousEnabled(previousAutonomy);restoring=false;}
            core=null;
        }
        public void Cancel(bool immediate=false)
        {
            ++request;if(acceptedRipple!=null)acceptedRipple.Hide();if(State==Phase.None)return;
            bound?.Motions?.EndLocomotion(immediate);Finish();Status="行走已停止，重新选目标后再走";
        }
        void OnApplicationPause(bool paused){applicationPaused=paused;if(paused)Cancel();}
        void OnApplicationFocus(bool focused){focusLost=!focused;if(!focused)Cancel();}
        void OnDisable()=>Cancel(true);
        void OnDestroy(){if(marker!=null)Destroy(marker.gameObject);}
    }
}
