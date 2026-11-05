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
        double nextTick,requested,changed,launched;
        int epoch, generation, serial, pending;
        bool ready, active, prepared, acquired, priorManual, priorEnabled, priorFreeze, streaming, side;
        public bool Active => active;
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
        static double Now => Time.realtimeSinceStartupAsDouble;
        bool Valid => active && live && live.Ready && live.Avatar==avatar && live.Interaction==core &&
                      core.State==PetState.Idle && core.PlaneConfirmed && core.Generation==generation;

        public async void Begin(FeiyuPreview preview)
        {
            if (active || !preview || !preview.Ready || preview.Interaction.State!=PetState.Idle) return;
            live=preview; avatar=live.Avatar; core=live.Interaction;
            priorManual=live.Locomotion.ManualStep; priorEnabled=core.AutonomousEnabled; priorFreeze=avatar.FreezePresentation;
            active=true; int startEpoch=++epoch;
            try
            {
                generation=core.Generation;
                bool loaded=await avatar.Motions.PrepareLocomotion();
                if(!Valid || startEpoch!=epoch) return;
                if(!loaded) throw new InvalidOperationException(avatar.Motions.LocomotionStatus);
                live.Locomotion.Cancel(true);
                live.Locomotion.ManualStep=true;
                avatar.Motions.EndWelcome();
                if(!avatar.Motions.BeginLocomotion()) throw new InvalidOperationException("身体控制者忙，请先停止当前动作");
                acquired=true;
                core.SetAutonomousEnabled(true); generation=core.Generation;
                basis=live.SpatialRotation; center=FeiyuPreview.ToUnity(core.PlaneCenter);
                avatar.Motions.SpatialControlRevoked+=Revoke;
                avatar.FreezePresentation=true;
                leg=Vector3.Distance(Bone(HumanBodyBones.LeftUpperLeg).position,Bone(HumanBodyBones.LeftLowerLeg).position)
                    +Vector3.Distance(Bone(HumanBodyBones.LeftLowerLeg).position,Bone(HumanBodyBones.LeftFoot).position);
                speed=leg*avatar.Motions.MovementSpeedPerLeg("walk");
                EnsureWorker();
                prepared=true; phase=0; nextTick=Now;
                streaming=false; side=false; changed=0; Submitted=Underruns=0; FirstNewFrameMs=0;
                Status="固定步行准备接步"; Owner="固定走路";
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
            worker=new Process{StartInfo=info};
            worker.OutputDataReceived+=(s,e)=>{if(e.Data?.StartsWith("YUCHAT ")==true) replies.Enqueue(e.Data.Substring(7));};
            worker.ErrorDataReceived+=(s,e)=>{if(e.Data!=null) System.Diagnostics.Trace.WriteLine(e.Data);};
            worker.Start();launched=Now;worker.BeginOutputReadLine();worker.BeginErrorReadLine();
        }

        public void ChangeTarget()
        {
            if(!Valid || !prepared) return;
            side=!side; ++epoch; pending=0; buffer.Clear(); changed=Now; FirstNewFrameMs=0;
            Status="按已播放历史改目标"; Request();
        }

        void Update()
        {
            try
            {
                if(active && !Valid) End("空间或角色代次已变化");
                while(replies.TryDequeue(out string line)) Accept(JObject.Parse(line));
                if(!Valid || !prepared) return;
                if(worker.HasExited) throw new IOException("ARDY 进程已退出");
                if(!ready && Now-launched>120) throw new TimeoutException("ARDY 试载超时");
                if(pending!=0 && Now-requested>12) throw new TimeoutException("ARDY 单步超时");
                if(Now<nextTick) return;
                nextTick=Math.Max(nextTick+.05,Now);
                if(!streaming && ((!ready) || (pending==0 && history.Count<16)))
                {
                    avatar.Motions.SampleLocomotion("walk",phase);
                    // 冷启动期间循环固定动作，服务就绪后再开始桌面位移。
                    if(ready)
                    {
                        Vector3 next=FeiyuPreview.ToUnity(core.Position)+basis*Vector3.forward*(speed*.05f);
                        if(!live.TryAutonomousPose(next,basis,generation)) throw new InvalidOperationException("前方缺少接步空间");
                    }
                    avatar.Motions.Step(.05f); avatar.Instance.Runtime.Process();
                    Remember(CaptureRow());phase+=.05f;
                }
                else if(buffer.Count>0)
                {
                    JObject row=buffer.Dequeue();
                    if((int)row["epoch"]!=epoch || !Valid) {Rejected++;return;}
                    Apply(row);
                    if(changed>0){FirstNewFrameMs=(Now-changed)*1000;changed=0;}
                    streaming=true; Submitted++; Owner="ARDY"; Status="接步中";
                    Remember(CaptureRow());
                }
                else if(pending!=0) { Underruns++; Status="等待补帧"; }
                // 试玩时端到端往返约 0.3 秒。八帧中消费一帧即预取，余七帧提供 350 ms 裕量。
                if(ready && history.Count==16 && pending==0 && buffer.Count<=7) Request();
            }
            catch(Exception e){End(e.Message);}
        }

        static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        static Vector3 Vector(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
        Vector3 Local(Vector3 world)=>Quaternion.Inverse(basis)*(world-center);
        float baseHipsY;
        JObject CaptureRow()
        {
            Transform hips=Bone(HumanBodyBones.Hips);
            if(history.Count==0) baseHipsY=hips.localPosition.y;
            var pose=new JObject();
            foreach(var bone in avatar.Instance.Runtime.ControlRig.Bones)
            {
                var q=bone.Value.ControlBone.localRotation;
                pose[bone.Key.ToString()]=new JArray(q.x,q.y,q.z,q.w);
            }
            return new JObject{["pose"]=pose,["hips"]=V(Local(hips.position)),["root"]=V(Local(FeiyuPreview.ToUnity(core.Position))),
                ["y"]=(hips.localPosition.y-baseHipsY)*hips.parent.lossyScale.y};
        }
        void Remember(JObject row){history.Enqueue(row);while(history.Count>16)history.Dequeue();}
        void Request()
        {
            if(!Valid || !ready || history.Count!=16 || pending!=0) return;
            var trail=history.Concat(buffer).ToArray();
            // 通常补帧接在已承诺播出的缓存尾部。改目标时缓存清空，只取已实际播放历史。
            var samples=new JArray(trail.Skip(trail.Length-16));
            var plane=Quaternion.Euler(0,core.PlaneYawRadians*Mathf.Rad2Deg,0);
            var dest=center+plane*(side?Vector3.right*core.HalfWidth*.70f:Vector3.forward*core.HalfDepth*.74f);
            Vector3 local=Local(dest);
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
            foreach(JObject row in rows){row["epoch"]=epoch;buffer.Enqueue(row);}
        }
        void Apply(JObject row)
        {
            Vector3 next=center+basis*Vector(row["root"]);
            if(!Valid || !live.TryAutonomousPose(next,basis,generation)) throw new InvalidDataException("桌面拒绝生成根");
            var pose=new Dictionary<HumanBodyBones,Quaternion>();
            foreach(var property in ((JObject)row["pose"]).Properties())
            {
                var q=property.Value;
                if(Enum.TryParse(property.Name,out HumanBodyBones b)) pose[b]=new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]);
            }
            if(!avatar.Motions.SubmitLocomotionFrame(pose,(float)row["y"]/Bone(HumanBodyBones.Hips).parent.lossyScale.y)) throw new InvalidOperationException("身体拒绝提交");
            avatar.Motions.Step(.05f); avatar.Instance.Runtime.Process();
        }
        void Revoke()=>End("交互已接管");
        public void End(string reason="已停止")
        {
            if(!active) return;
            ++epoch;active=false;prepared=false;pending=0;history.Clear();buffer.Clear();
            if(acquired && avatar) avatar.Motions.SpatialControlRevoked-=Revoke;
            if(acquired && avatar && live && live.Avatar==avatar)
            {
                avatar.Motions.EndLocomotion(true); avatar.FreezePresentation=priorFreeze;
                core.SetAutonomousEnabled(priorEnabled);
            }
            if(live && live.Locomotion) live.Locomotion.ManualStep=priorManual;
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
