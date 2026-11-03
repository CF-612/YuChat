using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UniGLTF;
using UnityEngine;

namespace LingChatXR.Chat
{
    public sealed partial class XrMotionPlayer
    {
        sealed class MotionPackageHeader
        {
            public string schemaVersion,bodyFile,bodySha256,appendageMode,worldRootPolicy,quaternionOrder,posePolicy;
            public float sampleRateHz,lastSampleTimeSec;
            public int frameCount;
        }

        public string PackageLastError { get; private set; }
        public bool PlayingPackage => action!=null&&action.generated;

        // Candidate assets are authoring previews. They never enter the automatic chat director.
        public async Task<bool> PreviewPackage(string folder)
        {
            if(!PreviewMode||disposed||!spatialAllowed||GrabbedActive||LocomotionActive)return false;
            int ticket=++generation;Loading=true;PackageLastError=null;
            try
            {
                var bytes=await PackageFiles.ReadAsync(folder,"metadata.json",lifetime.Token);
                if(disposed||ticket!=generation||!spatialAllowed)return false;
                var header=JsonConvert.DeserializeObject<MotionPackageHeader>(Encoding.UTF8.GetString(bytes));
                if(header==null||header.schemaVersion!="lingchat.motion.v1"||header.worldRootPolicy!="external"
                    ||header.appendageMode!="none"||header.quaternionOrder!="xyzw"
                    ||header.frameCount<2||!float.IsFinite(header.sampleRateHz)||header.sampleRateHz<=0
                    ||!float.IsFinite(header.lastSampleTimeSec)
                    ||Mathf.Abs(header.lastSampleTimeSec-(header.frameCount-1)/header.sampleRateHz)>.0001f
                    ||header.bodySha256?.Length!=64||header.bodySha256.Any(c=>!Uri.IsHexDigit(c)))
                    throw new InvalidDataException("身体动作包版本、时间轴或根策略不受支持");
                if(!string.IsNullOrEmpty(header.posePolicy)&&header.posePolicy!="absolute"&&header.posePolicy!="relativeToCurrentIdle")
                    throw new InvalidDataException("未知的身体起始姿态校准方式");
                var id="package_"+header.bodySha256.ToLowerInvariant()+"_"+(header.posePolicy??"absolute");
                var asset=new XrMotionAsset{id=id,label="视频身体动作候选",sha256=header.bodySha256};
                return await PlayInternal(id,false,"package",asset,folder,header);
            }
            catch(OperationCanceledException){return false;}
            catch(Exception e){if(!disposed&&ticket==generation){PackageLastError=e.Message;Status="动作包校验失败，保留当前待机";}return false;}
            finally{if(ticket==generation)Loading=false;}
        }

        static void ValidatePackageBody(GltfData data,MotionPackageHeader header)
        {
            if(data.GLTF.animations?.Count!=1)throw new InvalidDataException("动作包需要单一身体动画");
            var animation=data.GLTF.animations[0];
            foreach(var channel in animation.channels)
            {
                var sampler=animation.samplers[channel.sampler];
                if(sampler.interpolation!="LINEAR")throw new InvalidDataException("身体轨道需要 LINEAR 采样");
                var times=data.GetArrayFromAccessor<float>(sampler.input);
                if(times.Length!=header.frameCount||data.GLTF.accessors[sampler.output].count!=header.frameCount)
                    throw new InvalidDataException("身体轨道帧数不一致");
                for(int i=0;i<times.Length;i++)
                    if(!float.IsFinite(times[i])||Mathf.Abs(times[i]-i/header.sampleRateHz)>.0001f)
                        throw new InvalidDataException("身体轨道时间采样不一致");
                if(channel.target.path=="rotation")
                {
                    var values=data.GetArrayFromAccessor<Quaternion>(sampler.output);
                    foreach(var q in values)
                    {
                        float norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
                        if(!float.IsFinite(norm)||Mathf.Abs(norm-1)>.002f)throw new InvalidDataException("身体轨道四元数无效");
                    }
                }
                else if(channel.target.path=="translation")
                {
                    foreach(var p in data.GetArrayFromAccessor<Vector3>(sampler.output))
                        if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)||Mathf.Abs(p.x)>.0001f||Mathf.Abs(p.z)>.0001f)
                            throw new InvalidDataException("external 身体轨道不能包含世界平移");
                }
                else throw new InvalidDataException("当前动作包只支持身体旋转与髋部局部起伏");
            }
        }

        void TrimPackageCache(string keep)
        {
            int remove=clips.Count(p=>p.Value.generated)-3;
            if(remove<=0)return;
            foreach(var entry in clips.Where(p=>p.Value.generated&&p.Key!=keep&&p.Value!=action&&p.Value!=idle).Take(remove).ToArray())
            {entry.Value.Dispose();clips.Remove(entry.Key);lastPlayed.Remove(entry.Key);}
        }
    }
}
