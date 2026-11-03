using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LingChatXR.Chat
{
    public sealed partial class XrMotionPlayer
    {
        readonly Dictionary<string,Clip> locomotionClips=new Dictionary<string,Clip>();
        readonly Dictionary<string,float> locomotionSpeeds=new Dictionary<string,float>(),locomotionLegs=new Dictionary<string,float>();
        readonly HashSet<Clip> verticalLocomotion=new HashSet<Clip>();
        Task<bool> locomotionPreparation;
        Clip locomotionClip;
        Quaternion[] locomotionFrom;
        float locomotionTime,locomotionBlend,locomotionBlendDuration=.22f,locomotionExit=-1;
        bool locomotionOwned;
        float locomotionFromY;
        public string DefaultMovement {get;private set;}="walk";
        public bool HasRunning=>locomotionClips.ContainsKey("run");
        public float MovementLength(string kind)=>locomotionClips[kind].animation.length;
        public float MovementSpeedPerLeg(string kind)=>locomotionSpeeds[kind]/locomotionLegs[kind];
        public bool LocomotionActive=>locomotionOwned||locomotionExit>=0;
        public bool LocomotionOwned=>locomotionOwned;
        public float LocomotionSourceLeg {get;private set;}
        public float LocomotionSourceSpeed {get;private set;}
        public string LocomotionStatus {get;private set;}="行走素材尚未准备";
        public Task<bool> PrepareLocomotion()=>locomotionPreparation??=LoadLocomotion();
        async Task<bool> LoadLocomotion()
        {
            try
            {
                string folder=Application.streamingAssetsPath+"/Motions/local-locomotion";
                var json=JObject.Parse(System.Text.Encoding.UTF8.GetString(await PackageFiles.ReadAsync(folder,"catalog.json",lifetime.Token)));
                int schema=(int?)json["schemaVersion"]??0;
                if(schema!=1&&schema!=2)throw new InvalidDataException("行走目录版本不支持");
                DefaultMovement=schema==2?"skip":"walk";
                foreach(string kind in schema==2?new[]{"idle","walk","left","right","skip","run"}:new[]{"idle","walk","left","right"})
                {
                    var item=json["actions"].Single(x=>(string)x["id"]=="locomotion_"+kind);
                    var asset=item.ToObject<XrMotionAsset>();if(!asset.loop||asset.sha256?.Length!=64)throw new InvalidDataException("行走目录无效");
                    var clip=await Load(asset,folder);if(disposed)return false;locomotionClips.Add(kind,clip);
                    locomotionSpeeds[kind]=(float)item["sourceSpeed"];locomotionLegs[kind]=(float)item["sourceLegLength"];
                    if((bool?)item["preserveVertical"]==true)verticalLocomotion.Add(clip);
                    if((kind==DefaultMovement||kind=="run")&&(!float.IsFinite(locomotionSpeeds[kind])||locomotionSpeeds[kind]<=0||!float.IsFinite(locomotionLegs[kind])||locomotionLegs[kind]<=0))throw new InvalidDataException("移动动作尺度无效");
                    if(kind==DefaultMovement)
                    {
                        LocomotionSourceLeg=(float)item["sourceLegLength"];LocomotionSourceSpeed=(float)item["sourceSpeed"];
                        if(!float.IsFinite(LocomotionSourceLeg)||LocomotionSourceLeg<=0||!float.IsFinite(LocomotionSourceSpeed)||LocomotionSourceSpeed<=0)throw new InvalidDataException("步幅信息无效");
                    }
                }
                LocomotionStatus="行走就绪";return true;
            }
            catch(OperationCanceledException){return false;}
            catch(Exception error){if(!disposed){LocomotionStatus="行走素材未就绪，请准备已授权的本地动作";Debug.LogWarning("XR_LOCOMOTION_LOAD_FAILED "+error);}return false;}
        }
        public bool BeginLocomotion()
        {
            if(disposed||!spatialAllowed||Busy||!locomotionClips.ContainsKey(DefaultMovement)||!locomotionClips.ContainsKey("right"))return false;
            locomotionFrom??=new Quaternion[bones.Length];BodySnapshot(locomotionFrom);
            locomotionFromY=hips.localPosition.y;
            locomotionOwned=true;locomotionExit=-1;locomotionClip=null;locomotionBlend=0;semanticBlocked=true;
            return true;
        }
        // 外部唯一空间 owner 用实际行进距离推进相位；不通过播放速度猜测根位移。
        public void SampleLocomotion(string kind,float seconds)
        {
            if(!locomotionOwned||!float.IsFinite(seconds)||!locomotionClips.TryGetValue(kind,out var clip))return;
            if(locomotionClip!=clip)
            {
                // 转身接前走缩短混合，保留当前姿态起点，避免启动前半步被长混合吞掉。
                locomotionBlendDuration=(kind==DefaultMovement||kind=="run")&&(locomotionClip==locomotionClips["left"]||locomotionClip==locomotionClips["right"])? .12f:.22f;
                BodySnapshot(locomotionFrom);locomotionFromY=hips.localPosition.y;locomotionBlend=0;locomotionClip=clip;
            }
            locomotionTime=Mathf.Max(0,seconds)%clip.animation.length;
        }
        public void EndLocomotion(bool immediate=false)
        {
            // 换模型时旧 ControlRig 可能先销毁；清控制权即可，不能再写旧髋骨。
            if(disposed||hips==null){locomotionOwned=false;locomotionClip=null;locomotionExit=-1;return;}
            if(!LocomotionActive)return;
            if(locomotionOwned&&!immediate){BodySnapshot(locomotionFrom);locomotionFromY=hips.localPosition.y;locomotionExit=0;}
            locomotionOwned=false;locomotionClip=null;
            if(immediate){locomotionExit=-1;hips.localPosition=hipsRest;}
        }
        bool StepLocomotion(float dt)
        {
            if(locomotionOwned)
            {
                if(locomotionClip==null){Write(locomotionFrom);return true;}
                bool vertical=verticalLocomotion.Contains(locomotionClip);
                Sample(locomotionClip,locomotionTime,vertical);float sampledY=hips.localPosition.y;Snapshot(source);
                locomotionBlend+=Mathf.Max(0,dt);float weight=Smooth(locomotionBlend/locomotionBlendDuration);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(locomotionFrom[i],source[i],weight);
                hips.localPosition=new Vector3(hipsRest.x,Mathf.Lerp(locomotionFromY,sampledY,weight),hipsRest.z);
                return true;
            }
            if(locomotionExit>=0)
            {
                locomotionExit+=Mathf.Max(0,dt);float weight=Smooth(locomotionExit/.28f);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(locomotionFrom[i],frameBase[i],weight);
                hips.localPosition=new Vector3(hipsRest.x,Mathf.Lerp(locomotionFromY,hipsRest.y,weight),hipsRest.z);
                if(weight>=1)locomotionExit=-1;return true;
            }
            return false;
        }
    }
}
