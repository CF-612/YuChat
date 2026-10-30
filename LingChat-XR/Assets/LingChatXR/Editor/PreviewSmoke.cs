using System;
using System.IO;
using LingChatXR.Core;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // Run with graphics enabled. This checks Unity integration, not mouse feel or Quest behavior.
    [InitializeOnLoad]
    public static class PreviewSmoke
    {
        private const string Pending = "LingChatXR.PreviewSmoke";
        private static double deadline;
        private static int stage;
        private static int frames;
        private static int generation;
        private static Ray heldPointerRay;
        private static FeiyuPreview preview;
        private static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../../evidence"));

        static PreviewSmoke()
        {
            if (SessionState.GetBool(Pending, false))
            {
                deadline = EditorApplication.timeSinceStartup + 180;
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
            }
        }

        public static void Run()
        {
            PreviewProject.ValidateProject();
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Preview smoke timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (preview == null) preview = UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
                if (preview == null || !preview.Ready) return;
                frames++;
                if (stage == 0)
                {
                    Require(preview.Place(new Vector3(0, .74f, 0)), "place");
                    Require(preview.Avatar.Instance != null, "VRM instance");
                    Require(preview.Avatar.TouchProxy != null && preview.Avatar.GrabProxy != null, "proxies");
                    Require(Math.Abs(preview.Avatar.MeasuredHeightMeters - preview.Avatar.HeightMeters) < .02f, "measured metric height");
                    Require(preview.Avatar.FootprintRadius > .015f && preview.Avatar.FootprintRadius < .05f, "foot support excludes hair and skirt");
                    Require(preview.Place(new Vector3(.54f, .74f, .34f)), "expanded corner with six centimetres clearance");
                    Require(!preview.Place(new Vector3(.595f, .74f, 0)), "feet may not cross boundary");
                    Require(preview.Place(new Vector3(0, .74f, 0)), "restore centre after edge checks");
                    // Disable mouse polling during programmatic transitions; presentation still runs.
                    preview.enabled = false;
                    Require(preview.Place(new Vector3(0, .74f, 0)), "replace after disable");
                    Ray headRay = RayTo(preview.Avatar.TouchProxy.transform.position);
                    preview.HandlePointer(headRay, true, false, true, false, false, 0);
                    Require(preview.Interaction.State == PetState.Idle, "UI blocks touch");
                    preview.HandlePointer(new Ray(Camera.main.transform.position, Vector3.up), false, false, true, false, false, 0);
                    Require(preview.Interaction.State == PetState.Idle, "blank blocks touch");
                    preview.HandlePointer(headRay, false, false, true, false, false, 0);
                    Require(preview.Interaction.State == PetState.Touched, "head ray touch");
                    stage = 1; frames = 0;
                }
                else if (stage == 1)
                {
                    preview.Avatar.SetFeedback(true, false);
                    preview.Avatar.Present(Camera.main.transform.position);
                    if (frames < 30) return;
                    Require(preview.Avatar.Instance.Runtime.Expression.GetWeight(ExpressionKey.Happy) > .1f, "touch expression");
                    Capture("unity6-touch.png");
                    heldPointerRay = RayTo(preview.Avatar.GrabProxy.bounds.center);
                    preview.HandlePointer(heldPointerRay, false, false, false, true, false, 0);
                    Require(preview.Interaction.State == PetState.Held, "body ray grab overrides touch");
                    preview.Avatar.SetFeedback(false, false);
                    stage = 2; frames = 0;
                }
                else if (stage == 2)
                {
                    preview.Avatar.Present(Camera.main.transform.position);
                    if (frames < 20) return;
                    Capture("unity6-held.png");
                    preview.HandlePointer(heldPointerRay, false, false, false, false, true, 0);
                    Require(preview.Interaction.LastReleaseWasValid, "mouse release without wheel lowering");
                    TestPointerDrop(new Vector3(.07f, 0, 0), false, true);
                    TestPointerDrop(new Vector3(1.2f, 0, 0), false, false);
                    TestPointerDrop(Vector3.zero, true, false);
                    var cameraPosition = Camera.main.transform.position;
                    preview.HandleViewInput(new Vector2(100, -30), 0, false);
                    Require(Vector3.Distance(cameraPosition, Camera.main.transform.position) > .1f, "orbit changes view");
                    var orbitPosition = Camera.main.transform.position;
                    preview.HandleViewInput(Vector2.zero, 2, false);
                    Require(Vector3.Distance(orbitPosition, Camera.main.transform.position) > .05f, "wheel zoom");
                    TestPointerDrop(new Vector3(-.07f, 0, 0), false, true);
                    preview.HandleViewInput(Vector2.zero, 0, true);
                    Require(Vector3.Distance(cameraPosition, Camera.main.transform.position) < .001f, "reset view");
                    for (int i = 0; i < 10; i++)
                    {
                        float x = (i % 2 == 0) ? -.07f : .07f;
                        Require(preview.Interaction.BeginGrab("smoke", out generation), "repeat grab");
                        Require(preview.Interaction.ReleaseGrab("smoke", generation, new Vec3(x, .75f, 0)), "repeat release");
                        Require(preview.Interaction.LastReleaseWasValid, "repeat valid landing");
                    }
                    Require(preview.Interaction.BeginGrab("smoke", out generation), "invalid grab");
                    preview.Interaction.ReleaseGrab("smoke", generation, new Vec3(1, .75f, 0));
                    Require(!preview.Interaction.LastReleaseWasValid, "invalid landing");
                    Require(Math.Abs(preview.Interaction.Position.X - .07f) < .001, "safe recovery");
                    // Disable a live preview while held: both rules and visible root must recover.
                    preview.enabled = true;
                    Require(preview.Interaction.BeginGrab("smoke", out generation), "disable grab");
                    preview.Interaction.MoveGrab("smoke", generation, new Vec3(.1f, 1.1f, 0));
                    preview.Avatar.transform.position = FeiyuPreview.ToUnity(preview.Interaction.Position);
                    preview.enabled = false;
                    Require(Math.Abs(preview.Avatar.transform.position.y - .74f) < .001, "disable visible recovery");
                    preview.SimulateTrackingLoss();
                    Require(!preview.Interaction.PlaneConfirmed && preview.Interaction.State == PetState.Suspended, "tracking loss");
                    Require(!preview.Interaction.MoveGrab("smoke", generation, new Vec3(1, 1, 1)), "late update rejected");
                    Require(preview.Place(new Vector3(0, .74f, 0)), "reconfirm");
                    stage = 3; frames = 0;
                }
                else if (stage == 3)
                {
                    preview.Avatar.SetFeedback(false, false);
                    preview.Avatar.Present(Camera.main.transform.position);
                    if (frames < 30) return;
                    Require(preview.Avatar.Instance.Runtime.Expression.GetWeight(ExpressionKey.Happy) < .05f, "touch fade out");
                    Capture("unity6-preview.png");
                    Directory.CreateDirectory(Evidence);
                    File.WriteAllText(Path.Combine(Evidence, "unity6-smoke.json"),
                        "{\"result\":\"passed\",\"unityVersion\":\"" + Application.unityVersion +
                        "\",\"scope\":\"Unity PlayMode model, simulated pointer rays, expressions, grab/release, disable recovery\",\"questTested\":false,\"physicalMouseTested\":false,\"visualAcceptedByUser\":false}");
                    Finish(0);
                }
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Evidence);
                File.AppendAllText(Path.Combine(Evidence, "unity6-smoke-failure.txt"), DateTime.UtcNow.ToString("O") + "\n" + error + "\n");
                Finish(1);
            }
        }

        private static void Require(bool value, string label) { if (!value) throw new Exception("Failed: " + label); }
        private static void TestPointerDrop(Vector3 shift, bool overUi, bool expectedValid)
        {
            Physics.SyncTransforms();
            Vector3 before = FeiyuPreview.ToUnity(preview.Interaction.Position);
            Ray ray = RayTo(preview.Avatar.GrabProxy.bounds.center);
            var plane = new Plane(Vector3.up, new Vector3(0, .74f, 0));
            Require(plane.Raycast(ray, out float distance), "test table ray");
            preview.HandlePointer(ray, false, false, false, true, false, 0);
            Require(preview.Interaction.State == PetState.Held && preview.DropPreviewValid, "grab green marker");
            var cameraPosition = Camera.main.transform.position;
            preview.HandleViewInput(new Vector2(100, 30), 2, true);
            Require(Camera.main.transform.position == cameraPosition, "held view remains stable");
            Ray release = RayTo(ray.GetPoint(distance) + shift);
            // Move only on button-up: catches accidentally using the previous held position.
            preview.HandlePointer(release, overUi, false, false, false, true, 0);
            Require(preview.Interaction.LastReleaseWasValid == expectedValid, "release frame / UI validity");
            Vector3 expected = expectedValid ? before + shift : before;
            Require(Vector3.Distance(FeiyuPreview.ToUnity(preview.Interaction.Position), expected) < .001f, "release destination / recovery");
            Require(!preview.DropPreviewValid, "marker hidden after release");
        }
        private static Ray RayTo(Vector3 point) => new Ray(Camera.main.transform.position, point - Camera.main.transform.position);

        private static void Capture(string file)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new Exception("Graphics device unavailable; cannot claim a render.");
            var camera = Camera.main;
            var target = new RenderTexture(1280, 800, 24);
            var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
                texture.Apply();
                Directory.CreateDirectory(Evidence);
                File.WriteAllBytes(Path.Combine(Evidence, file), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void OnLog(string text, string trace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            Directory.CreateDirectory(Evidence);
            File.AppendAllText(Path.Combine(Evidence, "unity6-smoke-errors.log"), text + "\n" + trace + "\n");
            Finish(1);
        }

        private static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Debug.Log(code == 0 ? "XR_SMOKE_PASSED" : "XR_SMOKE_FAILED");
            EditorApplication.Exit(code);
        }
    }
}
