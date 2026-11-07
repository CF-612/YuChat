#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 编辑器试玩专用。进程只读写标准流，离开 Play Mode 时释放。
    public sealed class ArdyPlaytest : MonoBehaviour
    {
        FeiyuPreview live;
        FeiyuAvatar avatar;
        PetInteraction core;
        Process worker;
        readonly ConcurrentQueue<string> replies = new ConcurrentQueue<string>();
        readonly Queue<JObject> history = new Queue<JObject>();
        readonly Queue<JObject> buffer = new Queue<JObject>();
        Quaternion basis;
        Vector3 center;
        float leg,speed,phase;
        // 使用当前普通走路的实际控制骨姿态作为直立参考，保留生成的转向和步态。
        static readonly HumanBodyBones[] Trunk={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.Neck,HumanBodyBones.Head};
        readonly Dictionary<HumanBodyBones,float> seedTilt=new();
        double started;
        float closestGoal;
        int unproductiveFrames;
        public double FirstFrameSeconds {get; private set;}
        double nextTick,requested,changed,launched;
        int epoch, generation, serial, pending;
        bool ready, active, prepared, acquired, priorManual, priorEnabled, controlsAutonomy, aligned, aligning, capturePending, captureGenerated, streaming, side;
        public bool Active => active;
        const string PrewarmKey="YuChat.ArdyUsedInEditorSession";
        public static void PrewarmForReturnVisit(FeiyuPreview preview)
        {
            if(preview.ExternalInput || preview.gameObject.scene.name!="FeiyuPreview" ||
                !UnityEditor.SessionState.GetBool(PrewarmKey,false))return;
            var demo=preview.GetComponent<ArdyPlaytest>()??preview.gameObject.AddComponent<ArdyPlaytest>();
            try{demo.EnsureWorker();}
            catch(Exception e){demo.Status="Core8 预加载失败："+e.Message;}
        }
        public static void StartOnTable(FeiyuPreview preview)
        {
            if(preview==null||!preview.Ready||preview.ExternalInput)return;
            if(!preview.Interaction.PlaneConfirmed&&!preview.Place(Vector3.zero))return;
            if(preview.CoinToy!=null && preview.CoinToy.Deployed)preview.CoinToy.Store();
            var demo=preview.GetComponent<ArdyPlaytest>()??preview.gameObject.AddComponent<ArdyPlaytest>();
            UnityEditor.SessionState.SetBool(PrewarmKey,true);
            demo.Begin(preview);
        }
        public bool Pending => pending != 0;
        public int Buffered => buffer.Count;
        public int Submitted {get; private set;}
        public int Rejected {get; private set;}
        public int RejectedBatches {get; private set;}
        public int Underruns {get; private set;}
        public double RoundTripMs {get; private set;}
        public double FirstNewFrameMs {get; private set;}
        public string Owner {get; private set;} = "待机";
        public string Target => side ? "B 侧方" : "A 前方";
        public string Status {get; private set;} = "就绪";
        public string WorkerPhase=>!active?Status:!prepared?"准备固定动作":!ready?"正在加载 Core8 · "+Math.Max(0,(int)(Now-launched))+" 秒":Submitted==0?"等待首批生成帧":"已播放 "+Submitted+" 帧 ARDY";
        static double Now => Time.realtimeSinceStartupAsDouble;
        bool Valid => active && live && live.Ready && live.Avatar==avatar && live.Interaction==core &&
                      core.State==PetState.Idle && core.PlaneConfirmed && core.Generation==generation;

        public async void Begin(FeiyuPreview preview)
        {
            if (active || !preview || !preview.Ready || preview.Interaction.State!=PetState.Idle) return;
            live=preview; avatar=live.Avatar; core=live.Interaction;
            priorManual=live.Locomotion.ManualStep; priorEnabled=core.AutonomousEnabled;
            active=true; Submitted=0; started=Now; FirstFrameSeconds=0; seedTilt.Clear(); history.Clear(); buffer.Clear(); int startEpoch=++epoch;
            try
            {
                live.Locomotion.Cancel(true);
                priorEnabled=core.AutonomousEnabled;
                live.Locomotion.ManualStep=true;
                generation=core.Generation;
                EnsureWorker();
                bool loaded=await avatar.Motions.PrepareLocomotion();
                if(startEpoch!=epoch)return;
                if(!Valid){End("准备期间角色或空间发生变化");return;}
                if(!loaded) throw new InvalidOperationException(avatar.Motions.LocomotionStatus);
                leg=Vector3.Distance(Bone(HumanBodyBones.LeftUpperLeg).position,Bone(HumanBodyBones.LeftLowerLeg).position)
                    +Vector3.Distance(Bone(HumanBodyBones.LeftLowerLeg).position,Bone(HumanBodyBones.LeftFoot).position);
                speed=leg*avatar.Motions.MovementSpeedPerLeg("walk");

                closestGoal=float.PositiveInfinity;unproductiveFrames=0;
                prepared=true; phase=0; nextTick=Now;
                streaming=false; aligned=false; side=false; changed=0; Submitted=Underruns=0; FirstNewFrameMs=0;
                Status="等待 Core8 就绪"; Owner="角色待机";
            }
            catch(Exception e){End(e.Message);}
        }

        Transform Bone(HumanBodyBones bone) => avatar.Instance.Runtime.ControlRig.GetBoneTransform(bone);
        void EnsureWorker()
        {
            if(worker!=null && !worker.HasExited) return;
            ready=false;
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            var info=new ProcessStartInfo(Path.Combine(root,".cache/video-motion/worker311/Scripts/python.exe"),
                "-u \""+Path.Combine(root,"tools/ardy/path_worker.py")+"\"") {
                WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,
                RedirectStandardOutput=true,RedirectStandardError=true };
            string cache=Path.Combine(root,".cache/ardy-horizon8");
            foreach(string variable in new[]{"TEMP","TMP","TMPDIR","HF_HOME","TORCH_HOME","TORCHINDUCTOR_CACHE_DIR"})
            {
                string dir=Path.Combine(cache,"runtime",variable);
                Directory.CreateDirectory(dir); info.EnvironmentVariables[variable]=dir;
            }
            string errorPath=Path.Combine(cache,"last-worker-error.txt");
            File.WriteAllText(errorPath,"");
            worker=new Process{StartInfo=info};
            worker.OutputDataReceived+=(s,e)=>{if(e.Data?.StartsWith("YUCHAT ")==true) replies.Enqueue(e.Data.Substring(7));};
            worker.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)try{File.AppendAllText(errorPath,e.Data+Environment.NewLine);}catch(IOException){}};
            worker.Start();launched=Now;worker.BeginOutputReadLine();worker.BeginErrorReadLine();
        }

        public void ChangeTarget()
        {
            if(!Valid || !prepared) return;
            RememberGenerated();
            rootMoving=false;side=!side; ++epoch; pending=0; buffer.Clear(); changed=Now; FirstNewFrameMs=0;
            closestGoal=float.PositiveInfinity;unproductiveFrames=0;
            Status="按已播放历史改目标"; Request();
        }

        async void StartAlignment()
        {
            int ticket=epoch;
            aligning=true;
            try
            {
                center=FeiyuPreview.ToUnity(core.PlaneCenter);
                core.SetAutonomousEnabled(true);generation=core.Generation;controlsAutonomy=true;
                avatar.Motions.EndWelcome();
                bool started=await live.Locomotion.FacePoint(TargetWorld());
                if(ticket!=epoch)return;
                if(!started)throw new InvalidOperationException("无法面向起步方向："+live.Locomotion.Status);
                aligned=true;
            }
            catch(Exception e){if(ticket==epoch)End(e.Message);}
        }

        void Update()
        {
            try
            {
                if(active && !Valid) End("空间或角色代次已变化");
                while(replies.TryDequeue(out string line)) Accept(JObject.Parse(line));
                if(!Valid || !prepared) return;
                if(worker.HasExited) throw new IOException("Core8 进程退出（"+worker.ExitCode+"），见 .cache/ardy-horizon8/last-worker-error.txt");
                if(!ready && Now-launched>120) throw new TimeoutException("ARDY 试载超时");
                if(!ready)return;
                if(!aligned){if(!aligning)StartAlignment();return;}
                if(aligning)
                {
                    if(live.Locomotion.Active){live.Locomotion.Step(Time.unscaledDeltaTime);return;}
                    if(avatar.Motions.LocomotionActive)return;
                    aligning=false;
                }
                if(!acquired)
                {
                    avatar.Motions.EndWelcome();
                    if(!avatar.Motions.BeginLocomotion())throw new InvalidOperationException("身体控制者忙，请先停止当前动作");
                    acquired=true;
                    basis=live.SpatialRotation;center=FeiyuPreview.ToUnity(core.PlaneCenter);
                    avatar.Motions.SpatialControlRevoked+=Revoke;
                    Owner="固定走路";Status="采集 16 帧实际步态";
                }
                if(pending!=0 && Now-requested>12) throw new TimeoutException("ARDY 单步超时");
                bool seeding=!streaming && pending==0 && history.Count<16;
                if(seeding)
                {
                    float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
                    phase+=dt;
                    avatar.Motions.SampleLocomotion("walk",phase);
                    var next=Vector3.MoveTowards(FeiyuPreview.ToUnity(core.Position),TargetWorld(),speed*dt);
                    if(!live.TryAutonomousPose(next,basis,generation))throw new InvalidOperationException("前方缺少接步空间");
                }
                if(rootMoving)
                {
                    AdvanceRoot();
                    if(!active || rootMoving)return;
                }
                if(Now<nextTick) return;
                nextTick=Math.Max(nextTick+.05,Now);
                RememberGenerated();
                if(streaming)Prefetch();
                if(seeding)capturePending=true;
                else if(buffer.Count>0)
                {
                    JObject row=buffer.Dequeue();
                    if((int)row["epoch"]!=epoch || !Valid) {Rejected++;return;}
                    Apply(row);
                    if(changed>0){FirstNewFrameMs=(Now-changed)*1000;changed=0;}
                    streaming=true; Submitted++; Owner="ARDY"; Status="接步中";
                    if(Submitted==1)UnityEngine.Debug.Log("XR_ARDY_FIRST_FRAME model="+avatar.Package.model.sha256+" rttMs="+RoundTripMs.ToString("F0"));
                    captureGenerated=true;
                    if(Submitted==1)FirstFrameSeconds=Now-started;
                }
                else if(pending!=0) { Underruns++; Status="等待补帧"; }
                // 试玩时端到端往返约 0.3 秒。八帧中消费一帧即预取，余七帧提供 350 ms 裕量。
                if(!capturePending && !captureGenerated) Prefetch();
            }
            catch(Exception e){End(e.Message);}
        }
        // FeiyuPreview (-50) 已在 LateUpdate 按正常动画管线写好控制骨。
        // 实验按 20 Hz 送目标帧，历史只记录实际呈现的姿态。
        void LateUpdate()
        {
            if(!capturePending || !Valid)return;
            capturePending=false;
            try
            {
                var sample=CaptureRow();
                if(!streaming)
                {
                    var upright=Quaternion.identity;
                    foreach(var bone in Trunk)
                    {
                        var part=Bone(bone);if(part==null)continue;
                        upright*=part.localRotation;
                        seedTilt.TryGetValue(bone,out var prev);
                        seedTilt[bone]=Mathf.Max(prev,Vector3.Angle(Vector3.up,upright*Vector3.up));
                    }
                    float y=(float)sample["y"];
                    if(history.Count==0)seedMinY=seedMaxY=y;
                    seedMinY=Mathf.Min(seedMinY,y);seedMaxY=Mathf.Max(seedMaxY,y);
                }
                Remember(sample);
                Prefetch();
            }
            catch(Exception e){End(e.Message);}
        }
        // 到下一次提交前再取刚播过的姿势，避免把过渡首帧反馈为整个生成步的结果。
        void RememberGenerated()
        {
            if(!captureGenerated)return;
            captureGenerated=false;
            Remember(CaptureRow());
        }

        void Prefetch()
        {
            if(ready && history.Count==16 && pending==0 && buffer.Count<=7)Request();
        }
        static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        static Vector3 Vector(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
        Vector3 Local(Vector3 world)=>Quaternion.Inverse(basis)*(world-center);
        float seedMinY,seedMaxY;
        JObject CaptureRow()
        {
            Transform hips=Bone(HumanBodyBones.Hips);
            var pose=new JObject();
            foreach(var bone in avatar.Instance.Runtime.ControlRig.Bones)
            {
                var q=bone.Value.ControlBone.localRotation;
                pose[bone.Key.ToString()]=new JArray(q.x,q.y,q.z,q.w);
            }
            return new JObject{["pose"]=pose,["hips"]=V(Local(hips.position)),["root"]=V(Local(FeiyuPreview.ToUnity(core.Position))),
                ["y"]=avatar.Motions.LocomotionHipsOffset*hips.parent.lossyScale.y};
        }
        void Remember(JObject row){history.Enqueue(row);while(history.Count>16)history.Dequeue();}
        Vector3 TargetWorld()
        {
            var plane=Quaternion.Euler(0,core.PlaneYawRadians*Mathf.Rad2Deg,0);
            float extent=side?core.HalfWidth:core.HalfDepth;
            float distance=Mathf.Min(extent*.72f,Mathf.Max(0,extent-core.RadiusMeters-.01f));
            return center+plane*((side?Vector3.right:Vector3.forward)*distance);
        }
        void Request()
        {
            if(!Valid || !ready || history.Count!=16 || pending!=0) return;
            var trail=history.Concat(buffer).ToArray();
            // 通常补帧接在已承诺播出的缓存尾部。改目标时缓存清空，只取已实际播放历史。
            var samples=new JArray(trail.Skip(trail.Length-16));
            Vector3 local=Local(TargetWorld());
            int ticket=++serial;pending=ticket;requested=Now;
            var request=new JObject{["ticket"]=ticket,["seed"]=1000+serial,["frames"]=samples,["speed"]=speed,["leg"]=leg,["target"]=new JArray(local.x,local.z)};
            worker.StandardInput.WriteLine(request.ToString(Formatting.None));worker.StandardInput.Flush();
        }
        void Accept(JObject answer)
        {
            if((bool?)answer["ready"]==true){ready=true;history.Clear();return;}
            if(!Valid || (int?)answer["ticket"]!=pending)
            {
                Rejected++;
                if(answer["rows"] is JArray stale && stale.Count==8) RejectedBatches++;
                return;
            }
            pending=0;RoundTripMs=(Now-requested)*1000;
            if(answer["error"]!=null) throw new InvalidOperationException((string)answer["error"]);
            if(!(answer["rows"] is JArray rows) || rows.Count!=8) throw new InvalidDataException("未来帧数量错误");
            foreach(JObject row in rows)
            {
                var pose=(JObject)row["pose"];
                var parent=Quaternion.identity;
                foreach(var bone in Trunk)
                {
                    var q=pose[bone.ToString()];
                    if(q==null || !seedTilt.TryGetValue(bone,out float reference))continue;
                    var local=new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]).normalized;
                    var world=parent*local;
                    var up=world*Vector3.up;
                    var allowed=Mathf.Clamp(reference+6,10,24);
                    var tilt=Vector3.Angle(up,Vector3.up);
                    if(tilt>allowed){
                        var correction=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(up,Vector3.up),1-allowed/tilt);
                        world=correction*world;
                        local=Quaternion.Inverse(parent)*world;
                        pose[bone.ToString()]=new JArray(local.x,local.y,local.z,local.w);
                    }
                    parent=world;
                }
                float rawY=(float)row["y"];
                float y=Mathf.Clamp(rawY,seedMinY-leg*.04f,seedMaxY+leg*.04f);
                row["y"]=y;
                row["hips"][1]=(float)row["hips"][1]+y-rawY;
                row["epoch"]=epoch;buffer.Enqueue(row);
            }
        }
        Vector3 rootFrom,rootTo;
        double rootStart;
        bool rootMoving;
        void AdvanceRoot()
        {
            float t=Mathf.Clamp01((float)((Now-rootStart)/.05));
            var position=Vector3.Lerp(rootFrom,rootTo,t);
            if(!live.TryAutonomousPose(position,basis,generation))throw new InvalidDataException("桌面拒绝生成根");
            var goal=TargetWorld();
            var delta=position-rootFrom;
            float fraction=Mathf.Clamp01(Vector3.Dot(goal-rootFrom,delta)/Mathf.Max(delta.sqrMagnitude,1e-10f));
            if(Vector3.Distance(goal,rootFrom+delta*fraction)<Mathf.Max(core.RadiusMeters*.28f,speed*.065f))
            {
                End("已到达 · 回到待机");
                return;
            }
            if(t<1)return;
            rootMoving=false;
            float distance=Vector3.Distance(position,goal);
            if(distance<closestGoal-leg*.025f){closestGoal=distance;unproductiveFrames=0;}
            else if(++unproductiveFrames>=50)End("未继续接近目标 · 回到待机");
        }
        void Apply(JObject row)
        {
            Vector3 next=center+basis*Vector(row["root"]);
            if(!Valid || !core.ContainsFootprint(FeiyuPreview.ToCore(next),core.RadiusMeters))throw new InvalidDataException("桌面拒绝生成根");
            rootFrom=FeiyuPreview.ToUnity(core.Position);rootTo=next;rootStart=Now;rootMoving=true;
            var pose=new Dictionary<HumanBodyBones,Quaternion>();
            foreach(var property in ((JObject)row["pose"]).Properties())
            {
                var q=property.Value;
                if(Enum.TryParse(property.Name,out HumanBodyBones b)) pose[b]=new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]);
            }
            if(!avatar.Motions.SubmitLocomotionFrame(pose,(float)row["y"]/Bone(HumanBodyBones.Hips).parent.lossyScale.y,.05f)) throw new InvalidOperationException("身体拒绝提交");
        }
        void Revoke()=>End("交互已接管");
        public void End(string reason="已停止")
        {
            if(!active) return;
            ++epoch;rootMoving=false;captureGenerated=false;capturePending=false;active=false;prepared=false;pending=0;history.Clear();buffer.Clear();
            if(acquired && avatar) avatar.Motions.SpatialControlRevoked-=Revoke;
            if(acquired && avatar && live && live.Avatar==avatar)
            {
                avatar.Motions.EndLocomotion(core.State!=PetState.Idle);
            }
            if(live && live.Locomotion)
            {
                if(aligning)live.Locomotion.Cancel(true);
                live.Locomotion.ManualStep=priorManual;
            }
            if(controlsAutonomy)core.SetAutonomousEnabled(priorEnabled);
            controlsAutonomy=false;aligning=false;
            acquired=false;Owner=core?.State==PetState.Held?"抓取":"空间互动";Status=reason;
        }
        void OnDestroy()
        {
            End();
            if(worker==null)return;
            try
            {
                if(!worker.HasExited)
                {
                    worker.StandardInput.Close();
                    if(!worker.WaitForExit(2500))
                    {
                        var kill=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"taskkill.exe"),"/PID "+worker.Id+" /T /F")
                            {UseShellExecute=false,CreateNoWindow=true};
                        using(var terminator=Process.Start(kill)) terminator?.WaitForExit(2000);
                    }
                }
            }
            finally{worker.Dispose();}
        }
    }
}
#endif
