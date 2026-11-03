using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
namespace LingChatXR.Editor
{
    [InitializeOnLoad]
    public static class Alpha2Probe
    {
        const string Key = "LingChatXR.Alpha2Probe";
        static double started;
        static int frames;
        static FeiyuPreview preview;
        static string Evidence => Environment.GetEnvironmentVariable("XR_EVIDENCE_DIR") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../evidence/f1-alpha2"));
        static Alpha2Probe()
        {
            if (!SessionState.GetBool(Key,false)) return;
            started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=OnLog;
        }
        public static void Run()
        {
            LilToonTemplates.Ensure();Alpha2Project.CreateScene();EditorSceneManager.OpenScene(Alpha2Project.ScenePath);
            SessionState.SetBool(Key,true);
            // 仅隔离批处理避开 Unity 6 的域重载 GC handle 故障；不屏蔽错误日志。
            if(Application.isBatchMode)
            {
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
                started=EditorApplication.timeSinceStartup;frames=0;preview=null;
                EditorApplication.update-=Tick;EditorApplication.update+=Tick;
                Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            }
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>240)throw new Exception("Alpha2 load timeout");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
                if(preview==null)preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
                if(preview==null||!preview.Ready)return;
                if(frames++==0){preview.ManualInput=true;preview.Place(new Vector3(0,.74f,0));}
                if(frames<20)return;
                var a=preview.Avatar;
                if(!a.IsAlpha2||a.FootprintRadius<=0||a.MeasuredHeightMeters<.2f||a.MeasuredHeightMeters>.28f)throw new Exception("Alpha2 calibration invalid");
                Directory.CreateDirectory(Evidence);Capture(preview.ViewCamera,Path.Combine(Evidence,"alpha2-neutral.png"));
                preview.ViewCamera.transform.position=new Vector3(.04f,.98f,.48f);preview.ViewCamera.transform.LookAt(new Vector3(0,.87f,0));preview.ViewCamera.fieldOfView=33;
                Capture(preview.ViewCamera,Path.Combine(Evidence,"alpha2-close.png"));
                string chains=string.Join(",",a.Instance.SpringBone.Springs.Select(s=>"{\"name\":\""+s.Name+"\",\"joints\":"+s.Joints.Count+",\"center\":\""+(s.Center==null?"world":s.Center.name)+"\"}"));
                string F(float value)=>value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string spheres=string.Join(",",a.Instance.GetComponentsInChildren<UniVRM10.VRM10SpringBoneCollider>().Select(c=>"{\"name\":\""+c.name+"\",\"worldRadiusMeters\":"+F(c.Radius*Mathf.Abs(c.transform.lossyScale.x))+"}"));
                File.WriteAllText(Path.Combine(Evidence,"runtime-scale.json"),"{\"modelSha\":\""+a.Package.model.sha256+"\",\"modelScale\":"+F(a.Instance.transform.lossyScale.x)+",\"colliders\":["+spheres+"]}");
                File.WriteAllText(Path.Combine(Evidence,"load.json"),"{\"result\":\"passed\",\"height\":"+a.MeasuredHeightMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"footprintRadius\":"+a.FootprintRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"loadAndWarmupSeconds\":"+(EditorApplication.timeSinceStartup-started).ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"unityAllocatedBytes\":"+Profiler.GetTotalAllocatedMemoryLong()+",\"springs\":["+chains+"],\"userAccepted\":false,\"questTested\":false}");
                EditorApplication.update-=Tick;
                LilToonValidation.Begin(preview,Evidence,Finish);
            }
            catch(Exception e){Debug.LogWarning(e.ToString());Finish(1);}
        }
        public static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1280,720,24){antiAliasing=4};var old=camera.targetTexture;var active=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.Destroy(pixels);rt.Release();UnityEngine.Object.Destroy(rt);
        }
        static void OnLog(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert){Directory.CreateDirectory(Evidence);File.WriteAllText(Path.Combine(Evidence,"last-load-error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;Debug.Log(code==0?"XR_ALPHA2_PROBE_PASSED":"XR_ALPHA2_PROBE_FAILED");EditorApplication.Exit(code);}
    }
}
