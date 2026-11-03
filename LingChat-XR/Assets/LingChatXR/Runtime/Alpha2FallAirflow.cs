using System.Collections.Generic;
using LingChatXR.Core;
using UniGLTF.SpringBoneJobs;
using UniGLTF.SpringBoneJobs.Blittables;
using UniVRM10;
using UnityEngine;

namespace LingChatXR
{
    // 仅由 SHA 校验后的 alpha.2 适配器创建。向现有 SpringBone 注入相对气流，
    // 不写骨骼、不驱动空间根、不创建第二套模拟，也不改源 VRM 的参数。
    public sealed class Alpha2FallAirflow
    {
        sealed class Joint
        {
            public Transform transform;
            public BlittableJointMutable source;
            public float response, limit;
        }
        readonly Vrm10Instance instance;
        readonly List<Joint> joints = new List<Joint>();
        float pressure;

        public Alpha2FallAirflow(Vrm10Instance instance)
        {
            this.instance = instance;
            foreach (var spring in instance.SpringBone.Springs)
            {
                bool skirt = spring.Name.StartsWith("Skirt gentle ");
                bool side = spring.Name.StartsWith("HairSide");
                bool back = spring.Name.StartsWith("HairBack");
                if (!skirt && !side && !back) continue;
                // 末端节点无后继模拟段；每段保留原半径、阻尼、回正与重力快照。
                for (int i=0;i<spring.Joints.Count-1;i++)
                {
                    var j=spring.Joints[i];
                    joints.Add(new Joint { transform=j.transform, source=j.Blittable,
                        response=skirt?1f:side?.25f:.65f,
                        limit=(skirt?30f:side?(i==0?18f:28f):(i==0?28f:40f))*Mathf.Deg2Rad });
                }
            }
        }

        public void Update(PetInteraction state, float deltaTime)
        {
            if (!Vec3.Finite(deltaTime) || deltaTime<=0) return;
            bool falling=state.State==PetState.Descending;
            float speed=falling?Mathf.Max(0,-state.VerticalVelocity):0;
            // 这是 UniVRM 外力参数，不是牛顿；按速度平方增长，并限制高速吹翻。
            float target=Mathf.Min(2.8f,.25f*speed*speed);
            bool cancelled=state.State==PetState.Held||state.State==PetState.Suspended||state.State==PetState.Unplaced;
            pressure=cancelled?0:Mathf.Lerp(pressure,target,1-Mathf.Exp(-Mathf.Min(deltaTime,.05f)/(falling?.045f:.09f)));
            if (pressure<.0001f) pressure=0;
            Apply();
        }

        public void Clear() { pressure=0;Apply(); }

        void Apply()
        {
            foreach(var j in joints)
            {
                var b=j.source;
                if(pressure==0) { instance.Runtime.SpringBone.SetJointLevel(j.transform,b);continue; }
                Vector3 force=(Vector3)b.gravityDir*b.gravityPower+Vector3.up*(pressure*j.response);
                // 用包内的 Cone 限幅，避免侧发横扫眼睛、裙摆整片向上翻折。
                // 源资产已有角限时沿用，绝不覆盖作者限制。
                bool limit=b.anglelimitType==AnglelimitTypes.None;
                instance.Runtime.SpringBone.SetJointLevel(j.transform,new BlittableJointMutable(
                    b.stiffnessForce,force.magnitude,force.normalized,b.dragForce,b.radius,
                    limit?(float)AnglelimitTypes.Cone:(float)b.anglelimitType,
                    limit?j.limit:b.anglelimit1,b.anglelimit2,b.anglelimitOffset));
            }
        }
    }
}
