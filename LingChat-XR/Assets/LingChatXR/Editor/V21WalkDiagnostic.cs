#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using LingChatXR.Chat;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace LingChatXR.Editor
{
    public static class V21WalkDiagnostic
    {
        static IEnumerator work;
        static int frame=-1;
        static double deadline;
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SceneManager.sceneLoaded+=Select;deadline=EditorApplication.timeSinceStartup+200;
            EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Select(Scene scene,LoadSceneMode mode){Object.FindAnyObjectByType<FeiyuPreview>()?.ConfigureCharacter("local-smallbluefish-alpha2",true);}
        static void Tick()
        {
            try{
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("V21 行走诊断超时");
                if(!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;
                work??=Check();if(work.MoveNext())return;
                Debug.Log("XR_M1_PASSED V21 walk");Finish(0);
            }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(1);}
        }
        static void Finish(int code){SceneManager.sceneLoaded-=Select;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
        static IEnumerator Check()
        {
            var live=Object.FindAnyObjectByType<FeiyuPreview>();
            while(!live.Ready){if(live.LoadingPhase==FeiyuPreview.LoadPhase.Failed)throw new Exception(live.Status);yield return null;}
            if(!live.PlaceOnPlane(new Vector3(0,.74f,0),.6f,.4f,new Vector3(0,.74f,-.1f)))throw new Exception("放置失败");
            var avatar=live.Avatar;var player=avatar.Motions;
            var cam=live.ViewCamera;var center=new Vector3(0,.86f,-.1f);
            cam.orthographic=true;cam.orthographicSize=.16f;cam.transform.position=center+new Vector3(.37f,.08f,.55f);cam.transform.LookAt(center);
            var config=avatar.Package.springPhysics;
            var springs=config.bindings.Select(b=>avatar.Instance.SpringBone.Springs[b.spring]).ToArray();
            var joints=springs.SelectMany(s=>s.Joints).Select(j=>j.transform).Distinct().ToArray();
            var rows=new JArray();
            void Sample(string phase){
                rows.Add(new JObject{["phase"]=phase,["t"]=Time.time,["root"]=live.Interaction.Position.ToString(),
                    ["rotations"]=new JArray(joints.Select(j=>new JArray(j.localRotation.x,j.localRotation.y,j.localRotation.z,j.localRotation.w)))});
            }
            for(int i=0;i<70;i++){Sample("place");if(i==1||i==20||i==69)LocomotionProbe.Capture(cam,"place-"+i);yield return null;}
            player.EndWelcome();
            var start=FeiyuPreview.ToUnity(live.Interaction.Position);
            var go=live.Locomotion.GoTo(start+new Vector3(.32f,0,0));while(!go.IsCompleted)yield return null;
            if(!go.Result)throw new Exception(live.Locomotion.Status);
            for(int i=0;i<100;i++){
                center=FeiyuPreview.ToUnity(live.Interaction.Position)+Vector3.up*.12f;
                cam.transform.position=center+new Vector3(.37f,.08f,.55f);cam.transform.LookAt(center);
                Sample("walk");if(i%10==0)LocomotionProbe.Capture(cam,"walk-"+i);yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"spring.json"),new JObject{["bones"]=new JArray(joints.Select(j=>j.name)),
                ["configuredJoints"]=avatar.Physics.ConfiguredJointCount,["rows"]=rows}.ToString());
            live.Locomotion.Cancel(true);
            if(Environment.GetEnvironmentVariable("XR_SKIRT_SIDE")=="1") CheckCapsule();
            else CompareCoverage(live,config);
            if(Environment.GetEnvironmentVariable("XR_V21_TEST_ARDY")!="1")yield break;
            live.Locomotion.Cancel(true);
            live.SendMessage("OnApplicationFocus",false);live.SendMessage("OnApplicationFocus",true);
            if(!live.Interaction.PlaneConfirmed)throw new Exception("编辑器窗口切换丢失桌面");
            var view=Object.FindAnyObjectByType<XrChatView>();
            var button=view.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ardy-game-start");
            void Click()
            {
                if(button==null || !button.enabledInHierarchy || button.worldBound.width<=0 ||
                    button.panel.Pick(button.worldBound.center)!=button) throw new Exception("Game 按钮被挡住或不可用");
                using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
            }
            live.ResetPlacement();
            for(int i=0;i<8;i++)yield return null;
            C0SettingsProbe.CaptureCombined(Path.Combine(Output,"Game-start.png"),view,live);
            Click();
            ArdyPlaytest stream;
            while((stream=live.GetComponent<ArdyPlaytest>())==null)yield return null;
            var demoFrames=new JArray();
            var poses=new List<(Dictionary<HumanBodyBones,Quaternion> pose,float y)>();
            float initialHeight=avatar.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.Hips).localPosition.y; int lastPose=-1;
            double demoStart=EditorApplication.timeSinceStartup, nextShot=0;
            var hair=config.bindings.Where(b=>b.material=="hair")
                .SelectMany(b=>avatar.Instance.SpringBone.Springs[b.spring].Joints.SkipLast(1))
                .Select(j=>j.transform).ToArray();
            var hem=config.bindings.Where(b=>b.material=="cloth")
                .Select(b=>avatar.Instance.SpringBone.Springs[b.spring].Joints.Last().transform).ToArray();
            Vector3? seedStart=null;
            Vector3 seedTravel=Vector3.zero;
            void CaptureDemo()
            {
                if(stream.Owner=="角色待机")return;
                if(stream.Owner=="固定走路")
                {
                    var pos=FeiyuPreview.ToUnity(live.Interaction.Position);
                    seedStart??=pos;
                    seedTravel=Quaternion.Inverse(live.SpatialRotation)*(pos-seedStart.Value);
                }
                var skeleton=avatar.Instance.Runtime.ControlRig;
                if(stream.Owner=="ARDY" && lastPose!=stream.Submitted){
                    poses.Add((skeleton.Bones.ToDictionary(b=>b.Key,b=>b.Value.ControlBone.localRotation),
                        skeleton.GetBoneTransform(HumanBodyBones.Hips).localPosition.y-initialHeight));
                    lastPose=stream.Submitted;
                }
                double t=EditorApplication.timeSinceStartup-demoStart;
                if(t<nextShot)return;nextShot=t+.12;
                cam.transform.LookAt(FeiyuPreview.ToUnity(live.Interaction.Position)+Vector3.up*.12f);
                var rig=avatar.Instance.Runtime.ControlRig;
                var hip=rig.GetBoneTransform(HumanBodyBones.Hips);
                var thigh=rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                demoFrames.Add(new JObject{["t"]=t,["owner"]=stream.Owner,["submitted"]=stream.Submitted,["target"]=stream.Target,
                    ["hairMeanDegrees"]=hair.Average(j=>Quaternion.Angle(j.localRotation,avatar.Instance.Runtime.InitPose[j].LocalRotation)),
                    ["hemBelowHipMm"]=1000*hem.Average(j=>hip.position.y-j.position.y),
                    ["status"]=stream.WorkerPhase,["hipYaw"]=Mathf.DeltaAngle(0,hip.localEulerAngles.y),["thighX"]=Mathf.DeltaAngle(0,thigh.localEulerAngles.x)});
                LocomotionProbe.Capture(cam,"demo-"+(demoFrames.Count-1).ToString("D4"));
            }
            while(stream.Active && stream.Submitted<20){CaptureDemo();yield return null;}
            File.WriteAllText(Path.Combine(Output,"ardy.txt"),stream.Status+" frames="+stream.Submitted+" owner="+stream.Owner);
            if(stream.Submitted<12)throw new Exception(stream.Status);
            if(seedTravel.z<=0 || Mathf.Abs(seedTravel.x)>seedTravel.z*.05f)
                throw new Exception("固定步态未沿角色前方移动："+seedTravel);

            C0SettingsProbe.CaptureCombined(Path.Combine(Output,"Game-generated.png"),view,live);
            Click();int before=stream.Submitted;
            while(stream.Active&&stream.Submitted<before+40){CaptureDemo();yield return null;}
            if(!stream.Active || stream.FirstNewFrameMs<=0)throw new Exception("切换目标未收到新帧："+stream.Status);
            File.AppendAllText(Path.Combine(Output,"ardy.txt")," B="+stream.FirstNewFrameMs+" ms frames="+stream.Submitted);
            File.WriteAllText(Path.Combine(Output,"demo-timeline.json"),demoFrames.ToString());
            if(Environment.GetEnvironmentVariable("XR_ARDY_STANCE")=="1")
            {
                double cold=stream.FirstFrameSeconds;
                stream.End();
                double settleUntil=EditorApplication.timeSinceStartup+.7;while(EditorApplication.timeSinceStartup<settleUntil)yield return null;
                if(avatar.FreezePresentation||player.LocomotionActive)throw new Exception("停止后没有回到角色待机");
                LocomotionProbe.Capture(cam,"stopped-idle");
                Click();while(stream.Active && stream.Submitted<1)yield return null;
                if(!stream.Active)throw new Exception(stream.Status);
                double warm=stream.FirstFrameSeconds;
                float maxTilt=0;
                double until=EditorApplication.timeSinceStartup+32;while(stream.Active && EditorApplication.timeSinceStartup<until){
                    avatar.Instance.TryGetBoneTransform(HumanBodyBones.Hips,out var hips);
                    avatar.Instance.TryGetBoneTransform(HumanBodyBones.Head,out var head);
                    maxTilt=Mathf.Max(maxTilt,Vector3.Angle(head.position-hips.position,Vector3.up));
                    yield return null;
                }
                File.WriteAllText(Path.Combine(Output,"stance.txt"),$"cold={cold:F2}s warm={warm:F2}s tilt={maxTilt:F1} end={stream.Status} submitted={stream.Submitted} underruns={stream.Underruns}");
                if(stream.Active || !stream.Status.Contains("待机") || maxTilt>32)throw new Exception("未稳定走到停止状态："+stream.Status);
                settleUntil=EditorApplication.timeSinceStartup+.7;while(EditorApplication.timeSinceStartup<settleUntil)yield return null;
                Vector3 idleCenter=FeiyuPreview.ToUnity(live.Interaction.Position)+Vector3.up*avatar.HeightMeters*.5f;
                cam.transform.position=idleCenter+live.SpatialRotation*new Vector3(.32f,.06f,.5f);
                cam.transform.LookAt(idleCenter);
                LocomotionProbe.Capture(cam,"arrival-idle");
                if(avatar.FreezePresentation||player.LocomotionActive)throw new Exception("到达后未恢复待机");
                Click();while(stream.Active&&!stream.Pending)yield return null;
                if(!live.Interaction.BeginGrab("stance-probe",out int grab))throw new Exception("生成期间无法提起");
                live.RefreshSpatialState();
                if(stream.Active||avatar.Motions.LocomotionOwned||stream.Buffered!=0)throw new Exception("抓取未及时中断");
                yield break;
            }
            stream.End();
            if(Environment.GetEnvironmentVariable("XR_SKIRT_SIDE")!="1")yield break;
            player.EndLocomotion(true);
            // 同一批实际 Core8 帧分别驱动两次，等待独立 Unity 帧完成蒙皮后再取侧面。
            live.Locomotion.ManualStep=true;avatar.FreezePresentation=true;
            var control=avatar.Instance.Runtime.ControlRig;
            avatar.Instance.TryGetBoneTransform(HumanBodyBones.LeftUpperLeg,out var left);
            avatar.Instance.TryGetBoneTransform(HumanBodyBones.RightUpperLeg,out var right);
            bool meshCheck=Environment.GetEnvironmentVariable("XR_SKIRT_MESH")=="1";
            var service=UniVRM10.FastSpringBones.FastSpringBoneService.Instance;
            var oldUpdate=service.UpdateType;
            if(meshCheck)service.UpdateType=UniVRM10.FastSpringBones.FastSpringBoneService.UpdateTypes.Manual;
            bool restSweep=Environment.GetEnvironmentVariable("XR_SKIRT_IDLE_SWEEP")=="1";
            bool comparePrior=meshCheck && Environment.GetEnvironmentVariable("XR_SKIRT_COMPARE_PRIOR")=="1";
            var tuned=config.colliders.First(c=>!string.IsNullOrEmpty(c.capsuleEndBone)).walkingRadius;
            foreach(var radius in restSweep?new[]{.10f,.14f,.16f,.18f,.20f}:meshCheck?new[]{comparePrior ? .22f : 0f,tuned}:new[]{0f,.035f,.055f})
            {
                var candidate=JsonUtility.FromJson<SpringPhysicsConfiguration>(JsonUtility.ToJson(config));
                if(comparePrior && radius==.22f) foreach(var material in candidate.materials)
                    {material.authoredGroundResponse=false;material.authoredGroundSpheres=false;}
                if(meshCheck){
                    foreach(var collider in candidate.colliders.Where(c=>!string.IsNullOrEmpty(c.capsuleEndBone))){
                        if(comparePrior && radius==.22f)collider.standingRadius=.14f;
                        if(restSweep){collider.radius=radius;collider.walkingRadius=0;}else collider.walkingRadius=radius;
                    }
                }else candidate.materials.Single(m=>m.id=="cloth").minimumContactRadius=radius;
                avatar.Physics.ConfigureMaterials(candidate);player.SetSpatialAllowed(true);
                if(!player.BeginLocomotion())throw new Exception("重播未取得控制权");
                player.SubmitLocomotionFrame(poses[0].pose,poses[0].y);
                player.Step(.05f);avatar.Instance.Runtime.Process();avatar.Physics.RequestReset();
                avatar.Physics.Prepare();
                if(meshCheck)for(int k=0;k<60;k++)service.ManualUpdate(1f/60);
                for(int i=0;!restSweep && i<poses.Count;i++)
                {
                    player.SubmitLocomotionFrame(poses[i].pose,poses[i].y);
                    player.Step(.05f);avatar.Instance.Runtime.Process();
                    if(meshCheck){
                        avatar.Physics.SetSpatialMotion(live.Interaction,.05f,player.LocomotionOwned);
                        avatar.Physics.Prepare();
                        for(int k=0;k<3;k++)service.ManualUpdate(1f/60);
                        yield return null;
                    }else for(int settle=0;settle<3;settle++)yield return null;
                    if(i%4!=0)continue;
                    var middle=(left.position+right.position)*.5f;
                    var axis=Vector3.ProjectOnPlane(control.GetBoneTransform(HumanBodyBones.Hips).right,Vector3.up).normalized;
                    cam.orthographicSize=avatar.HeightMeters*.34f;
                    cam.transform.position=middle+axis*.7f;cam.transform.LookAt(middle);
                    if(meshCheck) ExportSkirtSurfaces(avatar,config,$"mesh-{Mathf.RoundToInt(radius*1000)}-{i:D2}");
                    LocomotionProbe.Capture(cam,$"replay-{Mathf.RoundToInt(radius*1000)}-{i:D2}");
                }
                player.EndLocomotion(true);
                if(meshCheck)
                {
                    foreach(var gait in restSweep?new[]{"idle"}:new[]{"walk","skip","run","idle"})
                    {
                        bool moving=gait!="idle";
                        if(moving&&!player.BeginLocomotion())throw new Exception("本地步态无法取得控制权");
                        for(int k=0;k<90;k++)
                        {
                            if(moving)player.SampleLocomotion(gait,k/60f);
                            player.Step(1f/60);avatar.Instance.Runtime.Process();
                            avatar.Physics.SetSpatialMotion(live.Interaction,1f/60,moving);
                            avatar.Physics.Prepare();service.ManualUpdate(1f/60);
                            yield return null;
                            if(k>=24 && k%12==0){
                                string name=$"{Mathf.RoundToInt(radius*1000)}-{gait}-{k:D2}";
                                ExportSkirtSurfaces(avatar,config,"mesh-"+name);
                                if(k==48)LocomotionProbe.Capture(cam,"fixed-"+name);
                            }
                        }
                        player.EndLocomotion(true);
                    }
                }
            }
            service.UpdateType=oldUpdate;avatar.Physics.ConfigureMaterials(config);avatar.Physics.RequestReset();
            avatar.FreezePresentation=false;live.Locomotion.ManualStep=false;
        }
        // 记录真实蒙皮三角面。按材质与权重筛选，不把衣服正常遮住腿的屏幕投影算成穿插。
        static void ExportSkirtSurfaces(FeiyuAvatar a,SpringPhysicsConfiguration profile,string file)
        {
            var rig=a.Instance;
            var cloth=profile.bindings.Where(b=>b.material=="cloth").SelectMany(b=>rig.SpringBone.Springs[b.spring].Joints).Select(j=>j.transform).ToHashSet();
            var legs=new HashSet<Transform>();
            foreach(var id in new[]{HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg})
                if(rig.TryGetBoneTransform(id,out var t))legs.Add(t);
            using var writer=new BinaryWriter(File.Create(Path.Combine(Output,file+".bin")));
            void WriteVertex(Vector3 v){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
            foreach(var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=r.sharedMesh;if(mesh==null || mesh.boneWeights.Length==0)continue;
                var bones=r.bones;var weights=mesh.boneWeights;
                var baked=new Mesh();
                try{
                    r.BakeMesh(baked);var v=baked.vertices;
                    for(int i=0;i<v.Length;i++)v[i]=r.transform.TransformPoint(v[i]);
                    var kind=new byte[v.Length];
                    for(int i=0;i<kind.Length;i++){
                        var w=weights[i];float clothW=0,legW=0;
                        void Add(int index,float weight){if(weight<=0)return;if(cloth.Contains(bones[index]))clothW+=weight;if(legs.Contains(bones[index]))legW+=weight;}
                        Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                        kind[i]=(byte)(clothW>.25f?1:legW>.25f?2:0);
                    }
                    for(int sub=0;sub<mesh.subMeshCount;sub++){
                        var tris=mesh.GetTriangles(sub);var name=r.sharedMaterials[sub].name;
                        for(int i=0;i<tris.Length;i+=3){
                            int x=tris[i],y=tris[i+1],z=tris[i+2];
                            byte k=kind[x];if(k==0||kind[y]!=k||kind[z]!=k)continue;
                            writer.Write(k);writer.Write(name);WriteVertex(v[x]);WriteVertex(v[y]);WriteVertex(v[z]);
                        }
                    }
                }finally{Object.DestroyImmediate(baked);}
            }
        }
        static void CheckCapsule()
        {
            var point=new Unity.Mathematics.float3(.05f,.7f,0);
            var shape=new UniGLTF.SpringBoneJobs.Blittables.BlittableCollider(default,.1f,
                new Unity.Mathematics.float3(0,1,0),UniGLTF.SpringBoneJobs.Blittables.BlittableColliderType.Capsule);
            var logic=new UniGLTF.SpringBoneJobs.Blittables.BlittableJointImmutable(length:Unity.Mathematics.math.length(point));
            if(!UniGLTF.SpringBoneJobs.SpringBoneCollision.TryCollide(logic,default,default,shape,default,1,
                new Unity.Mathematics.float3(0,1,0),default,point,out var pushed) || pushed.x<=point.x)
                throw new Exception("位于胶囊中段的裙骨端点未被推出");
        }
        static void CompareCoverage(FeiyuPreview live,SpringPhysicsConfiguration profile)
        {
            var a=live.Avatar;var rig=a.Instance;
            var cloth=new HashSet<Transform>(profile.bindings.Where(b=>b.material=="cloth").SelectMany(b=>rig.SpringBone.Springs[b.spring].Joints).Select(j=>j.transform));
            var meshes=new List<(SkinnedMeshRenderer renderer,Mesh baked,int[] indices)>();
            foreach(var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var boneFlags=r.bones.Select(cloth.Contains).ToArray();
                if(!boneFlags.Any(x=>x))continue;
                var indices=new List<int>();var weights=r.sharedMesh.boneWeights;
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    float total=(boneFlags[w.boneIndex0]?w.weight0:0)+(boneFlags[w.boneIndex1]?w.weight1:0)+
                        (boneFlags[w.boneIndex2]?w.weight2:0)+(boneFlags[w.boneIndex3]?w.weight3:0);
                    if(total>.5f)indices.Add(i);
                }
                if(indices.Count>0)meshes.Add((r,new Mesh(),indices.ToArray()));
            }
            if(meshes.Count==0)throw new Exception("没有找到实际裙骨蒙皮顶点");
            var svc=UniVRM10.FastSpringBones.FastSpringBoneService.Instance;
            var update=svc.UpdateType;var vrmUpdate=rig.UpdateType;
            bool freeze=a.FreezePresentation;
            var results=new JArray();
            try
            {
                a.FreezePresentation=true;svc.UpdateType=UniVRM10.FastSpringBones.FastSpringBoneService.UpdateTypes.Manual;
                rig.UpdateType=UniVRM10.Vrm10Instance.UpdateTypes.None;
                foreach(bool capsule in new[]{false,true})
                {
                    var config=JsonUtility.FromJson<SpringPhysicsConfiguration>(JsonUtility.ToJson(profile));
                    foreach(var c in config.colliders.Where(c=>!string.IsNullOrEmpty(c.capsuleEndBone)))
                        if(!capsule){c.capsuleEndBone=null;c.radius=.0585f;}
                    a.Physics.ConfigureMaterials(config);
                    var p=a.Motions;p.Stop(true);p.SetSpatialAllowed(true);
                    if(!p.BeginLocomotion())throw new Exception("固定动作被占用");
                    p.SampleLocomotion("skip",0);p.Step(.25f);rig.Runtime.Process();
                    a.Physics.RequestReset();a.Physics.Prepare();
                    for(int i=0;i<90;i++)svc.ManualUpdate(1f/60);
                    var depths=new List<float>();var vertices=new List<Vector3>();
                    for(int i=0;i<90;i++)
                    {
                        p.SampleLocomotion("skip",i/60f);p.Step(1f/60);rig.Runtime.Process();
                        a.Physics.Prepare();svc.ManualUpdate(1f/60);
                        if(i%6!=0)continue;
                        var hips=new Vector3[2];var knees=new Vector3[2];var radii=new float[2];
                        for(int side=0;side<2;side++)
                        {
                            rig.TryGetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg,out var top);
                            rig.TryGetBoneTransform(side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg,out var end);
                            hips[side]=top.position;knees[side]=end.position;radii[side]=.10f*top.lossyScale.magnitude/Mathf.Sqrt(3f);
                        }
                        foreach(var (r,mesh,indices) in meshes)
                        {
                            r.BakeMesh(mesh);mesh.GetVertices(vertices);
                            foreach(var ix in indices)
                            {
                                var v=r.transform.TransformPoint(vertices[ix]);float depth=0;
                                for(int side=0;side<2;side++)
                                {
                                    var axis=knees[side]-hips[side];
                                    var closest=hips[side]+axis*Mathf.Clamp01(Vector3.Dot(v-hips[side],axis)/axis.sqrMagnitude);
                                    depth=Mathf.Max(depth,radii[side]-Vector3.Distance(v,closest));
                                }
                                depths.Add(depth);
                            }
                        }
                        if(i==42||i==66)LocomotionProbe.Capture(live.ViewCamera,(capsule?"capsule":"ball")+"-"+i);
                    }
                    p.EndLocomotion(true);
                    results.Add(new JObject{["configuration"]=capsule?"capsule":"old sphere",["samples"]=depths.Count,
                        ["insideOverHalfMm"]=depths.Count(d=>d>.0005f),["maxMm"]=depths.Max()*1000,["meanMm"]=depths.Average()*1000});
                }
            }
            finally
            {
                foreach(var item in meshes)Object.Destroy(item.baked);
                a.Physics.ConfigureMaterials(profile);a.Physics.RequestReset();a.FreezePresentation=freeze;
                rig.UpdateType=vrmUpdate;svc.UpdateType=update;
            }
            File.WriteAllText(Path.Combine(Output,"coverage.json"),results.ToString());
        }
    }
}
#endif
