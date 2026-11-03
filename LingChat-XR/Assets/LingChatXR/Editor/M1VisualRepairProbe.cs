using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UniGLTF;
using UniVRM10;

namespace LingChatXR.Editor
{
    // 对照导入时的真实 T 姿势，避免“坐标有限/骨骼在动”掩盖重定向基准错误。
    public static class M1VisualRepairProbe
    {
        static IEnumerator work; static double deadline; static readonly JArray results=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static void Check(bool ok,string name){results.Add(new JObject{["check"]=name,["passed"]=ok});if(!ok)throw new Exception(name);}
        static IEnumerator Frames(int count){for(int i=0;i<count;i++){int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;}}
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try {if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!EditorApplication.isPlaying)return;
                work??=Checks();if(!work.MoveNext()){File.WriteAllText(Path.Combine(Output,"pose-baseline.json"),results.ToString());Debug.Log("XR_M1_PASSED");EditorApplication.update-=Tick;EditorApplication.Exit(0);}}
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());EditorApplication.Exit(1);}
        }
        static IEnumerator Checks()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;
            while(!live.Ready)yield return null;
            var view=UnityEngine.Object.FindAnyObjectByType<XrChatView>();
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;}
                live.Place(new Vector3(0,.74f,0));var avatar=live.Avatar;avatar.FreezePresentation=true;
                var runtime=avatar.Instance.Runtime;var rig=runtime.ControlRig;
                foreach(var bone in rig.Bones.Values)bone.ControlBone.localRotation=Quaternion.identity;
                runtime.Process();
                var differences=new JObject();
                foreach(var pair in rig.Bones)differences[pair.Key.ToString()]=Quaternion.Angle(pair.Value.ControlTarget.localRotation,runtime.InitPose[pair.Value.ControlTarget].LocalRotation);
                results.Add(new JObject{["model"]=folder,["identityAgainstImportedTPoseDegrees"]=differences});
                File.WriteAllText(Path.Combine(Output,"pose-baseline.json"),results.ToString());
                Check(differences.Properties().Where(p=>!p.Name.Contains("Eye")).All(p=>(float)p.Value<.1f),"normalized-identity-is-imported-t-pose-"+folder);
                avatar.FreezePresentation=false;
                view.MotionWindow.Open();double start=EditorApplication.timeSinceStartup;
                while(view.MotionWindow.Preparing)yield return null;
                results.Add(new JObject{["model"]=folder,["previewSeconds"]=EditorApplication.timeSinceStartup-start});
                var clone=view.MotionWindow.PreviewAvatar;
                if(clone==null)throw new Exception("preview failed");
                var resources=avatar.ResourcesForPreview();
                File.WriteAllText(Path.Combine(Output,"import-profile-"+folder+".txt"),resources.LastPreviewImportProfile??"");
                Check(resources.Textures.Count>0,"source-textures-cached-"+folder);
                Check(!clone.Instance.GetComponent<RuntimeGltfInstance>().RuntimeResources.Any(x=>x.Item2 is Texture),"preview-decodes-no-duplicate-textures-"+folder);
                var mainMaterials=avatar.Instance.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).ToArray();
                var previewMaterials=clone.Instance.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).ToArray();
                Check(!mainMaterials.Intersect(previewMaterials).Any(),"preview-materials-independent-"+folder);
                Check(previewMaterials.Where(m=>m.HasProperty("_MainTex")&&m.GetTexture("_MainTex")!=null).All(m=>resources.Textures.Values.Contains(m.GetTexture("_MainTex"))),"preview-references-source-textures-"+folder);
                foreach(string id in new[]{"wave_normal_right_661","idle_normal_both_550","bow_normal_both_571"})
                {
                    view.MotionWindow.SelectAction(id,true);while(clone.Motions.Loading)yield return null;
                    for(int sample=1;sample<=(id=="wave_normal_right_661"?10:3);sample++)
                    {
                        float until=Time.unscaledTime+(id=="wave_normal_right_661"?.45f:2);while(Time.unscaledTime<until)yield return null;
                        view.CaptureTestUi(Path.Combine(Output,id+"-"+folder+"-"+sample+".png"));
                        var actionInstance=clone.Instance.GetComponentsInChildren<Vrm10AnimationInstance>().FirstOrDefault(a=>a.name.Contains(id));
                        if(actionInstance==null)actionInstance=clone.Instance.GetComponentsInChildren<Vrm10AnimationInstance>().LastOrDefault();
                        var bone=clone.Instance.Runtime.ControlRig.Bones[HumanBodyBones.RightUpperArm];
                        clone.Instance.Runtime.Process();
                        var initial=clone.Instance.Runtime.InitPose[bone.ControlTarget];
                        var expected=initial.LocalRotation*Quaternion.Inverse(initial.Rotation)*bone.ControlBone.localRotation*initial.Rotation;
                        Check(Quaternion.Angle(bone.ControlTarget.localRotation,expected)<.1f,"render-bone-matches-normalized-pose-"+folder+"-"+id+"-"+sample);
                        results.Add(new JObject{["action"]=id,["sample"]=sample,["model"]=folder,["status"]=clone.Motions.Status,
                            ["controlRotation"]=bone.ControlBone.localEulerAngles.ToString(),["targetRotation"]=bone.ControlTarget.localEulerAngles.ToString(),
                            ["sourceRotation"]=actionInstance?.ControlRig.Item1.GetNormalizedLocalRotation(HumanBodyBones.RightUpperArm,HumanBodyBones.RightShoulder).eulerAngles.ToString()});
                    }
                    clone.Motions.Stop(true);
                }
                var cached=clone;view.MotionWindow.Close();view.MotionWindow.Open();
                Check(view.MotionWindow.PreviewAvatar==cached&&!view.MotionWindow.Preparing,"reopen-without-import-"+folder);view.MotionWindow.Close();
                Check(resources.Textures.Values.All(t=>t!=null),"closing-preview-preserves-textures-"+folder);
                live.BeginSizeEdit(out int generation);live.PreviewSize(generation,.45f);
                var frames=Frames(8);while(frames.MoveNext())yield return null;
                var panel=view.GetComponent<UIDocument>().rootVisualElement;
                var handle=panel.Q("size-handle");var toolbar=panel.Q("size-toolbar");
                Check(!handle.worldBound.Overlaps(toolbar.worldBound),"resize-handle-clear-of-toolbar-"+folder);
                Check(panel.panel.Pick(handle.worldBound.center)==handle,"resize-handle-is-topmost-pick-"+folder);
                C0SettingsProbe.CaptureCombined(Path.Combine(Output,"resize-"+folder+".png"),view,live);
                live.CancelSizeEdit();
                foreach(var size in new[]{new Vector2(816,627),new Vector2(1600,900),new Vector2(480,800)})
                    foreach(var corner in new[]{new Vector2(-200,-100),new Vector2(size.x+200,12),new Vector2(size.x*.5f,size.y+100)})
                    {var rect=XrSizeEditView.HandleBounds(size,corner,90);Check(rect.xMin>=12&&rect.yMin>=12&&rect.xMax<=size.x-12&&rect.yMax<size.y-24-90,"offscreen-handle-remains-reachable-"+size+"-"+corner);}
            }
            // 在共享资源的异步预览导入中取消并切换主模型；两边释放完后不能留下旧资源。
            var previous=live.Avatar.ResourcesForPreview();var texture=previous.Textures.Values.First();
            var pendingObject=new GameObject("cancelled-preview-fixture");var pendingAvatar=pendingObject.AddComponent<FeiyuAvatar>();pendingAvatar.MotionPreviewMode=true;
            using var cancellation=new CancellationTokenSource();
            var pending=pendingAvatar.LoadAsync(Application.streamingAssetsPath+"/Characters/"+live.ActivePackageFolder,.24f,cancellation.Token,sharedSource:live.Avatar);
            var progress=Frames(3);while(progress.MoveNext())yield return null;
            cancellation.Cancel();UnityEngine.Object.Destroy(pendingObject);
            var switchTask=live.SwitchCharacterAsync("local-smallbluefish-alpha2");while(!switchTask.IsCompleted||!pending.IsCompleted)yield return null;
            Check(switchTask.Result&&pending.IsCanceled,"cancel-import-and-switch-model");
            progress=Frames(12);while(progress.MoveNext())yield return null;
            Check(previous.Textures.Count==0&&texture==null,"shared-resources-released-after-last-consumer");
        }
    }
}
