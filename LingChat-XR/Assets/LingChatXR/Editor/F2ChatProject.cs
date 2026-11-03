using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LingChatXR.Editor
{
    public static class F2ChatProject
    {
        public const string ScenePath = "Assets/Scenes/F2ChatPreview.unity";
        [MenuItem("LingChat XR/Create Model-free Chat Scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Chat background").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f,.09f,.16f);
            camera.gameObject.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("F2_CHAT_SCENE_READY");
        }
    }
}
