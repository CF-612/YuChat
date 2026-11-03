using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniGLTF;
using UniVRM10;
using UnityEngine;

namespace LingChatXR.Chat
{
    // VRMA 解析/采样/重定向复用 UniVRM 与 Unity AnimationClip；仅适配生命周期和控制权。
    // 过渡曲线及当前待机恢复语义来源 LingChat motion-transition / vrma-motion（AGPL-3.0）。
    public sealed partial class XrMotionPlayer : IDisposable
    {
        sealed class Clip : IDisposable
        {
            public RuntimeGltfInstance root; public Vrm10AnimationInstance vrma; public AnimationClip animation;
            public Quaternion[] start,end;
            public bool generated;
            public bool relativeToIdle;
            public int packageFrameCount;
            public float packageSampleRate;
            public void Dispose(){if(root!=null)UnityEngine.Object.Destroy(root.gameObject);}
        }
        readonly Vrm10Instance instance;
        readonly Vrm10RuntimeControlRig rig;
        readonly Transform[] bones;
        readonly MotionRegion[] boneRegions;
        readonly Quaternion[] rest,frameBase,source,entry,idleFrom,lastBody;
        readonly Transform hips;
        readonly Vector3 hipsRest;
        readonly Dictionary<string,Clip> clips=new Dictionary<string,Clip>();
        readonly SemaphoreSlim loadGate=new SemaphoreSlim(1,1);
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Dictionary<string,float> lastPlayed=new Dictionary<string,float>();
        int generation;bool disposed,releasing,spatialAllowed=true,idleFading;
        float age,releaseAge,idleAge,enterDuration=.65f;
        float exitDuration,exitAt,cancelDuration=.9f,sourceTime,entryWeight,returnWeight;
        float entryHipsY,releaseHipsY;
        bool fresh;
        public (float age,float sourceTime,float enterDuration,float exitAt,float exitDuration,float entryWeight,float returnWeight) InspectPlayback()=>
            (age,sourceTime,enterDuration,exitAt,exitDuration,entryWeight,returnWeight);
        Clip action,idle;XrMotionAsset activeAsset,idleAsset;
        readonly string modelSha;
        readonly XrConversationMotion conversation=new XrConversationMotion();
        readonly Dictionary<HumanBodyBones,Transform> controlBones=new Dictionary<HumanBodyBones,Transform>();
        readonly HashSet<string> decisions=new HashSet<string>();
        readonly HashSet<string> turnActions=new HashSet<string>();
        string headId,headOwner,pendingOwner,bodyOwner="manual",turnId;
        float headAge,poseExitAge=-1,nextDailyAt,lastAudioTime;
        long audioKey=-1;int dailyCue;bool semanticBlocked;
        float speechTime;bool talking,waiting;XrVoiceRhythm rhythm;
        public bool HeadOccupied=>GrabbedActive||headId!=null||(Occupied&MotionRegion.Head)!=0;
        public MotionRegion Occupied=>LocomotionActive?MotionRegion.All:action==null?MotionRegion.None:Regions(activeAsset);
        public string PoseId=>activeAsset?.poseLoop!=null&&!releasing&&poseExitAge<0?activeAsset.id:null;
        public int ConversationGestureSerial=>conversation.GestureSerial;
        public string HeadId=>headId;
        public bool PreviewMode {get;}
        public string Status{get;private set;}="已准备 · 选择动作试播";
        public string ActiveId=>activeAsset?.id;
        public string IdleId=>idleAsset?.id??"none";
        public bool Busy=>GrabbedActive||LocomotionActive||Loading||action!=null||headId!=null;
        public bool Loading{get;private set;}
        public XrMotionPreferences Preferences{get;private set;}
        public XrMotionAsset[] Catalog{get;private set;}=Array.Empty<XrMotionAsset>();
        public Task CatalogReady{get;}
        public XrMotionPlayer(Vrm10Instance model,bool previewMode,string sha)
        {
            instance=model;rig=model.Runtime.ControlRig;PreviewMode=previewMode;
            modelSha=sha;foreach(var b in rig.Bones)controlBones[b.Key]=b.Value.ControlBone;
            bones=rig.Bones.Values.Select(b=>b.ControlBone).ToArray();
            boneRegions=rig.Bones.Keys.Select(BoneRegion).ToArray();
            rest=new Quaternion[bones.Length];frameBase=new Quaternion[bones.Length];source=new Quaternion[bones.Length];entry=new Quaternion[bones.Length];idleFrom=new Quaternion[bones.Length];lastBody=new Quaternion[bones.Length];
            // 将现有通用放松姿态映射进官方控制骨，保留原外观，禁止模型名/骨名校正表。
            foreach(var pair in rig.Bones)
            {
                var b=pair.Value;var t=model.Runtime.InitPose[b.ControlTarget];
                b.ControlBone.localRotation=t.Rotation*Quaternion.Inverse(t.LocalRotation)*b.ControlTarget.localRotation*Quaternion.Inverse(t.Rotation);
            }
            Snapshot(rest);Snapshot(idleFrom);Snapshot(lastBody);Snapshot(frameBase);hips=rig.GetBoneTransform(HumanBodyBones.Hips);hipsRest=hips.localPosition;
            try{Preferences=XrMotionPreferences.Read(sha);}catch(Exception e)when(PreferenceFiles.IsStorageError(e)){Preferences=new XrMotionPreferences();Status="动作偏好无法读取，原文件已保留";}
            explicitPermissions=System.IO.File.Exists(XrMotionPreferences.PathFor(sha));
            CatalogReady=LoadCatalog();
        }
        async Task LoadCatalog()
        {
            try{Catalog=await XrMotionCatalog.Load(lifetime.Token);if(!disposed&&Preferences.enabled&&Preferences.idle!="none")await SetIdle(Preferences.idle);}
            catch(OperationCanceledException){}
            catch(Exception){if(!disposed)Status="本地动作资源未就绪，请先准备已授权动作库";}
        }
        void Snapshot(Quaternion[] values){for(int i=0;i<bones.Length;i++)if(bones[i]!=null)values[i]=bones[i].localRotation;}
        static MotionRegion BoneRegion(HumanBodyBones b)
        {
            string n=b.ToString();if(n=="Head"||n=="Neck"||n.Contains("Eye")||n=="Jaw")return MotionRegion.Head;
            if(n.Contains("Leg")||n.Contains("Foot")||n.Contains("Toes"))return MotionRegion.Legs;
            if(n.StartsWith("Left"))return MotionRegion.LeftArm;if(n.StartsWith("Right"))return MotionRegion.RightArm;return MotionRegion.Torso;
        }
        void BodySnapshot(Quaternion[] values)=>Array.Copy(lastBody,values,values.Length);
        void Write(Quaternion[] values){for(int i=0;i<bones.Length;i++)if(bones[i]!=null)bones[i].localRotation=values[i];if(hips!=null)hips.localPosition=hipsRest;}
        async Task<Clip> Load(XrMotionAsset asset,string folder=null,MotionPackageHeader package=null)
        {
            await loadGate.WaitAsync(lifetime.Token);
            try
            {
                if(clips.TryGetValue(asset.id,out var cached))
                {
                    if(package!=null&&(cached.packageFrameCount!=package.frameCount||Mathf.Abs(cached.packageSampleRate-package.sampleRateHz)>.0001f))
                        throw new InvalidOperationException("动作包与缓存采样率或帧数不一致");
                    return cached;
                }
                var bytes=await PackageFiles.ReadAsync(folder??XrMotionCatalog.Folder,package?.bodyFile??asset.id+".vrma",lifetime.Token);
                if(!string.Equals(PackageFiles.Hash(bytes),asset.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("动作文件校验失败");
                using var data=new GlbBinaryParser(bytes,asset.id).Parse();
                if(package!=null)ValidatePackageBody(data,package);
                using var loader=new VrmAnimationImporter(new VrmAnimationData(data));
                var loaded=await loader.LoadAsync(new RuntimeOnlyAwaitCaller());
                if(disposed){UnityEngine.Object.Destroy(loaded.gameObject);throw new OperationCanceledException();}
                loaded.transform.SetParent(instance.transform,false);
                var vrma=loaded.GetComponent<Vrm10AnimationInstance>();var animation=loaded.GetComponent<Animation>();
                if(vrma==null||animation?.clip==null||!float.IsFinite(animation.clip.length)||animation.clip.length<=0){UnityEngine.Object.Destroy(loaded.gameObject);throw new InvalidOperationException("动作不包含可播放的人形轨道");}
                animation.Stop();animation.enabled=false;vrma.ShowBoxMan(false);
                if(package!=null&&Mathf.Abs(animation.clip.length-package.lastSampleTimeSec)>.002f){UnityEngine.Object.Destroy(loaded.gameObject);throw new InvalidOperationException("动作包与身体时间轴不一致");}
                var clip=new Clip{root=loaded,vrma=vrma,animation=animation.clip,generated=package!=null,relativeToIdle=package?.posePolicy=="relativeToCurrentIdle",packageFrameCount=package?.frameCount??0,packageSampleRate=package?.sampleRateHz??0,start=new Quaternion[bones.Length],end=new Quaternion[bones.Length]};
                // 只采样控制骨并立即恢复，不把源起止站姿写进可见模型。
                var saved=new Quaternion[bones.Length];Snapshot(saved);var savedHips=hips.localPosition;
                try{Sample(clip,0);Snapshot(clip.start);Sample(clip,clip.animation.length);Snapshot(clip.end);}
                finally{Write(saved);hips.localPosition=savedHips;}
                clips.Add(asset.id,clip);
                if(package!=null)TrimPackageCache(asset.id);
                return clip;
            }
            finally{loadGate.Release();}
        }
        const string WelcomeWave="wave_normal_right_661";
        bool explicitPermissions;
        bool AllowedForOwner(string id,string owner) => Preferences.Allowed(id) ||
            (owner=="welcome" && id==WelcomeWave && !explicitPermissions);
        public Task<bool> Welcome() => Busy ? Task.FromResult(false) : PlayInternal(WelcomeWave,false,"welcome");
        public void EndWelcome(){CancelPending("welcome");if(bodyOwner=="welcome"&&action!=null)Stop(true);}
        public Task<bool> Play(string id)=>PlayInternal(id,false);
        bool Compatible(XrMotionAsset a)=>a.poseLoop==null||(a.approvedModels!=null&&a.approvedModels.Contains(modelSha));
        static MotionRegion Regions(XrMotionAsset asset)
        {
            if(asset==null)return MotionRegion.None;if(asset.builtin)return MotionRegion.Head;
            if(!(asset.daily?["affected"] is Newtonsoft.Json.Linq.JArray parts))return MotionRegion.All;
            MotionRegion result=MotionRegion.None;
            foreach(string p in parts.Values<string>())result|=p=="head"?MotionRegion.Head:p=="torso"?MotionRegion.Torso:p=="legs"?MotionRegion.Legs:p=="leftArm"?MotionRegion.LeftArm:p=="rightArm"?MotionRegion.RightArm:p=="arms"||p=="hands"?MotionRegion.LeftArm|MotionRegion.RightArm:MotionRegion.All;
            return result;
        }
        public XrMotionAsset[] Capabilities()=>Catalog.Where(a=>!a.loop&&Compatible(a)&&Preferences.Allowed(a.id)).ToArray();
        async Task<bool> PlayInternal(string id,bool fromSpeech,string owner=null,XrMotionAsset provided=null,string folder=null,MotionPackageHeader package=null)
        {
            owner??=fromSpeech?"speech":"manual";
            var asset=provided??Catalog.FirstOrDefault(a=>a.id==id);
            if(disposed||LocomotionActive||!spatialAllowed||asset==null||!Compatible(asset)||(!PreviewMode&&!AllowedForOwner(id,owner))){Status="当前动作未获许可、缺少兼容姿势声明，或角色尚未放稳";return false;}
            if(asset.builtin)
            {
                if((Occupied&MotionRegion.Head)!=0){if(fromSpeech)return false;Stop();}
                headId=id;headAge=0;headOwner=owner;Status="动作播放中 · "+asset.label;return true;
            }
            if(asset.loop)return await SetIdle(id);
            int ticket=++generation;Loading=true;pendingOwner=owner;Status="正在准备动作…";
            try
            {
                var clip=await Load(asset,folder,package);
                if(disposed||ticket!=generation||!spatialAllowed||(!PreviewMode&&!AllowedForOwner(id,owner)))return false;
                if(package!=null&&Mathf.Abs(clip.animation.length-package.lastSampleTimeSec)>.002f)throw new InvalidOperationException("动作包与缓存身体时间轴不一致");
                if(asset.poseLoop!=null&&!asset.poseLoop.Valid(clip.animation.length))throw new InvalidOperationException("持续姿势区间无效");
                Snapshot(entry);entryHipsY=hips.localPosition.y;action=clip;activeAsset=asset;releasing=false;age=0;poseExitAge=-1;bodyOwner=owner;fresh=true;
                if((Regions(asset)&MotionRegion.Head)!=0){headId=null;headOwner=null;}
                var regions=Regions(asset);
                var rebasedEnd=new Quaternion[bones.Length];
                for(int i=0;i<bones.Length;i++)rebasedEnd[i]=clip.generated&&!clip.relativeToIdle?clip.end[i]:XrMotionTransition.RelativeToIdle(clip.end[i],clip.start[i],entry[i]);
                enterDuration=asset.poseLoop!=null?.65f:XrMotionTransition.EnterDuration(clip.animation.length,XrMotionTransition.Minimum);
                exitDuration=XrMotionTransition.ExitDuration(clip.animation.length,XrMotionTransition.PoseDuration(rebasedEnd,entry,boneRegions,regions,XrMotionTransition.ExitMinimum,XrMotionTransition.ExitMaximum));
                exitAt=clip.animation.length-exitDuration+(clip.generated?0:enterDuration/2);sourceTime=0;entryWeight=returnWeight=0;
                lastPlayed[id]=Time.unscaledTime;Status="动作播放中 · "+asset.label;return true;
            }
            catch(OperationCanceledException){return false;}
            catch(Exception e){if(package!=null)PackageLastError=e.Message;if(!disposed&&ticket==generation)Status="该动作暂不能播放，已保留当前姿态";return false;}
            finally{if(ticket==generation){Loading=false;pendingOwner=null;}}
        }
        public async Task<bool> SetIdle(string id)
        {
            if(LocomotionActive)return false;
            Stop();int ticket=generation;
            if(id=="none"||string.IsNullOrEmpty(id)){BodySnapshot(idleFrom);idleAge=0;idleFading=true;idle=null;idleAsset=null;Status="已回到基础自然待机";return true;}
            var asset=Catalog.FirstOrDefault(a=>a.id==id&&a.loop);
            if(asset==null||(!PreviewMode&&!Preferences.Allowed(id)))return false;
            try
            {
                var clip=await Load(asset);if(disposed||ticket!=generation||!spatialAllowed)return false;
                BodySnapshot(idleFrom);idleFading=false;idle=clip;idleAsset=asset;idleAge=0;Status="已切换待机 · "+asset.label;return true;
            }
            catch(OperationCanceledException){return false;}
            catch(Exception){if(!disposed)Status="待机资源不可用，保留基础待机";return false;}
        }
        public void ApplyPreferences(XrMotionPreferences value)
        {
            if(LocomotionActive)EndLocomotion(true);
            explicitPermissions=true;Preferences=value;
            if(!value.enabled)Stop(true);
            else if(activeAsset!=null&&!value.Allowed(activeAsset.id))Stop();
            if(idleAsset!=null&&!value.Allowed(idleAsset.id)){idle=null;idleAsset=null;}
            if(value.enabled&&value.idle!=IdleId)_=SetIdle(value.idle);
            if(headId!=null&&!value.Allowed(headId))headId=null;
            if(!value.director)CancelOwner("director");
        }
        public void SetSpatialAllowed(bool allowed)
        {
            if(spatialAllowed&&!allowed)Stop(true);
            bool resumed=!spatialAllowed&&allowed;spatialAllowed=allowed;
            if(resumed&&!PreviewMode&&Preferences.enabled&&Preferences.idle!="none")_=SetIdle(Preferences.idle);
        }
        public void Stop(bool immediate=false)
        {
            if(disposed)return;
            EndLocomotion(immediate);
            ++generation;Loading=false;pendingOwner=null;
            headId=null;headOwner=null;
            if(!immediate&&action!=null&&activeAsset.poseLoop!=null&&age>=activeAsset.poseLoop.enterEnd&&poseExitAge<0){poseExitAge=0;Status="正在退出姿势";return;}
            if(action!=null&&!immediate){Snapshot(entry);releaseHipsY=hips.localPosition.y;cancelDuration=XrMotionTransition.CancelDuration(XrMotionTransition.PoseDuration(entry,frameBase,boneRegions,Regions(activeAsset),XrMotionTransition.Minimum,.75f));releasing=true;releaseAge=0;Status="正在收势，回到当前待机";}
            else{action=null;activeAsset=null;releasing=false;Status="已停止 · 回到当前待机";}
            if(immediate){idle=null;idleAsset=null;idleFading=false;Write(rest);Snapshot(lastBody);conversation.Reset();semanticBlocked=true;}
        }
        public void CancelSpeech(){CancelOwner("speech");semanticBlocked=true;}
        void CancelPending(string owner)
        {
            if(!Loading || pendingOwner!=owner)return;
            ++generation;
            Loading=false;
            pendingOwner=null;
        }
        void CancelOwner(string owner)
        {
            CancelPending(owner);
            if(headOwner==owner){headId=null;headOwner=null;}
            if(bodyOwner==owner&&action!=null)
            {string keep=headId,keepOwner=headOwner;float keepAge=headAge;Stop();headId=keep;headOwner=keepOwner;headAge=keepAge;}
        }
        public void BeginTurn(string id){CancelPending("director");if(PoseId==null)CancelOwner("director");CancelOwner("speech");turnId=id;decisions.Clear();turnActions.Clear();semanticBlocked=Preferences.director;}
        public void EndTurn(){turnId=null;decisions.Clear();turnActions.Clear();CancelOwner("director");CancelOwner("speech");semanticBlocked=true;}
        public async Task<bool> Decide(string turn,string request,string id)
        {
            if(disposed||turn!=turnId||string.IsNullOrEmpty(request)||!Preferences.director||!Preferences.enabled||!spatialAllowed||!decisions.Add(request))return false;
            semanticBlocked=true;
            if(id=="none"){CancelOwner("director");CancelOwner("speech");return true;}
            if(!Capabilities().Any(a=>a.id==id)||(!turnActions.Add(id)&&PoseId!=id))return false;
            if(PoseId==id&&bodyOwner=="director")return true;
            if((action!=null&&bodyOwner=="manual"||pendingOwner=="manual")||headId!=null&&headOwner=="manual")return false;
            CancelOwner("speech");
            bool result=await PlayInternal(id,true,"director");
            // 资源返回后仍核对轮次；模型销毁和空间打断另由 generation 拒绝。
            if(turn!=turnId)return false;
            return result;
        }
        void Sample(Clip clip,float time,bool preserveVertical=false)
        {
            clip.animation.SampleAnimation(clip.root.gameObject,time);
            Vrm10Retarget.Retarget(clip.vrma.ControlRig,(rig,rig));
            // M1 原地动作：移动仍归空间控制者；不让源文件髋位移绕过桌面边界。
            hips.localPosition=new Vector3(hipsRest.x,preserveVertical||clip.generated?hips.localPosition.y:hipsRest.y,hipsRest.z);
        }
        static float Smooth(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
        static float Enter(float t){t=Mathf.Clamp01(t);return 1-Mathf.Pow(1-t,4)*(1+4*t);}
        public void Step(float dt)
        {
            if(disposed)return;StepBody(dt);StepGrabbed(dt);Snapshot(lastBody);
            if(!spatialAllowed)return;
            var occupied=Occupied|(headId!=null?MotionRegion.Head:MotionRegion.None);
            conversation.Step(dt,!PreviewMode&&Preferences.enabled&&Preferences.conversation,talking,waiting,audioKey,speechTime,rhythm,Preferences.style,occupied,AddRotation);
            if(headId!=null){headAge+=dt;AddRotation(HumanBodyBones.Head,XrConversationMotion.HeadAction(headId,headAge));if(headAge>=(headId=="nod"?1.06f:1.42f))headId=null;}
        }
        void AddRotation(HumanBodyBones bone,Vector3 degrees){if(controlBones.TryGetValue(bone,out var t)&&t!=null)t.localRotation*=Quaternion.Euler(degrees);}
        void StepBody(float dt)
        {
            if(disposed)return;Write(rest);hips.localPosition=hipsRest;
            if(!spatialAllowed)return;
            if(idle!=null&&(PreviewMode||Preferences.Allowed(idleAsset.id)))
            {
                idleAge+=Mathf.Max(0,dt);float phase=idleAge%(idle.animation.length+XrMotionTransition.Seam);
                Sample(idle,Mathf.Min(phase,idle.animation.length));Snapshot(source);
                float seam=Smooth((phase-idle.animation.length)/XrMotionTransition.Seam);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(idleFrom[i],phase>idle.animation.length?Quaternion.Slerp(idle.end[i],idle.start[i],seam):source[i],Smooth(idleAge/XrMotionTransition.Seam));
            }
            else if(idleFading){idleAge+=dt;float w=Smooth(idleAge/.9f);for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(idleFrom[i],rest[i],w);if(w>=1)idleFading=false;}
            Snapshot(frameBase);
            if(StepLocomotion(dt))return;
            if(action==null)return;
            if(releasing)
            {
                releaseAge+=Mathf.Max(0,dt);float weight=Smooth(releaseAge/Mathf.Max(.001f,cancelDuration));
                var owned=Regions(activeAsset);
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=(owned&boneRegions[i])!=0?Quaternion.Slerp(entry[i],frameBase[i],weight):frameBase[i];
                hips.localPosition=new Vector3(hipsRest.x,Mathf.Lerp(releaseHipsY,hipsRest.y,weight),hipsRest.z);
                if(weight>=1){action=null;activeAsset=null;releasing=false;Status="已停止 · 回到当前待机";}return;
            }
            if(fresh)fresh=false;else age+=Mathf.Max(0,dt);
            // 与 LingChat 同步：从当前姿态进入，在源收势段内部就回混当前待机。
            float clock=action.generated?age:activeAsset.poseLoop==null&&age>exitAt?action.animation.length-exitDuration+age-exitAt:XrMotionTransition.EntryTime(age,enterDuration);
            float sampleClock=clock;
            if(activeAsset.poseLoop!=null)
            {
                if(poseExitAge>=0){poseExitAge+=dt;sampleClock=activeAsset.poseLoop.exitFrom+poseExitAge;}
                else
                {
                    activeAsset.poseLoop.Sample(clock,out sampleClock,out float incoming,out float blend);
                    Sample(action,sampleClock);Snapshot(source);
                    if(blend>0){Sample(action,incoming);for(int i=0;i<bones.Length;i++)bones[i].localRotation=Quaternion.Slerp(source[i],bones[i].localRotation,blend);}
                    Snapshot(source);
                }
            }
            if(activeAsset.poseLoop==null||poseExitAge>=0){Sample(action,Mathf.Min(sampleClock,action.animation.length));Snapshot(source);}
            float sampledHipsY=hips.localPosition.y;
            // 一次性动作相对于自身首帧的旋转变化叠到当前待机；循环待机和声明姿势仍保留作者绝对姿态。
            if(activeAsset.poseLoop==null&&(!action.generated||action.relativeToIdle))for(int i=0;i<bones.Length;i++)source[i]=XrMotionTransition.RelativeToIdle(source[i],action.start[i],frameBase[i]);
            float release=activeAsset.poseLoop==null?Smooth((age-exitAt)/Mathf.Max(.001f,exitDuration)):poseExitAge<0?0:Smooth((sampleClock-action.animation.length)/.9f);
            sourceTime=Mathf.Min(sampleClock,action.animation.length);entryWeight=activeAsset.poseLoop==null?Smooth(age/Mathf.Max(.001f,enterDuration)):Enter(age/enterDuration);returnWeight=release;
            var regions=Regions(activeAsset);
            for(int i=0;i<bones.Length;i++)bones[i].localRotation=(regions&boneRegions[i])!=0?Quaternion.Slerp(Quaternion.Slerp(entry[i],source[i],entryWeight),frameBase[i],release):frameBase[i];
            hips.localPosition=new Vector3(hipsRest.x,Mathf.Lerp(Mathf.Lerp(entryHipsY,sampledHipsY,entryWeight),hipsRest.y,release),hipsRest.z);
            if(release>=1){action=null;activeAsset=null;Status="已完成 · 回到当前待机";}
        }
        public void UpdateConversation(bool speaking,bool thinking,long key,float time,XrVoiceRhythm features,string emotion)
        {
            talking=speaking;waiting=thinking;speechTime=time;rhythm=features;
            bool newVoice=audioKey!=key,seek=!newVoice&&(time<lastAudioTime-.05f||time-lastAudioTime>.5f);
            if(newVoice||seek){audioKey=key;dailyCue=0;while(features!=null&&dailyCue<features.Cues.Length&&features.Cues[dailyCue]<=time+.18f)dailyCue++;}
            lastAudioTime=time;bool boundary=false;
            while(features!=null&&dailyCue<features.Cues.Length&&features.Cues[dailyCue]<=time+.18f){float cue=features.Cues[dailyCue++];boundary|=cue>=time-.1f;}
            if(!speaking||!Preferences.automatic||!Preferences.enabled||Busy||!spatialAllowed||semanticBlocked||newVoice||seek||!boundary||Time.unscaledTime<nextDailyAt)return;
            var candidate=Catalog.Where(a=>!a.loop&&a.poseLoop==null&&a.Speaking&&a.MatchesStyle(Preferences.style)&&Preferences.Allowed(a.id)
                &&(!(a.daily["emotions"] is Newtonsoft.Json.Linq.JArray emotions)||emotions.Values<string>().Contains(emotion))
                &&(!lastPlayed.TryGetValue(a.id,out var at)||Time.unscaledTime-at>=((float?)a.daily["minimumRepeatMs"]??60000)/1000))
                .OrderBy(a=>lastPlayed.TryGetValue(a.id,out var at)?at:float.NegativeInfinity).FirstOrDefault();
            if(candidate!=null){nextDailyAt=Time.unscaledTime+15;_=PlayInternal(candidate.id,true);}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;++generation;lifetime.Cancel();
            foreach(var clip in clips.Values)clip.Dispose();clips.Clear();
            // 进行中的加载仍会释放信号量，取消后不提前销毁它。
        }
    }
}
