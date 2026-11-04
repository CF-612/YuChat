using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 仅用于隔离项目的实际导入与静态表情回归，不代表动态表情或真机验收。
    public static class V21ModelProbe
    {
        static IEnumerator run;
        static string output;
        static double deadline;
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+600;
            EditorApplication.update+=Tick;
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("V21 import");
                if(!EditorApplication.isPlaying)return;
                run??=Check();
                if(!run.MoveNext()){EditorApplication.update-=Tick;Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);}
            }
            catch(Exception e)
            {
                EditorApplication.update-=Tick;
                File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());
                Debug.LogException(e);EditorApplication.Exit(1);
            }
        }
        static IEnumerator Check()
        {
            var avatar=new GameObject("V21 static acceptance").AddComponent<FeiyuAvatar>();
            avatar.MotionPreviewMode=true;avatar.FreezePresentation=true;
            double start=EditorApplication.timeSinceStartup;
            var load=avatar.LoadAsync(Path.Combine(Application.streamingAssetsPath,"Characters/local-smallbluefish-alpha2"),.24f,CancellationToken.None);
            while(!load.IsCompleted)yield return null;
            load.GetAwaiter().GetResult();
            double seconds=EditorApplication.timeSinceStartup-start;
            var expression=avatar.Instance.Runtime.Expression;
            string[] names={"eyeSmile","browHappy","mouthSmile","cheekSlashes","eyeTroubled","mouthOpenCircle","eyeCircleShock","eyeSpiral"};
            foreach(string name in names)
                if(!expression.ExpressionKeys.Contains(ExpressionKey.CreateCustom(name)))throw new Exception("Missing expression: "+name);
            avatar.Instance.UpdateType=Vrm10Instance.UpdateTypes.None;
            var cam=new GameObject("V21 camera").AddComponent<Camera>();
            cam.orthographic=true;cam.orthographicSize=.15f;cam.nearClipPlane=.001f;
            cam.backgroundColor=new Color(.1f,.14f,.19f);cam.clearFlags=CameraClearFlags.SolidColor;
            cam.transform.position=new Vector3(0,.14f,.65f);cam.transform.LookAt(new Vector3(0,.13f,0));
            new GameObject("Light").AddComponent<Light>().type=LightType.Directional;RenderSettings.ambientLight=Color.white;
            var states=new[]{new float[8],new[]{1f,1f,.5f,0f,0f,0f,0f,0f},new[]{0f,0f,0f,0f,1f,1f,0f,0f},new[]{0f,0f,0f,0f,0f,1f,1f,0f},new float[8]};
            for(int state=0;state<states.Length;state++)
            {
                foreach(var key in expression.ExpressionKeys)expression.SetWeight(key,0);
                for(int i=0;i<names.Length;i++)expression.SetWeight(ExpressionKey.CreateCustom(names[i]),states[state][i]);
                avatar.Instance.Runtime.Process();
                foreach(var renderer in avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if(!float.IsFinite(renderer.bounds.center.sqrMagnitude))throw new Exception("Non-finite mesh bounds");
                Capture(cam,Path.Combine(output,"static-"+state+".png"));
            }
            File.WriteAllText(Path.Combine(output,"result.json"),new JObject{["passed"]=true,["loadSeconds"]=seconds,["expressionCount"]=expression.ExpressionKeys.Count(),["staticStates"]=5,["dynamicExpressionsIntegrated"]=false,["visualAccepted"]=false}.ToString());
        }
        static void Capture(Camera cam,string path)
        {
            var rt=new RenderTexture(640,720,24);rt.Create();var old=RenderTexture.active;
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var texture=new Texture2D(640,720,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,640,720),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            cam.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(texture);
        }
    }
}
