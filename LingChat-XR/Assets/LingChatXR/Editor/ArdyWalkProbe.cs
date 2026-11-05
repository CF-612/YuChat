#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LingChatXR.Chat;
using LingChatXR.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 独立 PC 实验采样器。使用正在运行的 UniVRM 控制骨记录姿态和接地参考。
    public static class ArdyWalkProbe
    {
        static IEnumerator work;
        static int lastFrame = -1;
        static double deadline;
        static bool liveMode;
        static ArdyPlaytest session;
        static FeiyuPreview preview;
        static double recordStart, nextCapture;
        static readonly JArray timeline=new JArray();
        public static void RunLive(){liveMode=true;Run();}
        static string Output => Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            var chat = UnityEngine.Object.FindAnyObjectByType<XrChatController>();
            if (chat != null) chat.enabled = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("步态采样超时");
                if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                work ??= liveMode?LiveTrial():Capture();
                if(liveMode && recordStart>0) RecordFrame();
                if (work.MoveNext()) return;
                if(liveMode) File.WriteAllText(Path.Combine(Output,"timeline.json"),timeline.ToString());
                Debug.Log("XR_M1_PASSED ARDY walk capture");
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());
                EditorApplication.update -= Tick;
                EditorApplication.Exit(1);
            }
        }

        static JArray V(Vector3 v) => new JArray(v.x,v.y,v.z);
        static double Now=>Time.realtimeSinceStartupAsDouble;
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        static void RecordFrame()
        {
            if(Now<nextCapture)return;
            nextCapture=Now+.075;
            var root=FeiyuPreview.ToUnity(preview.Interaction.Position);
            var cam=preview.ViewCamera;
            cam.orthographic=true;cam.orthographicSize=.33f;
            var center=Vector3.Lerp(new Vector3(0,.87f,0),root+Vector3.up*.12f,.55f);
            cam.transform.position=center+new Vector3(.75f,.25f,1.05f);cam.transform.LookAt(center);
            int frame=timeline.Count;
            timeline.Add(new JObject {["t"]=Now-recordStart,["state"]=preview.Interaction.State.ToString(),
                ["owner"]=session.Active?session.Owner:preview.Interaction.State!=PetState.Idle?"空间互动":preview.Locomotion.Active?"固定走路":"本地待机",
                ["target"]=session.Target,["rtt"]=session.RoundTripMs,["buffer"]=session.Buffered,
                ["rejected"]=session.Rejected,["submitted"]=session.Submitted,["root"]=V(root)});
            LocomotionProbe.Capture(cam,"live-"+frame.ToString("D4"));
        }
        static IEnumerator LiveTrial()
        {
            preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            preview.ManualInput=true;
            while(!preview.Ready)yield return null;
            Check(preview.ActivePackageFolder=="local-physics-reference","占位包不符");
            var start=new Vector3(0,.74f,-.19f);
            Check(preview.PlaceOnPlane(new Vector3(0,.74f,0),.6f,.4f,start),"放置失败");
            session=preview.gameObject.AddComponent<ArdyPlaytest>();
            var core=preview.Interaction;
            var results=new JObject();
            for(int attempt=0;attempt<2;attempt++)
            {
                session.Begin(preview);
                while(!session.Pending)
                {
                    if(!session.Active)throw new Exception(session.Status);
                    yield return null;
                }
                if(recordStart==0)recordStart=Now;
                if(attempt==1)
                {
                    while(session.Submitted<6){Check(session.Active,session.Status);yield return null;}
                    session.ChangeTarget();
                    double change=Now;
                    var hips=preview.Avatar.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.Hips);
                    var before=hips.localRotation*Vector3.forward;
                    float initialYaw=Mathf.Atan2(before.x,before.z)*Mathf.Rad2Deg;
                    while(true)
                    {
                        Check(session.Active,session.Status);
                        var forward=hips.localRotation*Vector3.forward;
                        var yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
                        if(Mathf.Abs(Mathf.DeltaAngle(initialYaw,yaw))>12)break;
                        if(Now-change>8)throw new Exception("改目标后 8 秒未观察到实际转向");
                        yield return null;
                    }
                    results["visibleTurnOver12DegreesMs"]=(Now-change)*1000;
                    results["firstNewFrameMs"]=session.FirstNewFrameMs;
                    while(session.Submitted<45){Check(session.Active,session.Status);yield return null;}
                    results["refillMisses"]=session.Underruns;
                    results["lastRttMs"]=session.RoundTripMs;
                }
                int frame=Time.frameCount, priorRejected=session.RejectedBatches;
                Check(core.BeginGrab("probe",out int generation),"提起失败");
                preview.RefreshSpatialState();
                Check(!session.Active && session.Buffered==0 && !preview.Avatar.Motions.LocomotionOwned && Time.frameCount==frame,"同帧撤权失败");
                var safe=core.LastSafePosition;
                var raised=new Vec3(safe.X+.025f,safe.Y+.13f,safe.Z);
                Check(core.MoveGrab("probe",generation,raised),"持有移动失败");preview.RefreshSpatialState();
                double held=Now;while(Now-held<2.5)yield return null;
                if(attempt==0)Check(session.RejectedBatches>priorRejected,"未拒收包含八帧的晚到结果");
                var landing=new Vec3(raised.X,safe.Y,raised.Z);
                Check(core.ReleaseGrabWithDescent("probe",generation,landing),"释放失败");preview.RefreshSpatialState();
                while(core.State!=PetState.Idle)yield return null;
                Check(Vector3.Distance(FeiyuPreview.ToUnity(core.Position),FeiyuPreview.ToUnity(landing))<.001f && !session.Active && session.Buffered==0,"落地恢复到旧位置或旧控制者");
                results[attempt==0?"pendingInterruptedSameFrame":"playingInterruptedSameFrame"]=true;
                double pause=Now;while(Now-pause<1.2)yield return null;
            }
            var walker=preview.Locomotion;
            var localStart=FeiyuPreview.ToUnity(core.Position);
            var goal=Vector3.MoveTowards(localStart,FeiyuPreview.ToUnity(core.PlaneCenter),.16f);
            var fixedWalk=walker.GoTo(goal);while(!fixedWalk.IsCompleted)yield return null;
            Check(fixedWalk.Result,"关闭 ARDY 后固定走路不可用");
            double wait=Now;while(Now-wait<.8)yield return null;
            Check(Vector3.Distance(localStart,FeiyuPreview.ToUnity(core.Position))>.005f,"固定行走未移动");
            Check(core.BeginGrab("fixed",out var g),"固定行走抓取失败");preview.RefreshSpatialState();
            var safeFixed=core.LastSafePosition;
            core.MoveGrab("fixed",g,new Vec3(safeFixed.X,safeFixed.Y+.12f,safeFixed.Z));preview.RefreshSpatialState();
            wait=Now;while(Now-wait<1.0)yield return null;
            Check(core.ReleaseGrabWithDescent("fixed",g,safeFixed),"固定动作释放失败");preview.RefreshSpatialState();
            while(core.State!=PetState.Idle)yield return null;
            results["fixedWalkGrabLand"]=true;
            // 再启动一次完整 A → B，用于在连续录像里展示可重复的恢复。
            session.Begin(preview);
            while(session.Submitted<6 || !session.Active){Check(session.Active,session.Status);yield return null;}
            session.ChangeTarget();
            while(Now-recordStart<31){Check(session.Active,session.Status);yield return null;}
            session.End();
            results["rejected"]=session.Rejected;
            results["rejectedEightFrameBatches"]=session.RejectedBatches;
            File.WriteAllText(Path.Combine(Output,"acceptance.json"),results.ToString());
        }
        static IEnumerator Capture()
        {
            var live = UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            live.ManualInput = true;
            while (!live.Ready)
            {
                if (live.LoadingPhase == FeiyuPreview.LoadPhase.Failed) throw new Exception(live.Status);
                yield return null;
            }
            if (live.ActivePackageFolder != "local-physics-reference") throw new Exception("占位包不匹配");
            live.Locomotion.ManualStep = true;
            var avatar = live.Avatar;
            avatar.FreezePresentation = true;
            var player = avatar.Motions;
            var prepared = player.PrepareLocomotion();
            while (!prepared.IsCompleted) yield return null;
            if (!prepared.Result) throw new Exception(player.LocomotionStatus);
            var start = new Vector3(0,.74f,-.24f);
            if (!live.PlaceOnPlane(new Vector3(0,.74f,0),.6f,.4f,start)) throw new Exception("平台无效");
            player.EndWelcome();
            player.SetSpatialAllowed(true);
            live.Interaction.SetAutonomousEnabled(true);
            var rig = avatar.Instance.Runtime.ControlRig;
            var thigh = rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var shin = rig.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var foot = rig.GetBoneTransform(HumanBodyBones.LeftFoot);
            float leg = Vector3.Distance(thigh.position,shin.position) + Vector3.Distance(shin.position,foot.position);
            float speed = leg * player.MovementSpeedPerLeg("walk");
            if (!player.BeginLocomotion()) throw new Exception("固定步行未取得控制权");
            var camera = live.ViewCamera;
            camera.orthographic = true;
            camera.orthographicSize = avatar.HeightMeters * .82f;
            var frames = new JArray();
            for (int i = 0; i < 48; i++)
            {
                float t = i / 20f;
                var root = start + Vector3.forward * (t * speed);
                if (!live.TryAutonomousPose(root,Quaternion.identity,live.Interaction.Generation)) throw new Exception("越界");
                player.SampleLocomotion("walk",t);
                player.Step(.05f);
                avatar.Instance.Runtime.Process();
                var pose = new JObject();
                for (int j = 0; j < (int)HumanBodyBones.LastBone; j++)
                {
                    var bone = (HumanBodyBones)j;
                    var joint = rig.GetBoneTransform(bone);
                    if (joint == null) continue;
                    var q = joint.localRotation;
                    pose[bone.ToString()] = new JArray(q.x,q.y,q.z,q.w);
                }
                var hips = rig.GetBoneTransform(HumanBodyBones.Hips);
                frames.Add(new JObject { ["t"]=t, ["root"]=V(root-start), ["hips"]=V(hips.position-start),
                    ["leftFoot"]=V(foot.position-start), ["rightFoot"]=V(rig.GetBoneTransform(HumanBodyBones.RightFoot).position-start), ["pose"]=pose });
                var center = root + Vector3.up * (avatar.HeightMeters * .5f);
                camera.transform.position = center + new Vector3(.75f,.22f,.95f);
                camera.transform.LookAt(center);
                LocomotionProbe.Capture(camera,"fixed-"+i.ToString("D3"));
                yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"walk.json"), new JObject {
                ["model"]=avatar.Package.model.sha256, ["id"]="locomotion_walk", ["fps"]=20,
                ["loopSeconds"]=player.MovementLength("walk"), ["height"]=avatar.HeightMeters,
                ["speed"]=speed, ["leg"]=leg, ["frames"]=frames }.ToString());
            string samples = Environment.GetEnvironmentVariable("XR_ARDY_SAMPLES");
            if (string.IsNullOrEmpty(samples)) yield break;
            var data = JObject.Parse(File.ReadAllText(samples));
            if ((string)data["model"] != avatar.Package.model.sha256) throw new Exception("回放模型身份不匹配");
            float scale = (float)data["scale"];
            var names = (JArray)data["bones"];
            var historyRoot = data["history"]["root"].Last.ToObject<float[]>();
            var seamRoot = FeiyuPreview.ToUnity(live.Interaction.Position);
            var metrics = new JObject();
            foreach (string branch in new[]{"A","B"})
            {
                int frame = 0;
                var trace = new JArray();
                foreach (JToken block in new[]{data["first"],data["branches"][branch]})
                {
                    int count = frame == 0 ? 4 : ((JArray)block["root"]).Count;
                    for (int j=0;j<count;j++,frame++)
                    {
                        var r = block["root"][j].ToObject<float[]>();
                        var next = seamRoot + new Vector3(r[0]-historyRoot[0],0,r[2]-historyRoot[2])/scale;
                        if (!live.TryAutonomousPose(next,Quaternion.identity,live.Interaction.Generation)) throw new Exception("生成根越过合法桌面");
                        var pose = new Dictionary<HumanBodyBones,Quaternion>();
                        for (int k=0;k<names.Count;k++)
                        {
                            if (!Enum.TryParse((string)names[k],out HumanBodyBones bone)) continue;
                            var q=block["pose"][j][k].ToObject<float[]>();
                            pose[bone]=new Quaternion(q[0],q[1],q[2],q[3]);
                        }
                        if (!player.SubmitLocomotionFrame(pose,(r[1]-historyRoot[1])/scale/rig.GetBoneTransform(HumanBodyBones.Hips).parent.lossyScale.y)) throw new Exception("生成帧失去所有者");
                        player.Step(.05f);
                        avatar.Instance.Runtime.Process();
                        trace.Add(new JObject { ["root"]=V(next), ["leftFoot"]=V(foot.position), ["rightFoot"]=V(rig.GetBoneTransform(HumanBodyBones.RightFoot).position) });
                        var center=next+Vector3.up*(avatar.HeightMeters*.5f);
                        camera.transform.position=center+new Vector3(.75f,.22f,.95f);
                        camera.transform.LookAt(center);
                        LocomotionProbe.Capture(camera,branch+"-"+frame.ToString("D3"));
                        yield return null;
                    }
                }
                metrics[branch]=trace;
            }
            File.WriteAllText(Path.Combine(Output,"replay.json"),metrics.ToString());
        }
    }
}
#endif
