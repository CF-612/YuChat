using System.Collections.Generic;
using UniGLTF.SpringBoneJobs;
using UniGLTF.SpringBoneJobs.Blittables;
using UniVRM10;
using UniVRM10.FastSpringBones;
using UnityEngine;

namespace LingChatXR
{
    // UniVRM 0.131.4 接线层：单位、缓存坐标与运动输入，不另建积分器或修改作者数据。
    [DefaultExecutionOrder(11005)]
    public sealed class PortableSpringPhysics : MonoBehaviour
    {
        Vrm10Instance instance;
        readonly Dictionary<Transform,BlittableJointMutable> source=new Dictionary<Transform,BlittableJointMutable>();
        readonly Dictionary<Transform,BlittableJointMutable> materials=new Dictionary<Transform,BlittableJointMutable>();
        readonly Dictionary<VRM10SpringBoneCollider,(Vector3 offset,float radius,VRM10SpringBoneColliderTypes type,Vector3 tail)> colliderSource=new();
        readonly List<VRM10SpringBoneColliderGroup[]> groupSource=new List<VRM10SpringBoneColliderGroup[]>();
        readonly Dictionary<Transform,(VRM10SpringBoneCollider collider,float standingRadius,float walkingRadius)> gaitCapsules=new();
        readonly List<(int index,BlittableCollider original,float standingRadius,float walkingRadius)> gaitSlots=new();
        readonly HashSet<Transform> gaitRoots=new();
        readonly Dictionary<Transform,List<VRM10SpringBoneCollider>> sphereChains=new();
        readonly List<(int index,BlittableCollider calibrated,Vector3 offset,float radius)> groundSpheres=new();
        readonly HashSet<Transform> authoredGroundJoints=new();
        readonly HashSet<Transform> authoredIdleJoints=new();
        readonly Dictionary<Transform,List<VRM10SpringBoneCollider>> idleChains=new();
        readonly List<(int index,BlittableCollider calibrated,Vector3 offset,float radius,float standing,float walking)> idleSpheres=new();
        float appliedIdleBlend=-1;
        float IdleBlend=>groundBlend*(1-gaitBlend);
        float gaitBlend,groundBlend=1,appliedGroundBlend=-1;
        public int ColliderRebuildCount { get; private set; }
        public int ConfiguredJointCount => materials.Count;
        FastSpringBoneCombinedBuffer configured;
        bool resetPending=true, paused;
        float lastScale=-1;
        float structuralScale;
        public int ScaleRebuildCount { get; private set; }
        public void CaptureStructuralScale()
        {
            var scale=instance.transform.lossyScale;
            structuralScale=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
        }
        bool configuredAir;
        // 相对静止空气的运动输入；力的积分、碰撞和约束仍全部由 UniVRM 执行。
        // 数值是 UniVRM 的外力单位，不是牛顿。所有模型共用，不识别角色/骨名。
        public bool MotionAirEnabled { get; set; } = true;
        public bool WorldMotionEnabled { get; set; } = true;
        public Vector3 MotionAirForce { get; private set; }
        Vector3 previousRootPosition;
        bool hasRootPosition;
        Vector3 previousPosition;
        bool hasPosition;
        public void SetSpatialMotion(Core.PetInteraction state,float dt,bool walking=false)
        {
            if(float.IsFinite(dt)){
                var step=Mathf.Clamp(dt,0,.1f)*6;
                bool grounded=state.State==Core.PetState.Idle || state.State==Core.PetState.Touched;
                groundBlend=Mathf.MoveTowards(groundBlend,grounded?1:0,step);
                gaitBlend=Mathf.MoveTowards(gaitBlend,walking&&grounded?1:0,step);
            }
            Vector3 position=FeiyuPreview.ToUnity(state.Position),velocity=Vector3.zero;
            if(state.State==Core.PetState.Descending)velocity=Vector3.up*state.VerticalVelocity;
            else if((state.State==Core.PetState.Held || state.State==Core.PetState.Idle&&state.AutonomousEnabled) &&
                hasPosition&&float.IsFinite(dt)&&dt>0&&dt<=.10f)
                velocity=(position-previousPosition)/dt;
            previousPosition=position;hasPosition=true;
            if(!MotionAirEnabled||paused||!float.IsFinite(velocity.sqrMagnitude)){MotionAirForce=Vector3.zero;return;}
            MotionAirForce=-velocity.normalized*Mathf.Min(.8f,.08f*velocity.sqrMagnitude);
        }
        public void Initialize(Vrm10Instance value,SpringPhysicsConfiguration configuration=null)
        {
            instance=value;
            CaptureStructuralScale();
            foreach(var spring in value.SpringBone.Springs)
                foreach(var joint in spring.Joints)source[joint.transform]=joint.Blittable;
            foreach(var group in value.SpringBone.ColliderGroups)
                foreach(var collider in group.Colliders)if(collider!=null)colliderSource[collider]=(collider.Offset,collider.Radius,collider.ColliderType,collider.Tail);
            foreach(var spring in value.SpringBone.Springs)groupSource.Add(spring.ColliderGroups.ToArray());
            ConfigureMaterials(configuration);
        }
        // 映射完全来自角色包；运行时不根据角色身份、骨名关键词猜部件类型。
        public void ConfigureMaterials(SpringPhysicsConfiguration configuration)
        {
            // 配置切换时先还原仍在使用的合并缓冲，再重新解析原生胶囊槽位。
            var liveBuffer=FastSpringBoneService.Instance.BufferCombiner.Combined;
            if(liveBuffer!=null && liveBuffer==configured)
            {
                var entries=liveBuffer.Colliders;
                foreach(var slot in idleSpheres)entries[slot.index]=slot.calibrated;
                foreach(var slot in groundSpheres)entries[slot.index]=slot.calibrated;
                foreach(var slot in gaitSlots)entries[slot.index]=slot.original;
            }
            groundSpheres.Clear();sphereChains.Clear();idleChains.Clear();idleSpheres.Clear();authoredIdleJoints.Clear();
            gaitCapsules.Clear();gaitSlots.Clear();gaitRoots.Clear();authoredGroundJoints.Clear();
            materials.Clear();configured=null;
            ConfigureColliders(configuration);
            if(configuration?.bindings==null||configuration.materials==null)return;
            foreach(var binding in configuration.bindings)
            {
                if(binding==null||binding.spring<0||binding.spring>=instance.SpringBone.Springs.Count)continue;
                var chain=instance.SpringBone.Springs[binding.spring];
                var material=System.Array.Find(configuration.materials,m=>m!=null&&m.id==binding.material&&m.Valid);
                if(material==null||chain.Joints.Count==0||chain.Joints[0].transform.name!=binding.root)
                {Debug.LogWarning("物理材质绑定无效，保留作者配置："+binding.root);continue;}
                if(material.authoredGroundSpheres || material.authoredIdleResponse)
                {
                    var colliders=new List<VRM10SpringBoneCollider>();
                    foreach(var group in chain.ColliderGroups)colliders.AddRange(group.Colliders);
                    (material.authoredIdleResponse?idleChains:sphereChains)[chain.Joints[0].transform]=colliders;
                }
                if(material.useWalkingClearance)gaitRoots.Add(chain.Joints[0].transform);
                for(int i=0;i<chain.Joints.Count;i++)
                {
                    var joint=chain.Joints[i].transform;var b=source[joint];
                    if(material.authoredGroundResponse)authoredGroundJoints.Add(joint);
                    if(material.authoredIdleResponse)authoredIdleJoints.Add(joint);
                    float t=chain.Joints.Count<=1?0:(float)i/(chain.Joints.Count-1);
                    bool authored=b.anglelimitType!=AnglelimitTypes.None;
                    materials[joint]=new BlittableJointMutable(Mathf.Lerp(material.rootStiffness,material.tipStiffness,t),material.gravity,
                        new Unity.Mathematics.float3(0,-1,0),material.drag,Mathf.Max(material.minimumContactRadius,b.radius),
                        authored?(float)b.anglelimitType:(float)AnglelimitTypes.Cone,
                        authored?b.anglelimit1:Mathf.Lerp(material.rootAngle,material.tipAngle,t)*Mathf.Deg2Rad,b.anglelimit2,b.anglelimitOffset);
                }
            }
        }
        // 只应用明确的角色包校准；加载/切换配置时重建一次，不在逐帧更新里改碰撞结构。
        void ConfigureColliders(SpringPhysicsConfiguration configuration)
        {
            bool changed=false;
            var desired=new Dictionary<VRM10SpringBoneCollider,(Vector3 offset,float radius,VRM10SpringBoneColliderTypes type,Vector3 tail)>(colliderSource);
            if(configuration?.colliders!=null)foreach(var entry in configuration.colliders)
            {
                if(entry==null||entry.group<0||entry.group>=instance.SpringBone.ColliderGroups.Count)continue;
                var group=instance.SpringBone.ColliderGroups[entry.group];
                if(entry.index<0||entry.index>=group.Colliders.Count)continue;
                var collider=group.Colliders[entry.index];
                if(collider==null||collider.transform.name!=entry.node||!float.IsFinite(entry.offset.sqrMagnitude)||
                    !float.IsFinite(entry.radius)||entry.radius<=0||entry.radius>1)
                {Debug.LogWarning("碰撞校准无效，保留作者配置："+entry.node);continue;}
                var original=colliderSource[collider];
                var shape=original.type;
                Vector3 tail=original.tail;
                if(!string.IsNullOrEmpty(entry.capsuleEndBone))
                {
                    if(!System.Enum.TryParse(entry.capsuleEndBone,out HumanBodyBones bone) || bone>=HumanBodyBones.LastBone ||
                        !instance.TryGetBoneTransform(bone,out var end) || !end.IsChildOf(collider.transform))
                    {Debug.LogWarning("碰撞胶囊终点无效："+entry.capsuleEndBone);continue;}
                    tail=collider.transform.InverseTransformPoint(end.position);
                    shape=VRM10SpringBoneColliderTypes.Capsule;
                    if(float.IsFinite(entry.walkingRadius)&&entry.walkingRadius>entry.radius&&entry.walkingRadius<=1)
                    {
                        float standing=float.IsFinite(entry.standingRadius)?Mathf.Clamp(entry.standingRadius,entry.radius,entry.walkingRadius):entry.radius;
                        gaitCapsules[collider.transform]=(collider,standing,entry.walkingRadius);
                    }
                }
                desired[collider]=(entry.offset,entry.radius,shape,tail);
            }
            foreach(var pair in desired)
            {
                var c=pair.Key;var v=pair.Value;
                if(c.Offset!=v.offset||!Mathf.Approximately(c.Radius,v.radius)||c.ColliderType!=v.type||c.Tail!=v.tail)
                {c.Offset=v.offset;c.Radius=v.radius;c.ColliderType=v.type;c.Tail=v.tail;changed=true;}
            }
            for(int i=0;i<instance.SpringBone.Springs.Count;i++)
            {
                var groups=new List<VRM10SpringBoneColliderGroup>(groupSource[i]);
                if(configuration?.bindings!=null)foreach(var binding in configuration.bindings)
                {
                    var spring=instance.SpringBone.Springs[i];
                    if(binding==null||binding.spring!=i||spring.Joints.Count==0||spring.Joints[0].transform.name!=binding.root||binding.extraColliderGroups==null)continue;
                    foreach(int index in binding.extraColliderGroups)if(index>=0&&index<instance.SpringBone.ColliderGroups.Count&&!groups.Contains(instance.SpringBone.ColliderGroups[index]))groups.Add(instance.SpringBone.ColliderGroups[index]);
                }
                var current=instance.SpringBone.Springs[i].ColliderGroups;
                if(current.Count!=groups.Count||!System.Linq.Enumerable.SequenceEqual(current,groups)){current.Clear();current.AddRange(groups);changed=true;}
            }
            if(changed){instance.Runtime.SpringBone.ReconstructSpringBone();ColliderRebuildCount++;resetPending=true;}
        }
        void BindGaitColliders(FastSpringBoneCombinedBuffer buffer)
        {
            gaitSlots.Clear();
            groundSpheres.Clear();idleSpheres.Clear();
            foreach(var spring in buffer.Springs)
            {
                if(spring.logicSpan.count==0)continue;
                int offset=spring.transformIndexOffset;
                int root=buffer.Logics[spring.logicSpan.startIndex].headTransformIndex;
                var rootBone=buffer.TransformAccessArray[offset+root];
                // 同一头骨可能有多个球，使用 UniVRM 展平后的组内顺序匹配。
                bool idle=idleChains.TryGetValue(rootBone,out var spheres);
                if((idle || sphereChains.TryGetValue(rootBone,out spheres)) && spheres.Count==spring.colliderSpan.count)
                {
                    for(int j=0;j<spheres.Count;j++)
                    {
                        int index=spring.colliderSpan.startIndex+j;
                        var native=buffer.Colliders[index];
                        var sphere=spheres[j];
                        if(colliderSource.TryGetValue(sphere,out var original) && original.type==VRM10SpringBoneColliderTypes.Sphere &&
                            (sphere.ColliderType==original.type || idle && sphere.ColliderType==VRM10SpringBoneColliderTypes.Capsule) && sphere.transform==buffer.TransformAccessArray[offset+native.transformIndex])
                        {
                            if(idle)
                            {
                                bool hasGait=gaitCapsules.TryGetValue(sphere.transform,out var gait) && gait.collider==sphere && gaitRoots.Contains(rootBone);
                                idleSpheres.Add((index,native,original.offset,original.radius,
                                    hasGait?gait.standingRadius:native.radius,hasGait?gait.walkingRadius:native.radius));
                            }
                            else groundSpheres.Add((index,native,original.offset,original.radius));
                        }
                    }
                }
                if(!gaitRoots.Contains(rootBone))continue;
                for(int i=spring.colliderSpan.startIndex;i<spring.colliderSpan.EndIndex;i++)
                {
                    var original=buffer.Colliders[i];
                    var bone=buffer.TransformAccessArray[offset+original.transformIndex];
                    if(original.colliderType!=BlittableColliderType.Capsule || !gaitCapsules.TryGetValue(bone,out var pair))continue;
                    if(!Mathf.Approximately(original.radius,pair.collider.Radius) ||
                        (Vector3)original.offset!=pair.collider.Offset || (Vector3)original.tailOrNormal!=pair.collider.Tail)continue;
                    gaitSlots.Add((i,original,pair.standingRadius,pair.walkingRadius));
                }
            }
        }
        public void RequestReset() {resetPending=true;hasPosition=false;hasRootPosition=false;MotionAirForce=Vector3.zero;}
        public void SetPaused(bool value)
        {
            if(paused==value)return;
            paused=value;RequestReset();
        }
        void OnDisable(){if(instance!=null)SetPaused(true);}
        void OnEnable(){if(instance!=null)SetPaused(false);}
        bool recoveringLargeStep;
        int skippedGapFrames;
        void LateUpdate()
        {
            // 导入后的超长帧不能作为实时物理的一步。第一帧正常时重新播种当前位置。
            bool gap=Time.deltaTime>.10f;
            if(!gap)skippedGapFrames=0;
            bool skip=gap && (resetPending || recoveringLargeStep) && skippedGapFrames<2;
            if(skip)skippedGapFrames++;
            if(skip || recoveringLargeStep)RequestReset();
            recoveringLargeStep=skip;
            Prepare();
        }

