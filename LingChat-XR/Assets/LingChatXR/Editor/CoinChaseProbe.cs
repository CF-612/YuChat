#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using LingChatXR.Chat;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LingChatXR.Core;
using Object=UnityEngine.Object;

namespace LingChatXR.Editor
{
    public static class CoinChaseProbe
    {
        static IEnumerator work;
        static int lastFrame=-1;
        static double deadline;
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        [MenuItem("YuChat/玩具箱/PC 硬币追逐")]
        static void OpenPlaytest()
        {
            if(EditorApplication.isPlaying)return;
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorApplication.EnterPlaymode();
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SceneManager.sceneLoaded+=Select;deadline=EditorApplication.timeSinceStartup+450;
            EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Select(Scene scene,LoadSceneMode mode)=>Object.FindAnyObjectByType<FeiyuPreview>().ConfigureCharacter("local-smallbluefish-alpha2",true);
        static void Tick()
        {
            try{
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException();
                if(!EditorApplication.isPlaying||lastFrame==Time.frameCount)return;
                lastFrame=Time.frameCount;work??=Play();
                if(work.MoveNext())return;
                Debug.Log("XR_M1_PASSED coin chase");End(0);
            }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());End(1);}
        }
        static void End(int result){SceneManager.sceneLoaded-=Select;EditorApplication.update-=Tick;EditorApplication.Exit(result);}
        static IEnumerator Play()
        {
            var live=Object.FindAnyObjectByType<FeiyuPreview>();
            while(!live.Ready)yield return null;
            live.ManualInput=true;
            if(!live.PlaceOnPlane(new Vector3(0,.74f,0),.59f,.43f,new Vector3(0,.74f,0)))throw new Exception("放置失败");
            var model=live.Avatar;var camera=live.ViewCamera;var coin=live.GetComponent<PcCoinChase>();
            while(!live.Locomotion.Ready)yield return null;
            model.Motions.EndWelcome();var core=live.Interaction;
            Vector3 origin=FeiyuPreview.ToUnity(core.Position);
            var forward=live.SpatialRotation*Vector3.forward;var right=live.SpatialRotation*Vector3.right;
            camera.orthographic=true;camera.orthographicSize=.30f;
            var center=origin+forward*.11f+Vector3.up*.09f;
            camera.transform.position=center+forward*.64f+right*.29f+Vector3.up*.51f;camera.transform.LookAt(center);
            var view=Object.FindAnyObjectByType<XrChatView>();
            view.SetDialogueCollapsed(true);
            var ui=view.GetComponent<UIDocument>().rootVisualElement;
            void Click(string name)
            {
                var button=ui.Q<Button>(name);
                if(button==null || !button.enabledInHierarchy || button.panel.Pick(button.worldBound.center)!=button)
                    throw new Exception("按钮未显示或被遮挡："+name);
                using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
            }
            Ray MouseAt(Vector3 point)=>new Ray(point+Vector3.up,Vector3.down);
            const int fps=20;Time.captureFramerate=fps;
            var target=new RenderTexture(832,624,24);target.Create();
            var texture=new Texture2D(832,624,TextureFormat.RGB24,false);
            if(Environment.GetEnvironmentVariable("XR_COIN_GRIP_REVIEW")=="1")
            {
                var review=ReviewGrip(live,coin,camera,target,texture);
                while(review.MoveNext())yield return review.Current;
                Time.captureFramerate=0;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);
                yield break;
            }
            if(Environment.GetEnvironmentVariable("XR_COIN_HUG")=="1")
            {
                coin.Take();
                var initial=FeiyuPreview.ToUnity(core.Position);
                float peakHeight=0;int heldFrames=0;
                var hugFrames=Path.Combine(Output,"frames");Directory.CreateDirectory(hugFrames);
                var approachTime=EditorApplication.timeSinceStartup;
                model.Instance.TryGetBoneTransform(HumanBodyBones.LeftHand,out var left);
                model.Instance.TryGetBoneTransform(HumanBodyBones.RightHand,out var rightHand);
                model.Instance.TryGetBoneTransform(HumanBodyBones.LeftFoot,out var footL);
                model.Instance.TryGetBoneTransform(HumanBodyBones.RightFoot,out var footR);
                model.Instance.TryGetBoneTransform(HumanBodyBones.Hips,out var pelvis);
                var poses=new List<string>{"frame,action,inArms,leftDist,rightDist,height,leftFoot,rightFoot,hip"};
                for(int frame=0;heldFrames<fps*3 && EditorApplication.timeSinceStartup-approachTime<55;frame++)
                {
                    yield return null;
                    if(coin.InArms){
                        heldFrames++;peakHeight=Mathf.Max(peakHeight,coin.CoinPosition.y-core.PlaneCenter.Y);
                        if(!model.Motions.HoldingToyPose)throw new Exception("抱币动作失去所有权");
                    }
                    poses.Add(FormattableString.Invariant($"{frame},{model.Motions.ActiveId},{coin.InArms},{Vector3.Distance(left.position,coin.CoinPosition):F4},{Vector3.Distance(rightHand.position,coin.CoinPosition):F4},{coin.CoinPosition.y-core.PlaneCenter.Y:F4},{footL.position.y-core.PlaneCenter.Y:F4},{footR.position.y-core.PlaneCenter.Y:F4},{pelvis.position.y-core.PlaneCenter.Y:F4}"));
                    var subject=FeiyuPreview.ToUnity(core.Position)+Vector3.up*model.HeightMeters*.47f;
                    camera.orthographicSize=model.HeightMeters*.83f;
                    camera.transform.position=subject+forward*.55f+right*.21f+Vector3.up*.15f;camera.transform.LookAt(subject);
                    if(heldFrames==fps || heldFrames==fps*2){
                        var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;
                        var savedSize=camera.orthographicSize;
                        var focus=FeiyuPreview.ToUnity(core.Position)+Vector3.up*model.HeightMeters*.48f;
                        camera.orthographicSize=model.HeightMeters*.69f;
                        camera.transform.position=focus+live.SpatialRotation*new Vector3(heldFrames==fps?.12f:.65f,.09f,.53f);
                        camera.transform.LookAt(focus);
                        LocomotionProbe.Capture(camera,heldFrames==fps?"hug-front":"hug-side");
                        camera.orthographicSize=savedSize;camera.transform.SetPositionAndRotation(savedPosition,savedRotation);
                    }
                    var priorTarget=camera.targetTexture;var priorActive=RenderTexture.active;
                    camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,832,624),0,0);texture.Apply();camera.targetTexture=priorTarget;RenderTexture.active=priorActive;
                    File.WriteAllBytes(Path.Combine(hugFrames,$"{frame:D4}.jpg"),texture.EncodeToJPG(85));
                }
                File.WriteAllLines(Path.Combine(Output,"poses.csv"),poses);
                string summaryHug=$"holdFrames={heldFrames} contactGap={coin.ContactGap:F4} peakHeight={peakHeight:F4} travel={Vector3.Distance(initial,FeiyuPreview.ToUnity(core.Position)):F4} status={coin.Status} motion={model.Motions.Status}";
                File.WriteAllText(Path.Combine(Output,"hug.txt"),summaryHug);
                if(heldFrames<fps*2 || peakHeight<.035f)throw new Exception(summaryHug);
                var grip=model.GrabProxy.bounds.center;
                var grabRay=new Ray(camera.transform.position,grip-camera.transform.position);
                live.HandlePointer(grabRay,false,false,false,true,false,0);
                if(core.State!=PetState.Held || coin.InArms || model.Motions.HoldingToyPose)throw new Exception("提起未打断抱币");
                live.HandlePointer(grabRay,false,false,false,false,true,0);
                coin.Store();
                while(core.State!=PetState.Idle)yield return null;
                var prefs=model.Motions.Preferences;
                var disabled=Newtonsoft.Json.Linq.JObject.FromObject(prefs).ToObject<XrMotionPreferences>();
                disabled.items["ual2_walk_carry_loop"]=false;
                model.Motions.ApplyPreferences(disabled);
                coin.Take();
                for(int i=0;i<fps*5;i++){
                    yield return null;
                    if(coin.InArms || model.Motions.HoldingToyPose)throw new Exception("关闭搬运动作后仍取得硬币");
                }
                coin.Store();model.Motions.ApplyPreferences(prefs);
                coin.Take();
                double timeout=EditorApplication.timeSinceStartup+25;
                while(!model.Motions.HoldingToyPose && EditorApplication.timeSinceStartup<timeout)yield return null;
                if(!model.Motions.HoldingToyPose || coin.InArms)throw new Exception("未进入接触前伸手阶段");
                coin.Store();
                for(int i=0;i<fps;i++)yield return null;
                if(coin.Deployed || coin.InArms || model.Motions.HoldingToyPose)throw new Exception("提前收回后旧抱币动作未撤销");
                File.AppendAllText(Path.Combine(Output,"hug.txt")," grabInterrupt=True deniedMotion=True storeDuringReach=True");
                Time.captureFramerate=0;
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);yield break;
            }
            var frames=Path.Combine(Output,"frames");Directory.CreateDirectory(frames);
            var rows=new List<string>{"frame,state,status,searches,actorX,actorZ,coinX,coinZ,seenX,seenZ,move"};
            Vector3 throwStart=default,covered=default,hidden=default,memory=default,road=default;
            bool chased=false,blocked=false,revealed=false,grabbed=false,landed=false,edge=false,safe=true,memoryHeld=true;
            int searchStart=0,stickerStart=0;bool singleSearch=false,detour=false,noPath=false,grabDuringTravel=false;Ray holdRay=default;
            for(int i=0;i<fps*50;i++){
                Vector3 feet=FeiyuPreview.ToUnity(core.Position);
                if(i==8)Click("toy-box-toggle");
                if(i==20)Click("toy-box-take");
                if(i==28){var same=coin.transform.Find("Token 硬币");Click("toy-box-take");if(coin.transform.Find("Token 硬币")!=same)throw new Exception("重复取出未复用");}
                if(i==40)C0SettingsProbe.CaptureCombined(Path.Combine(Output,"toybox.png"),view,live);
                if(i==45)Click("toy-box-toggle");
                if(i==58)throwStart=coin.CoinPosition;
                if(i==58 && !coin.HandlePointer(MouseAt(throwStart),false,true,false,false))throw new Exception($"桌面鼠标按下失败 部署={coin.Deployed} 状态={core.State} 焦点={XrChatView.HasTextFocus} 点={throwStart}");
                if(i==60 && !coin.HandlePointer(MouseAt(throwStart+forward*.05f+right*.12f),false,false,true,false))throw new Exception("拖放滚币失败");
                if(i==160){
                    Vector3 goal=default;bool found=false;
                    foreach(float yaw in new[]{0f,25f,-25f,50f,-50f,70f,-70f})
                    {
                        goal=feet+live.SpatialRotation*Quaternion.Euler(0,yaw,0)*Vector3.forward*.37f;
                        if(core.ContainsFootprint(FeiyuPreview.ToCore(goal),.05f)){found=true;break;}
                    }
                    if(!found)throw new Exception("缺少合法、可见的挡路演示位置");
                    coin.Roll(goal,Vector3.zero);road=Vector3.Lerp(feet,goal,.41f);
                }
                if(i>=161&&i<195)coin.Hand(true,feet+live.SpatialRotation*Vector3.forward*.015f);
                if(i>=195&&i<240)coin.Hand(true,road);
                if(i==240)coin.Hand(false,default);
                if(i==293)coin.Hand(false,default);
                if(i==320){covered=coin.CoinPosition;hidden=feet+live.SpatialRotation*new Vector3(.19f,0,.065f);
                    if(!core.ContainsFootprint(FeiyuPreview.ToCore(hidden),.03f))hidden=feet+live.SpatialRotation*new Vector3(-.19f,0,.065f);
                    hidden.x=Mathf.Clamp(hidden.x,-.55f,.55f);hidden.z=Mathf.Clamp(hidden.z,-.39f,.39f);searchStart=coin.Searches;stickerStart=model.GetComponentInChildren<MoodSticker>().Starts;}
                if(i>=320&&i<540)coin.HandlePointer(MouseAt(Vector3.Lerp(covered,hidden,Mathf.Clamp01((i-370)/100f))),false,false,false,true);
                if(i==356)memory=coin.LastSeen;
                if(i>=357 && i<540)memoryHeld &= coin.Searching && (memory-coin.LastSeen).sqrMagnitude<.000001f;
                if(i==540){singleSearch=coin.Searches==searchStart+1 && model.GetComponentInChildren<MoodSticker>().Starts==stickerStart+1;coin.Hand(false,default);}
                if(i==551){
                    grabDuringTravel=live.Locomotion.Active;
                    var point=model.GrabProxy.bounds.center;var ray=new Ray(camera.transform.position,point-camera.transform.position);
                    holdRay=ray;live.HandlePointer(ray,false,false,false,true,false,0);grabbed=core.State==PetState.Held && !live.Locomotion.Active && !coin.Searching;
                }
                if(i>551&&i<669 && core.State==PetState.Held){
                    live.HandlePointer(holdRay,false,false,false,false,false,i<561?.32f:0);
                }
                if(i==669){live.HandlePointer(holdRay,false,false,false,false,true,0);}
                if(i==800){
                    coin.Take();var direction=live.SpatialRotation*Vector3.forward;
                    var edgePoint=feet;
                    while(core.ContainsFootprint(FeiyuPreview.ToCore(edgePoint+direction*.01f),coin.Radius+.005f))edgePoint+=direction*.01f;
                    coin.Roll(edgePoint,direction*.34f);
                }
                if(i==900)coin.Take();
                if(i==935)Click("toy-box-toggle");
                if(i==950)Click("toy-box-store");
                yield return null;
                feet=FeiyuPreview.ToUnity(core.Position);
                if(core.State==PetState.Idle)safe &=core.ContainsFootprint(core.Position,core.RadiusMeters);
                if(i<160)chased |= live.Locomotion.Active && (feet-origin).magnitude>.015f;
                if(i>=195 && i<240)detour |=coin.Status=="绕开手掌";
                if(i>=161 && i<195)noPath |=coin.Status=="手掌挡路 · 等待";
                if(i>=160&&i<293)blocked |= coin.Status=="绕开手掌"||coin.Status=="手掌挡路 · 等待";
                if(i>=540&&i<610)revealed |= !coin.Searching && live.Locomotion.Active && Vector3.Distance(coin.LastSeen,hidden)<.06f;
                if(i>=685&&i<800)landed |=core.State==PetState.Idle;
                if(i>=800&&i<900)edge |= coin.Status=="硬币出界 · 可取回" && !core.ContainsFootprint(FeiyuPreview.ToCore(coin.CoinPosition),.001f);
                var pos=coin.Deployed?coin.CoinPosition:Vector3.zero;
                rows.Add(FormattableString.Invariant($"{i},{core.State},{coin.Status},{coin.Searches},{feet.x:F4},{feet.z:F4},{pos.x:F4},{pos.z:F4},{coin.LastSeen.x:F4},{coin.LastSeen.z:F4},{live.Locomotion.State}"));
                var focus=feet+Vector3.up*.08f;
                if(coin.Deployed && Vector3.Distance(feet,pos)<.65f)focus=Vector3.Lerp(focus,pos+Vector3.up*.07f,.35f);
                center=Vector3.Lerp(center,focus,.09f);
                camera.transform.position=center+forward*.64f+right*.29f+Vector3.up*.51f;camera.transform.LookAt(center);
                var old=RenderTexture.active;var prior=camera.targetTexture;
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,832,624),0,0);texture.Apply();camera.targetTexture=prior;RenderTexture.active=old;
                File.WriteAllBytes(Path.Combine(frames,$"{i:D4}.jpg"),texture.EncodeToJPG(84));
            }
            Time.captureFramerate=0;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);
            File.WriteAllLines(Path.Combine(Output,"trace.csv"),rows);
            Time.captureFramerate=fps;
            coin.Take();
            for(int j=0;j<fps*2;j++)yield return null;
            var disk=coin.transform.Find("Token 硬币");
            var cover=coin.transform.Find("模拟手").GetComponentInChildren<MeshCollider>(true);
            model.Instance.TryGetBoneTransform(HumanBodyBones.Head,out var viewerHead);
            bool partialCover=false;
            // 找到遮住币心、仍露出币缘的实际位置，再放行一帧观察角色是否保留目标。
            for(int step=0;step<24 && !partialCover;step++){
                var offset=Quaternion.AngleAxis(step*15f,Vector3.up)*forward*(coin.Radius*1.2f);
                coin.Hand(true,coin.CoinPosition+offset);
                bool Covered(Vector3 point){
                    var delta=point-viewerHead.position;
                    return cover.Raycast(new Ray(viewerHead.position,delta.normalized),out _,delta.magnitude);
                }
                if(!Covered(coin.CoinPosition))continue;
                bool rimVisible=Enumerable.Range(0,8).Any(k=>!Covered(disk.TransformPoint(
                    new Vector3(Mathf.Cos(k*Mathf.PI/4)*coin.Radius,.003f,Mathf.Sin(k*Mathf.PI/4)*coin.Radius))));
                if(!rimVisible)continue;
                yield return null;
                partialCover=!coin.PalmOwnsCoin && !coin.Searching && Vector3.Distance(coin.LastSeen,coin.CoinPosition)<.002f;
            }
            coin.Hand(false,default);
            var coverPoint=coin.CoinPosition;var remembered=coin.LastSeen;
            int priorSearches=coin.Searches;
            bool heldToStore=true;
            for(int j=0;j<fps*12;j++)
            {
                coin.Hand(true,coverPoint+right*.065f*Mathf.Clamp01((j-20)/60f));
                yield return null;
                if(j>10)heldToStore &=coin.Searching && (coin.LastSeen-remembered).sqrMagnitude<.000001f;
            }
            heldToStore &=coin.Searches==priorSearches+1;
            coin.Store();heldToStore &= !coin.Deployed && !coin.Searching && !live.Locomotion.Active;
            Time.captureFramerate=0;
            coin.Take();live.ResetPlacement();bool reset=!coin.Deployed && !live.Locomotion.Active;
            live.Place(Vector3.zero);coin.Take();
            var switching=live.SwitchCharacterAsync("local-physics-reference");
            bool switched=!coin.Deployed;
            while(!switching.IsCompleted)yield return null;
            switched &=switching.Result && !coin.Deployed && !live.Locomotion.Active;
            string summary=$"partialCover={partialCover} continuousCover={heldToStore} chased={chased} detour={detour} noPath={noPath} singleSearch={singleSearch} grabDuringTravel={grabDuringTravel} reset={reset} switched={switched} obstacle={blocked} memory={memoryHeld} reveal={revealed} grab={grabbed} landing={landed} edge={edge} footprint={safe} stored={!coin.Deployed} searchesAtCover={searchStart} searches={coin.Searches}";
            File.WriteAllText(Path.Combine(Output,"result.txt"),summary);
            if(!(partialCover&&heldToStore&&chased&&detour&&noPath&&singleSearch&&grabDuringTravel&&reset&&switched&&blocked&&memoryHeld&&revealed&&grabbed&&landed&&edge&&safe&&!coin.Deployed))throw new Exception(summary);
        }
        static IEnumerator ReviewGrip(FeiyuPreview scene,PcCoinChase toy,Camera camera,RenderTexture rt,Texture2D texture)
        {
            var actor=scene.Avatar;
            Vector3 Bone(HumanBodyBones id){actor.Instance.TryGetBoneTransform(id,out var bone);return bone.position;}
            Vector3 Palm(bool left)=>.5f*(Bone(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal)+Bone(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal));
            var rows=new List<string>{"frame,phase,leftError,rightError,leftSurface,rightSurface,lctl,rctl,status,lExcess,rExcess,leftSkinZone,leftSkinSdf,leftSkinInside,rightSkinZone,rightSkinSdf,rightSkinInside,leftCuffZone,leftCuffSdf,leftCuffInside,rightCuffZone,rightCuffSdf,rightCuffInside"};
            using var surfaces=new CoinGripSurfaceProbe(actor);
            var images=new Newtonsoft.Json.Linq.JObject();
            bool movie=Environment.GetEnvironmentVariable("XR_COIN_MOVIE")=="1";
            var frames=Path.Combine(Output,"frames");if(movie)Directory.CreateDirectory(frames);
            var diameter=Environment.GetEnvironmentVariable("XR_COIN_DIAMETER");
            if(!string.IsNullOrEmpty(diameter))toy.SetReviewDiameter(float.Parse(diameter,System.Globalization.CultureInfo.InvariantCulture));
            toy.Take();
            if(Environment.GetEnvironmentVariable("XR_COIN_STATIC")=="1"){
                var setup=toy.ReviewStaticGrasp();while(!setup.IsCompleted)yield return null;
                if(!setup.Result)throw new Exception("静态姿势准备失败");
            }
            var disk=scene.transform.Find("Token 硬币");
            float radius=disk.GetComponentInChildren<MeshCollider>().transform.localScale.x*.5f;
            int consecutive=0,total=movie?720:2000;
            void View(float degrees)
            {
                var center=FeiyuPreview.ToUnity(scene.Interaction.Position)+Vector3.up*actor.HeightMeters*.5f;
                var direction=scene.SpatialRotation*Quaternion.Euler(0,degrees,0)*Vector3.forward;
                camera.orthographic=true;camera.aspect=4f/3f;camera.orthographicSize=actor.HeightMeters*.69f;
                camera.transform.position=center+direction*.65f;camera.transform.LookAt(center);
            }
            Vector3 takePoint=default;
            for(int n=0;n<total;n++){
                if(movie && n==550)takePoint=toy.CoinPosition;
                if(movie && n>=550 && n<580)toy.Hand(true,takePoint+scene.SpatialRotation*Vector3.right*Mathf.Clamp01((n-550)/22f)*.12f);
                if(movie && n==579 && (!toy.PalmOwnsCoin || toy.InArms))
                    throw new Exception("模拟掌未完成从角色手中取币");
                if(movie && n==580){toy.Hand(false,default);toy.Store();}
                yield return null;
                bool active=toy.InArms;
                consecutive=toy.HugSettled?consecutive+1:0;
                // 使用实际硬币轴，取朝玩家的表面与向上的边缘轴。平躺时使用世界朝向厘定两侧。
                Vector3 normal=disk.up;
                if(Vector3.Dot(normal,scene.SpatialRotation*Vector3.forward)<0)normal=-normal;
                Vector3 a=Palm(true),b=Palm(false);
                float sign=Vector3.Dot(Bone(HumanBodyBones.LeftUpperArm)-Bone(HumanBodyBones.RightUpperArm),scene.SpatialRotation*Vector3.right)>0?1:-1;
                var gripL=toy.GripPoint(sign);var gripR=toy.GripPoint(-sign);
                var rig=actor.Instance.Runtime.ControlRig;
                Vector3 Cpad(bool l)=>(rig.GetBoneTransform(l?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal).position+rig.GetBoneTransform(l?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal).position)*.5f;
                float Surface(Vector3 point){
                    var local=disk.InverseTransformPoint(point);
                    float radial=new Vector2(local.x,local.z).magnitude-radius, depth=Mathf.Abs(local.y)-.003f;
                    return new Vector2(Mathf.Max(0,radial),Mathf.Max(0,depth)).magnitude+Mathf.Min(0,Mathf.Max(radial,depth));
                }
                string phase=toy.HugSettled?"steady":active?"lifting":toy.PalmOwnsCoin?"player":!toy.Deployed?"stored":actor.Motions.HoldingToyPose?"reach":"table";
                var profile=consecutive==60?new List<string>{"group,material,x,y,z"}:null;
                var mesh=surfaces.Measure(disk,radius,gripL,gripR,profile,toy.GripFrame);
                if(profile!=null){
                    string Landmark(HumanBodyBones id){
                        var v=Quaternion.Inverse(toy.GripFrame)*(Bone(id)-disk.position);
                        return FormattableString.Invariant($"4,{id},{v.x:F5},{v.y:F5},{v.z:F5}");
                    }
                    foreach(var id in new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm})profile.Add(Landmark(id));
                    File.WriteAllLines(Path.Combine(Output,"surface-sample.csv"),profile);
                }
                rows.Add(FormattableString.Invariant($"{n},{phase},{Vector3.Distance(a,gripL):F5},{Vector3.Distance(b,gripR):F5},{Surface(a):F5},{Surface(b):F5},{Vector3.Distance(Cpad(true),gripL):F5},{Vector3.Distance(Cpad(false),gripR):F5},{toy.Status},{actor.Motions.GripReachDeficit.x:F5},{actor.Motions.GripReachDeficit.y:F5},{mesh}"));
                if(consecutive==60){
                    foreach(int angle in new[]{0,45,90}){
                        View(angle);string name="grip-"+angle;
                        LocomotionProbe.Capture(camera,name);
                        Newtonsoft.Json.Linq.JArray Screen(Vector3 world){var v=camera.WorldToViewportPoint(world);return new Newtonsoft.Json.Linq.JArray(v.x,1-v.y);}
                        images[name]=new Newtonsoft.Json.Linq.JObject{["left"]=Screen(a),["right"]=Screen(b),["zoneL"]=Screen(gripL),["zoneR"]=Screen(gripR),["center"]=Screen(disk.position),["face"]=Screen(disk.position+normal*radius),["owner"]=phase,["fingerL"]=Screen(Bone(HumanBodyBones.LeftMiddleDistal)),["fingerR"]=Screen(Bone(HumanBodyBones.RightMiddleDistal)),
                            ["elbowL"]=Screen(Bone(HumanBodyBones.LeftLowerArm)),["elbowR"]=Screen(Bone(HumanBodyBones.RightLowerArm)),
                            ["rim"]=new Newtonsoft.Json.Linq.JArray(Enumerable.Range(0,65).Select(i=>Screen(disk.TransformPoint(new Vector3(radius*Mathf.Cos(i*Mathf.PI/32),0,radius*Mathf.Sin(i*Mathf.PI/32))))))};
                    }
                    if(!movie)break;
                }
                if(movie){
                    View(Mathf.SmoothStep(0,90,Mathf.InverseLerp(180,440,n)));
                    var old=camera.targetTexture;var prior=RenderTexture.active;
                    camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();
                    camera.targetTexture=old;RenderTexture.active=prior;File.WriteAllBytes(Path.Combine(frames,$"{n:D4}.jpg"),texture.EncodeToJPG(88));
                }
            }
            File.WriteAllLines(Path.Combine(Output,"grip.csv"),rows);
            File.WriteAllText(Path.Combine(Output,"views.json"),images.ToString());
            File.WriteAllText(Path.Combine(Output,"size.txt"),$"diameter={2*radius:F3} height={actor.HeightMeters:F3}");
            if(images.Count!=3)throw new Exception("未取得连续三秒双手抱持观察帧");
        }
    }
}
#endif
