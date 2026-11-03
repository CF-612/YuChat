using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LingChatXR.Core;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace LingChatXR.Editor
{
    // 同一入口、同一设置；路径来自测试调用者，不识别角色身份或私有骨名。
    public static class NativePhysicsProbe
    {
        [Serializable] class Result
        {
            public string sha;public int chains,joints,colliders,fps,descentFrames;
            public float scale,maxJointMotion,settledOffset;public double solverMeanMs,solverMaxMs;
            public long solverAllocatedBytes;public bool authorDataUnchanged,clothConnectivityProvided=false;
        }
        [Serializable] class Report { public List<Result> runs=new List<Result>();public bool userAccepted=false,questTested=false; }
        static IEnumerator routine;static double deadline;static string evidence;
        public static void Run()
        {
            Unity.Collections.NativeLeakDetection.Mode=Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            evidence=Environment.GetEnvironmentVariable("XR_NATIVE_EVIDENCE");Directory.CreateDirectory(evidence);
            deadline=EditorApplication.timeSinceStartup+480;
            Application.logMessageReceived+=Log;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Native physics timeout");
                if(!EditorApplication.isPlaying)return;
                if(routine==null)routine=Check();
                if(!routine.MoveNext())Finish(0);
            }
            catch(Exception e){Debug.LogWarning(e);Finish(1);}
        }
        static IEnumerator Check()
        {
            var report=new Report();
            var camera=new GameObject("Evidence camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.14f,.19f);
            camera.transform.position=new Vector3(.6f,1.5f,2.5f);camera.transform.LookAt(new Vector3(0,1.24f,0));camera.fieldOfView=38;
            var light=new GameObject("Evidence light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,20,0);
            var service=UniVRM10.FastSpringBones.FastSpringBoneService.Instance;
            service.UpdateType=UniVRM10.FastSpringBones.FastSpringBoneService.UpdateTypes.Manual;
            int asset=0;
            foreach(string folder in Environment.GetEnvironmentVariable("XR_NATIVE_PACKAGES").Split('|'))
            {
                var root=new GameObject("Native character");var a=root.AddComponent<FeiyuAvatar>();
                var load=a.LoadAsync(folder,.24f,CancellationToken.None,Environment.GetEnvironmentVariable("XR_PORTABLE_PHYSICS")!="1");
                while(!load.IsCompleted)yield return null;
                load.GetAwaiter().GetResult();a.FreezePresentation=true;a.Instance.UpdateType=Vrm10Instance.UpdateTypes.None;
                var joints=a.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>();
                string Snapshot()=>string.Join("\n",joints.Select(j=>JsonUtility.ToJson(j)));
                string source=Snapshot();var centers=a.Instance.SpringBone.Springs.Select(s=>s.Center).ToArray();
                foreach(int fps in new[]{30,60,90})
                {
                    float dt=1f/fps;root.transform.position=new Vector3(0,.74f,0);root.transform.rotation=Quaternion.identity;
                    a.ResetSecondaryMotion();a.Instance.Runtime.SpringBone.ReconstructSpringBone();
                    for(int i=0;i<fps;i++){a.Physics?.Prepare();service.ManualUpdate(dt);}
                    var rest=joints.Select(j=>root.transform.InverseTransformPoint(j.transform.position)).ToArray();
                    var result=new Result{sha=a.Package.model.sha256,chains=a.Instance.SpringBone.Springs.Count,joints=joints.Length,
                        colliders=a.Instance.GetComponentsInChildren<VRM10SpringBoneCollider>().Length,fps=fps,scale=a.Instance.transform.lossyScale.x};
                    report.runs.Add(result);
                    var state=new PetInteraction(a.FootprintRadius);state.ConfirmPlane(new Vec3(0,.74f,0),1,1,new Vec3(0,.74f,0));state.BeginGrab("probe",out int grab);
                    string frames=Path.Combine(evidence,"asset-"+asset+"-fixed");if(fps==60)Directory.CreateDirectory(frames);
                    long ticks=0,allocated=0;var watch=new Stopwatch();
                    for(int frame=0;frame<fps*5;frame++)
                    {
                        float t=frame*dt;
                        if(t<1){state.MoveGrab("probe",grab,new Vec3(.12f*Mathf.Sin(2*Mathf.PI*t),.74f,0));root.transform.rotation=Quaternion.Euler(0,15*Mathf.Sin(2*Mathf.PI*t),0);}
                        if(frame==fps){state.MoveGrab("probe",grab,new Vec3(0,1.74f,0));root.transform.rotation=Quaternion.identity;root.transform.position=new Vector3(0,1.74f,0);a.ResetSecondaryMotion();}
                        if(frame==fps*2)state.ReleaseGrabWithDescent("probe",grab,new Vec3(0,.74f,0));
                        if(frame>=fps*2)state.StepDescent(dt,state.Generation);
                        root.transform.position=new Vector3(state.Position.X,state.Position.Y,state.Position.Z);a.SetSpatialFeedback(state,dt);
                        long before=GC.GetAllocatedBytesForCurrentThread();watch.Restart();a.Physics?.Prepare();service.ManualUpdate(dt);watch.Stop();
                        allocated+=GC.GetAllocatedBytesForCurrentThread()-before;ticks+=watch.ElapsedTicks;result.solverMaxMs=Math.Max(result.solverMaxMs,watch.Elapsed.TotalMilliseconds);
                        if(state.State==PetState.Descending)result.descentFrames++;
                        for(int i=0;i<joints.Length;i++)
                        {
                            var p=root.transform.InverseTransformPoint(joints[i].transform.position);if(!float.IsFinite(p.sqrMagnitude))throw new Exception("Nonfinite joint");
                            float delta=Vector3.Distance(p,rest[i]);result.maxJointMotion=Mathf.Max(result.maxJointMotion,delta);
                            if(frame==fps*5-1)result.settledOffset=Mathf.Max(result.settledOffset,delta);
                        }
                        if(fps==60&&frame%2==0)Alpha2Probe.Capture(camera,Path.Combine(frames,(frame/2).ToString("D4")+".png"));
                        if(frame==fps*2+fps/3 || frame==fps*5-1)
                        {
                            var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
                            camera.transform.position=root.transform.position+new Vector3(.25f,.16f,.5f);camera.transform.LookAt(root.transform.position+Vector3.up*.12f);
                            Alpha2Probe.Capture(camera,Path.Combine(evidence,"asset-"+asset+"-"+fps+"-"+frame+"-close.png"));camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
                        }
                        if(frame%10==0)yield return null;
                    }
                    result.solverMeanMs=ticks*1000d/Stopwatch.Frequency/(fps*5);result.solverAllocatedBytes=allocated;
                    result.authorDataUnchanged=source==Snapshot()&&centers.SequenceEqual(a.Instance.SpringBone.Springs.Select(s=>s.Center));
                    if(!result.authorDataUnchanged)throw new Exception("Author physics data changed");
                    File.WriteAllText(Path.Combine(evidence,"baseline.json"),JsonUtility.ToJson(report,true));
                }
                if(a.Physics!=null)
                {
                    // 同一个默认适配器验证轻放、低高处、重抓、非法目标、暂停与空间失效。
                    foreach(int fps in new[]{30,60,90})foreach(string scenario in new[]{"gentle","low","high","regrab","invalid","suspend","disable"})
                    {
                        float dt=1f/fps;
                        var state=new PetInteraction(a.FootprintRadius);state.ConfirmPlane(new Vec3(0,.74f,0),1,1,new Vec3(0,.74f,0));
                        state.BeginGrab("owner",out int generation);
                        float height=scenario=="gentle"?.02f:scenario=="low"?.15f:1f;
                        state.MoveGrab("owner",generation,new Vec3(0,.74f+height,0));
                        root.transform.SetPositionAndRotation(new Vector3(0,.74f+height,0),Quaternion.identity);
                        a.ResetSecondaryMotion();a.SetSpatialFeedback(state,dt);a.Physics.Prepare();service.ManualUpdate(dt);
                        state.ReleaseGrabWithDescent("owner",generation,scenario=="invalid"?new Vec3(10,.74f,0):new Vec3(0,.74f,0));
                        int stale=state.Generation;bool interrupted=false;
                        for(int frame=0;frame<fps*3;frame++)
                        {
                            if(frame==fps/5)
                            {
                                if(scenario=="regrab"){if(!state.BeginGrab("new-owner",out int fresh))throw new Exception("Regrab rejected");interrupted=true;}
                                if(scenario=="suspend"||scenario=="disable")
                                {
                                    state.Suspend();a.SetSpatialFeedback(state,dt);a.ResetSecondaryMotion();
                                    if(scenario=="disable"){root.SetActive(false);root.SetActive(true);}
                                    interrupted=true;
                                }
                            }
                            if(interrupted&&state.StepDescent(dt,stale))throw new Exception("Stale descent accepted");
                            state.StepDescent(dt,state.Generation);root.transform.position=new Vector3(state.Position.X,state.Position.Y,state.Position.Z);
                            a.SetSpatialFeedback(state,dt);a.Physics.Prepare();service.ManualUpdate(dt);
                            foreach(var j in joints)if(!float.IsFinite(j.transform.position.sqrMagnitude))throw new Exception("Lifecycle nonfinite joint");
                            if(frame%60==0)yield return null;
                        }
                        if(!interrupted&&(state.State!=PetState.Idle||Mathf.Abs(root.transform.position.y-.74f)>.001f))throw new Exception("Landing failed");
                        // 恢复：同一根平移/旋转后重新确认。检查重置缓存与作者 center 坐标一致。
                        state.ConfirmPlane(new Vec3(0,.74f,0),1,1,new Vec3(0,.74f,0));
                        root.transform.SetPositionAndRotation(new Vector3(.2f,.74f,-.1f),Quaternion.Euler(0,30,0));
                        a.SetSpatialFeedback(state,dt);a.ResetSecondaryMotion();a.Physics.Prepare();
                        var buffer=service.BufferCombiner.Combined;
                        foreach(var s in buffer.Springs)for(int i=s.logicSpan.startIndex;i<s.logicSpan.EndIndex;i++)
                        {
                            var logic=buffer.Logics[i];var point=buffer.TransformAccessArray[s.transformIndexOffset+logic.tailTransformIndex].position;
                            if(s.centerTransformIndex>=0)point=buffer.TransformAccessArray[s.transformIndexOffset+s.centerTransformIndex].InverseTransformPoint(point);
                            if(Vector3.Distance(point,buffer.CurrentTails[i])>1e-5f||Vector3.Distance(buffer.CurrentTails[i],buffer.PrevTails[i])>1e-5f)throw new Exception("Stale tail state after restore");
                        }
                        File.AppendAllText(Path.Combine(evidence,"lifecycle.txt"),a.Package.model.sha256+" "+fps+" "+scenario+" passed\n");
                    }
                }
                UnityEngine.Object.Destroy(root);int last=Time.frameCount;while(last==Time.frameCount)yield return null;
                service.ManualUpdate(1f/60f);
                if(UnityEngine.Object.FindObjectsByType<VRM10SpringBoneJoint>(FindObjectsSortMode.None).Length!=0)throw new Exception("Old avatar joints survived unload");
                asset++;
            }
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Debug.Log(code==0?"XR_NATIVE_PHYSICS_PASSED":"XR_NATIVE_PHYSICS_FAILED");EditorApplication.Exit(code);}
    }
}
