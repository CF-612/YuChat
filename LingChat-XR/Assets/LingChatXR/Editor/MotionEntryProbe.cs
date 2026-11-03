using System;
using System.Collections;
using System.IO;
using System.Linq;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 连续观察进入过程，而非只证明首帧没有跳变。
    public static class MotionEntryProbe
    {
        static int lastFrame=-1;static IEnumerator work;static double deadline;static JArray samples=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run(){EditorSceneManager.OpenScene(PreviewProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();}
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;work??=Checks();if(!work.MoveNext()){File.WriteAllText(Path.Combine(Output,"entry.json"),samples.ToString());Debug.Log("XR_M1_PASSED");EditorApplication.update-=Tick;EditorApplication.Exit(0);}}catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());EditorApplication.Exit(1);}}
        static IEnumerator Frame(){int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;}
        static IEnumerator Checks()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;while(!live.Ready)yield return null;
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;}
                live.Place(new Vector3(0,.74f,0));var avatar=live.Avatar;avatar.FreezePresentation=true;avatar.GazeEnabled=false;
                var player=avatar.Motions;while(!player.CatalogReady.IsCompleted)yield return null;
                var prefs=new XrMotionPreferences{enabled=true,conversation=false};foreach(var item in player.Catalog)prefs.items[item.id]=true;player.ApplyPreferences(prefs);player.SetSpatialAllowed(true);
                var rig=avatar.Instance.Runtime.ControlRig;
                var camera=live.ViewCamera;camera.transform.position=new Vector3(0,.89f,.65f);camera.transform.LookAt(new Vector3(0,.86f,0));camera.orthographic=true;camera.orthographicSize=.18f;
                foreach(string id in new[]{"wave_normal_right_661","bow_normal_both_571"})
                {
                    player.Stop(true);player.Step(0);avatar.Instance.Runtime.Process();yield return null;Capture(camera,folder+"-"+id+"-idle");
                    var start=rig.Bones.ToDictionary(p=>p.Key,p=>p.Value.ControlBone.localRotation);
                    var play=player.Play(id);while(!play.IsCompleted)yield return null;if(!play.Result)throw new Exception("play "+id);
                    player.Step(0);
                    for(int frame=0;frame<=180;frame++)
                    {
                        if(frame>0)player.Step(1f/60);avatar.Instance.Runtime.Process();yield return null;
                        if(id=="wave_normal_right_661"&&frame==45&&Quaternion.Angle(start[HumanBodyBones.LeftUpperArm],rig.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation)>10)throw new Exception("挥手开头把未挥动手臂带入素材默认待机："+folder);
                        if(frame%15==0){var row=new JObject{["model"]=folder,["id"]=id,["seconds"]=frame/60f,["sourceSeconds"]=player.InspectPlayback().sourceTime};foreach(var bone in new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.Head,HumanBodyBones.Spine})row[bone.ToString()]=Quaternion.Angle(start[bone],rig.GetBoneTransform(bone).localRotation);samples.Add(row);Capture(camera,folder+"-"+id+"-"+frame.ToString("D3"));}
                    }
                }
                player.Stop(true);avatar.FreezePresentation=false;
            }
        }
        static void Capture(Camera camera,string name)
        {
            var target=new RenderTexture(512,640,24);target.Create();var old=camera.targetTexture;var active=RenderTexture.active;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;var texture=new Texture2D(512,640,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,512,640),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),texture.EncodeToPNG());camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(texture);target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
