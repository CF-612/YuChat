using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    [InitializeOnLoad]
    public static class Alpha2PhysicsCheck
    {
        const string Key="LingChatXR.Alpha2Physics";
        static double start;
        static Alpha2PhysicsCheck(){if(SessionState.GetBool(Key,false)){start=EditorApplication.timeSinceStartup;EditorApplication.update+=Wait;}}
        public static void Run(){Alpha2Project.CreateScene();EditorSceneManager.OpenScene(Alpha2Project.ScenePath);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void Wait()
        {
            if(EditorApplication.timeSinceStartup-start>240){Finish(1);return;}
            if(!EditorApplication.isPlaying)return;
            var preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();if(preview==null||!preview.Ready)return;
            EditorApplication.update-=Wait;preview.gameObject.AddComponent<Alpha2PhysicsDriver>().Initialize(preview);
        }
        public static void Finish(int code)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Wait;Debug.Log(code==0?"XR_ALPHA2_PHYSICS_PASSED":"XR_ALPHA2_PHYSICS_FAILED");EditorApplication.Exit(code);}
    }

    [DefaultExecutionOrder(100)]
    public sealed class Alpha2PhysicsDriver : MonoBehaviour
    {
        [Serializable] class RunResult {public int fps;public bool worldCenters;public float[] hairSkirtTailMax=new float[3];public float[] settledOffset=new float[3];public float maxBoneAngle;public float maxFrameMilliseconds;public bool finite=true;}
        [Serializable] class Report {public List<RunResult> runs=new List<RunResult>();public bool userAccepted=false;public bool questTested=false;public bool skinnedVertexMeasured=true;}
        sealed class Probe {public SkinnedMeshRenderer renderer;public int index;public Mesh baked=new Mesh();public Vector3 baseline;}
        FeiyuPreview preview;FeiyuAvatar avatar;Transform head;Quaternion headRest;
        readonly Report report=new Report();readonly List<Probe> probes=new List<Probe>();
        Transform[] joints;Quaternion[] jointRest;RunResult result;int run,frame,grab,videoFrame;float timer;double stamp;
        string evidence;bool ready;
        Camera backCamera;
        public void Initialize(FeiyuPreview value)
        {
            try
            {
                preview=value;avatar=value.Avatar;preview.ManualInput=true;avatar.FreezePresentation=true;
                avatar.Instance.UpdateType=Vrm10Instance.UpdateTypes.None;
                avatar.Instance.TryGetBoneTransform(HumanBodyBones.Head,out head);headRest=head.localRotation;
                evidence=Environment.GetEnvironmentVariable("XR_EVIDENCE_DIR");Directory.CreateDirectory(evidence);
                preview.ViewCamera.transform.position=new Vector3(.27f,1.1f,.75f);preview.ViewCamera.transform.LookAt(new Vector3(0,.94f,0));preview.ViewCamera.fieldOfView=37;
                backCamera=new GameObject("Physics evidence rear camera").AddComponent<Camera>();backCamera.CopyFrom(preview.ViewCamera);backCamera.enabled=false;
                backCamera.transform.position=new Vector3(-.36f,1.15f,-.72f);backCamera.transform.LookAt(new Vector3(0,1.04f,0));
                Directory.CreateDirectory(Path.Combine(evidence,"motion-rear"));
                joints=avatar.Instance.SpringBone.Springs.SelectMany(s=>s.Joints).Select(j=>j.transform).Distinct().ToArray();jointRest=joints.Select(j=>j.localRotation).ToArray();
                foreach(string label in new[]{"HairBackL","Skirt gentle 0","Whale tail"})
                {
                    var chain=avatar.Instance.SpringBone.Springs.First(s=>s.Name.StartsWith(label));
                    Probe best=null;float highest=0;
                    foreach(var renderer in avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var weights=renderer.sharedMesh.boneWeights;var bones=renderer.bones;var vertices=renderer.sharedMesh.vertices;
                        for(int i=0;i<weights.Length;i++)
                        {
                            var w=weights[i];float Score(int bone,float weight)=>bone<bones.Length&&chain.Joints.Any(j=>j.transform==bones[bone])?weight:0;
                            float score=Score(w.boneIndex0,w.weight0)+Score(w.boneIndex1,w.weight1)+Score(w.boneIndex2,w.weight2)+Score(w.boneIndex3,w.weight3);
                            score=score<.5f?0:score*(renderer.transform.TransformPoint(vertices[i])-chain.Joints[0].transform.position).sqrMagnitude;
                            if(score>highest){highest=score;if(best==null)best=new Probe();best.renderer=renderer;best.index=i;}
                        }
                    }
                    if(best==null)throw new Exception("No skinned probe for "+label);probes.Add(best);
                }
                StartRun();ready=true;
            }
            catch(Exception e){Debug.LogWarning(e);Alpha2PhysicsCheck.Finish(1);}
        }
        void StartRun()
        {
            int fps=run<2?60:run==2?30:90;Time.captureFramerate=fps;
            result=new RunResult{fps=fps,worldCenters=run!=0};report.runs.Add(result);
            preview.Place(new Vector3(0,.74f,0));avatar.transform.rotation=Quaternion.identity;head.localRotation=headRest;
            avatar.SetWorldSpaceHair(run!=0);preview.Interaction.BeginGrab("physics",out grab);
            preview.Interaction.MoveGrab("physics",grab,new Vec3(0,.94f,0));preview.RefreshSpatialState();avatar.ResetSecondaryMotion();
            timer=0;frame=0;videoFrame=0;stamp=EditorApplication.timeSinceStartup;
            if(run<2)Directory.CreateDirectory(Path.Combine(evidence,"motion-"+run));
        }
        void Update()
        {
            if(!ready)return;
            timer=frame/(float)result.fps;
            float x=0,yaw=0,headYaw=0;
            if(timer>=1&&timer<3)x=.12f*Mathf.Sin((timer-1)*Mathf.PI*2);
            else if(timer>=4&&timer<6)yaw=18*Mathf.Sin((timer-4)*Mathf.PI*2);
            else if(timer>=7&&timer<9)headYaw=18*Mathf.Sin((timer-7)*Mathf.PI*2);
            preview.Interaction.MoveGrab("physics",grab,new Vec3(x,.94f,0));preview.RefreshSpatialState();
            avatar.transform.rotation=Quaternion.Euler(0,yaw,0);head.localRotation=headRest*Quaternion.Euler(0,headYaw,0);
        }
        Vector3 Sample(Probe probe)
        {probe.renderer.BakeMesh(probe.baked,true);return avatar.transform.InverseTransformPoint(probe.renderer.transform.TransformPoint(probe.baked.vertices[probe.index]));}
        void LateUpdate()
        {
            if(!ready)return;
            try
            {
                avatar.Instance.Runtime.Process();
                double now=EditorApplication.timeSinceStartup;result.maxFrameMilliseconds=Mathf.Max(result.maxFrameMilliseconds,(float)(now-stamp)*1000);stamp=now;
                foreach(var joint in joints) {var p=joint.position;if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.z))throw new Exception("Nonfinite spring joint");}
                if(frame==result.fps-1)for(int i=0;i<probes.Count;i++)probes[i].baseline=Sample(probes[i]);
                if(timer>=1&&timer<=3.5f)
                {
                    for(int i=0;i<probes.Count;i++)result.hairSkirtTailMax[i]=Mathf.Max(result.hairSkirtTailMax[i],Vector3.Distance(Sample(probes[i]),probes[i].baseline));
                    for(int i=0;i<joints.Length;i++)result.maxBoneAngle=Mathf.Max(result.maxBoneAngle,Quaternion.Angle(jointRest[i],joints[i].localRotation));
                }
                if(run<2&&frame%2==0)
                {
                    if(run==1)Alpha2Probe.Capture(backCamera,Path.Combine(evidence,"motion-rear",videoFrame.ToString("D4")+".png"));
                    Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"motion-"+run,(videoFrame++).ToString("D4")+".png"));
                }
                frame++;
                if(timer>=11)
                {
                    for(int i=0;i<probes.Count;i++)result.settledOffset[i]=Vector3.Distance(Sample(probes[i]),probes[i].baseline);
                    if(++run<4)StartRun();else
                    {
                        File.WriteAllText(Path.Combine(evidence,"physics-ab.json"),JsonUtility.ToJson(report,true));
                        foreach(var probe in probes)Destroy(probe.baked);Time.captureFramerate=0;Alpha2PhysicsCheck.Finish(0);
                    }
                }
            }
            catch(Exception e){Debug.LogWarning(e);Time.captureFramerate=0;Alpha2PhysicsCheck.Finish(1);}
        }
    }
}
