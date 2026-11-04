using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 实际加载尾巴候选，验证持有循环和退出，并等待蒙皮跨帧更新后截图。
    public static class TailGrabProbe
    {
        static IEnumerator work;
        static double deadline;
        static readonly List<string> checks = new List<string>();
        static readonly JArray models = new JArray();
        static string Output => Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");

        public static void Run()
        {
            work = null;
            checks.Clear();
            models.Clear();
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            deadline = EditorApplication.timeSinceStartup + 600;
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            checks.Add(label);
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Video motion baseline timeout");
                if (!EditorApplication.isPlaying) return;
                work ??= Verify();
                if (!work.MoveNext()) Finish(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Output, "failure.txt"), e.ToString());
                Finish(1);
            }
        }

        static IEnumerator Verify()
        {
            var extraPackage = Environment.GetEnvironmentVariable("XR_VIDEO_MODEL_PACKAGE");
            var packages = string.IsNullOrEmpty(extraPackage)
                ? new[] { "local-physics-reference", "local-smallbluefish-alpha2" }
                : Environment.GetEnvironmentVariable("XR_VIDEO_COMPARE_REFERENCE")=="1" ? new[] { extraPackage,"local-physics-reference" } : new[] { extraPackage };
            foreach (var folder in packages)
            {
                var owner = new GameObject("P0 model capability baseline");
                var avatar = owner.AddComponent<FeiyuAvatar>();
                avatar.MotionPreviewMode = true;
                avatar.FreezePresentation = true;
                var packagePath = Path.IsPathRooted(folder) ? folder : Path.Combine(Application.streamingAssetsPath, "Characters", folder);
                var load = avatar.LoadAsync(packagePath, .24f, CancellationToken.None);
                while (!load.IsCompleted) yield return null;
                load.GetAwaiter().GetResult();
                var instance = avatar.Instance;
                var animator = instance.GetComponent<Animator>();
                Check(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman,
                    folder + ": valid Unity Humanoid Avatar");
                var rig = instance.Runtime.ControlRig;
                Check(rig != null && rig.Bones.Count > 14, folder + ": UniVRM control rig");
                Check(float.IsFinite(avatar.MeasuredHeightMeters) && avatar.MeasuredHeightMeters > .1f,
                    folder + ": finite skinned geometry height");
                var player = avatar.Motions;
                while(!player.CatalogReady.IsCompleted)yield return null;
                var ready = player.PrepareGrabbed();
                while(!ready.IsCompleted)yield return null;
                Check(ready.Result && player.TailGrabReady, "普通和倒挂动作就绪");
                Check(avatar.TailGrabProxy != null, "角色包尾巴抓点已绑定");
                Check(avatar.SelectGrabPoint(true), "选择尾巴抓点");
                Check(avatar.HeldGripBone == avatar.TailGrabProxy.transform, "持有锚点切换到尾巴");
                player.SetSpatialAllowed(false);
                player.SetGrabbed(true);
                float sampled=0;
                var cam=new GameObject().AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=.19f;cam.nearClipPlane=.01f;
                cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.32f,.37f,.44f);
                new GameObject().AddComponent<Light>().type=LightType.Directional;RenderSettings.ambientLight=Color.white;
                foreach(float t in new[]{.5f,1.5f,3f,5f,8f,11f,13f}) {
                    while(sampled<t){player.Step(1f/60);instance.Runtime.Process();sampled+=1f/60;}
                    yield return null;
                    instance.Runtime.Process();
                    Check(player.GrabbedActive,"持续持有 "+t);
                    Check(Quaternion.Angle(owner.transform.rotation,Quaternion.identity)<.01f,"空间朝向不由动画改变");
                    var center=animator.GetBoneTransform(HumanBodyBones.Hips).position+Vector3.up*.075f;
                    cam.transform.position=center+new Vector3(.16f,.04f,.55f);cam.transform.LookAt(center);
                    File.AppendAllText(Path.Combine(Output,"bone-samples.txt"),t+" "+player.InspectPlayback()+" control="+rig.GetBoneTransform(HumanBodyBones.LeftUpperArm).localEulerAngles+" target="+animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localEulerAngles+Environment.NewLine);
                    Capture(cam,Path.Combine(Output,"pose-"+t.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"));
                }
                player.SetGrabbed(false);
                for(int i=0;i<30;i++)player.Step(1f/60);
                Check(!player.GrabbedActive,"放下释放提起动作");
                Check(avatar.SelectGrabPoint(false),"可回到抓头");
                Check(avatar.HeldGripBone==avatar.HeadGripBone,"抓头锚点恢复");
                player.Stop(true);
                UnityEngine.Object.Destroy(owner);
                yield return null;
                yield return null;
                Check(avatar == null, folder + ": model owner destroyed before next binding");
            }
        }

        static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(512,640,24);target.Create();var old=camera.targetTexture;var active=RenderTexture.active;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;var texture=new Texture2D(512,640,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,512,640),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            camera.targetTexture=old;RenderTexture.active=active;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(texture);
        }

        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                File.AppendAllText(Path.Combine(Output, "runtime-errors.txt"), message + "\n" + stack + "\n");
            }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            if (File.Exists(Path.Combine(Output, "runtime-errors.txt"))) code = 1;
            File.WriteAllText(Path.Combine(Output, "unity-baseline.json"), new JObject
            {
                ["stage"] = "Jitabata tail-held PC integration",
                ["unityVersion"] = Application.unityVersion, ["passed"] = code == 0,
                ["checks"] = new JArray(checks), ["models"] = models
            }.ToString());
            Debug.Log(code == 0 ? "XR_M1_PASSED" : "XR_VIDEO_BASELINE_FAILED");
            EditorApplication.Exit(code);
        }
    }
}
