using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UniGLTF.SpringBoneJobs;
using UniGLTF.SpringBoneJobs.Blittables;
using UniVRM10;
using UniVRM10.FastSpringBones;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LingChatXR.Editor {
 public static class InvertedPhysicsProbe {
  static IEnumerator run; static double deadline; static string output; static JArray rows=new JArray();
  public static void Run(){output=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");Directory.CreateDirectory(output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();}
  static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException();if(!EditorApplication.isPlaying)return;run??=Check();if(!run.MoveNext())Finish(0);}catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Finish(1);}}
  static IEnumerator Check(){
   var owner=new GameObject("Physics comparison");var a=owner.AddComponent<FeiyuAvatar>();a.MotionPreviewMode=true;a.FreezePresentation=true;
   var load=a.LoadAsync(Path.Combine(Application.streamingAssetsPath,"Characters/local-smallbluefish-alpha2"),.24f,CancellationToken.None);while(!load.IsCompleted)yield return null;load.GetAwaiter().GetResult();
   var ready=a.Motions.PrepareGrabbed();while(!ready.IsCompleted)yield return null;if(!ready.Result)throw new Exception("Grab motion missing");
   a.Instance.UpdateType=Vrm10Instance.UpdateTypes.None;
   var service=FastSpringBoneService.Instance;service.UpdateType=FastSpringBoneService.UpdateTypes.Manual;
   var camera=new GameObject("Comparison camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.19f;camera.nearClipPlane=.001f;camera.backgroundColor=new Color(.1f,.14f,.19f);camera.clearFlags=CameraClearFlags.SolidColor;
   new GameObject("Light").AddComponent<Light>().type=LightType.Directional;RenderSettings.ambientLight=Color.white;
   var joints=a.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>();var source=joints.Select(j=>j.Blittable).ToArray();
   foreach(string pose in new[]{"upright","inverted"}){
    a.ResizeUniform(.24f);a.Motions.SetGrabbed(false,true);a.SelectGrabPoint(pose=="inverted");if(pose=="inverted"){a.Motions.SetSpatialAllowed(false);a.Motions.SetGrabbed(true);for(int n=0;n<180;n++)a.Motions.Step(1f/60);}
    else a.Motions.Stop(true);
    a.Instance.Runtime.Process();
    foreach(string mode in new[]{"legacy","candidate"}){
     a.Physics.ConfigureMaterials(mode=="candidate"?a.Package.springPhysics:null);a.Physics.RequestReset();a.Physics.Prepare();
     if(mode!="candidate")for(int i=0;i<joints.Length;i++){
      var b=source[i];var scale=joints[i].transform.lossyScale;float radius=b.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
      a.Instance.Runtime.SpringBone.SetJointLevel(joints[i].transform,new BlittableJointMutable(mode=="gravity"?Mathf.Min(b.stiffnessForce,.65f):b.stiffnessForce,mode=="gravity"?Mathf.Max(b.gravityPower,1.5f):b.gravityPower,mode=="gravity"?new Unity.Mathematics.float3(0,-1,0):b.gravityDir,mode=="gravity"?Mathf.Min(b.dragForce,.65f):b.dragForce,radius,mode=="legacy"&&b.anglelimitType==AnglelimitTypes.None?(float)AnglelimitTypes.Cone:(float)b.anglelimitType,mode=="legacy"&&b.anglelimitType==AnglelimitTypes.None?30*Mathf.Deg2Rad:b.anglelimit1,b.anglelimit2,b.anglelimitOffset));
     }
     for(int n=0;n<240;n++){service.ManualUpdate(1f/60);if(n%30==0)yield return null;}
     var data=new JArray();foreach(var s in a.Instance.SpringBone.Springs){var first=s.Joints.First().transform;var last=s.Joints.Last().transform;data.Add(new JObject{["chain"]=first.name,["tipHeightFromRoot"]=last.position.y-first.position.y});}
     var cs=new JArray();foreach(var c in a.Instance.GetComponentsInChildren<VRM10SpringBoneCollider>())cs.Add(new JObject{["node"]=c.transform.name,["boneY"]=c.transform.position.y,["centerY"]=c.transform.TransformPoint(c.Offset).y,["radius"]=c.Radius});
     rows.Add(new JObject{["pose"]=pose,["mode"]=mode,["chains"]=data,["colliders"]=cs});
     var renderers=a.Instance.GetComponentsInChildren<SkinnedMeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
     var target=a.Instance.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position+(pose=="inverted"?Vector3.down:Vector3.up)*.04f;
     camera.transform.position=target+new Vector3(.15f,.035f,.6f);camera.transform.LookAt(target);yield return null;Capture(camera,Path.Combine(output,pose+"-"+mode+".png"));
     if(mode=="candidate"){
      foreach(float height in new[]{.12f,.24f,.45f})foreach(int fps in new[]{30,60,90}){
       a.ResizeUniform(height);float dt=1f/fps;owner.transform.position=Vector3.zero;a.Physics.RequestReset();a.Physics.Prepare();
       for(int n=0;n<fps*3;n++)service.ManualUpdate(dt);
       var rest=joints.Select(j=>j.transform.position).ToArray();int rebuilds=a.Physics.ScaleRebuildCount,colliderRebuilds=a.Physics.ColliderRebuildCount;
       var state=new LingChatXR.Core.PetInteraction(.03f);state.ConfirmPlane(default,1,1,default);state.BeginGrab("probe",out int g);
       float peak=0;
       for(int n=0;n<fps*5;n++){
        float t=n*dt;
        if(t<1)state.MoveGrab("probe",g,new LingChatXR.Core.Vec3(.035f*Mathf.Sin(t*12),.3f*t,0));
        if(n==fps)state.ReleaseGrabWithDescent("probe",g,default);
        if(n>=fps)state.StepDescent(dt,state.Generation);
        owner.transform.position=FeiyuPreview.ToUnity(state.Position);a.Physics.SetSpatialMotion(state,dt);a.Physics.Prepare();service.ManualUpdate(dt);
        peak=Mathf.Max(peak,a.Physics.MotionAirForce.magnitude);
        foreach(var joint in joints)if(!float.IsFinite(joint.transform.position.sqrMagnitude))throw new Exception("Non finite spring");
       }
       float residual=0;for(int n=0;n<joints.Length;n++)residual=Mathf.Max(residual,Vector3.Distance(joints[n].transform.position,rest[n]));
       if(residual>height*.01f)throw new Exception("Spring failed to settle within 1% of character height");
       if(a.Physics.ColliderRebuildCount!=colliderRebuilds)throw new Exception("Interaction rebuilt colliders");
       if(a.Physics.ScaleRebuildCount!=rebuilds)throw new Exception("Interaction rebuilt spring structure");
       rows.Add(new JObject{["pose"]=pose,["fps"]=fps,["height"]=height,["residualMm"]=residual*1000,["peakAirForce"]=peak,["interactionRebuilds"]=a.Physics.ScaleRebuildCount-rebuilds});
      }
      owner.transform.position=Vector3.zero;
     }
    }
   }
   a.ResizeUniform(.24f);owner.transform.position=Vector3.zero;a.Physics.ConfigureMaterials(a.Package.springPhysics);a.Physics.RequestReset();
   a.Motions.SetGrabbed(false,true);a.SelectGrabPoint(true);a.Motions.SetGrabbed(true);
   int beforeCycle=a.Physics.ColliderRebuildCount;
   for(int frame=0;frame<720;frame++){
    a.Motions.Step(1f/60);a.Instance.Runtime.Process();a.Physics.Prepare();service.ManualUpdate(1f/60);
    foreach(var joint in joints)if(!float.IsFinite(joint.transform.position.sqrMagnitude))throw new Exception("Cycle non finite spring");
    if(frame%120==119){var center=a.Instance.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position+Vector3.down*.04f;camera.transform.position=center+new Vector3(.15f,.035f,.6f);camera.transform.LookAt(center);yield return null;Capture(camera,Path.Combine(output,"cycle-"+(frame+1)+".png"));}
   }
   if(a.Physics.ColliderRebuildCount!=beforeCycle)throw new Exception("Cycle rebuilt colliders");
   a.Physics.ConfigureMaterials(null);a.Physics.Prepare();
   if(a.Physics.ConfiguredJointCount!=0)throw new Exception("Default restoration failed");
   rows.Add(new JObject{["fullMotionFrames"]=720,["colliderRebuildsDuringMotion"]=0,["defaultRestored"]=true});
  }
  static void Capture(Camera cam,string file){var rt=new RenderTexture(640,720,24);rt.Create();var old=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var t=new Texture2D(640,720,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,640,720),0,0);t.Apply();File.WriteAllBytes(file,t.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(t);}
  static void Finish(int code){EditorApplication.update-=Tick;File.WriteAllText(Path.Combine(output,"comparison.json"),new JObject{["passed"]=code==0,["visualAccepted"]=false,["rows"]=rows}.ToString());Debug.Log(code==0?"XR_M1_PASSED":"XR_PHYSICS_FAILED");EditorApplication.Exit(code);}
 }
}