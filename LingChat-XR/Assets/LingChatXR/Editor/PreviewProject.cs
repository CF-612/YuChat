using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LingChatXR.Editor
{
    public static class PreviewProject
    {
        public const string ScenePath = "Assets/Scenes/FeiyuPreview.unity";

        [MenuItem("LingChat XR/Create Editor Preview Scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log("Preview scene already exists; preserving it.");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(.36f, 1.13f, 1.05f);
            camera.transform.LookAt(new Vector3(-.08f, .84f, 0));
            camera.fieldOfView = 42;
            camera.nearClipPlane = .02f;
            camera.farClipPlane = 10;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.10f, .14f, .19f);
            camera.gameObject.AddComponent<AudioListener>();
            var light = new GameObject("Soft key light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.72f, .76f, .81f);
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Simulated table (not passthrough)";
            table.transform.position = new Vector3(0, .72f, 0);
            table.transform.localScale = new Vector3(1.4f, .04f, 1.0f);
            var material = new Material(Shader.Find("Standard")) { color = new Color(.43f, .51f, .55f) };
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.CreateAsset(material, "Assets/Scenes/PreviewTable.mat");
            table.GetComponent<Renderer>().sharedMaterial = material;
            var preview = new GameObject("Feiyu Editor Preview").AddComponent<FeiyuPreview>();
            preview.SetCamera(camera);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "LingChatXRLocal";
            PlayerSettings.productName = "LingChat XR Preview";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.runInBackground = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            Debug.Log("XR_PREVIEW_SCENE_CREATED");
        }

        public static void ValidateProject()
        {
            if (!Application.unityVersion.StartsWith("6000.")) throw new Exception("Unity 6 is required.");
            CreateScene();
            if (!File.Exists("Assets/StreamingAssets/Characters/blue-placeholder/manifest.json"))
                throw new FileNotFoundException("Create the placeholder package first.");
            Debug.Log("XR_PROJECT_STATIC_READY Unity=" + Application.unityVersion);
        }
    }
}
