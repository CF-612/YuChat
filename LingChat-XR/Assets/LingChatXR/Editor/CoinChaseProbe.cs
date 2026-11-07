#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
                    while(core.ContainsFootprint(FeiyuPreview.ToCore(edgePoint+direction*.01f),.028f))edgePoint+=direction*.01f;
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
            string summary=$"continuousCover={heldToStore} chased={chased} detour={detour} noPath={noPath} singleSearch={singleSearch} grabDuringTravel={grabDuringTravel} reset={reset} switched={switched} obstacle={blocked} memory={memoryHeld} reveal={revealed} grab={grabbed} landing={landed} edge={edge} footprint={safe} stored={!coin.Deployed} searchesAtCover={searchStart} searches={coin.Searches}";
            File.WriteAllText(Path.Combine(Output,"result.txt"),summary);
            if(!(heldToStore&&chased&&detour&&noPath&&singleSearch&&grabDuringTravel&&reset&&switched&&blocked&&memoryHeld&&revealed&&grabbed&&landed&&edge&&safe&&!coin.Deployed))throw new Exception(summary);
        }
    }
}
#endif
