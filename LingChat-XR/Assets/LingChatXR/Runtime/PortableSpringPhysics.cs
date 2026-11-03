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
        FastSpringBoneCombinedBuffer configured;
        bool resetPending=true, paused;
        float lastScale=-1;
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
        public void SetSpatialMotion(Core.PetInteraction state,float dt)
        {
            Vector3 position=FeiyuPreview.ToUnity(state.Position),velocity=Vector3.zero;
            if(state.State==Core.PetState.Descending)velocity=Vector3.up*state.VerticalVelocity;
            else if(state.State==Core.PetState.Held&&hasPosition&&float.IsFinite(dt)&&dt>0)
                velocity=(position-previousPosition)/dt;
            previousPosition=position;hasPosition=true;
            if(!MotionAirEnabled||paused||!float.IsFinite(velocity.sqrMagnitude)){MotionAirForce=Vector3.zero;return;}
            MotionAirForce=-velocity.normalized*Mathf.Min(.8f,.08f*velocity.sqrMagnitude);
        }
        public void Initialize(Vrm10Instance value)
        {
            instance=value;
            foreach(var spring in value.SpringBone.Springs)
                foreach(var joint in spring.Joints)source[joint.transform]=joint.Blittable;
        }
        public void RequestReset() {resetPending=true;hasPosition=false;MotionAirForce=Vector3.zero;}
        public void SetPaused(bool value)
        {
            if(paused==value)return;
            paused=value;RequestReset();
        }
        void OnDisable(){if(instance!=null)SetPaused(true);}
        void OnEnable(){if(instance!=null)SetPaused(false);}
        void LateUpdate()=>Prepare();

        // 自动路径在 VRM 姿态之后、唯一 SpringBone Service 之前；手动验证也调用同一入口。
        public void Prepare()
        {
            if(instance==null)return;
            var combiner=FastSpringBoneService.Instance.BufferCombiner;
            float scale=Mathf.Max(Mathf.Abs(instance.transform.lossyScale.x),Mathf.Abs(instance.transform.lossyScale.y),Mathf.Abs(instance.transform.lossyScale.z));
            if(!float.IsFinite(scale)||scale<=0)throw new System.InvalidOperationException("物理要求有效正尺度。");
            // 原生逻辑段长在构造时固定；运行缩放变化必须重建长度，不能只更新半径。
            if(lastScale>0&&!Mathf.Approximately(lastScale,scale))
            {
                instance.Runtime.SpringBone.ReconstructSpringBone();resetPending=true;
            }
            // 注册/卸载只排队；先完成合并，避免把参数写入即将废弃的旧 buffer。
            combiner.ReconstructIfDirty(default).Complete();
            var buffer=combiner.Combined;if(buffer==null)return;
            if(configured!=buffer||!Mathf.Approximately(lastScale,scale)||configuredAir!=MotionAirEnabled)
            {
                foreach(var entry in source)
                {
                    var b=entry.Value;var s=entry.Key.lossyScale;
                    float radiusScale=Mathf.Max(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));
                    // 原生 Cone 约束作为通用运动模式的最大弯曲范围；已有作者角限优先。
                    // 无约束链直接受逆向风力会翻转 180°，不能只加大外力。
                    bool fallbackLimit=MotionAirEnabled&&b.anglelimitType==AnglelimitTypes.None;
                    instance.Runtime.SpringBone.SetJointLevel(entry.Key,new BlittableJointMutable(b.stiffnessForce,b.gravityPower,b.gravityDir,b.dragForce,
                        b.radius*radiusScale,fallbackLimit?(float)AnglelimitTypes.Cone:(float)b.anglelimitType,fallbackLimit?30f*Mathf.Deg2Rad:b.anglelimit1,b.anglelimit2,b.anglelimitOffset));
                }
                configured=buffer;lastScale=scale;configuredAir=MotionAirEnabled;
            }
            instance.Runtime.SpringBone.SetModelLevel(instance.transform,new BlittableModelLevel(externalForce:MotionAirEnabled&&!paused?MotionAirForce:Vector3.zero,stopSpringBoneWriteback:paused,supportsScalingAtRuntime:true));
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
                }
            }
            resetPending=false;
        }
    }
}
