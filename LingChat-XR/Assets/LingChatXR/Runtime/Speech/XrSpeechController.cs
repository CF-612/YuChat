using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace LingChatXR.Speech
{
    public enum VoiceKind { None, Dialogue, Reaction }
    public enum VoiceOutcome { Started, Completed, Cancelled, Skipped, Failed }

    public interface IXrSpeechOutput
    {
        bool SpeakDialogue(string text);
        bool PlayReaction(AudioClip preloadedClip);
        void Stop();
    }

    /// <summary>
    /// One foreground voice owner. All entry points and callbacks run on Unity's main thread.
    /// No synthesis, settings import, or external service starts automatically.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class XrSpeechController : MonoBehaviour, IXrSpeechOutput
    {
        [SerializeField] private XrTtsSettings settings = new XrTtsSettings();
        private const ulong MaximumAudioBytes = 32UL * 1024UL * 1024UL;
        private AudioSource voice;
        private AudioClip ownedClip;
        private UnityWebRequest activeRequest;
        private Coroutine routine;
        private ITtsRequestFactory requestFactory = new WavHttpTtsAdapter();
        private long generation;
        private int reactionPriority;
        private float volume = 1, reactionGain = 1;
        public float ReactionGain { set { reactionGain = Mathf.Clamp01(value); if(voice!=null) voice.volume=volume*reactionGain; } }
        private readonly System.Collections.Generic.HashSet<string> reactionEvents = new System.Collections.Generic.HashSet<string>();
        private readonly System.Collections.Generic.Queue<string> recentReactionEvents = new System.Collections.Generic.Queue<string>();

        public VoiceKind CurrentKind { get; private set; }
        public bool IsWaiting => activeRequest != null;
        public bool IsPlaying => voice != null && voice.isPlaying;
        // Unity 6 无 AudioClip 时读取 time 会逐帧报警；静音状态没有可用语音时钟。
        public float PlaybackSeconds => voice == null || voice.clip == null ? 0f : voice.time;
        public Chat.XrVoiceRhythm Rhythm {get;private set;}
        public string LastStatus { get; private set; } = "Silent until an endpoint or cached reaction is explicitly supplied.";
        public long Generation => generation;
        public XrTtsSettings Settings => settings.Copy();
        public float Volume { get => volume; set { volume = Mathf.Clamp01(value); if (voice != null) voice.volume = volume * reactionGain; } }
        public float SampleAmplitude(float[] buffer)
        {
            if (!IsPlaying || buffer == null || buffer.Length == 0) return 0;
            voice.GetOutputData(buffer, 0); float sum = 0;
            foreach (float sample in buffer) sum += sample * sample;
            return Mathf.Sqrt(sum / buffer.Length);
        }
        public string SettingsPath => Path.Combine(Application.persistentDataPath, "xr-tts-v1.json");
        // Reaction outcomes never imply dialogue advancement; callers must inspect VoiceKind.
        public event Action<VoiceKind, VoiceOutcome, long> PlaybackEvent;

        private void Awake()
        {
            voice = GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.Stop();
            voice.clip = null;
        }

        public void Configure(XrTtsSettings value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!value.TryValidate(out string reason)) throw new ArgumentException(reason, nameof(value));
            settings = value.Copy();
            Stop();
        }

        public void SetRequestFactory(ITtsRequestFactory value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            requestFactory = value;
            Stop();
        }

        public bool LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var loaded = JsonUtility.FromJson<XrTtsSettings>(File.ReadAllText(SettingsPath));
                Configure(loaded);
                LastStatus = "Loaded XR-only TTS settings; no request sent.";
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                LastStatus = "XR TTS settings could not be loaded; current settings retained.";
                return false;
            }
        }

        public void SaveSettings()
        {
            if (!settings.TryValidate(out string reason)) throw new InvalidOperationException(reason);
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
        }

        public bool SpeakDialogue(string text)
        {
            if (!isActiveAndEnabled || voice == null) return false;
            long beforeStop = generation;
            Stop();
            // A cancellation observer may start a newer request synchronously.
            if (generation != beforeStop + 1) return false;
            long token = generation;
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(settings.endpoint))
            {
                LastStatus = "Dialogue skipped: no text or no configured endpoint. No fallback selected.";
                PlaybackEvent?.Invoke(VoiceKind.Dialogue, VoiceOutcome.Skipped, token);
                return false;
            }

            CurrentKind = VoiceKind.Dialogue;
            try
            {
                activeRequest = requestFactory.Create(settings.Copy(), text);
                var operation = activeRequest.SendWebRequest();
                LastStatus = "Waiting for configured TTS service.";
                StartOwnedRoutine(ReceiveAndPlay(activeRequest, operation, token), token);
                return true;
            }
            catch (Exception)
            {
                FailCurrent(token, "TTS request could not start. No fallback selected.");
                return false;
            }
        }

        // Consume the backend's existing sentence audio; never synthesize it a second time.
        public bool PlayDialogueAudio(string url, string bearerToken)
        {
            if (!isActiveAndEnabled || voice == null || CurrentKind != VoiceKind.None) return false;
            long token = ++generation;
            CurrentKind = VoiceKind.Dialogue;
            try
            {
                activeRequest = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
                activeRequest.timeout = 60;
                activeRequest.redirectLimit = 0;
                activeRequest.SetRequestHeader("Authorization", "Bearer " + bearerToken);
                StartOwnedRoutine(ReceiveAndPlay(activeRequest, activeRequest.SendWebRequest(), token), token);
                return true;
            }
            catch { FailCurrent(token, "Audio download failed."); return false; }
        }

        public bool PlayReaction(AudioClip preloadedClip) => PlayReaction(preloadedClip, 0, null);

        // 同一声音出口执行业务优先级；事件在抢占前去重，避免重复事件停止自身。
        public bool PlayReaction(AudioClip preloadedClip, int priority, string eventId)
        {
            if (!isActiveAndEnabled || voice == null || preloadedClip == null) return false;
            if (!string.IsNullOrEmpty(eventId) && reactionEvents.Contains(eventId)) return false;
            bool busy = CurrentKind != VoiceKind.None || IsWaiting || voice.isPlaying;
            int currentPriority = CurrentKind == VoiceKind.Reaction ? reactionPriority : 0;
            if (busy && priority <= currentPriority) return false;
            if (preloadedClip.loadState != AudioDataLoadState.Loaded)
            {
                LastStatus = "Cached reaction skipped: clip was not preloaded.";
                PlaybackEvent?.Invoke(VoiceKind.Reaction, VoiceOutcome.Skipped, generation);
                return false;
            }
            if (busy)
            {
                long beforeStop = generation;
                Stop();
                if (generation != beforeStop + 1 || CurrentKind != VoiceKind.None) return false;
            }
            if (!string.IsNullOrEmpty(eventId))
            {
                reactionEvents.Add(eventId);
                recentReactionEvents.Enqueue(eventId);
                if (recentReactionEvents.Count > 64) reactionEvents.Remove(recentReactionEvents.Dequeue());
            }
            reactionPriority = priority;
            long token = ++generation;
            StartVoice(preloadedClip, VoiceKind.Reaction, token);
            if (token != generation) return false;
            StartOwnedRoutine(WaitForPlayback(token), token);
            return token == generation;
        }

        private IEnumerator ReceiveAndPlay(UnityWebRequest request, UnityWebRequestAsyncOperation operation, long token)
        {
            while (!operation.isDone)
            {
                if (token != generation) yield break;
                if (request.downloadedBytes > MaximumAudioBytes)
                {
                    FailCurrent(token, "TTS response exceeded the 32 MiB audio limit.");
                    yield break;
                }
                yield return null;
            }
            if (token != generation) yield break;
            if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > MaximumAudioBytes)
            {
                FailCurrent(token, "TTS download or WAV decode failed. Local interaction remains available.");
                yield break;
            }

            AudioClip clip = null;
            try { clip = DownloadHandlerAudioClip.GetContent(request); }
            catch (Exception) { /* Invalid audio remains a silent failure below. */ }
            ReleaseRequest(false);
            if (clip == null)
            {
                FailCurrent(token, "TTS service did not provide a usable WAV clip.");
                yield break;
            }
            if (token != generation)
            {
                Destroy(clip);
                yield break;
            }
            ownedClip = clip;
            StartVoice(clip, VoiceKind.Dialogue, token);
            if (token != generation) yield break;
            yield return WaitForPlayback(token);
        }

        private void StartOwnedRoutine(IEnumerator work, long token)
        {
            Coroutine started = StartCoroutine(work);
            if (token == generation && CurrentKind != VoiceKind.None)
                routine = started;
            else if (started != null)
                StopCoroutine(started);
        }

        private void StartVoice(AudioClip clip, VoiceKind kind, long token)
        {
            Rhythm=kind==VoiceKind.Dialogue?Chat.XrVoiceRhythm.Build(clip):null;
            CurrentKind = kind;
            voice.clip = clip;
            voice.Play();
            LastStatus = kind == VoiceKind.Dialogue ? "Playing dialogue." : "Playing cached reaction.";
            PlaybackEvent?.Invoke(kind, VoiceOutcome.Started, token);
        }

        private IEnumerator WaitForPlayback(long token)
        {
            yield return null;
            while (token == generation && voice != null && voice.isPlaying) yield return null;
            if (token != generation) yield break;
            VoiceKind finished = CurrentKind;
            ClearPlayback();
            CurrentKind = VoiceKind.None;
            routine = null;
            LastStatus = "Playback completed.";
            PlaybackEvent?.Invoke(finished, VoiceOutcome.Completed, token);
        }

        /// <summary>
        /// Cancels client waiting/playback and invalidates late results. Aborting HTTP does
        /// not prove that the remote service stopped its GPU inference.
        /// </summary>
        public void Stop()
        {
            VoiceKind cancelled = CurrentKind;
            long cancelledToken = generation;
            ++generation;
            if (routine != null) StopCoroutine(routine);
            routine = null;
            ReleaseRequest(true);
            ClearPlayback();
            CurrentKind = VoiceKind.None;
            LastStatus = "Stopped locally; remote inference cancellation is unverified.";
            if (cancelled != VoiceKind.None)
                PlaybackEvent?.Invoke(cancelled, VoiceOutcome.Cancelled, cancelledToken);
        }

        private void FailCurrent(long token, string status)
        {
            if (token != generation) return;
            ReleaseRequest(true);
            ClearPlayback();
            CurrentKind = VoiceKind.None;
            routine = null;
            LastStatus = status;
            PlaybackEvent?.Invoke(VoiceKind.Dialogue, VoiceOutcome.Failed, token);
        }

        private void ReleaseRequest(bool abort)
        {
            if (activeRequest == null) return;
            if (abort) activeRequest.Abort();
            activeRequest.Dispose();
            activeRequest = null;
        }

        private void ClearPlayback()
        {
            if (voice != null)
            {
                voice.Stop();
                voice.clip = null;
                reactionGain=1; voice.volume=volume;
            }
            if (ownedClip != null) Destroy(ownedClip);
            ownedClip = null;
        }

        private void OnDisable() => Stop();
        private void OnApplicationPause(bool paused) { if (paused) Stop(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Stop(); }
    }
}
