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
            if(Environment.GetEnvironmentVariable("XR_V21_BIND_AUDIT")=="1")
            {
                var rows=new JArray();
                var shock=avatar.Instance.Vrm.Expression.Clips.First(x=>avatar.Instance.Vrm.Expression.CreateKey(x.Clip).Equals(ExpressionKey.CreateCustom("eyeCircleShock"))).Clip;
                foreach(var key in expression.ExpressionKeys)expression.SetWeight(key,0);
                expression.SetWeight(ExpressionKey.CreateCustom("eyeCircleShock"),1);
                for(int phase=0;phase<3;phase++)
                {
                    if(phase==1){expression.SetWeight(ExpressionKey.Blink,1);avatar.Instance.LookAtTarget.position=new Vector3(1,1,1);}
                    if(phase==2)
                    {
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                        typeof(FeiyuAvatar).GetField("heldReaction",flags).SetValue(avatar,true);
                        typeof(FeiyuAvatar).GetMethod("ComposeFace",flags).Invoke(avatar,new object[]{1f/60,false});
                    }
                    avatar.Instance.Runtime.Process();
                    var bindings=new JArray();
                    foreach(var b in shock.MorphTargetBindings)
                    {
                        var tr=avatar.Instance.transform.Find(b.RelativePath);
                        var renderer=tr==null?null:tr.GetComponent<SkinnedMeshRenderer>();
                        bindings.Add(new JObject{["path"]=b.RelativePath,["index"]=b.Index,["key"]=renderer==null?"MISSING":renderer.sharedMesh.GetBlendShapeName(b.Index),["expected"]=b.Weight*100,["actual"]=renderer==null?-1:renderer.GetBlendShapeWeight(b.Index)});
                    }
                    rows.Add(new JObject{["phase"]=phase,["shock"]=expression.GetWeight(ExpressionKey.CreateCustom("eyeCircleShock")),["blinkInput"]=expression.GetWeight(ExpressionKey.Blink),["bindings"]=bindings});
                    yield return null;
                }
                File.WriteAllText(Path.Combine(output,"binding-audit.json"),rows.ToString());
                yield break;
            }
            var cam=new GameObject("V21 camera").AddComponent<Camera>();
            cam.orthographic=true;cam.orthographicSize=.15f;cam.nearClipPlane=.001f;
            cam.backgroundColor=new Color(.1f,.14f,.19f);cam.clearFlags=CameraClearFlags.SolidColor;
            cam.transform.position=new Vector3(0,.14f,.65f);cam.transform.LookAt(new Vector3(0,.13f,0));
            new GameObject("Light").AddComponent<Light>().type=LightType.Directional;RenderSettings.ambientLight=Color.white;
            if(Environment.GetEnvironmentVariable("XR_V21_MATERIAL_AB")=="1")
            {
                var check=V21MaterialAbProbe.Check(avatar,cam,output);
                while(check.MoveNext())yield return null;
                yield break;
            }
            if(Environment.GetEnvironmentVariable("XR_V21_DRAW_AUDIT")=="1")
            {
                if(Environment.GetEnvironmentVariable("XR_V21_LILTOON")=="1")AnimePresentation.Apply(avatar);
                var audit=new V21EyeDrawAudit();
                foreach(string state in new[]{"neutral","shock"})
                {
                    foreach(var key in expression.ExpressionKeys)expression.SetWeight(key,0);
                    if(state=="shock")expression.SetWeight(ExpressionKey.CreateCustom("eyeCircleShock"),1);
                    avatar.Instance.Runtime.Process();
                    yield return null;yield return null;
                    Camera.CameraCallback callback=c=>{if(c==cam)audit.Capture(avatar,cam,output,state);};
                    Camera.onPreRender+=callback;
                    try{Capture(cam,Path.Combine(output,state+"-draw.png"));}
                    finally{Camera.onPreRender-=callback;}
                }
                yield break;
            }
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
        internal static void Capture(Camera cam,string path)
        {
            var rt=new RenderTexture(640,720,24);rt.Create();var old=RenderTexture.active;
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var texture=new Texture2D(640,720,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,640,720),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            cam.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(texture);
        }
    }
}
