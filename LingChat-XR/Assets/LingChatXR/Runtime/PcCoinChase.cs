using System.Collections.Generic;
using System.Threading.Tasks;
using LingChatXR.Core;
using UnityEngine;

namespace LingChatXR
{
    [DefaultExecutionOrder(11020)]
    public sealed class PcCoinChase : MonoBehaviour
    {
        [SerializeField,Range(.03f,.06f)] float radius=.040f;
        public float Radius=>radius;
        // 归一化板面坐标，指根贴近正面、掌根位于边缘。朝向按身体左右计算。
        [SerializeField] Vector2 lowerGrip=new Vector2(1.075f,-.12f);
        [SerializeField] float facePad=.002f;
        [SerializeField] Vector2 holdForwardAndHeight=new Vector2(.196f,.404f);
        [SerializeField] float holdTilt=-13f;
        Quaternion gripLocalFrame;
        Vector3 liftFrom;
        public Quaternion GripFrame=>coin.rotation*gripLocalFrame;
        public Vector3 GripPoint(float side)
        {
            var face=GripFrame*Vector3.forward;
            float padDepth=Mathf.Lerp(facePad*.25f,-.003f-facePad,Mathf.InverseLerp(.6f,1,-face.y));
            return coin.position+GripFrame*new Vector3(side*lowerGrip.x*Radius,lowerGrip.y*Radius,padDepth);
        }
#if UNITY_EDITOR
        public async Task<bool> ReviewStaticGrasp()
        {
            StopTracking();
            var facing=scene.SpatialRotation;
            coin.rotation=facing*Quaternion.Euler(83,0,0);
            gripLocalFrame=Quaternion.Inverse(coin.rotation)*facing*Quaternion.Euler(holdTilt,0,0);
            coin.position=Feet+facing*Vector3.forward*(actor.HeightMeters*holdForwardAndHeight.x)+Vector3.up*actor.HeightMeters*holdForwardAndHeight.y;
            coin.isKinematic=true;
            if(!await actor.Motions.BeginToyCarry(this))return false;
            actor.Motions.AcceptToyContact();liftFrom=coin.position;liftRotation=coin.rotation;liftAge=1;inArms=true;
            return true;
        }
        public void SetReviewDiameter(float diameter){if(!coin)radius=diameter*.5f;}
#endif
        public float LeftGripError {get;private set;}
        public float RightGripError {get;private set;}
        float PalmRadius=>Mathf.Max(.058f,Radius*1.52f);
        FeiyuPreview scene;
        FeiyuAvatar actor;
        PetInteraction table;
        Rigidbody coin;
        Transform palm;
        Collider palmCover;
        readonly List<Material> paints=new();
        PhysicsMaterial friction;
        Vector3 lastSeen,dragStart,carryOffset;
        bool open,dragging,carrying,seen,searching,controlling,outside;
        float invisibleAge,visibleAge,searchAge,nextPlan;
        Transform leftPadA,leftPadB,rightPadA,rightPadB;
        MeshCollider coinHull;
        Transform leftUpperArm,rightUpperArm;
        public float ContactGap {get;private set;}
        Task<bool> carryRequest;
        float settledTime,liftAge,bothContactTime,missedContactTime;
        float handTakeTime;
        bool easingHandTake;
        Pose handTakeFrom;
        bool inArms,carryDeclined;