        // 自动路径在 VRM 姿态之后、唯一 SpringBone Service 之前；手动验证也调用同一入口。
        public void Prepare()
        {
            if(instance==null)return;
            var combiner=FastSpringBoneService.Instance.BufferCombiner;
            float scale=structuralScale;
            if(!float.IsFinite(scale)||scale<=0)throw new System.InvalidOperationException("物理要求有效正尺度。");
            // 原生逻辑段长在构造时固定；运行缩放变化必须重建长度，不能只更新半径。
            if(lastScale>0&&!Mathf.Approximately(lastScale,scale))
            {
                instance.Runtime.SpringBone.ReconstructSpringBone();ScaleRebuildCount++;resetPending=true;
            }
            // 注册/卸载只排队；先完成合并，避免把参数写入即将废弃的旧 buffer。
            combiner.ReconstructIfDirty(default).Complete();
            var buffer=combiner.Combined;if(buffer==null)return;
            if(configured!=buffer)RequestReset();
            if(configured!=buffer||!Mathf.Approximately(lastScale,scale)||configuredAir!=MotionAirEnabled || (authoredIdleJoints.Count>0 && !Mathf.Approximately(IdleBlend,appliedIdleBlend)) || (authoredGroundJoints.Count>0 && !Mathf.Approximately(groundBlend,appliedGroundBlend)))
            {
                foreach(var entry in source)
                {
                    var b=materials.TryGetValue(entry.Key,out var material)?material:entry.Value;var s=entry.Key.lossyScale;
                    float radiusScale=Mathf.Max(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));
                    // 原生 Cone 约束作为通用运动模式的最大弯曲范围；已有作者角限优先。
                    // 无约束链直接受逆向风力会翻转 180°，不能只加大外力。
                    bool fallbackLimit=MotionAirEnabled&&b.anglelimitType==AnglelimitTypes.None;
                    float blend=authoredGroundJoints.Contains(entry.Key)?groundBlend:authoredIdleJoints.Contains(entry.Key)?IdleBlend:0;
                    var authored=entry.Value;
                    Vector3 gravity=Vector3.Lerp((Vector3)b.gravityDir*b.gravityPower,(Vector3)authored.gravityDir*authored.gravityPower,blend);
                    instance.Runtime.SpringBone.SetJointLevel(entry.Key,new BlittableJointMutable(
                        Mathf.Lerp(b.stiffnessForce,authored.stiffnessForce,blend),gravity.magnitude,gravity.normalized,
                        Mathf.Lerp(b.dragForce,authored.dragForce,blend),
                        Mathf.Lerp(b.radius,authored.radius,authoredIdleJoints.Contains(entry.Key)?blend:0)*radiusScale,fallbackLimit?(float)AnglelimitTypes.Cone:(float)b.anglelimitType,fallbackLimit?30f*Mathf.Deg2Rad:b.anglelimit1,b.anglelimit2,b.anglelimitOffset));
                }
                if(configured!=buffer) BindGaitColliders(buffer);
                configured=buffer;lastScale=scale;configuredAir=MotionAirEnabled;appliedGroundBlend=groundBlend;appliedIdleBlend=IdleBlend;
            }
            // 只改变现有大腿胶囊的接触距离，不重建物理世界。退出行走后回到角色包的静止半径。
            var colliders=buffer.Colliders;
            foreach(var slot in groundSpheres)
            {
                var c=slot.calibrated;
                Vector3 center=Vector3.Lerp(c.offset,slot.offset,groundBlend);
                float radius=Mathf.Lerp(c.radius,slot.radius,groundBlend);
                if((Vector3)colliders[slot.index].offset!=center || !Mathf.Approximately(colliders[slot.index].radius,radius))
                    colliders[slot.index]=new BlittableCollider(center,radius,c.tailOrNormal,c.colliderType,c.transformIndex);
            }
            foreach(var slot in gaitSlots){
                var c=slot.original;
                float radius=Mathf.Lerp(c.radius,Mathf.Lerp(slot.standingRadius,slot.walkingRadius,gaitBlend),groundBlend);
                if(!Mathf.Approximately(colliders[slot.index].radius,radius))
                    colliders[slot.index]=new BlittableCollider(c.offset,radius,c.tailOrNormal,c.colliderType,c.transformIndex);
            }
            // 站定时还原 VRM 球心与半径。胶囊的两端收拢后等价于同一原始球。
            foreach(var slot in idleSpheres)
            {
                var c=slot.calibrated;
                var center=Vector3.Lerp(c.offset,slot.offset,IdleBlend);
                var tail=c.colliderType==BlittableColliderType.Capsule
                    ?Vector3.Lerp(c.tailOrNormal,slot.offset,IdleBlend):(Vector3)c.tailOrNormal;
                var activeRadius=Mathf.Lerp(c.radius,Mathf.Lerp(slot.standing,slot.walking,gaitBlend),groundBlend);
                colliders[slot.index]=new BlittableCollider(center,Mathf.Lerp(activeRadius,slot.radius,IdleBlend),tail,c.colliderType,c.transformIndex);
            }
            bool hold=paused||recoveringLargeStep;
            instance.Runtime.SpringBone.SetModelLevel(instance.transform,new BlittableModelLevel(externalForce:MotionAirEnabled&&!hold?MotionAirForce:Vector3.zero,stopSpringBoneWriteback:hold,supportsScalingAtRuntime:true));
            Vector3 rootPosition=transform.position;
            Vector3 translation=hasRootPosition?rootPosition-previousRootPosition:Vector3.zero;
            previousRootPosition=rootPosition;hasRootPosition=true;
            // center 随角色移动时，原生缓存会被整体搬走，丢掉世界惯性。
            // 只抵消空间根平移：中心自身的动画/旋转仍按作者定义工作。
            // 无 center 的链已在世界空间，不重复传递；重置/暂停时不引入瞬移冲量。
            if(WorldMotionEnabled&&!paused&&!resetPending&&translation.sqrMagnitude>0)
            {
                var currentMotion=buffer.CurrentTails;var previousMotion=buffer.PrevTails;var nextMotion=buffer.NextTails;
                foreach(var spring in buffer.Springs)
                {
                    if(spring.centerTransformIndex<0)continue;
                    int offset=spring.transformIndexOffset;
                    var center=buffer.TransformAccessArray[offset+spring.centerTransformIndex];
                    // 桌面小比例模型不应承受完整根位移冲量。统一继承 8%，
                    // 不按角色或链分组；其余仍跟随作者 center，避免发片翻到脸前。
                    Vector3 localTranslation=center.InverseTransformVector(translation*.08f);
                    for(int i=spring.logicSpan.startIndex;i<spring.logicSpan.EndIndex;i++)
                    {
                        var head=buffer.TransformAccessArray[offset+buffer.Logics[i].headTransformIndex];
                        if(!source.ContainsKey(head))continue;
                        currentMotion[i]-=(Unity.Mathematics.float3)localTranslation;
                        previousMotion[i]-=(Unity.Mathematics.float3)localTranslation;
                        nextMotion[i]-=(Unity.Mathematics.float3)localTranslation;
                    }
                }
            }
            if(!resetPending)return;
            instance.Runtime.SpringBone.RestoreInitialTransform();
            // 原包 RestoreInitialTransform 将世界 tail 写进所有链。带 center 的缓存需局部坐标。
            var current=buffer.CurrentTails;var prev=buffer.PrevTails;var next=buffer.NextTails;
            bool seeded=false;
            foreach(var spring in buffer.Springs)
            {
                int offset=spring.transformIndexOffset;
                for(int i=spring.logicSpan.startIndex;i<spring.logicSpan.EndIndex;i++)
                {
                    var logic=buffer.Logics[i];var head=buffer.TransformAccessArray[offset+logic.headTransformIndex];
                    if(!source.ContainsKey(head)||logic.tailTransformIndex<0)continue;
                    var point=buffer.TransformAccessArray[offset+logic.tailTransformIndex].position;
                    if(spring.centerTransformIndex>=0)point=buffer.TransformAccessArray[offset+spring.centerTransformIndex].InverseTransformPoint(point);
                    current[i]=prev[i]=next[i]=point;
                    seeded=true;
                }
            }
            // 全局缓冲可能先包含其他实例；等当前实例真正注册后再完成复位。
            resetPending=!seeded && source.Count>0;
        }
    }
}
