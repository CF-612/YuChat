using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using UniGLTF.SpringBoneJobs.Blittables;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 隔离 Editor 中比较相同重力轨迹；采样真实蒙皮顶点，而非只记录骨骼。
    public static class FallingClothCheck
    {
        [Serializable] sealed class RunResult
        {
            public string mode;
            public int fps;
            public float height;
            public float[] lift = new float[3], displacement = new float[3], settled = new float[3];
            public float maxAngle;
            public int fallingFrames;
        }
        [Serializable] sealed class Report
        {
            public string modelSha;
            public string[] probes = { "HairBackL", "HairSideL", "Skirt gentle 0" };
            public List<RunResult> runs = new List<RunResult>();
            public bool questTested = false, userAccepted = false;
        }
        sealed class Probe
        {
            public SkinnedMeshRenderer renderer;
            public int index;
            public readonly Mesh baked = new Mesh();
            public Vector3 rest;
            public Vector3 Sample(Transform root)
            {
                renderer.BakeMesh(baked, true);
                return root.InverseTransformPoint(renderer.transform.TransformPoint(baked.vertices[index]));
            }
        }
        static double deadline;
        static IEnumerator check;
        static string evidence;
        public static void Run()
        {
            LilToonTemplates.Ensure();Alpha2Project.CreateScene();EditorSceneManager.OpenScene(Alpha2Project.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            evidence = Path.Combine(Environment.GetEnvironmentVariable("XR_EVIDENCE_DIR"), "fall-cloth");
            Directory.CreateDirectory(evidence);
            deadline = EditorApplication.timeSinceStartup + 240;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Fall cloth timeout");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (check == null)
                {
                    var preview = UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
                    if (preview == null || !preview.Ready) return;
                    check = Check(preview);
                }
                if (!check.MoveNext()) Finish(0);
            }
            catch (Exception e) { Debug.LogWarning(e);Finish(1); }
        }
        static IEnumerator Check(FeiyuPreview preview)
        {
            preview.ManualInput = true;preview.Place(new Vector3(0,.74f,0));preview.enabled = false;
            var a = preview.Avatar;a.FreezePresentation = true;a.Instance.UpdateType = Vrm10Instance.UpdateTypes.None;
            var service = UniVRM10.FastSpringBones.FastSpringBoneService.Instance;
            service.UpdateType = UniVRM10.FastSpringBones.FastSpringBoneService.UpdateTypes.Manual;
            var joints = a.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>();
            a.ResetSecondaryMotion();
            var rotations = joints.Select(j=>j.transform.localRotation).ToArray();
            var report = new Report { modelSha = a.Package.model.sha256 };var probes = new List<Probe>();
            foreach (string label in report.probes)
            {
                var chain = a.Instance.SpringBone.Springs.First(s=>s.Name.StartsWith(label));
                Probe best = null;float highest = 0;
                foreach (var r in a.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var weights = r.sharedMesh.boneWeights;var bones = r.bones;var vertices = r.sharedMesh.vertices;
                    for (int i=0;i<weights.Length;i++)
                    {
                        var w=weights[i];float Score(int b,float v)=>b<bones.Length&&chain.Joints.Any(j=>j.transform==bones[b])?v:0;
                        float score=Score(w.boneIndex0,w.weight0)+Score(w.boneIndex1,w.weight1)+Score(w.boneIndex2,w.weight2)+Score(w.boneIndex3,w.weight3);
                        score=score<.5f?0:score*(r.transform.TransformPoint(vertices[i])-chain.Joints[0].transform.position).sqrMagnitude;
                        if(score>highest){highest=score;if(best==null)best=new Probe();best.renderer=r;best.index=i;}
                    }
                }
                if(best==null)throw new Exception("No skinned probe: "+label);probes.Add(best);
            }
            var cases=new[]{("baseline",60,1f),("production",60,1f),("production",30,1f),("production",90,1f),
                ("production",60,.15f),("production",60,1.8f),("regrab",60,1f),("suspend",60,1f)};
            foreach(var test in cases)
            {
                string mode=test.Item1;int fps=test.Item2;float height=test.Item3;float dt=1f/fps;
                string tag=mode+"-"+fps+"-"+height.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
                var result=new RunResult{mode=mode,fps=fps,height=height};report.runs.Add(result);
                var state=new PetInteraction(a.FootprintRadius);
                state.ConfirmPlane(new Vec3(0,.74f,0),1,1,new Vec3(0,.74f,0));
                state.BeginGrab("fall",out int grab);state.MoveGrab("fall",grab,new Vec3(0,.74f+height,0));
                a.transform.position=new Vector3(0,.74f+height,0);a.ResetSecondaryMotion();
                a.Instance.Runtime.SpringBone.ReconstructSpringBone();service.ManualUpdate(dt);
                a.Instance.Runtime.SpringBone.SetModelLevel(a.Instance.transform,new BlittableModelLevel());
                for(int n=0;n<120;n++)service.ManualUpdate(dt);
                foreach(var p in probes)p.rest=p.Sample(a.transform);
                state.ReleaseGrabWithDescent("fall",grab,new Vec3(0,.74f,0));
                for(int frame=0;frame<fps*3;frame++)
                {
                    if(frame==fps/4 && mode=="regrab")state.BeginGrab("caught",out int caught);
                    if(frame==fps/4 && mode=="suspend")state.Suspend();
                    state.StepDescent(dt,state.Generation);
                    a.transform.position=new Vector3(state.Position.X,state.Position.Y,state.Position.Z);
                    if(mode!="baseline")a.SetSpatialFeedback(state,dt);
                    service.ManualUpdate(dt);
                    if(state.State==PetState.Descending)
                    {
                        result.fallingFrames++;
                        for(int i=0;i<probes.Count;i++)
                        {
                            var delta=probes[i].Sample(a.transform)-probes[i].rest;
                            if(!float.IsFinite(delta.sqrMagnitude))throw new Exception("Nonfinite cloth vertex");
                            result.lift[i]=Mathf.Max(result.lift[i],delta.y);result.displacement[i]=Mathf.Max(result.displacement[i],delta.magnitude);
                        }
                        for(int i=0;i<joints.Length;i++)result.maxAngle=Mathf.Max(result.maxAngle,Quaternion.Angle(rotations[i],joints[i].transform.localRotation));
                    }
                    if(frame==0||frame==fps/4||frame==fps*2/5||frame==fps*3/5||frame==fps*3-1)
                    {
                        var root=a.transform.position;
                        preview.ViewCamera.transform.position=root+new Vector3(.28f,.17f,.48f);
                        preview.ViewCamera.transform.LookAt(root+Vector3.up*.12f);preview.ViewCamera.fieldOfView=33;
                        Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,tag+"-"+frame.ToString("D3")+".png"));
                    }
                    if(frame%10==0)yield return null;
                }
                for(int i=0;i<probes.Count;i++)result.settled[i]=Vector3.Distance(probes[i].Sample(a.transform),probes[i].rest);
                if(result.settled.Any(v=>!float.IsFinite(v)||v>.002f))throw new Exception("Cloth did not return to rest: "+tag);
            }
            File.WriteAllText(Path.Combine(evidence,"comparison.json"),JsonUtility.ToJson(report,true));
            var baseline=report.runs[0];var after=report.runs[1];
            if(after.lift[2]<baseline.lift[2]*1.15f)throw new Exception("Skirt airflow did not improve lift");
            if(after.fallingFrames!=baseline.fallingFrames)throw new Exception("Airflow changed fall duration");
            // 最后走真实 FeiyuPreview LateUpdate 接线：不手调空间根或 SpringBone。
            a.FreezePresentation=false;a.Instance.UpdateType=Vrm10Instance.UpdateTypes.LateUpdate;
            service.UpdateType=UniVRM10.FastSpringBones.FastSpringBoneService.UpdateTypes.LateUpdate;
            preview.enabled=true;preview.ManualInput=true;Time.captureFramerate=60;
            preview.Place(new Vector3(0,.74f,0));
            preview.Interaction.BeginGrab("integration",out int token);
            preview.Interaction.MoveGrab("integration",token,new Vec3(0,1.74f,0));preview.RefreshSpatialState();a.ResetSecondaryMotion();
            int last=Time.frameCount;
            for(int n=0;n<45;n++){while(last==Time.frameCount)yield return null;last=Time.frameCount;}
            preview.Interaction.ReleaseGrabWithDescent("integration",token,new Vec3(0,.74f,0));
            Directory.CreateDirectory(Path.Combine(evidence,"playmode-frames"));
            int descentFrames=0;float initialY=a.transform.position.y;
            for(int n=0;n<150;n++)
            {
                while(last==Time.frameCount)yield return null;last=Time.frameCount;
                if(preview.Interaction.State==PetState.Descending)descentFrames++;
                var root=a.transform.position;
                preview.ViewCamera.transform.position=root+new Vector3(.28f,.17f,.48f);
                preview.ViewCamera.transform.LookAt(root+Vector3.up*.12f);preview.ViewCamera.fieldOfView=33;
                if(n%2==0)Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"playmode-frames",(n/2).ToString("D4")+".png"));
            }
            Time.captureFramerate=0;
            if(descentFrames<20||descentFrames>35||Mathf.Abs(a.transform.position.y-.74f)>.001f)
                throw new Exception("Real preview descent did not run continuously");
            File.WriteAllText(Path.Combine(evidence,"integration.json"),JsonUtility.ToJson(new Integration{descentFrames=descentFrames,startY=initialY,endY=a.transform.position.y},true));
            foreach(var p in probes)UnityEngine.Object.Destroy(p.baked);
        }
        [Serializable] sealed class Integration { public int descentFrames;public float startY,endY;public bool manualSpringStep=false; }
        static void OnLog(string text,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code)
        {EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;Debug.Log(code==0?"XR_FALL_CLOTH_PASSED":"XR_FALL_CLOTH_FAILED");EditorApplication.Exit(code);}
    }
}
