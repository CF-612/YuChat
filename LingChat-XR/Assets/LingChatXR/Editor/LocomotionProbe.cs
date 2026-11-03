using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class LocomotionProbe
    {
        static IEnumerator work;static int lastFrame=-1;static double deadline;static JArray checks=new JArray(),trajectories=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static string runtimeError;
        static void RuntimeLog(string message,string stack,LogType type){if(type==LogType.Exception)runtimeError=message+"\n"+stack;}
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
            Application.logMessageReceived+=RuntimeLog;
        }
        static void Tick()
        {
            try
            {
                if(runtimeError!=null)throw new Exception(runtimeError);
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Locomotion probe timeout");
                if(!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
                work??=RunChecks();if(work.MoveNext())return;
                File.WriteAllText(Path.Combine(Output,"walk.json"),new JObject{["checks"]=checks,["trajectories"]=trajectories,["questTested"]=false}.ToString());
                Debug.Log("XR_M1_PASSED");Application.logMessageReceived-=RuntimeLog;EditorApplication.update-=Tick;EditorApplication.Exit(0);
            }
            catch(Exception error){File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());Application.logMessageReceived-=RuntimeLog;Debug.LogException(error);EditorApplication.update-=Tick;EditorApplication.Exit(1);}
        }
        static void Check(bool value,string name){checks.Add(new JObject{["name"]=name,["passed"]=value});File.WriteAllText(Path.Combine(Output,"checks-in-progress.json"),checks.ToString());if(!value)throw new Exception(name);}
        static IEnumerator RunChecks()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;while(!live.Ready)yield return null;
            var walker=live.Locomotion;walker.ManualStep=true;
            var camera=live.ViewCamera;camera.orthographic=true;camera.orthographicSize=.31f;camera.transform.position=new Vector3(.36f,1.05f,.85f);camera.transform.LookAt(new Vector3(.02f,.88f,.02f));
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;yield return null;Check(change.Result,"换模型 "+folder);Check(!walker.Active,"换模型取消行走目标");}
                live.PlaceOnPlane(new Vector3(0,.74f,0),.55f,.42f,new Vector3(0,.74f,0),31);yield return null;
                var avatar=live.Avatar;avatar.FreezePresentation=true;avatar.GazeEnabled=false;var player=avatar.Motions;
                                var grabReady=player.PrepareGrabbed();while(!grabReady.IsCompleted)yield return null;
                Check(grabReady.Result,"用户 grabbed 动作载入 "+folder);
                player.SetSpatialAllowed(false);player.SetGrabbed(true);
                var grabArm=avatar.Instance.Runtime.ControlRig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var beforeGrab=grabArm.localRotation;
                for(int g=0;g<45;g++)player.Step(1f/60);
                Check(player.GrabbedActive&&Quaternion.Angle(beforeGrab,grabArm.localRotation)>2,"提起动作实际接管骨骼 "+folder);
                if(avatar.UsesMouth24Face)
                {
                    avatar.Instance.Runtime.Process();
                    var shot=new GameObject("提起复核").AddComponent<Camera>();shot.orthographic=true;shot.orthographicSize=.105f;shot.nearClipPlane=.01f;
                    var head=avatar.HeadGripBone.position;
                    shot.transform.position=head+avatar.transform.forward*.48f+avatar.transform.right*.085f-Vector3.up*.06f;
                    shot.transform.LookAt(head);shot.backgroundColor=new Color(.09f,.13f,.18f);shot.clearFlags=CameraClearFlags.SolidColor;
                    Alpha2Probe.Capture(shot,Path.Combine(Output,"held-oblique.png"));
                    var feet=(rigFoot(HumanBodyBones.LeftFoot)+rigFoot(HumanBodyBones.RightFoot))*.5f;
                    Vector3 rigFoot(HumanBodyBones key){avatar.Instance.TryGetBoneTransform(key,out var bone);return bone.position;}
                    var midpoint=(head+feet)*.5f;shot.orthographicSize=avatar.HeightMeters*.75f;shot.transform.position=midpoint+avatar.transform.right*.4f+avatar.transform.forward*.3f;shot.transform.LookAt(midpoint);
                    Alpha2Probe.Capture(shot,Path.Combine(Output,"held-full.png"));
                    Debug.Log("XR_HELD_ANGLE "+Vector3.Angle(head-feet,Vector3.up));
                    UnityEngine.Object.Destroy(shot);
                }
                for(int g=0;g<600;g++)player.Step(1f/60);
                Check(float.IsFinite(grabArm.localRotation.x),"提起跨循环保持有限姿态 "+folder);
                player.SetGrabbed(false);for(int g=0;g<30;g++)player.Step(1f/60);
                Check(!player.GrabbedActive,"松手退出提起动作 "+folder);
                player.SetGrabbed(true);player.Step(.1f);player.SetGrabbed(false,true);player.Step(.1f);
                Check(!player.GrabbedActive,"失追清除提起动作 "+folder);
                player.SetSpatialAllowed(true);
                var prepare=player.PrepareLocomotion();while(!prepare.IsCompleted)yield return null;Check(prepare.Result,"四段动作载入 "+folder);player.SetSpatialAllowed(true);
                var rig=avatar.Instance.Runtime.ControlRig;avatar.Instance.TryGetBoneTransform(HumanBodyBones.LeftFoot,out var left);avatar.Instance.TryGetBoneTransform(HumanBodyBones.RightFoot,out var right);
                foreach(float height in new[]{.12f,.24f,.45f})
                {
                    walker.Cancel(true);player.EndLocomotion(true);Check(live.BeginSizeEdit(out int edit),"开始尺寸事务");Check(live.PreviewSize(edit,height),"调整尺寸 "+height);Check(live.CommitSizeEdit(edit),"提交尺寸");
                    live.PlaceOnPlane(new Vector3(0,.74f,0),.55f,.42f,new Vector3(0,.74f,-.12f),31);player.SetSpatialAllowed(true);player.Step(0);avatar.Instance.Runtime.Process();yield return null;
                    // 以静止姿态的桌面高度标定踝下参考点，随足骨旋转。
                    // 它是支撑代理；鞋底形状、脚尖翻滚和蒙皮接触仍需看录像。
                    float tableY = live.Interaction.PlaneCenter.Y;
                    Vector3 leftSole = left.InverseTransformPoint(new Vector3(left.position.x, tableY, left.position.z));
                    Vector3 rightSole = right.InverseTransformPoint(new Vector3(right.position.x, tableY, right.position.z));
                    bool autonomy=live.Interaction.AutonomousEnabled;live.Interaction.SetAutonomousEnabled(true);Check(live.TryAutonomousPose(new Vector3(0,.74f,-.12f),Quaternion.Euler(0,-65,0),live.Interaction.Generation),"设置受控起始朝向");live.Interaction.SetAutonomousEnabled(autonomy);
                    var destination=new Vector3(.18f,.74f,.13f);var go=walker.GoTo(destination);while(!go.IsCompleted)yield return null;Check(go.Result,"开始行走 "+folder+" "+height);
                    Check(!awaitedBool(player.Play("nod")),"行走独占动作，不插入聊天动作");
                    var beginLeg=rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation;float swing=0,maxStep=0;Vector3 prior=FeiyuPreview.ToUnity(live.Interaction.Position);int frame=0;
                    var rows=new JArray();float modelSpeed=walker.NominalSpeed;int turnFrames=0;float maxTurnStep=0;
                    while(walker.Active&&frame<1800)
                    {
                        var beforeRotation=live.SpatialRotation;if(walker.State==PetLocomotion.Phase.Turning)turnFrames++;
                        walker.Step(1f/60);player.Step(1f/60);avatar.Instance.Runtime.Process();maxTurnStep=Mathf.Max(maxTurnStep,Quaternion.Angle(beforeRotation,live.SpatialRotation));
                        var position=FeiyuPreview.ToUnity(live.Interaction.Position);float step=Vector3.Distance(prior,position);maxStep=Mathf.Max(maxStep,step);prior=position;
                        CheckFinite(position);if(!live.Interaction.ContainsFootprint(live.Interaction.Position,live.Interaction.RadiusMeters))throw new Exception("行走越界");
                        swing=Mathf.Max(swing,Quaternion.Angle(beginLeg,rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation));
                        rows.Add(new JObject{["frame"]=frame,["phase"]=walker.State.ToString(),["speed"]=walker.Speed,["root"]=Vector(position),["leftFoot"]=Vector(left.position),["rightFoot"]=Vector(right.position),["leftSole"]=Vector(left.TransformPoint(leftSole)),["rightSole"]=Vector(right.TransformPoint(rightSole))});
                        if(Mathf.Approximately(height,.24f)&&frame%2==0)Capture(camera,folder+"-"+(frame/2).ToString("D4"));
                        frame++;yield return null;
                    }
                    Check(!walker.Active&&Vector3.Distance(FeiyuPreview.ToUnity(live.Interaction.Position),destination)<.001f,"到达指定目标 "+folder+" "+height);
                    var towardViewer=Vector3.ProjectOnPlane(camera.transform.position-destination,Vector3.up).normalized;
                    Check(Vector3.Angle(live.SpatialRotation*Vector3.forward,towardViewer)<12.1f,"到达后身体面向玩家 "+folder+" "+height);
                    Check(maxStep<=modelSpeed/60+.0001f,"连续位移而非瞬移");Check(swing>10,"腿部真实摆动 "+swing);
                    Check(turnFrames/60f>.3f&&turnFrames/60f<.8f,"约百度转身少于 0.8 秒且不是瞬转");Check(maxTurnStep<6,"转身连续无角度跳变");
                    for(int i=0;i<30;i++){player.Step(1f/60);avatar.Instance.Runtime.Process();if(Mathf.Approximately(height,.24f)&&i%2==0)Capture(camera,folder+"-"+((frame+1)/2+i/2).ToString("D4"));yield return null;}
                    Check(!player.LocomotionActive,"到达后退出步行，恢复当前待机");
                    trajectories.Add(new JObject{["model"]=folder,["height"]=height,["nominalSpeed"]=modelSpeed,["frames"]=frame,["turnSeconds"]=turnFrames/60f,["maxTurnStep"]=maxTurnStep,["maxStep"]=maxStep,["legSwing"]=swing,["samples"]=rows});
                    var outside=walker.GoTo(new Vector3(2,.74f,2));while(!outside.IsCompleted)yield return null;Check(!outside.Result,"拒绝平台外目标");
                }
                var forwardGoal=FeiyuPreview.ToUnity(live.Interaction.Position)+live.SpatialRotation*Vector3.forward*.04f;
                var smallTurn=walker.GoTo(forwardGoal);while(!smallTurn.IsCompleted)yield return null;
                Check(smallTurn.Result&&walker.State==PetLocomotion.Phase.Walking,"正前方目标直接起步不先插入转身");
                CheckGaze(avatar,true,"前走时目视前方 "+folder);walker.Cancel(true);CheckGaze(avatar,false,"取消后恢复看玩家 "+folder);
                var headRange=CheckHeadRange(avatar,folder);while(headRange.MoveNext())yield return headRange.Current;
                // 通过运行时 Update 验待机注意力，镜头到侧后方才转身；不做持续摄像机 billboard。
                var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
                camera.transform.position=FeiyuPreview.ToUnity(live.Interaction.Position)-live.SpatialRotation*Vector3.forward*.7f+Vector3.up*.4f;
                avatar.FreezePresentation=false;walker.ManualStep=false;walker.SendMessage("OnApplicationFocus",true);walker.SendMessage("OnApplicationPause",false);
                bool sawFacing=false;float until=Time.unscaledTime+3;
                while(Time.unscaledTime<until){sawFacing|=walker.State==PetLocomotion.Phase.Facing;if(sawFacing&&!walker.Active)break;yield return null;}
                Check(sawFacing&&!walker.Active,"玩家到背后时待机自动转身 "+folder);
                Check(Vector3.Angle(live.SpatialRotation*Vector3.forward,Vector3.ProjectOnPlane(camera.transform.position-FeiyuPreview.ToUnity(live.Interaction.Position),Vector3.up))<1,"待机转身实际朝向玩家");
                walker.ManualStep=true;avatar.FreezePresentation=true;camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                for(int i=0;i<30;i++)player.Step(1f/60);
                var request=walker.GoTo(new Vector3(-.18f,.74f,-.1f));while(!request.IsCompleted)yield return null;Check(request.Result,"打断前开始行走");
                CheckGaze(avatar,true,"转身时不盯玩家 "+folder);
                for(int i=0;i<20;i++){walker.Step(1f/60);player.Step(1f/60);avatar.Instance.Runtime.Process();yield return null;}
                Check(live.Interaction.BeginGrab("probe",out int generation),"行走中可抓取");walker.Step(1f/60);Check(!walker.Active,"抓起取消旧目标");
                live.Interaction.ReleaseGrab("probe",generation,live.Interaction.LastSafePosition);live.RefreshSpatialState();var stopped=live.Interaction.Position;
                for(int i=0;i<60;i++){walker.Step(1f/60);player.Step(1f/60);yield return null;}Check(Vector3.Distance(FeiyuPreview.ToUnity(stopped),FeiyuPreview.ToUnity(live.Interaction.Position))<.00001f,"放回后不追旧目标");
                player.SetSpatialAllowed(true);var next=walker.GoTo(new Vector3(-.16f,.74f,.1f));while(!next.IsCompleted)yield return null;Check(next.Result,"恢复后可选新目标");
                Check(live.BeginSizeEdit(out _),"行走中进入尺寸编辑");walker.Step(1f/60);Check(!walker.Active,"尺寸编辑取消旧目标");live.CancelSizeEdit();player.EndLocomotion(true);avatar.FreezePresentation=false;
                if(folder=="local-smallbluefish-alpha2")
                {
                    player.SetSpatialAllowed(true);var pending=walker.GoTo(new Vector3(-.2f,.74f,-.12f));while(!pending.IsCompleted)yield return null;Check(pending.Result,"换模型前有活动目标");
                }
            }
            // 使用实际桌面输入入口；UI 命中时不得透传，目标通过同一空间校验。
            live.Avatar.FreezePresentation=true;var motions=live.Avatar.Motions;motions.EndLocomotion(true);motions.SetSpatialAllowed(true);
            var ray=new Ray(new Vector3(-.22f,1.5f,.12f),Vector3.down);
            live.HandlePointer(ray,true,true,false,false,false,0);yield return null;Check(!walker.Active,"点击 UI 不会发起行走");
            live.HandlePointer(ray,false,true,false,false,false,0);for(int i=0;i<5;i++)yield return null;Check(walker.Active,"左键桌面进入实际行走入口");
            walker.Cancel();motions.EndLocomotion(true);Check(!walker.Active,"停下按钮使用的取消入口");
        }
        // 已载入目录后的拒绝分支应同步返回，不阻塞 Editor。
        static IEnumerator CheckHeadRange(FeiyuAvatar avatar,string folder)
        {
            avatar.Instance.TryGetBoneTransform(HumanBodyBones.Head,out var head);
            for(int i=0;i<90;i++){avatar.GazeEnabled=false;avatar.FreezePresentation=false;avatar.Present(head.position+avatar.transform.forward);avatar.Instance.Runtime.Process();avatar.FreezePresentation=true;yield return null;}
            var rest=head.localRotation;
            for(int i=0;i<90;i++){avatar.GazeEnabled=true;avatar.FreezePresentation=false;avatar.Present(head.position+avatar.transform.right*2+avatar.transform.forward*.3f);avatar.Instance.Runtime.Process();avatar.FreezePresentation=true;yield return null;}
            float angle=Quaternion.Angle(rest,head.localRotation);
            Check(angle>35&&angle<80,"实际头骨转角扩大且有上限 "+folder+" "+angle);
        }
        static void CheckGaze(FeiyuAvatar avatar,bool forward,string name)
        {
            avatar.Instance.TryGetBoneTransform(HumanBodyBones.Head,out var head);
            avatar.GazeEnabled=true;avatar.FreezePresentation=false;
            avatar.Present(head.position+avatar.transform.right*2+avatar.transform.forward*.3f);
            var local=avatar.transform.InverseTransformDirection(avatar.Instance.LookAtTarget.position-head.position).normalized;
            Check(forward?Vector3.Dot(local,Vector3.forward)>.999f:local.x>.25f,name);
            if(!forward)Check(local.x>.9f,"侧面玩家注视范围超过原 35 度");
            Check(avatar.GazeEnabled,"临时前视未覆盖用户视线偏好");avatar.FreezePresentation=true;
        }
        static bool awaitedBool(System.Threading.Tasks.Task<bool> task){if(!task.IsCompleted)throw new Exception("拒绝分支不应载入动作");return task.Result;}
        static void CheckFinite(Vector3 p){if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))throw new Exception("非有限坐标");}
        static JArray Vector(Vector3 p)=>new JArray(p.x,p.y,p.z);
        internal static void Capture(Camera camera,string name)
        {
            var target=new RenderTexture(960,720,24);target.Create();var old=camera.targetTexture;var active=RenderTexture.active;var texture=new Texture2D(960,720,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,960,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),texture.EncodeToPNG());}
            finally{camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(texture);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