        Quaternion liftRotation;
        public bool PalmOwnsCoin=>carrying;
        public bool InArms=>inArms;
        public bool HugSettled=>inArms && actor && actor.Motions.CarryLiftComplete && bothContactTime>=.15f;
        public bool Deployed=>coin!=null && coin.gameObject.activeSelf;
        public Vector3 CoinPosition=>coin.position;
        public Vector3 LastSeen=>lastSeen;
        public int Searches {get;private set;}
        public string Status {get;private set;}
        public bool Searching=>searching;
        public void Configure(FeiyuPreview value)=>scene=value;
        Rect Panel=>new Rect(Screen.width-222,62,210,open?146:32);
        internal bool OverPanel(Vector2 point)=>!Chat.XrChatView.SettingsAvailable && scene.Ready && Panel.Contains(point);
        void OnGUI()
        {
            if(Chat.XrChatView.SettingsAvailable || !scene || scene.ExternalInput || !scene.Ready || !scene.Interaction.PlaneConfirmed)return;
            GUILayout.BeginArea(Panel,GUI.skin.box);
            if(GUILayout.Button(open?"收起玩具箱":"玩具箱 · Token 硬币"))open=!open;
            if(open){
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(Deployed?"取回硬币":"取出硬币"))Take();
                GUI.enabled=Deployed;
                if(GUILayout.Button("收回"))Store();
                GUI.enabled=true;GUILayout.EndHorizontal();
                GUILayout.Label("桌面左键拖放滚币\n按住 H 移动模拟手，松开揭晓");
                GUILayout.Label(Status??"准备好后取出一枚硬币");
            }
            GUILayout.EndArea();
        }
        Material Paint(Color color)
        {
            var m=new Material(Shader.Find("Standard")){color=color};paints.Add(m);return m;
        }
        GameObject Shape(PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material paint)
        {
            var part=GameObject.CreatePrimitive(type);
            part.layer=LayerMask.NameToLayer("Ignore Raycast");part.transform.SetParent(parent,false);
            part.transform.localPosition=pos;part.transform.localScale=scale;
            part.GetComponent<Renderer>().sharedMaterial=paint;
            var collider=part.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            return part;
        }
        void Build()
        {
            var root=new GameObject("Token 硬币");root.transform.SetParent(transform,false);
            var gold=Paint(new Color(1,.62f,.12f));var stamp=Paint(new Color(.55f,.28f,.04f));
            var disk=Shape(PrimitiveType.Cylinder,root.transform,Vector3.zero,new Vector3(2*Radius,.003f,2*Radius),gold);
            gold.EnableKeyword("_EMISSION");gold.SetColor("_EmissionColor",new Color(.32f,.14f,.018f));
            var hull=coinHull=disk.AddComponent<MeshCollider>();hull.sharedMesh=disk.GetComponent<MeshFilter>().sharedMesh;hull.convex=true;
            friction=new PhysicsMaterial("硬币摩擦"){dynamicFriction=.38f,staticFriction=.48f,bounciness=.06f};
            hull.sharedMaterial=friction;
            var rim=Paint(new Color(.98f,.78f,.37f));
            var face=Paint(new Color(.96f,.61f,.17f));
            foreach(var material in new[]{rim,face}){
                material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",material.color*.65f);
            }
            foreach(float sign in new[]{-1f,1f}){
                Shape(PrimitiveType.Cylinder,root.transform,Vector3.up*(sign*.0032f),new Vector3(Radius*1.84f,.0003f,Radius*1.84f),rim);
                Shape(PrimitiveType.Cylinder,root.transform,Vector3.up*(sign*.00355f),new Vector3(Radius*1.68f,.00025f,Radius*1.68f),face);
                Shape(PrimitiveType.Cube,root.transform,new Vector3(0,sign*.004f,0),new Vector3(.004f,.0005f,.022f),stamp);
                Shape(PrimitiveType.Cube,root.transform,new Vector3(0,sign*.004f,.01f),new Vector3(.017f,.0005f,.004f),stamp);
            }
            coin=root.AddComponent<Rigidbody>();coin.mass=.025f;coin.angularDamping=.85f;coin.linearDamping=.95f;
            coin.maxAngularVelocity=65;coin.interpolation=RigidbodyInterpolation.Interpolate;
            coin.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var hand=new GameObject("模拟手");palm=hand.transform;palm.SetParent(transform,false);
            var tint=Paint(new Color(.70f,.85f,.97f));
            var pad=Shape(PrimitiveType.Sphere,palm,Vector3.zero,new Vector3(.116f,.040f,.116f),tint);
            var cover=pad.AddComponent<MeshCollider>();cover.sharedMesh=pad.GetComponent<MeshFilter>().sharedMesh;
            cover.convex=true;cover.isTrigger=true;palmCover=cover;
            for(int i=0;i<4;i++)Shape(PrimitiveType.Sphere,palm,new Vector3((i-1.5f)*.021f,0,.044f),new Vector3(.022f,.020f,.062f-.006f*Mathf.Abs(i-1.5f)),tint);
            Shape(PrimitiveType.Sphere,palm,new Vector3(-.049f,0,.012f),new Vector3(.035f,.024f,.045f),tint);
            palm.localScale=Vector3.one*(PalmRadius/.058f);
            hand.SetActive(false);
        }
        public void Take()
        {
            if(!scene.Ready || !scene.Interaction.PlaneConfirmed)return;
#if UNITY_EDITOR
            var ardy=scene.GetComponent<LingChatXR.Editor.ArdyPlaytest>();
            if(ardy!=null && ardy.Active)ardy.End("切换至玩具箱");
#endif
            Store();actor=scene.Avatar;table=scene.Interaction;
            actor.Instance.TryGetBoneTransform(HumanBodyBones.LeftUpperArm,out leftUpperArm);
            actor.Instance.TryGetBoneTransform(HumanBodyBones.RightUpperArm,out rightUpperArm);
            _=actor.Motions.WarmToyCarry();
            actor.Instance.TryGetBoneTransform(HumanBodyBones.LeftIndexProximal,out leftPadA);
            actor.Instance.TryGetBoneTransform(HumanBodyBones.LeftLittleProximal,out leftPadB);
            actor.Instance.TryGetBoneTransform(HumanBodyBones.RightIndexProximal,out rightPadA);
            actor.Instance.TryGetBoneTransform(HumanBodyBones.RightLittleProximal,out rightPadB);
            if(!coin)Build();
            coin.gameObject.SetActive(true);
            var pos=Feet+scene.SpatialRotation*Vector3.forward*.16f;
            Roll(Clamp(pos,Radius),Vector3.zero);
            Status="拖动鼠标，让硬币滚起来";
        }
        public void Roll(Vector3 start,Vector3 delta)
        {
            if(!Deployed || !Inside(start,Radius))return;
            ReleaseFromArms();Hand(false,default);outside=false;
            Vector3 velocity=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(delta,Vector3.up)*2.4f,.85f);
            var axis=velocity.sqrMagnitude>.00001f?Vector3.Cross(Vector3.up,velocity.normalized):Vector3.right;
            coin.position=new Vector3(start.x,table.PlaneCenter.Y+Radius+.002f,start.z);
            coin.rotation=Quaternion.FromToRotation(Vector3.up,axis);
            coin.isKinematic=false;coin.linearVelocity=velocity;coin.angularVelocity=axis*(velocity.magnitude/Radius);
            coin.WakeUp();
        }
        public void Hand(bool down,Vector3 at)
        {
            if(!Deployed)return;
            palm.gameObject.SetActive(down);
            if(!down){if(carrying){coin.isKinematic=false;coin.WakeUp();}carrying=false;easingHandTake=false;return;}
            at.y=table.PlaneCenter.Y+.043f;palm.position=at;
            if(!carrying && Flat(coin.position-at).magnitude+Radius<=PalmRadius && (inArms || Mathf.Abs(coin.position.y-at.y)<.06f)){
                easingHandTake=inArms;handTakeTime=Time.time;handTakeFrom=new Pose(coin.position,coin.rotation);
                ReleaseFromArms();
                carrying=true;coin.isKinematic=true;carryOffset=Flat(coin.position-at);
            }
            if(carrying){
                at.y=table.PlaneCenter.Y+.006f+.020f*palm.localScale.y;
                float t=easingHandTake?Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-handTakeTime)/.26f)):1;
                palm.position=Vector3.Lerp(handTakeFrom.position+Vector3.up*.018f,at,t);
                coin.position=Vector3.Lerp(handTakeFrom.position,new Vector3(at.x,table.PlaneCenter.Y+.004f,at.z)+carryOffset,t);
                coin.rotation=Quaternion.Slerp(handTakeFrom.rotation,Quaternion.identity,t);
            }
            Physics.SyncTransforms();
        }
        internal bool Pointer(Ray ray,bool overUi)
        {
            return HandlePointer(ray,overUi,Input.GetMouseButtonDown(0),Input.GetMouseButtonUp(0),Input.GetKey(KeyCode.H));
        }
        public bool HandlePointer(Ray ray,bool overUi,bool press,bool release,bool useHand)
        {
            if(!Deployed || table.State!=PetState.Idle || scene.SizeEditing)return false;
            if(overUi || Chat.XrChatView.HasTextFocus || !new Plane(Vector3.up,Vector3.up*table.PlaneCenter.Y).Raycast(ray,out float hit)){
                dragging=false;Hand(false,default);return false;
            }
            Vector3 at=ray.GetPoint(hit);
            bool hitHeldCoin=inArms && (press || useHand) && coinHull.Raycast(ray,out _,4f);
            if(hitHeldCoin)at=Clamp(coin.position,Radius);
            Hand(useHand,at);
            if(useHand){dragging=false;return true;}
            bool onCoin=Flat(at-coin.position).magnitude<Radius*1.3f;
            onCoin |= hitHeldCoin;
            if(press&&Inside(at,Radius) && (onCoin || Flat(at-Feet).magnitude>table.RadiusMeters)){
                dragging=true;dragStart=at;
            }
            if(dragging && release){dragging=false;Roll(dragStart,at-dragStart);return true;}
            return dragging;
        }
        Vector3 Feet=>FeiyuPreview.ToUnity(table.Position);
        static Vector3 Flat(Vector3 v)=>Vector3.ProjectOnPlane(v,Vector3.up);
        bool Inside(Vector3 p,float r)=>table.ContainsFootprint(FeiyuPreview.ToCore(p),r);
        Vector3 Clamp(Vector3 p,float r)
        {
            var rotation=Quaternion.Euler(0,table.PlaneYawRadians*Mathf.Rad2Deg,0);
            var center=FeiyuPreview.ToUnity(table.PlaneCenter);
            var local=Quaternion.Inverse(rotation)*(p-center);
            float x=Mathf.Max(0,table.HalfWidth-r-.001f),z=Mathf.Max(0,table.HalfDepth-r-.001f);
            return center+rotation*new Vector3(Mathf.Clamp(local.x,-x,x),0,Mathf.Clamp(local.z,-z,z));
        }
        bool Visible()
        {
            var delta=coin.position-actor.EyePosition;
            bool lookingDown=carryRequest!=null && delta.y<0 && Flat(delta).magnitude<actor.HeightMeters*.5f;
            if(!lookingDown && Vector3.Angle(searching?Flat(actor.SightDirection):scene.SpatialRotation*Vector3.forward,Flat(delta))>100)return false;
            if(!palm.gameObject.activeSelf)return true;
            var eye=actor.EyePosition;
            // 任一币缘仍在视野中，就能更新记忆。提币的所有权本身不能作为遮挡证据。
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*.25f;
                var rim=coin.position+coin.rotation*new Vector3(Mathf.Cos(angle)*Radius,.003f,Mathf.Sin(angle)*Radius);
                var ray=rim-eye;
                if(!palmCover.Raycast(new Ray(eye,ray.normalized),out _,ray.magnitude))return true;
            }
            return false;
        }
        static bool Crosses(Vector3 a,Vector3 b,Vector3 center,float r)
        {
            var ab=Flat(b-a);
            float t=Mathf.Clamp01(Vector3.Dot(Flat(center-a),ab)/Mathf.Max(.000001f,ab.sqrMagnitude));
            return Flat(a+t*ab-center).sqrMagnitude<r*r;
        }
        bool TryBypass(Vector3 start,Vector3 goal,Vector3 obstacle,float clearance,out Vector3 next)
        {
            next=start;
            obstacle.y=start.y;
            var entry=Flat(start-obstacle);
            var exit=Flat(goal-obstacle);
            if(Mathf.Min(entry.magnitude,exit.magnitude)<=clearance)return false;
            float angle=Vector3.SignedAngle(entry,exit,Vector3.up);
            // 沿手掌安全圆的两侧检查短折线。每段都验证桌边及足迹，重新观察后再推进。
            for(int side=0;side<2;side++)
            {
                float arc=side==0?angle:angle-Mathf.Sign(angle)*360;
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(arc)/20f));
                var previous=start;
                Vector3? first=null;
                bool legal=true;
                for(int i=0;i<=steps;i++)
                {
                    var direction=Quaternion.AngleAxis(arc*i/steps,Vector3.up)*entry.normalized;
                    var point=obstacle+direction*(clearance*1.08f);
                    if(!Inside(point,table.RadiusMeters)||Crosses(previous,point,obstacle,clearance)){legal=false;break;}
                    if(!first.HasValue && Flat(point-start).magnitude>.012f)first=point;
                    previous=point;
                }
                if(legal && !Crosses(previous,goal,obstacle,clearance)){next=first??goal;return true;}
            }
            return false;
        }
        public void Store()
        {
            if(Deployed){Hand(false,default);coin.gameObject.SetActive(false);}
            StopTracking();actor=null;table=null;outside=dragging=false;Status="已收回";
        }
        internal void Interrupt()
        {
            if(!Deployed)return;
            dragging=false;Hand(false,default);
            if(seen||controlling||searching)StopTracking();
            Status="互动中 · 追逐暂停";
        }
        void StopTracking()
        {
            ReleaseFromArms();
            if(controlling && scene && scene.Locomotion)scene.Locomotion.CancelToy(true);
            if(actor){actor.ToyAttention=null;actor.SearchingForToy=false;}
            controlling=seen=searching=false;lastSeen=default;invisibleAge=visibleAge=searchAge=nextPlan=0;
        }
        void ReleaseFromArms()
        {
            if((carryRequest!=null || inArms) && actor)actor.Motions.EndToyCarry();
            if(inArms && coin)coin.isKinematic=false;
            inArms=false;bothContactTime=missedContactTime=0;carryRequest=null;settledTime=0;carryDeclined=false;
        }
        void AttemptCarry(float dt)
        {
            if(!leftPadA || !leftPadB || !rightPadA || !rightPadB || dragging || carrying || actor.Motions.Busy || carryDeclined)return;
            if(coin.linearVelocity.magnitude>.045f){settledTime=0;return;}
            settledTime+=dt;
            if(settledTime<.55f)return;
            var normal=coin.rotation*Vector3.up;
            if(Vector3.Dot(normal,-Vector3.up+scene.SpatialRotation*Vector3.forward*.2f)<0)normal=-normal;
            var side=Vector3.ProjectOnPlane(scene.SpatialRotation*Vector3.right,normal);
            if(side.sqrMagnitude<.01f)return;
            var frame=Quaternion.LookRotation(normal,Vector3.Cross(normal,side));
            gripLocalFrame=Quaternion.Inverse(coin.rotation)*frame;
            carryRequest=actor.Motions.BeginToyCarry(this);
        }
        float LeftSide=>Vector3.Dot(leftUpperArm.position-rightUpperArm.position,scene.SpatialRotation*Vector3.right)>=0?1:-1;
        void MeasureGrip()
        {
            float side=LeftSide;
            LeftGripError=Vector3.Distance((leftPadA.position+leftPadB.position)*.5f,GripPoint(side));
            RightGripError=Vector3.Distance((rightPadA.position+rightPadB.position)*.5f,GripPoint(-side));
            ContactGap=Mathf.Max(LeftGripError,RightGripError);
        }
        void LateUpdate()
        {
            if(!inArms || !actor)return;
            MeasureGrip();
            bool both=ContactGap<=Mathf.Min(Radius*.14f,actor.HeightMeters*.025f);
            bothContactTime=both?bothContactTime+Time.deltaTime:0;
            missedContactTime=both?0:missedContactTime+Time.deltaTime;
            if(!actor.Motions.CarryLiftComplete || liftAge<.64f){Status="抬起硬币";return;}
            if(HugSettled){Status="双手抱稳了";return;}
            Status="正在合拢双手";
            if(missedContactTime>.4f){ReleaseFromArms();carryDeclined=true;Status="松开硬币";}
        }
        void AdvanceCoin()
        {
            liftAge+=Time.deltaTime;
            var facing=scene.SpatialRotation;
            var frame=facing*Quaternion.Euler(holdTilt,0,0);
            var anchor=Feet+facing*Vector3.forward*(actor.HeightMeters*holdForwardAndHeight.x)
                +Vector3.up*(actor.HeightMeters*holdForwardAndHeight.y);
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(liftAge/.64f));
            coin.position=Vector3.Lerp(liftFrom,anchor,t);
            coin.rotation=Quaternion.Slerp(liftRotation,frame*Quaternion.Inverse(gripLocalFrame),t);
            lastSeen=coin.position;
            var ahead=Feet+facing*Vector3.forward*actor.HeightMeters+Vector3.up*actor.HeightMeters*.78f;
            actor.ToyAttention=Vector3.Lerp(coin.position,ahead,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.7f,1.4f,liftAge)));
        }
        void Update()
        {
            if(!Deployed)return;
            if(!scene.Ready || actor!=scene.Avatar || table!=scene.Interaction || !table.PlaneConfirmed){Store();return;}
            if(table.State!=PetState.Idle || scene.SizeEditing){StopTracking();return;}
            bool visible=Visible();
            if(inArms){
                if(actor.Motions.HoldingToyPose){
                    AdvanceCoin();return;
                }
                ReleaseFromArms();
            }
            if(carryRequest!=null){
                bool reachable=visible && !dragging && !carrying && Flat(coin.position-Feet).magnitude<=table.RadiusMeters+Radius+.018f;
                if(!reachable)ReleaseFromArms();
                else if(carryRequest.IsCompleted){
                    if(carryRequest.IsCompletedSuccessfully && carryRequest.Result && actor.Motions.HoldingToyPose){
                        Status="俯身捡硬币";
                        if(!actor.Motions.ReachingToy)return;
                        MeasureGrip();
                        if(ContactGap>actor.HeightMeters*.028f){
                            if(actor.Motions.CarryReachExpired){ReleaseFromArms();carryDeclined=true;Status="硬币够不到";}
                            return;
                        }
                        actor.Motions.AcceptToyContact();
                        liftFrom=coin.position;liftRotation=coin.rotation;liftAge=0;
                        coin.isKinematic=true;inArms=true;Status="抬起硬币";
                    }else carryDeclined=true;
                    carryRequest=null;
                }
                if(inArms || carryRequest!=null)return;
            }
            visibleAge=visible?visibleAge+Time.deltaTime:0;
            visible &= !searching || visibleAge>=.10f;
            if(visible){
                if(!Inside(coin.position,.001f) || coin.position.y<table.PlaneCenter.Y-.06f){
                    StopTracking();outside=true;Status="硬币出界 · 可取回";return;
                }
            }
            if(outside)return;
            if(visible){
                seen=true;lastSeen=coin.position;searching=false;actor.SearchingForToy=false;invisibleAge=searchAge=0;
                actor.ToyAttention=lastSeen;
            }else{
                if(!seen)return;
                invisibleAge+=Time.deltaTime;
                if(invisibleAge>=.18f && !searching){searching=true;Searches++;actor.SearchingForToy=true;}
                searchAge+=Time.deltaTime;
                actor.ToyAttention=lastSeen+scene.SpatialRotation*Vector3.right*(searching?.09f*Mathf.Sin(searchAge*3):0);
            }
            if(Time.time<nextPlan)return;
            nextPlan=Time.time+.10f;
            var delta=Flat(lastSeen-Feet);float reach=Mathf.Max(table.RadiusMeters, Radius*.95f);
            var goal=Clamp(lastSeen-delta.normalized*reach,table.RadiusMeters);
            var travel=scene.Locomotion;
            if(delta.magnitude<=reach+.012f || Flat(goal-Feet).magnitude<.008f){
                if(controlling && travel.Active)travel.CancelToy();
                controlling=false;
                if(visible && !dragging && !carrying)AttemptCarry(.1f);else settledTime=0;
                Status=searching?"在刚才的位置寻找":"够到硬币了";return;
            }
            settledTime=0;
            if(palm.gameObject.activeSelf && !carrying && Crosses(Feet,goal,palm.position,PalmRadius+table.RadiusMeters)){
                float clearance=PalmRadius+table.RadiusMeters;
                if(!TryBypass(Feet,goal,palm.position,clearance,out var via)){
                    if(controlling)travel.SetBlocked(true);
                    Status="手掌挡路 · 等待";return;
                }
                goal=via;
                Status="绕开手掌";
            }else{Status=searching?"去最后看到的地方寻找":"追硬币";}
            if(controlling)travel.SetBlocked(false);
            if(travel.State==PetLocomotion.Phase.Preparing || (travel.Active && Flat(travel.Target-goal).magnitude<.009f))return;
            controlling=true;_=travel.FollowToy(goal);
        }
        void OnDisable(){Store();}
        void OnDestroy(){if(coin)Destroy(coin.gameObject);if(palm)Destroy(palm.gameObject);foreach(var m in paints)Destroy(m);if(friction)Destroy(friction);}
    }
}
