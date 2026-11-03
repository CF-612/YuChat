using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UniVRM10;
namespace LingChatXR.Editor
{
    public static class PetTouchProbe
    {
        static IEnumerator work;static int frame=-1;static double deadline;static JArray checks=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run(){EditorSceneManager.OpenScene(PreviewProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();}
        static void Check(bool ok,string name){checks.Add(new JObject{["name"]=name,["passed"]=ok});if(!ok)throw new Exception(name);}
        static void Tick()
        {
            try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;work??=Checks();if(work.MoveNext())return;File.WriteAllText(Path.Combine(Output,"checks.json"),checks.ToString());Debug.Log("XR_M1_PASSED");EditorApplication.update-=Tick;EditorApplication.Exit(0);}
            catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.update-=Tick;EditorApplication.Exit(1);}
        }
        public static void RunSpeechFace(){work=SpeechFace();Run();}
        static IEnumerator SpeechFace()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;
            while(!live.Ready)yield return null;
            live.Place(Vector3.zero);yield return null;
            var a=live.Avatar;var core=live.Interaction;var voice=UnityEngine.Object.FindAnyObjectByType<Chat.XrChatController>().Voice;
            var clip=Resources.Load<AudioClip>("LocalLingChatEffects/faceSample");
            var cam=new GameObject("表情录屏").AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=a.HeightMeters*.73f;
            var focus=a.transform.position+Vector3.up*a.HeightMeters*.67f;
            cam.transform.position=focus+a.transform.forward*.7f+Vector3.up*.08f;cam.transform.LookAt(focus);cam.nearClipPlane=.01f;
            var trace=new JArray();int token=0;float start=Time.realtimeSinceStartup;bool held=false,released=false,petStarted=false;
            var basePos=core.Position;float heldPeak=0;bool pauseOwned=false,shortShock=false;
            Check(clip!=null,"样句资源可读");
            voice.Stop();clip.LoadAudioData();while(clip.loadState==AudioDataLoadState.Loading)yield return null;
            start=Time.realtimeSinceStartup;Check(voice.PlayReaction(clip),"离线实际语音起播 "+voice.LastStatus+" "+clip.loadState);
            float endAt=-1;
            while(endAt<0||Time.realtimeSinceStartup-endAt<.7f)
            {
                if(!voice.IsPlaying && endAt<0)endAt=Time.realtimeSinceStartup;
                if(Time.realtimeSinceStartup-start>30)throw new TimeoutException("录屏时钟超时");
                float t=endAt<0?voice.PlaybackSeconds:clip.length+Time.realtimeSinceStartup-endAt;
                if(t>.2f&&!petStarted){petStarted=core.BeginTouch("face-probe");live.RefreshSpatialState();}
                if(t>.85f&&!held){held=core.BeginGrab("face-probe",out token);a.PreviewEmotion="害羞";}
                if(held&&!released){core.MoveGrab("face-probe",token,new Core.Vec3(basePos.X,basePos.Y+Mathf.Min(.07f,(t-.85f)*.25f),basePos.Z));live.RefreshSpatialState();}
                if(held&&!released&&t>1f)a.PreviewEmotion="心动";
                if(held&&!released&&t>4.4f){core.ReleaseGrabWithDescent("face-probe",token,core.LastSafePosition);released=true;live.RefreshSpatialState();}
                var e=a.Instance.Runtime.Expression;float aa=e.GetWeight(ExpressionKey.Aa);
                if(t>1.35f&&t<4.3f)heldPeak=Mathf.Max(aa,heldPeak);
                pauseOwned|=t>2.68f&&t<3.08f&&a.MouthOwner=="角色语音"&&aa<.03f;
                shortShock|=held&&!released&&e.GetWeight(ExpressionKey.CreateCustom("mouthOpenCircle"))>.9f;
                var name="frame-"+trace.Count.ToString("D4")+".png";
                trace.Add(new JObject{["time"]=t,["aa"]=aa,["mouth"]=a.MouthOwner,["state"]=core.State.ToString(),["file"]=name});
                Alpha2Probe.Capture(cam,Path.Combine(Output,name));yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"timeline.json"),trace.ToString());
            Check(shortShock&&heldPeak>.025f,"提起惊讶后仍持有并重新发音");
            Check(pauseOwned,"句内停顿嘴合上仍由音频占用");
                        Check(core.State==Core.PetState.Idle&&a.PreviewEmotion=="心动"&&a.MouthOwner=="当前心情","落稳恢复最新表情");
            Check(voice.PlayReaction(clip),"再次起播");long prior=voice.Generation;
            voice.Stop();Check(voice.PlayReaction(clip),"旧音频停止后新样句起播");
            for(int i=0;i<4;i++)yield return null;
            Check(voice.Generation>prior&&voice.IsPlaying&&a.MouthOwner=="角色语音","旧播放周期不会清除新语音");
            voice.Stop();
            var change=live.SwitchCharacterAsync("local-physics-reference");while(!change.IsCompleted)yield return null;
            Check(change.Result&&!live.Avatar.UsesMouth24Face&&live.Avatar.PreviewEmotion==null,"旧鱼使用标准回退并清空旧手动心情");
        }
        public static void RunOral(){work=OralComparison();Run();}
        static IEnumerator OralComparison()
        {
            var bytes=File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,"Characters/local-smallbluefish-alpha2/model.vrm"));
            var task=Vrm10.LoadBytesAsync(bytes);
            while(!task.IsCompleted)yield return null;
            var model=task.GetAwaiter().GetResult();
            model.transform.position=Vector3.right*20;
            var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var cavity=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Single(m=>m.name.Contains("FOUNDATION_OralCavity"));
            var tongue=renderers.First(r=>r.sharedMaterials.Any(m=>m.name.Contains("FOUNDATION_Tongue")));
            Check(cavity.HasProperty("_RenderQueueOffset")&&cavity.GetInt("_RenderQueueOffset")==0,"原始暗红口腔偏移为零");
            var camera=new GameObject("口腔固定机位").AddComponent<Camera>();
            camera.orthographic=true;camera.nearClipPlane=.001f;camera.clearFlags=CameraClearFlags.SolidColor;
            int originalQueue=cavity.renderQueue;
            foreach(string pose in new[]{"mouthOpenHappy","mouthOpenCircle","mouthOpenLaugh"})
            {
                var key=ExpressionKey.CreateCustom(pose);model.Runtime.Expression.SetWeight(key,1);model.Runtime.Process();
                var mesh=new Mesh();tongue.BakeMesh(mesh);mesh.RecalculateBounds();
                var center=tongue.transform.TransformPoint(mesh.bounds.center);
                float width=tongue.transform.TransformVector(Vector3.right*mesh.bounds.size.x).magnitude;
                camera.orthographicSize=Mathf.Max(.01f,width*1.5f);
                camera.transform.position=center+Vector3.forward*2;camera.transform.LookAt(center);
                try
                {
                    foreach(string phase in new[]{"zero","minus2","restored"})
                    {
                        int offset=phase=="minus2"?-2:0;
                        cavity.SetInt("_RenderQueueOffset",offset);
                        new VRM10.MToon10.MToonValidator(cavity).Validate();
                        camera.Render();camera.Render();
                        Alpha2Probe.Capture(camera,Path.Combine(Output,pose+"-"+phase+".png"));
                        Debug.Log("ORAL_QUEUE "+pose+" "+offset+" => "+cavity.renderQueue);
                    }
                }
                finally{cavity.SetInt("_RenderQueueOffset",0);new VRM10.MToon10.MToonValidator(cavity).Validate();}
                Check(cavity.renderQueue==originalQueue,"队列还原 "+pose);
                model.Runtime.Expression.SetWeight(key,0);UnityEngine.Object.Destroy(mesh);
            }
        }
        static IEnumerator Checks()
        {
            var s=new PetTouchSession();s.Begin(0,false);s.Tick(.05f,true);s.Move(Vector3.zero,.24f);s.Move(Vector3.right*.01f,.24f);s.End(.08f,false,false);
            Check(s.CompletedCount==1&&s.LastCompleted.duringSpeech&&s.LastCompleted.distance>.009f,"会话结束摘要含接触路程和说话状态");
            s.End(.1f,false,false);Check(s.CompletedCount==1,"重复结束不重复提交");s.Begin(1,false);s.End(1.01f,false,false);Check(s.CompletedCount==1,"过滤不足40毫秒误触");s.Begin(2,false);s.Tick(2.1f,false);s.End(2.1f,false,true);Check(s.CompletedCount==1&&!s.Active,"取消不产生完成事件");
                        s.Begin(3,false);s.Move(Vector3.zero,.24f);s.Contact(false,3.1f,false);s.Tick(5,false);s.Move(Vector3.right,.24f);s.Contact(true,5,false);s.Move(Vector3.left,.24f);s.Move(Vector3.left+Vector3.up*.01f,.24f);s.End(5.1f,false,false);
            Check(Mathf.Abs(s.LastCompleted.duration-.2f)<.001f&&Mathf.Abs(s.LastCompleted.distance-.01f)<.001f,"移出重入不计空白距离和时间");
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;while(!live.Ready)yield return null;
            live.Locomotion.ManualStep=true;
            foreach(var folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var task=live.SwitchCharacterAsync(folder);while(!task.IsCompleted)yield return null;Check(task.Result,"切模型 "+folder);}
                live.PlaceOnPlane(new Vector3(0,.74f,0),.55f,.42f,new Vector3(0,.74f,0),0);yield return null;
                var a=live.Avatar;a.FreezePresentation=true;var scale=a.Instance.transform.localScale;var pos=a.Instance.transform.localPosition;float height=a.HeightMeters;
                a.Physics.Prepare();int rebuilds=a.Physics.ScaleRebuildCount;
                Check(a.TouchSound.Ready,"原版pet音效可用 "+folder);
                a.SetFeedback(true,false);var point=a.TouchProxy.transform.position;
                for(int i=0;i<45;i++){a.SampleTouch(point-Vector3.up*Mathf.Min(i,20)*height*.001f);a.FreezePresentation=false;a.Present(live.ViewCamera.transform.position);a.Instance.Runtime.Process();a.FreezePresentation=true;yield return null;}
                Check(a.TouchCompression>.005f&&a.TouchCompression<=.065f,"下压有界弹性 "+folder);
                var petKey=a.UsesMouth24Face?ExpressionKey.CreateCustom("eyeSmile"):ExpressionKey.Blink;
                Check(a.Instance.Runtime.Expression.GetWeight(petKey)>.9f,"抚摸闭眼 "+folder);
                Check(a.TouchSound.Pulses>0,"有效接触实际启动短音 "+folder);
                Check(a.HeightMeters==height,"弹性不写身高设置 "+folder);
                var camera=live.ViewCamera;camera.orthographic=true;camera.orthographicSize=height*.34f;var face=a.TouchProxy.transform.position;var direction=(camera.transform.position-face).normalized;camera.transform.position=face+direction*.65f;camera.transform.LookAt(face);
                LocomotionProbe.Capture(live.ViewCamera,folder+"-stroke");
                var lids=a.Instance.Runtime.Expression;float blink=lids.GetWeight(petKey);
                if(a.UsesMouth24Face)Check(lids.GetWeight(ExpressionKey.CreateCustom("browHappy"))==1&&lids.GetWeight(ExpressionKey.CreateCustom("cheekSlashes"))==0&&lids.GetWeight(ExpressionKey.CreateCustom("mouthSmile"))==.5f&&lids.GetWeight(ExpressionKey.Blink)==0,"摸头调用作者指定组合");
                a.SetFeedback(false,false);
                float holdUntil=Time.unscaledTime+.25f;
                while(Time.unscaledTime<holdUntil){a.FreezePresentation=false;a.Present(camera.transform.position);a.FreezePresentation=true;yield return null;}
                Check(lids.GetWeight(petKey)>=blink-.01f,"相邻抚摸间眼睑保持 "+folder);
                a.SetFeedback(true,false);a.SampleTouch(point);var pressedPoint=point-Vector3.up*height*.12f;
                float pressureUntil=Time.unscaledTime+1.7f;
                while(Time.unscaledTime<pressureUntil){a.SampleTouch(pressedPoint);a.FreezePresentation=false;a.Present(camera.transform.position);a.Instance.Runtime.Process();a.FreezePresentation=true;yield return null;}
                Check(a.TouchSound.PlayingMiddle,"持续按压进入原速中段循环 "+folder);
                Check(a.TouchSound.PressureStarts==1,"持续按压长音只起音一次 "+folder);
                Check(a.TouchCompression>.045f,"手指停住仍持续压扁 "+folder);
                if(a.UsesMouth24Face)Check(lids.GetWeight(ExpressionKey.CreateCustom("eyeTroubled"))==1&&lids.GetWeight(ExpressionKey.CreateCustom("mouthOpenCircle"))==1&&lids.GetWeight(petKey)==0&&lids.GetWeight(ExpressionKey.Aa)==0,"挤压独占眼口通道");
                LocomotionProbe.Capture(camera,folder+"-pressed");
                a.SetFeedback(false,false); bool rebounded=false;
                for(int i=0;i<100;i++){a.FreezePresentation=false;a.Present(live.ViewCamera.transform.position);rebounded|=a.TouchCompression<-.0001f;a.Instance.Runtime.Process();a.FreezePresentation=true;yield return null;}
                Check(a.TouchSound.PressureReleases==1,"松压只播一次原始弹性尾音 "+folder);
                Check(a.Physics.ScaleRebuildCount==rebuilds,"轻抚持续按压及回弹不重建骨链 "+folder);
                a.ResizeUniform(height+.01f);a.Physics.Prepare();a.Physics.Prepare();
                Check(a.Physics.ScaleRebuildCount==rebuilds+1,"身高编辑重建一次 "+folder);
                a.ResizeUniform(height);a.Physics.Prepare();
                Check(rebounded,"释放有反向回弹 "+folder);Check(Mathf.Abs(a.TouchCompression)<.001f,"释放弹性收稳 "+folder);
                Check(a.TouchSession.CompletedCount==2,"两次接触分别提交 "+folder);
                Check(Vector3.Distance(scale,a.Instance.transform.localScale)<.001f&&Vector3.Distance(pos,a.Instance.transform.localPosition)<.001f,"释放恢复外观尺度和位置 "+folder);
                int pulses=a.TouchSound.Pulses;a.TouchSound.Tick(false,Time.unscaledTime+10,1,true);Check(a.TouchSound.Pulses==pulses,"结束不补迟到音效 "+folder);
                a.SetFeedback(true,false);a.SampleTouch(point);a.CancelTouch();Check(!a.TouchSession.Active&&!a.TouchSound.Playing&&a.TouchCompression==0,"取消即时清理会话音效和弹性 "+folder);
                a.TouchSound.Tick(true,100,0,true);Check(a.TouchSound.Pulses==pulses,"静音不启动短音 "+folder);a.TouchSound.Cancel();
                a.TouchSound.Tick(true,101,1,false);Check(!a.TouchSound.Playing,"音效关闭生效 "+folder);
                int starts=a.TouchSound.PressureStarts, releases=a.TouchSound.PressureReleases;
                a.TouchSound.Tick(true,105,1,true,true);
                a.TouchSound.Tick(false,105.1f,1,true);
                a.TouchSound.Tick(false,105.2f,1,true);
                Check(!a.TouchSound.PlayingMiddle && a.TouchSound.PressureStarts==starts+1 && a.TouchSound.PressureReleases==releases+1,"提前松手取消中段只播一次尾音 "+folder);
                a.FreezePresentation=false;
            }
        }
    }
}
