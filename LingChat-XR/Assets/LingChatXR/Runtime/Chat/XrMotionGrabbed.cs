using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace LingChatXR.Chat
{
    public sealed partial class XrMotionPlayer
    {
        Clip grabbedClip, tailGrabbedClip;
        bool tailGrabbed;
        public bool TailGrabReady => tailGrabbedClip != null;
        public bool TailGrabbed => tailGrabbed;
        public bool SelectTailGrab(bool value)
        {
            if (grabbed || (value && !TailGrabReady)) return false;
            tailGrabbed = value;
            return true;
        }
        Task<bool> grabbedPreparation;
        bool grabbed;
        float grabbedAge,grabbedBlend,grabbedExit=-1;
        Quaternion[] grabbedFrom;
        public bool GrabbedActive=>grabbed||grabbedExit>=0;
        public Task<bool> PrepareGrabbed()=>grabbedPreparation??=LoadGrabbed();
        async Task<bool> LoadGrabbed()
        {
            try
            {
                var folder=Application.streamingAssetsPath+"/Motions/local-grab";
                var json=JObject.Parse(System.Text.Encoding.UTF8.GetString(await PackageFiles.ReadAsync(folder,"catalog.json",lifetime.Token)));
                grabbedClip=await Load(json.ToObject<XrMotionAsset>(),folder);
                // 尾巴候选是可选资源，加载失败时保留普通提起。
                try
                {
                    var tail = JObject.Parse(System.Text.Encoding.UTF8.GetString(await PackageFiles.ReadAsync(folder,"tail.json",lifetime.Token)));
                    tailGrabbedClip = await Load(tail.ToObject<XrMotionAsset>(),folder);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Debug.LogWarning("尾巴提起候选未就绪：" + e.Message); }
                return !disposed;
            }
            catch(OperationCanceledException){return false;}
            catch(Exception e){if(!disposed)Debug.LogWarning("提起动作无法载入："+e.Message);return false;}
        }
        public void SetGrabbed(bool held,bool reset=false)
        {
            if(disposed)return;
            if(reset){grabbed=false;grabbedExit=-1;return;}
            if(held==grabbed)return;
            grabbedFrom??=new Quaternion[bones.Length];Snapshot(grabbedFrom);
            grabbed=held;grabbedAge=0;grabbedBlend=0;grabbedExit=held?-1:0;
        }
        void StepGrabbed(float dt)
        {
            if(!GrabbedActive)return;
            dt=Mathf.Clamp(dt,0,.05f);
            if(grabbed)
            {
                var heldClip = tailGrabbed ? tailGrabbedClip : grabbedClip;
                if(heldClip==null)return;
                // 完整播放用户片段；循环接缝混合，不插入其他待机，不采用髋根平移。
                grabbedAge+=dt;grabbedBlend+=dt;
                float length=heldClip.animation.length,phase=grabbedAge%(length+.2f);
                Sample(heldClip,Mathf.Min(phase,length));
                float seam=Smooth((phase-length)/.2f),weight=Smooth(grabbedBlend/.25f);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(grabbedFrom[i],phase>length?Quaternion.Slerp(heldClip.end[i],heldClip.start[i],seam):bones[i].localRotation,weight);
                // 在作者当帧上把脚到头的方向扶正，保留关节相对姿态和空间根的水平朝向。
                if(controlBones.TryGetValue(HumanBodyBones.Head,out var head)
                    &&controlBones.TryGetValue(HumanBodyBones.LeftFoot,out var leftFoot)
                    &&controlBones.TryGetValue(HumanBodyBones.RightFoot,out var rightFoot))
                {
                    var axis=head.position-(leftFoot.position+rightFoot.position)*.5f;
                    if(tailGrabbed) axis = head.position - hips.position;
                    if(axis.sqrMagnitude>.000001f)
                        hips.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(axis,tailGrabbed ? Vector3.down : Vector3.up),weight*(tailGrabbed ? 1f : .55f))*hips.rotation;
                    var direction=head.forward;
                    var flat=Vector3.ProjectOnPlane(direction,Vector3.up);
                    if(!tailGrabbed && flat.sqrMagnitude>.001f)
                    {
                        float elevation=Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg;
                        float excess=Mathf.Max(0,elevation+8);
                        var pitchAxis=Vector3.Cross(Vector3.up,flat.normalized);
                        head.rotation=Quaternion.AngleAxis(excess*weight,pitchAxis)*head.rotation;
                    }
                }
            }
            else
            {
                grabbedExit+=dt;float weight=Smooth(grabbedExit/.3f);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(grabbedFrom[i],bones[i].localRotation,weight);
                if(weight>=1)grabbedExit=-1;
            }
        }
    }
}
