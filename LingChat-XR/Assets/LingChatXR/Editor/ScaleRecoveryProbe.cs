using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UniVRM10;
using UniVRM10.FastSpringBones;
using UniGLTF.SpringBoneJobs.Blittables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class ScaleRecoveryProbe
    {
        [Serializable] class Row
        {
            public string sha;public bool units;public int fps;public float relativeScale;
            public float resetErrorBefore,resetErrorAfter,warmupDrift,settledDrift,lateMotion,normalizedScaleDifference;
            public bool unchanged;
        }
        [Serializable] class Report{public List<Row> rows=new List<Row>();public bool visualAccepted=false,questTested=false;}
        static IEnumerator run;static string evidence;static double deadline;
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            evidence=Environment.GetEnvironmentVariable("XR_NATIVE_EVIDENCE");Directory.CreateDirectory(evidence);
            deadline=EditorApplication.timeSinceStartup+520;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.EnterPlaymode();
        }
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("S2 timeout");if(!EditorApplication.isPlaying)return;if(run==null)run=Check();if(!run.MoveNext())Finish(0);}catch(Exception e){Debug.LogWarning(e);Finish(1);}}
        static IEnumerator Check()
        {
            var report=new Report();var service=FastSpringBoneService.Instance;service.UpdateType=FastSpringBoneService.UpdateTypes.Manual;
            foreach(var folder in Environment.GetEnvironmentVariable("XR_NATIVE_PACKAGES").Split('|'))
            {
                var root=new GameObject("Cross-model scale probe");var a=root.AddComponent<FeiyuAvatar>();
                var task=a.LoadAsync(folder,.24f,CancellationToken.None,true);while(!task.IsCompleted)yield return null;task.GetAwaiter().GetResult();
                a.FreezePresentation=true;a.Instance.UpdateType=Vrm10Instance.UpdateTypes.None;
                var adapter=root.AddComponent<PortableSpringPhysics>();adapter.enabled=false;adapter.Initialize(a.Instance);
                var joints=a.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>();var source=joints.Select(j=>JsonUtility.ToJson(j)).ToArray();
                Vector3 modelScale=a.Instance.transform.localScale;
                Vector3[] Sample(float factor)=>joints.Select(j=>root.transform.InverseTransformVector(j.transform.position-a.Instance.transform.position)/factor).ToArray();
                float Diff(Vector3[] x,Vector3[] y){float max=0;for(int i=0;i<x.Length;i++){if(!float.IsFinite(x[i].sqrMagnitude))throw new Exception("Nonfinite spring");max=Mathf.Max(max,Vector3.Distance(x[i],y[i]));}return max;}
                float ResetError()
                {
                    var buffer=service.BufferCombiner.Combined;float max=0;
                    foreach(var s in buffer.Springs)for(int i=s.logicSpan.startIndex;i<s.logicSpan.EndIndex;i++)
                    {
                        var l=buffer.Logics[i];var point=buffer.TransformAccessArray[s.transformIndexOffset+l.tailTransformIndex].position;
                        if(s.centerTransformIndex>=0)point=buffer.TransformAccessArray[s.transformIndexOffset+s.centerTransformIndex].InverseTransformPoint(point);
                        max=Mathf.Max(max,Vector3.Distance(point,buffer.CurrentTails[i]));
                    }
                    return max;
                }
                foreach(bool units in new[]{false,true})foreach(int fps in new[]{30,60,90})
                {
                    Vector3[] reference=null;
                    foreach(float factor in new[]{1f,.5f,2f})
                    {
                        root.transform.SetPositionAndRotation(new Vector3(.3f,.74f,-.2f),Quaternion.Euler(0,25,0));a.Instance.transform.localScale=modelScale*factor;
                        a.Instance.Runtime.SpringBone.ReconstructSpringBone();adapter.RequestReset();adapter.Prepare();
                        a.Instance.Runtime.SpringBone.RestoreInitialTransform();float before=ResetError();adapter.RequestReset();adapter.Prepare();float after=ResetError();
                        if(after>1e-5f)throw new Exception("Center-space reset failed");
                        if(!units){foreach(var j in joints)a.Instance.Runtime.SpringBone.SetJointLevel(j.transform,j.Blittable);a.Instance.Runtime.SpringBone.SetModelLevel(a.Instance.transform,new BlittableModelLevel());}
                        var row=new Row{sha=a.Package.model.sha256,units=units,fps=fps,relativeScale=factor,resetErrorBefore=before,resetErrorAfter=after};report.rows.Add(row);
                        float dt=1f/fps;Vector3[] one=null;
                        for(int frame=0;frame<fps*10;frame++){service.ManualUpdate(dt);if(frame==fps-1)one=Sample(factor);if(frame%60==0)yield return null;}
                        var steady=Sample(factor);row.warmupDrift=Diff(one,steady);
                        for(int frame=0;frame<fps;frame++)
                        {
                            float t=frame/(float)fps;root.transform.position=new Vector3(.3f+.12f*factor*Mathf.Sin(2*Mathf.PI*t),.74f,-.2f);
                            root.transform.rotation=Quaternion.Euler(0,25+15*Mathf.Sin(2*Mathf.PI*t),0);service.ManualUpdate(dt);
                        }
                        root.transform.SetPositionAndRotation(new Vector3(.3f,.74f,-.2f),Quaternion.Euler(0,25,0));
                        Vector3[] near=null;
                        for(int frame=0;frame<fps*10;frame++){service.ManualUpdate(dt);if(frame==fps*9-1)near=Sample(factor);if(frame%60==0)yield return null;}
                        var end=Sample(factor);row.settledDrift=Diff(steady,end);row.lateMotion=Diff(near,end);
                        if(reference==null)reference=end;row.normalizedScaleDifference=Diff(reference,end);
                        if(units&&row.normalizedScaleDifference>1e-4f)throw new Exception("Uniform scale invariance failed");
                        row.unchanged=source.SequenceEqual(joints.Select(j=>JsonUtility.ToJson(j)));
                        if(!row.unchanged)throw new Exception("Author settings modified");
                        File.WriteAllText(Path.Combine(evidence,"scale-recovery.json"),JsonUtility.ToJson(report,true));
                    }
                }
                UnityEngine.Object.Destroy(root);int last=Time.frameCount;while(last==Time.frameCount)yield return null;service.ManualUpdate(1f/60f);
                if(UnityEngine.Object.FindObjectsByType<VRM10SpringBoneJoint>(FindObjectsSortMode.None).Length!=0)throw new Exception("Stale joints");
            }
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Debug.Log(code==0?"XR_NATIVE_PHYSICS_PASSED":"XR_NATIVE_PHYSICS_FAILED");EditorApplication.Exit(code);}
    }
}
