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
    // P0 only: existing playback is a baseline, never evidence that GEM export works.
    public static class VideoMotionBaselineProbe
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
                while (!player.CatalogReady.IsCompleted) yield return null;
                player.CatalogReady.GetAwaiter().GetResult();
                var asset = player.Catalog.FirstOrDefault(a => !a.builtin && !a.loop && a.poseLoop == null);
                Check(asset != null, folder + ": baseline body asset available");
                var preferences = new XrMotionPreferences { enabled = true, automatic = false, conversation = false };
                preferences.items[asset.id] = true;
                player.ApplyPreferences(preferences);
                var play = player.Play(asset.id);
                while (!play.IsCompleted) yield return null;
                Check(play.GetAwaiter().GetResult(), folder + ": baseline VRMA imported");
                var rootPosition = owner.transform.position;
                var rootRotation = owner.transform.rotation;
                var before = rig.Bones.ToDictionary(p => p.Key, p => p.Value.ControlBone.localRotation);
                var springJoints = instance.SpringBone.Springs.SelectMany(s => s.Joints).Select(j => j.transform).Distinct().ToArray();
                float maxAngle = 0;
                for (var frame = 0; frame < 120; frame++)
                {
                    player.Step(1f / 60);
                    instance.Runtime.Process();
                    foreach (var pair in rig.Bones)
                    {
                        var rotation = pair.Value.ControlBone.localRotation;
                        Check(float.IsFinite(rotation.x) && float.IsFinite(rotation.y) && float.IsFinite(rotation.z) && float.IsFinite(rotation.w),
                            folder + ": finite body pose " + frame + " " + pair.Key);
                        maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(before[pair.Key], rotation));
                    }
                }
                Check(maxAngle > 1, folder + ": sampled baseline body motion");
                Check(owner.transform.position == rootPosition && owner.transform.rotation == rootRotation,
                    folder + ": external world root preserved");
                player.Stop(true);
                Check(!player.Busy, folder + ": cancellation releases body control");
                // Let the existing spring service actually run on the new model before checking it.
                for (var frame = 0; frame < 20; frame++)
                {
                    yield return null;
                    foreach (var joint in springJoints)
                    {
                        var q = joint.localRotation;
                        var p = joint.position;
                        Check(float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w)
                            && float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z),
                            folder + ": finite author spring " + frame + " " + joint.name);
                    }
                }
                var motionPackage=Environment.GetEnvironmentVariable("XR_VIDEO_MOTION_PACKAGE");
                if(!string.IsNullOrEmpty(motionPackage))
                {
                    var packageChecks=VerifyPackage(avatar,motionPackage,folder);
                    while(packageChecks.MoveNext())yield return packageChecks.Current;
                }
                var oldSprings = instance.SpringBone.Springs.Count;
                models.Add(new JObject
                {
                    ["folder"] = folder, ["sha256"] = avatar.Package.model.sha256,
                    ["measuredScaledHeightMeters"] = avatar.MeasuredHeightMeters,
                    ["uniformImportScale"] = instance.transform.localScale.x,
                    ["controlBones"] = rig.Bones.Count, ["springChains"] = oldSprings, ["springJoints"] = springJoints.Length,
                    ["baselineAsset"] = asset.id, ["maxSampledRotationDegrees"] = maxAngle,
                    ["expressionKeys"] = new JArray(instance.Runtime.Expression.ExpressionKeys.Select(k => k.ToString()))
                });
                UnityEngine.Object.Destroy(owner);
                yield return null;
                yield return null;
                Check(avatar == null, folder + ": model owner destroyed before next binding");
            }
        }

        static IEnumerator VerifyPackage(FeiyuAvatar avatar,string packagePath,string modelFolder)
        {
            var label=Path.GetFileName(modelFolder.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
            var output=Path.Combine(Output,label);Directory.CreateDirectory(output);
            var metadata=JObject.Parse(File.ReadAllText(Path.Combine(packagePath,"metadata.json")));
            float fps=(float)metadata["sampleRateHz"],last=(float)metadata["lastSampleTimeSec"];
            bool capture=Environment.GetEnvironmentVariable("XR_VIDEO_CAPTURE_FRAMES")!="0";
            int count=(int)metadata["frameCount"];
            var player=avatar.Motions;var rig=avatar.Instance.Runtime.ControlRig;
            var hips=rig.GetBoneTransform(HumanBodyBones.Hips);var initialHips=hips.localPosition;
            var rootPosition=avatar.transform.position;var rootRotation=avatar.transform.rotation;
            var play=player.PreviewPackage(packagePath);while(!play.IsCompleted)yield return null;
            Check(play.GetAwaiter().GetResult(),label+": generated package imported "+player.PackageLastError);
            var cameraObject=new GameObject("Body package evidence camera");var camera=cameraObject.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.78f,.83f,.87f);
            camera.orthographic=true;camera.orthographicSize=.145f;camera.nearClipPlane=.01f;camera.farClipPlane=5;
            void View(Vector3 offset){camera.transform.position=rootPosition+offset;camera.transform.LookAt(rootPosition+Vector3.up*.12f);}
            View(new Vector3(0,.12f,.6f));
            var lightObject=new GameObject("Body package evidence light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.85f;light.transform.rotation=Quaternion.Euler(40,-20,0);
            RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            float low=float.PositiveInfinity,high=float.NegativeInfinity;var samples=new JArray();
            for(int frame=0;frame<count;frame++)
            {
                player.Step(frame==0?0:1/fps);avatar.Instance.Runtime.Process();
                var clock=player.InspectPlayback();
                Check(Mathf.Abs(clock.sourceTime-Mathf.Min(frame/fps,last))<.002f,label+": body package common source clock "+frame);
                Check(avatar.transform.position==rootPosition&&avatar.transform.rotation==rootRotation,label+": package external world root "+frame);
                Check(Mathf.Abs(hips.localPosition.x-initialHips.x)<.0001f&&Mathf.Abs(hips.localPosition.z-initialHips.z)<.0001f,label+": no horizontal hips drift "+frame);
                low=Mathf.Min(low,hips.localPosition.y);high=Mathf.Max(high,hips.localPosition.y);
                foreach(var pair in rig.Bones)
                {
                    var q=pair.Value.ControlBone.localRotation;
                    Check(float.IsFinite(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w),label+": generated finite pose "+frame+" "+pair.Key);
                }
                if(capture)Capture(camera,Path.Combine(output,"front-"+frame.ToString("D3")+".png"));
                if(capture&&Environment.GetEnvironmentVariable("XR_COMPARE_WAVE")=="1"){
                    View(new Vector3(.6f,.12f,0));Capture(camera,Path.Combine(output,"side-"+frame.ToString("D3")+".png"));View(new Vector3(0,.12f,.6f));
                }
                if(capture&&frame==count/2)
                {
                    View(new Vector3(.6f,.12f,0));Capture(camera,Path.Combine(output,"side.png"));
                    View(new Vector3(0,.12f,-.6f));Capture(camera,Path.Combine(output,"back.png"));View(new Vector3(0,.12f,.6f));
                }
                avatar.Instance.TryGetBoneTransform(HumanBodyBones.RightHand,out var wrist);
                avatar.Instance.TryGetBoneTransform(HumanBodyBones.RightUpperArm,out var shoulder);
                samples.Add(new JObject{["frame"]=frame,["sourceTimeSec"]=clock.sourceTime,["hipsLocalY"]=hips.localPosition.y,
                    ["rightHandAboveShoulderMeters"]=wrist.position.y-shoulder.position.y});
                yield return null;
            }
            Check(high-low>.00001f,label+": local hips excursion preserved");
            File.WriteAllText(Path.Combine(output,"package-playback.json"),new JObject{["samples"]=samples,["hipsLocalYRange"]=high-low,["worldRootPolicy"]="external",["bodyOnly"]=true}.ToString());
            if(Environment.GetEnvironmentVariable("XR_COMPARE_WAVE")=="1")
            {
                player.Stop(true);
                var prefs=new XrMotionPreferences{enabled=true};prefs.items["wave_normal_right_661"]=true;player.ApplyPreferences(prefs);
                var wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;
                Check(wave.Result,"author wave comparison");
                for(int frame=0;frame<200 && player.ActiveId!=null;frame++){
                    player.Step(1f/20);avatar.Instance.Runtime.Process();
                    Capture(camera,Path.Combine(output,"authored-"+frame.ToString("D3")+".png"));
                }
            }
            // Pause keeps the same source time; resume/cancel remain with the existing player owner.
            play=player.PreviewPackage(packagePath);while(!play.IsCompleted)yield return null;Check(play.Result,label+": package replay");
            player.Step(.2f);var beforePause=player.InspectPlayback().sourceTime;player.Step(0);
            Check(player.InspectPlayback().sourceTime==beforePause,label+": package pause");
            player.SetSpatialAllowed(false);Check(!player.Busy&&!player.PlayingPackage,label+": spatial owner interrupts generated body");
            player.SetSpatialAllowed(true);
            var late=player.PreviewPackage(packagePath);player.Stop(true);while(!late.IsCompleted)yield return null;
            Check(!late.Result&&!player.PlayingPackage,label+": stale metadata result cannot reclaim playback");
            var invalid=Path.Combine(output,"invalid-timebase");Directory.CreateDirectory(invalid);
            metadata["frameCount"]=(int)metadata["frameCount"]+1;File.WriteAllText(Path.Combine(invalid,"metadata.json"),metadata.ToString());
            var denied=player.PreviewPackage(invalid);while(!denied.IsCompleted)yield return null;
            Check(!denied.Result&&!player.PlayingPackage,label+": incompatible package timebase rejected");
            // A changed sample count/rate can preserve duration; cached data must still reject it.
            metadata=JObject.Parse(File.ReadAllText(Path.Combine(packagePath,"metadata.json")));
            metadata["frameCount"]=(count-1)*2+1;metadata["sampleRateHz"]=fps*2;
            File.WriteAllText(Path.Combine(invalid,"metadata.json"),metadata.ToString());
            denied=player.PreviewPackage(invalid);while(!denied.IsCompleted)yield return null;
            Check(!denied.Result&&!player.PlayingPackage,label+": cached body rejects changed sampling with same duration");
            UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(lightObject);player.Stop(true);
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
                ["stage"] = "P0 existing playback baseline; GEM body not validated",
                ["unityVersion"] = Application.unityVersion, ["passed"] = code == 0,
                ["checks"] = new JArray(checks), ["models"] = models
            }.ToString());
            Debug.Log(code == 0 ? "XR_M1_PASSED" : "XR_VIDEO_BASELINE_FAILED");
            EditorApplication.Exit(code);
        }
    }
}
