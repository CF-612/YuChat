using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LingChatXR.Editor
{
    public static class Alpha2Project
    {
        public const string ScenePath = "Assets/Scenes/Alpha2Preview.unity";
        [MenuItem("LingChat XR/Create Alpha2 Preview Scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath)) return;
            PreviewProject.CreateScene();
            var scene = EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            var preview = Object.FindAnyObjectByType<FeiyuPreview>();
            preview.ConfigureCharacter("local-smallbluefish-alpha2", true);
            EditorSceneManager.SaveScene(scene, ScenePath, true);
            Debug.Log("XR_ALPHA2_SCENE_READY");
        }
    }
}
