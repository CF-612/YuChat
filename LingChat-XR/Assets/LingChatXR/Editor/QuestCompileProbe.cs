using UnityEditor;
using UnityEngine;
namespace LingChatXR.Editor
{
    public static class QuestCompileProbe
    {
        public static void Run(){QuestProject.Prepare();VrmRuntimeShaders.Validate();Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);}
    }
}
