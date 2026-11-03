using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using LingChatXR.XR;
using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace LingChatXR.Editor
{
    public static class QuestQualityCapture
    {
        public static void RunPreview()
        {
            Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");EditorSceneManager.OpenScene(QuestProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            double deadline=EditorApplication.timeSinceStartup+180;int placed=-1;EditorApplication.CallbackFunction tick=null;
            tick=()=>
            {
                try
                {
                    if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Quality preview timeout");
                    if(!EditorApplication.isPlaying)return;var entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();if(entry==null||!entry.Preview.Ready)return;
                    if(placed<0){entry.ConfirmTable(new Vector3(0,.74f,0));placed=Time.frameCount;return;}if(Time.frameCount<placed+5)return;
                    Run(entry,Environment.GetEnvironmentVariable("XR_M1_EVIDENCE"));EditorApplication.update-=tick;Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);
                }
                catch(Exception e){File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("XR_M1_EVIDENCE"),"failure.txt"),e.ToString());EditorApplication.update-=tick;EditorApplication.Exit(1);}
            };
            EditorApplication.update+=tick;EditorApplication.EnterPlaymode();
        }
        public static void Run(QuestEntry entry,string folder)
        {
            var camera=entry.HeadCamera;var avatar=entry.Preview.Avatar;var position=camera.transform.position;var rotation=camera.transform.rotation;bool orthographic=camera.orthographic;float size=camera.orthographicSize;var color=camera.backgroundColor;var old=camera.targetTexture;var active=RenderTexture.active;
            try
            {
                camera.orthographic=true;camera.orthographicSize=avatar.HeightMeters*.72f;camera.backgroundColor=new Color(.10f,.14f,.19f,1);
                camera.transform.position=avatar.transform.position+avatar.transform.right*.15f+avatar.transform.forward*.55f+Vector3.up*avatar.HeightMeters*.7f;camera.transform.LookAt(avatar.transform.position+Vector3.up*avatar.HeightMeters*.5f);
                foreach(int samples in new[]{1,4})
                {
                    var target=new RenderTexture(600,600,24){antiAliasing=samples};target.Create();var pixels=new Texture2D(600,600,TextureFormat.RGB24,false);
                    try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,600,600),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"msaa-"+samples+".png"),pixels.EncodeToPNG());}
                    finally{camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);}
                }
                File.WriteAllText(Path.Combine(folder,"render-quality.json"),new JObject{["scope"]="Editor RenderTexture A/B, not Quest eye buffer",["configuredMSAA"]=QualitySettings.antiAliasing,["cameraMSAA"]=camera.allowMSAA,["resolutionScale"]=QuestRenderQuality.ResolutionScale,["deviceFrameTimeMeasured"]=false}.ToString());
            }
            finally{camera.transform.SetPositionAndRotation(position,rotation);camera.orthographic=orthographic;camera.orthographicSize=size;camera.backgroundColor=color;camera.targetTexture=old;RenderTexture.active=active;}
        }
    }
}
