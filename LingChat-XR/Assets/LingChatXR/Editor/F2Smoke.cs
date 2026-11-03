using System;
using System.IO;
using LingChatXR.Chat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace LingChatXR.Editor
{
    [InitializeOnLoad]
    public static class F2Smoke
    {
        private const string Pending = "LingChatXR.F2Smoke";
        private static double deadline;
        private static int stage;
        private static double stamp;
        private static XrChatController chat;
        private static double nextLog;
        private static int round;
        private static int spoken;
        private static int beforeRound;
        private static readonly string[] Prompts = { "你好，我给你取个昵称叫泡泡，请简短回复。", "还记得刚才的昵称吗？请回复一句话。", "今天先聊到这里，简单告别吧。" };
        private static string Evidence => Environment.GetEnvironmentVariable("XR_EVIDENCE_DIR") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../evidence"));
        static F2Smoke()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            deadline = EditorApplication.timeSinceStartup + 200;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        public static void Run()
        {
            F2ChatProject.CreateScene(); EditorSceneManager.OpenScene(F2ChatProject.ScenePath);
            SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
        }
        public static void RunConversation() { SessionState.SetBool(Pending + ".Conversation", true); Run(); }
        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > nextLog) { nextLog = EditorApplication.timeSinceStartup + 10; Debug.Log("XR_F2_STAGE " + stage + " " + (chat == null ? "waiting" : chat.Status)); }
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("F2 smoke timeout stage=" + stage + " status=" + (chat == null ? "no controller" : chat.Status));
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (chat == null)
                {
                    chat = UnityEngine.Object.FindAnyObjectByType<XrChatController>();
                    if (chat != null) { chat.AutoAdvance=true;chat.Voice.PlaybackEvent += OnVoice; }
                }
                if (chat == null || !chat.Connected) return;
                if (stage == 0) { if (UnityEngine.Object.FindAnyObjectByType<FeiyuAvatar>() != null) throw new Exception("Model-free scene contains an avatar"); chat.TestVoice(); stage = 1; }
                else if (stage == 1 && chat.Voice.IsPlaying)
                { chat.Stop(); if (chat.Voice.IsPlaying || chat.Busy) throw new Exception("Stop did not stop playback"); stamp = EditorApplication.timeSinceStartup; stage = 2; }
                else if (stage == 2 && !chat.Stopping && EditorApplication.timeSinceStartup - stamp > 1)
                { if (chat.Voice.IsPlaying) throw new Exception("Late replay after stop"); chat.TestVoice(); stage = 3; }
                else if (stage == 3 && chat.Voice.IsPlaying)
                { Directory.CreateDirectory(Evidence); stage = 4; }
                else if (stage == 4 && !chat.Busy && !chat.Voice.IsPlaying)
                {
                    chat.GetComponent<XrChatView>().CaptureTestUi(Path.Combine(Evidence,"f2-chat-ui.png"));
                    File.WriteAllText(Path.Combine(Evidence,"f2-unity-smoke.json"),"{\"result\":\"passed\",\"modelRequired\":false,\"realAudioDownloaded\":true,\"audioSourcePlayed\":true,\"stopDuringPlayback\":true,\"subsequentPlayback\":true,\"userListeningAccepted\":false,\"questTested\":false}");
                    if (!SessionState.GetBool(Pending + ".Conversation", false)) Finish(0);
                    else { stage = 5; beforeRound = spoken; deadline = EditorApplication.timeSinceStartup + 180; chat.Send(Prompts[round]); }
                }
                else if (stage == 5 && !chat.Busy)
                {
                    if (spoken == beforeRound || chat.CanRetry) throw new Exception("Real conversation audio failed");
                    round++;
                    if (round < Prompts.Length) { beforeRound = spoken; deadline = EditorApplication.timeSinceStartup + 180; chat.Send(Prompts[round]); }
                    else
                    {
                        File.WriteAllText(Path.Combine(Evidence,"f2-unity-conversation.json"),"{\"result\":\"passed\",\"realLlmRounds\":3,\"audioSourceStarts\":" + spoken + ",\"subtitleMatchesChineseHistory\":true,\"userListeningAccepted\":false,\"questTested\":false}");
                        Finish(0);
                    }
                }
            }
            catch(Exception e) { Debug.LogWarning(e.Message); Finish(1); }
        }
        private static void OnVoice(Speech.VoiceKind kind, Speech.VoiceOutcome outcome, long generation)
        {
            if (kind != Speech.VoiceKind.Dialogue || outcome != Speech.VoiceOutcome.Started) return;
            spoken++;
            bool found = false; foreach (var row in chat.History) if (row.speaker == "assistant" && row.text == chat.FullSubtitle) found = true;
            if (!found) { Debug.LogWarning("Subtitle does not match Chinese sentence history"); Finish(1); }
        }
        private static void Log(string text,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) Finish(1); }
        private static void Finish(int code) {SessionState.SetBool(Pending,false); SessionState.SetBool(Pending + ".Conversation",false); EditorApplication.update-=Tick; Application.logMessageReceived-=Log;Debug.Log(code==0?"XR_F2_SMOKE_PASSED":"XR_F2_SMOKE_FAILED");EditorApplication.Exit(code);}
    }
}
