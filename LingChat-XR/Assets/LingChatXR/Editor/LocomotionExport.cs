using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LingChatXR.Editor
{
    // 按 UniVRM 官方 VrmAnimationMenu 的导出路径转换作者 Humanoid；不转换角色模型。
    static class LocomotionExport
    {
        static readonly string[] Ids={"locomotion_idle","locomotion_walk","locomotion_left","locomotion_right"};
        public static JObject Convert(GameObject template,AnimationClip clip,string workspace,int index)=>Convert(template,clip,workspace,Ids[index],"Kevin Iglesias Human Basic Motions FREE; restricted raw redistribution",index==1);
        public static JObject Convert(GameObject template,AnimationClip clip,string workspace,string id,string license,bool moving)
        {
            var go=UnityEngine.Object.Instantiate(template);var wrapper=new GameObject("LocomotionSkeleton");go.transform.SetParent(wrapper.transform,false);PlayableGraph graph=default;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var animator=go.GetComponent<Animator>();animator.Rebind();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var map=new Dictionary<HumanBodyBones,Transform>();
                foreach(HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))if(b!=HumanBodyBones.LastBone&&animator.GetBoneTransform(b)!=null)map.Add(b,animator.GetBoneTransform(b));
                float leg=Vector3.Distance(map[HumanBodyBones.LeftUpperLeg].position,map[HumanBodyBones.LeftLowerLeg].position)+Vector3.Distance(map[HumanBodyBones.LeftLowerLeg].position,map[HumanBodyBones.LeftFoot].position);
                // 新建实例仍在作者 Avatar 的绑定姿态，先 Prepare 再播放，避免把动作第一帧当作 T 姿势。
                float armHorizontal=Mathf.Abs(Vector3.Dot((map[HumanBodyBones.LeftLowerArm].position-map[HumanBodyBones.LeftUpperArm].position).normalized,Vector3.right));
                if(armHorizontal<.9f)throw new InvalidDataException("源 Avatar 并非 T 姿势，停止导出，不猜测校正角度");
                foreach(var renderer in go.GetComponentsInChildren<Renderer>())UnityEngine.Object.DestroyImmediate(renderer);
                foreach(var mesh in go.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.DestroyImmediate(mesh);
                // VRMA 导入器只读取 scene 的首个根节点。使用一个总根包住作者层级，
                // 避免 FBX 的空网格节点排在 Rig 前导致人形层级被丢弃；不修改第三方导入器。
                var data=new ExportingGltfData();using var exporter=new VrmAnimationExporter(data,new GltfExportSettings());exporter.Prepare(wrapper);
                var feet=new List<Vector3[]>();
                exporter.Export(vrma=>
                {
                    vrma.SetPositionBoneAndParent(map[HumanBodyBones.Hips],go.transform);
                    foreach(var pair in map)
                    {
                        var bone=Vrm10HumanoidBoneSpecification.ConvertFromUnityBone(pair.Key);Transform parent=null;
                        while(bone!=Vrm10HumanoidBones.Hips)
                        {
                            bone=Vrm10HumanoidBoneSpecification.GetDefine(bone).ParentBone.Value;
                            if(map.TryGetValue(Vrm10HumanoidBoneSpecification.ConvertToUnityBone(bone),out parent))break;
                        }
                        vrma.AddRotationBoneAndParent(pair.Key,pair.Value,parent??go.transform);
                    }
                    graph=PlayableGraph.Create("Official Humanoid to VRMA");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable=AnimationClipPlayable.Create(graph,clip);var sink=AnimationPlayableOutput.Create(graph,"Humanoid",animator);sink.SetSourcePlayable(playable);graph.Play();
                    int frames=Mathf.CeilToInt(clip.length*60);
                    for(int i=0;i<=frames;i++)
                    {
                        playable.SetTime(i*clip.length/frames);graph.Evaluate(0);
                        vrma.AddFrame(TimeSpan.FromSeconds(i*clip.length/frames));
                        feet.Add(new[]{map[HumanBodyBones.LeftFoot].position,map[HumanBodyBones.RightFoot].position});
                    }
                });
                // 由源脚在最低支撑区向后移动的速度估计步幅；不按角色名字或头身比指定步速。
                var speeds=new List<float>();float dt=clip.length/(feet.Count-1);
                for(int foot=0;foot<2;foot++)
                {
                    float low=feet.Min(f=>f[foot].y),high=feet.Max(f=>f[foot].y);
                    for(int i=1;i<feet.Count;i++)if(feet[i][foot].y<low+(high-low)*.25f)
                    {float v=-(feet[i][foot].z-feet[i-1][foot].z)/dt;if(v>.01f)speeds.Add(v);}
                }
                speeds.Sort();float speed=speeds.Count==0?0:speeds[speeds.Count/2];
                if(moving&&(speed<.05f||speed>8))throw new InvalidDataException("无法从支撑脚测量可信步速: "+clip.name+" "+speed);
                var bytes=data.ToGlbBytes();string folder=Path.Combine(workspace,"LingChat-XR/Assets/StreamingAssets/Motions/local-locomotion");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,id+".vrma"),bytes);
                return new JObject{["id"]=id,["label"]=clip.name,["sha256"]=PackageFiles.Hash(bytes),["loop"]=true,["length"]=clip.length,["sourceLegLength"]=leg,["sourceSpeed"]=speed,["license"]=license,["sourceFormat"]="Unity Humanoid → UniVRM VrmAnimationExporter",["sourceTposeArmHorizontal"]=armHorizontal};
            }
            finally{if(graph.IsValid())graph.Destroy();UnityEngine.Object.DestroyImmediate(wrapper);}
        }
    }
}
