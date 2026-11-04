using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LingChatXR.Core;
using LingChatXR.Speech;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace LingChatXR.Chat
{
    [Serializable] public sealed class HostConnection { public string endpoint; public string token; }
    [Serializable] internal sealed class PairRequest { public string code; }
    [Serializable] public sealed class ChatRow { public string speaker; public string text; }
    [Serializable] public sealed class SessionReply { public string sessionId; public string roleId; public long generation; public int cursor; public int historyBefore; public ChatRow[] history; }
    [Serializable] public sealed class TurnReply { public string turnId; public long generation; }
    [Serializable] public sealed class StageDecision { public string action; public string reason; }
    [Serializable] public sealed class StageCandidate { public string id; public string label; public bool persistent; }
    [Serializable] public sealed class ChatEvent { public int cursor; public string sessionId; public string roleId; public string turnId; public long generation; public int index; public int sentenceEndExclusive; public string kind; public string message; public string audioFile; public string emotion; public string motion; public StageDecision stageAction; public string stageRequestId; }
    [Serializable] public sealed class EventReply { public ChatEvent[] events; public bool cursorExpired; public int cursor; public int historyBefore; public ChatRow[] history; }
    [Serializable] internal sealed class OpenRequest { public string roleId; public int historyLimit=50; }
    [Serializable] internal sealed class TurnRequest { public string sessionId; public string text; public bool testVoice; public string voiceMode; public string retryTurn; public int retryIndex; public bool director; public StageCandidate[] available; public string style; public string previousPose; }
    [Serializable] internal sealed class StopRequest { public string sessionId; public long expectedGeneration; }

    [RequireComponent(typeof(XrSpeechController))]
    public sealed partial class XrChatController : MonoBehaviour
    {
        public XrSpeechController Voice { get; private set; }
        public ShortVoiceController ShortVoice { get; private set; }
        public CharacterVoiceMode VoiceMode { get; private set; } = CharacterVoiceMode.Clips;
        public string Status { get; private set; } = "正在连接本地语音…";
        public string Subtitle { get; private set; } = "今天想聊些什么？";
        public bool Connected { get; private set; }
        public bool Busy { get; private set; }
        public bool Stopping { get; private set; }
        public event Action<string> MessageAccepted;
        public string Role { get; private set; } = "feiyu";
        int historyBefore;
        public int HistoryPageRevision {get;private set;}
        bool fetchingHistory;
        public bool HasEarlierHistory=>historyBefore>0 && Connected;
        public void LoadEarlierHistory(){if(HasEarlierHistory&&!fetchingHistory)StartCoroutine(FetchHistory());}
        IEnumerator FetchHistory()
        {
            int before=historyBefore,revision=epoch;
            fetchingHistory=true;
            try
            {
                using(var scope=Request("/history?sessionId="+Uri.EscapeDataString(session)+"&before="+before))
                {
                    yield return scope.Value.SendWebRequest();
                    if(revision!=epoch||historyBefore!=before)yield break;
                    if(scope.Value.result!=UnityWebRequest.Result.Success){Status="历史记录加载失败，可重试";yield break;}
                    var page=JsonUtility.FromJson<SessionReply>(scope.Value.downloadHandler.text);
                    if(page.history==null||page.historyBefore<0||page.historyBefore+page.history.Length!=before)yield break;
                    history.InsertRange(0,page.history);historyBefore=page.historyBefore;HistoryPageRevision++;
                }
            }
            finally{fetchingHistory=false;Notify();}
        }
        public IReadOnlyList<ChatRow> History => history;
        public event Action Changed;
        private readonly List<ChatRow> history = new List<ChatRow>();
        private readonly Dictionary<int, string> textByIndex = new Dictionary<int, string>();
        private readonly DialogueQueue queue = new DialogueQueue();
        private FeiyuAvatar motionAvatar;
        private readonly Dictionary<int,(ChatEvent item,float arrived)> stageDecisions=new Dictionary<int,(ChatEvent,float)>();
        private HostConnection connection;
        private string session;
        private int cursor;
        private int epoch;
        private string retryTurn;
        private int retryIndex;
        private Coroutine polling;
        readonly HashSet<RequestScope> requests=new HashSet<RequestScope>();
        sealed class RequestScope:IDisposable
        {
            public UnityWebRequest Value {get;private set;}
            readonly HashSet<RequestScope> owner;
            public RequestScope(UnityWebRequest value,HashSet<RequestScope> owner){Value=value;this.owner=owner;owner.Add(this);}
            public void Dispose(){var request=Value;if(request==null)return;Value=null;owner.Remove(this);request.Abort();request.Dispose();}
        }
        private bool connecting;
        public bool Pairing{get;private set;}
        private string PairingPath=>Path.Combine(Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY")??Application.persistentDataPath,"xr-paired-host.json");
        public string PairedEndpoint{get;private set;}="";
        private bool pendingTurn;
        private bool replaying;
        public bool CanSend => Connected && !Busy && !Stopping && !Pairing && !connecting && !VoiceInputActive && !Recognizing;
        public bool CanRetry => CanSend && !string.IsNullOrEmpty(retryTurn);
        public bool HasPlayingCue => queue.Playing;
        public DialogueEffects Effects { get; private set; }
        public bool Thinking { get; private set; }
        public bool IsTyping => writer.Typing || motionWriter.Typing;
        public bool AutoAdvance { get; set; }
        public string FullSubtitle => writer.Target;
        public string MotionText => motionWriter.Text;
        public string Emotion { get; private set; } = "";
        public int TextSpeed { get => writer.Speed; set { writer.Speed = motionWriter.Speed = Mathf.Clamp(value,1,100); } }
        public int PresentationRevision { get; private set; }
        private readonly DialoguePresentation writer = new DialoguePresentation(), motionWriter = new DialoguePresentation();
        private DialogueQueue.Cue currentCue;
        private bool voiceEnded;
        private bool presentationStarted;
        private bool appendNext;
        private float advanceAt = -1;
        private string PreferencesPath => Path.Combine(Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY")??Application.persistentDataPath,"xr-dialogue-preferences.json");
        private void Awake()
        {
            Voice = GetComponent<XrSpeechController>(); ShortVoice=gameObject.AddComponent<ShortVoiceController>(); Voice.PlaybackEvent += OnVoice; Effects = gameObject.AddComponent<DialogueEffects>();
            var p=new DialoguePreferences();
            try{p=PreferenceFiles.Load<DialoguePreferences>(PreferencesPath);}catch(Exception e)when(PreferenceFiles.IsStorageError(e)){Status="偏好文件无法读取，原文件已保留";}
            ApplyDialogue(p);
            Role=DialogueCatalog.ValidId(p.role)?p.role:"feiyu";
        }
        public DialoguePreferences ReadDialogue() => new DialoguePreferences{voiceMode=VoiceMode.ToString().ToLowerInvariant(),voice=Voice.Volume,effects=Effects.Volume,effectEnabled=Effects.Enabled,auto=AutoAdvance,speed=TextSpeed,role=Role};
        public bool SaveDialogue(DialoguePreferences value,out string error)
        {
            error=null;
            if(value==null||!float.IsFinite(value.voice)||value.voice<0||value.voice>1||!float.IsFinite(value.effects)||value.effects<0||value.effects>1||value.speed<1||value.speed>100){error="音量需为 0–100%，出字速度需为 1–100";return false;}
            if(value.voiceMode!="clips" && value.voiceMode!="muted" && value.voiceMode!="tts"){error="声音模式无效";return false;}
            if(!DialogueCatalog.ValidId(value.role)||(value.role!=Role&&!AvailableRoles.Any(r=>r.id==value.role))){error="该对话角色尚未接入";return false;}
            if(value.role!=Role&&(Busy||Stopping||connecting)){error="请等待当前对话或连接结束后切换角色";return false;}
            try
            {
                PreferenceFiles.Save(PreferencesPath,Newtonsoft.Json.Linq.JObject.FromObject(value));ApplyDialogue(value);
                if(value.role!=Role)
                {
                    if(Connected)SwitchRole(value.role);
                    else{Role=value.role;history.Clear();Subtitle="今天想聊些什么？";}
                }
                Notify();return true;
            }
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){error="保存失败，请检查存储权限或配置格式；原文件已保留";return false;}
        }
        private void ApplyDialogue(DialoguePreferences p)
        {
            var nextMode = p.voiceMode=="tts" ? CharacterVoiceMode.Tts : p.voiceMode=="muted" ? CharacterVoiceMode.Muted : CharacterVoiceMode.Clips;
            if (VoiceMode != nextMode) Voice.Stop();
            VoiceMode = nextMode; ShortVoice.SetMode(nextMode);
            Voice.Volume=float.IsFinite(p.voice)?Mathf.Clamp01(p.voice):.8f;Effects.Volume=float.IsFinite(p.effects)?Mathf.Clamp01(p.effects):.8f;
            Effects.Enabled=p.effectEnabled;AutoAdvance=p.auto;TextSpeed=p.speed;if(!p.effectEnabled)Effects.Stop();
        }
        public void SavePreferences()
        {
            if(!SaveDialogue(ReadDialogue(),out string error)){Status=error;Notify();}
        }
        public void ReloadHostSettings()=>StartCoroutine(ReloadSettings());
        private IEnumerator ReloadSettings()
        {
            if(!Connected){Status="配置已保存，连接服务后可应用";Notify();yield break;}
            using(var scope=Request("/reload-settings","{}"))
            {
                var request=scope.Value;
                yield return request.SendWebRequest();
                Status=request.result==UnityWebRequest.Result.Success?"XR 服务配置已应用":request.responseCode==404?"配置已保存；当前宿主版本不支持热更新，下次启动生效":request.responseCode==409?"配置已保存；请等待对话结束后应用服务配置":"配置已保存，服务应用失败；请检查配置后重试";Notify();
            }
        }
        private void Start() { Connect(); }
        public void Connect() { if(!connecting&&!Pairing)StartCoroutine(ConnectLocal()); }
        private IEnumerator ConnectLocal()
        {
            CancelRecognition();AsrConfigured=false;
            connecting=true;
            try
            {
                if(Busy||Stopping){Stop();while(Stopping)yield return null;}
                // 重连只恢复历史和游标，不重发输入或重播旧音频。
                ++epoch;queue.Cancel();currentCue=null;writer.Cancel();motionWriter.Cancel();Voice.Stop();Effects.Stop();Connected=false;
                if(polling!=null){StopCoroutine(polling);polling=null;}
#if UNITY_EDITOR
                // 本机 Editor 的宿主按需启动；隔离测试/显式远端连接仍由调用方提供。
                if(!File.Exists(PairingPath)&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XR_HOST_CONNECTION")))
                {
                    string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
                    string script=Path.Combine(root,"tools/xr-host/start-host.ps1");
                    string binary=Path.Combine(root,"tools/xr-host/target/c0/debug/lingchat-xr-host.exe");
                    if(File.Exists(script)&&File.Exists(binary))
                    {
                        Status="正在确认本地语音服务…";Notify();
                        System.Diagnostics.Process process=null;
                        try
                        {
                            process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                                "-NoProfile -ExecutionPolicy Bypass -File \""+script+"\"")
                                {UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,
                                RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root});
                        }
                        catch(Exception){Status="本地服务启动失败 · 请检查宿主日志";Connected=false;Notify();}
                        if(process!=null)
                        {
                            using(process)
                            {
                                // 异步排空输出，日志不包含连接令牌；不阻塞 Unity 帧循环。
                                var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                                float until=Time.realtimeSinceStartup+40;
                                while(!process.HasExited&&Time.realtimeSinceStartup<until)yield return null;
                                if(!process.HasExited||process.ExitCode!=0)
                                {
                                    string output=stdout.IsCompleted&&!stdout.IsFaulted?stdout.Result:"";
                                    Status=!process.HasExited?"本地服务启动超时 · 稍后重新连接":
                                        output.Contains("XR_HOST_FAILED:settings")?"本地服务配置读取失败 · 请检查服务设置":
                                        output.Contains("XR_HOST_FAILED:port")?"本地服务端口被占用或凭据失效":
                                        output.Contains("XR_HOST_FAILED:build")?"本地服务编译失败 · 请检查宿主日志":
                                        "本地语音启动失败 · 请检查宿主日志";
                                    Connected=false;Notify();yield break;
                                }
                            }
                        }
                    }
                }
#endif
                yield return Open(Role);
            }
            finally{connecting=false;}
        }
        private string ConnectionPath()
        {
            if(File.Exists(PairingPath))return PairingPath;
            string configured = Environment.GetEnvironmentVariable("XR_HOST_CONNECTION");
            if (!string.IsNullOrEmpty(configured)) return configured;
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../../.cache/xr-host-connection.json"));
#else
            return Path.Combine(Application.persistentDataPath, "xr-host-connection.json");
#endif
        }
        private IEnumerator Open(string role)
        {
            int token = ++epoch;
            string connectionFailure=null;
            try
            {
                connection = JsonUtility.FromJson<HostConnection>(File.ReadAllText(ConnectionPath()));
                bool paired=ConnectionPath()==PairingPath;
                if (connection==null || !ValidEndpoint(connection.endpoint,paired) || string.IsNullOrEmpty(connection.token)) throw new InvalidDataException();
                PairedEndpoint=paired?connection.endpoint:"";
            }
            catch(FileNotFoundException){connectionFailure="本地连接文件不存在 · 可点重新连接启动服务";}
            catch(DirectoryNotFoundException){connectionFailure="本地连接目录不存在 · 可点重新连接启动服务";}
            catch(InvalidDataException){connectionFailure="本地连接配置无效 · 可点重新连接";}
            catch(ArgumentException){connectionFailure="本地连接配置无法解析 · 可点重新连接";}
            catch(IOException){connectionFailure="无法读取本地连接文件 · 检查文件权限后重试";}
            catch(UnauthorizedAccessException){connectionFailure="本地连接文件无读取权限 · 检查权限后重试";}
            if(connectionFailure!=null){Status=connectionFailure;Connected=false;Notify();yield break;}
            yield return FetchCatalog(token);
            if(token!=epoch)yield break;
            if(!catalogReady){Connected=false;Notify();yield break;}
            if(!AvailableRoles.Any(r=>r.id==role)){Status="已选角色未在此服务注册 · 请在设置中选择角色";Connected=false;Notify();yield break;}
            using (var scope = Request("/session", JsonUtility.ToJson(new OpenRequest { roleId = role })))
            {
                var request=scope.Value;
                yield return request.SendWebRequest();
                if (token != epoch) yield break;
                if (request.result != UnityWebRequest.Result.Success) { Status = "无法连接本地服务 · 可点重新连接"; Connected = false; Notify(); yield break; }
                var reply = JsonUtility.FromJson<SessionReply>(request.downloadHandler.text);
                session = reply.sessionId; Role = reply.roleId; cursor = reply.cursor;historyBefore=reply.historyBefore;
                history.Clear(); if (reply.history != null) history.AddRange(reply.history);
                Connected = true; Status = "已连接 · "+VoiceLabel; Notify();
                StartCoroutine(RefreshAsrAvailability());
                if (polling != null) StopCoroutine(polling);
                polling = StartCoroutine(Poll());
            }
        }
        public static bool ValidEndpoint(string value,bool allowPrivate)
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="http"||uri.AbsolutePath!="/"||uri.Query!=""||uri.Fragment!=""||uri.UserInfo!=""||uri.Port<=0)return false;
            if(uri.IsLoopback)return true;
            if(!allowPrivate||!System.Net.IPAddress.TryParse(uri.Host,out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)return false;
            var bytes=ip.GetAddressBytes();return bytes[0]==10||(bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)||(bytes[0]==192&&bytes[1]==168);
        }
        public void Pair(string address,string code)
        {
            if(Pairing||connecting)return;
            address=(address??"").Trim().TrimEnd('/');code=(code??"").Trim();
            if(!ValidEndpoint(address,true)||code.Length!=8||code.Any(c=>c<'0'||c>'9')){Status="请输入 PC 显示的私网地址和 8 位配对码";Notify();return;}
            StartCoroutine(PairWithHost(address,code));
        }
        private IEnumerator PairWithHost(string address,string code)
        {
            Pairing=true;Status="正在配对 PC…";Notify();
            try
            {
                Stop();while(Stopping)yield return null;
                using(var scope=new RequestScope(new UnityWebRequest(address+"/pair","POST"){downloadHandler=new DownloadHandlerBuffer(),uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new PairRequest{code=code}))),timeout=10,redirectLimit=0},requests))
                {
                    var request=scope.Value;
                    request.SetRequestHeader("Content-Type","application/json");yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success){Status="配对失败：检查地址、配对码及 PC 网关状态";Notify();yield break;}
                    string failure=null;try
                    {
                        var result=JsonUtility.FromJson<HostConnection>(request.downloadHandler.text);
                        if(result==null||!Guid.TryParse(result.token,out _))throw new InvalidDataException();
                        // 地址仅采用用户输入，不信任网络回复中的跳转地址。
                        result.endpoint=address;PreferenceFiles.Write(PairingPath,Newtonsoft.Json.Linq.JObject.FromObject(result));PairedEndpoint=address;
                    }
                    catch(Exception e)when(PreferenceFiles.IsStorageError(e)||e is ArgumentException){failure="配对凭据无法保存，原连接保持";}
                    if(failure!=null){Status=failure;Notify();yield break;}
                }
                yield return ConnectLocal();
            }
            finally{Pairing=false;Notify();}
        }
        public void ClearPairing(){if(!Pairing&&!connecting)StartCoroutine(ForgetPairing());}
        private IEnumerator ForgetPairing()
        {
            Stop();while(Stopping)yield return null;++epoch;if(polling!=null){StopCoroutine(polling);polling=null;}
            Connected=false;connection=null;session=null;string error=null;
            try{if(File.Exists(PairingPath))File.Delete(PairingPath);PairedEndpoint="";}catch(Exception e)when(PreferenceFiles.IsStorageError(e)){error="无法清除本机配对，请检查文件权限";}
            Status=error??"已清除本机配对；聊天历史保留在 PC";Notify();
        }
        public void Send(string text) { if (CanSend && !string.IsNullOrWhiteSpace(text)) StartCoroutine(StartTurn(text, false, false)); }
        public void TestVoice() { if(VoiceMode!=CharacterVoiceMode.Tts){ShortVoice.Preview("happy");return;} if (CanSend) StartCoroutine(StartTurn("", true, false)); }
        public void RetryVoice() { if (Connected && CanRetry) StartCoroutine(StartTurn("", false, true)); }
        private IEnumerator StartTurn(string text, bool test, bool retry)
        {
            Busy = true; Status = test ? "准备试听…" : retry ? "重试语音…" : "思考中…";
            writer.Cancel(); motionWriter.Cancel(); currentCue = null; advanceAt = -1; appendNext = false;
            Subtitle = ""; Emotion = ""; Thinking = true; Effects.Thinking(); PresentationRevision++;
            int token = epoch; textByIndex.Clear(); replaying = retry; pendingTurn = true; Notify();
            stageDecisions.Clear();
            var stage=FindAnyObjectByType<FeiyuPreview>();motionAvatar=stage!=null&&stage.Ready?stage.Avatar:null;
            var player=motionAvatar?.Motions;
            bool director=!test&&!retry&&player!=null&&player.Preferences.enabled&&player.Preferences.director;
            using (var scope = Request("/turn", JsonUtility.ToJson(new TurnRequest { sessionId = session, text = text, testVoice = test, voiceMode=VoiceMode.ToString().ToLowerInvariant(), retryTurn = retry ? retryTurn : "", retryIndex = retryIndex,
                director=director,style=player?.Preferences.style??"normal",previousPose=player?.PoseId??"",available=director?player.Capabilities().Select(a=>new StageCandidate{id=a.id,label=a.label,persistent=a.poseLoop!=null}).ToArray():Array.Empty<StageCandidate>() })))
            {
                var request=scope.Value;
                yield return request.SendWebRequest();
                pendingTurn = false;
                if (token != epoch) yield break;
                if (request.result != UnityWebRequest.Result.Success) { Busy = false; Thinking = false; Effects.Stop(); Status = "请求失败，文字可保留后重试"; Notify(); yield break; }
                var reply = JsonUtility.FromJson<TurnReply>(request.downloadHandler.text);
                queue.Begin(session, Role, reply.turnId, reply.generation, retry ? retryIndex : 0);
                if(motionAvatar!=null)motionAvatar.Motions.BeginTurn(reply.turnId);
                retryTurn = null;
                if (!test && !retry) { history.Add(new ChatRow { speaker = "user", text = text }); MessageAccepted?.Invoke(text); }
                Notify();
            }
        }
        private IEnumerator Poll()
        {
            while (Connected)
            {
                // Do not fetch until /turn returned its generation; otherwise early events can be lost.
                if (Busy && !pendingTurn && queue.Turn != null && !queue.Ended)
                using (var scope = Request("/events?sessionId=" + Uri.EscapeDataString(session) + "&after=" + cursor))
                {
                    var request=scope.Value;
                    int token = epoch;
                    yield return request.SendWebRequest();
                    if (token != epoch) continue;
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var reply = JsonUtility.FromJson<EventReply>(request.downloadHandler.text);
                        if(reply.cursorExpired)
                        {
                            StopGeneration(queue.Generation);
                            cursor=reply.cursor;
                            historyBefore=0;history.Clear();
                            if(reply.history!=null)history.AddRange(reply.history);
                            Status="连接中断过久，本轮播放已结束，已恢复对话记录";
                            Notify();
                        }
                        else if (reply.events != null) foreach (var item in reply.events) Accept(item);
                    }
                    else { Status = "连接中断，请停止后重新连接"; Notify(); }
                }
                yield return new WaitForSecondsRealtime(.2f);
            }
        }
        public void Accept(ChatEvent item)
        {
            cursor = Math.Max(cursor, item.cursor);
            if (!queue.Matches(item.sessionId, item.roleId, item.turnId, item.generation)) return;
            switch (item.kind)
            {
                case "stageAction":
                    if(!replaying&&item.stageAction!=null&&!stageDecisions.ContainsKey(item.index))
                    {stageDecisions[item.index]=(item,Time.unscaledTime);TryStageDecision();}
                    break;
                case "text":
                    // 生成文本只缓存。合成/下载期间绝不提前揭示正文或播放打字效果。
                    if (!textByIndex.ContainsKey(item.index)) textByIndex[item.index] = item.message;
                    break;
                case "silent":
                case "audio": queue.Add(item.sessionId, item.roleId, item.turnId, item.generation, new DialogueQueue.Cue { Index = item.index, Text = item.message, Audio = item.audioFile, Emotion = item.emotion, Motion = item.motion }); break;
                case "sentenceError":
                    retryTurn = item.turnId; retryIndex = item.index;
                    queue.Add(item.sessionId, item.roleId, item.turnId, item.generation, new DialogueQueue.Cue { Index = item.index, Text = textByIndex.TryGetValue(item.index, out var text) ? text : "", Emotion=item.emotion, Motion=item.motion, Failed = true });
                    Status = item.message; break;
                case "streamEnd":
                    if(!queue.End(item.sentenceEndExclusive))
                    {
                        StopGeneration(queue.Generation);
                        Status="本轮回复缺少句段，播放已结束，可重新连接查看记录";
                    }
                    break;
                case "error": Status = item.message; Thinking = false; Effects.Stop(); break;
                case "thinkingProgress": if (Thinking) Status = "小肥鱼正在思考…（已思考 " + item.message + " 字）"; break;
                default: if (currentCue == null) Status = item.kind == "synthesizing" || item.kind == "translating" ? "正在准备语音…" : "小肥鱼正在思考…"; break;
            }
            Notify();
        }
        private void Update()
        {
            if (!Busy || pendingTurn) return;
            bool updated = writer.Step(Time.unscaledDeltaTime);
            updated |= motionWriter.Step(Time.unscaledDeltaTime);
            if(updated) { Subtitle=writer.Text; if(currentCue != null && currentCue.Failed)Effects.TypeTick(); Notify(); }
            if(currentCue != null && voiceEnded && !IsTyping)
            {
                var next=queue.Peek(); bool merge=next!=null && writer.Target.Length+next.Text.Length<=55;
                if(AutoAdvance || merge)
                {
                    if(advanceAt<0)advanceAt=Time.unscaledTime+(merge?.2f:1f);
                    if(Time.unscaledTime>=advanceAt)Advance(merge);
                }
            }
            if (Voice.CurrentKind == VoiceKind.None && currentCue == null)
            {
                var cue = queue.Take();
                if (cue != null)
                {
                    currentCue=cue; voiceEnded=false; presentationStarted=false; advanceAt=-1;
                    if (VoiceMode != CharacterVoiceMode.Tts || cue.Failed)
                    {
                        BeginPresentation(); voiceEnded=true;
                        if(VoiceMode==CharacterVoiceMode.Clips && !cue.Failed)
                            ShortVoice.Play(Emotion=="惊讶"?"surprised":Emotion=="开心"?"happy":"curious",queue.Turn+"-"+cue.Index,1);
                    }
                    else if (Guid.TryParse(Path.GetFileNameWithoutExtension(cue.Audio), out _) && cue.Audio.EndsWith(".wav") && !cue.Audio.Contains("/"))
                    {
                        if(!Thinking && string.IsNullOrEmpty(Subtitle))Thinking=true;
                        Status = "正在准备语音…";
                        if (!Voice.PlayDialogueAudio(connection.endpoint + "/audio/" + cue.Audio, connection.token) && !voiceEnded) FailCue();
                    }
                    else FailCue();
                    Notify();
                }
            }
            if (queue.Ended && queue.Empty) { Busy = false; Thinking=false; Effects.Stop(); if (string.IsNullOrEmpty(retryTurn) && Status == "播放中") Status = "准备好听你说话了"; Notify(); }
        }
        private void BeginPresentation()
        {
            if(currentCue==null)return;
            presentationStarted=true;
            Thinking=false; Effects.Stop(); Emotion=currentCue.Emotion??"";
            writer.Start(currentCue.Text,appendNext); motionWriter.Start(currentCue.Motion??"");
            Subtitle=writer.Text; if(!appendNext)PresentationRevision++; appendNext=false;
            if(!replaying)history.Add(new ChatRow{speaker="assistant",text=currentCue.Text});
            TryStageDecision();
            Status=currentCue.Failed?"语音暂不可用 · 可阅读文字或重试语音":"播放中"; Notify();
        }
        private void FailCue()
        {
            if(currentCue==null)return;
            retryTurn=queue.Turn; retryIndex=currentCue.Index; currentCue.Failed=true;
            if(!presentationStarted)BeginPresentation();
            voiceEnded=true;Status="语音播放失败 · 可重试已有语音";Notify();
        }
        private void Advance(bool merge)
        {
            if(currentCue==null)return;
            currentCue=null; queue.Complete(); advanceAt=-1; appendNext=merge;
        }
        public void ContinueDialogue()
        {
            if(currentCue==null || Thinking)return;
            if(IsTyping) { writer.Finish();motionWriter.Finish();Subtitle=writer.Text;Notify();return; }
            // 同 LingChat：第一次补全文字，第二次才推进。显式推进才允许截断当前音频。
            if(Voice.CurrentKind==VoiceKind.Dialogue)Voice.Stop();
            Advance(false);Notify();
        }
        private void OnVoice(VoiceKind kind, VoiceOutcome outcome, long generation)
        {
            if (kind != VoiceKind.Dialogue) return;
            if(outcome==VoiceOutcome.Started) { BeginPresentation(); return; }
            if (outcome == VoiceOutcome.Completed || outcome == VoiceOutcome.Failed || outcome == VoiceOutcome.Cancelled)
            {
                voiceEnded=true;
                if (outcome == VoiceOutcome.Failed) FailCue();
                Notify();
            }
        }
        private void TryStageDecision()
        {
            // 接收和显示可能错序；只消费实际正在播放的句段，超时/切模型/重放均不追补。
            if(currentCue==null||!presentationStarted||(!currentCue.Failed&&VoiceMode==CharacterVoiceMode.Tts&&!Voice.IsPlaying)||motionAvatar==null||!stageDecisions.TryGetValue(currentCue.Index,out var decision))return;
            stageDecisions.Remove(currentCue.Index);
            var stage=FindAnyObjectByType<FeiyuPreview>();
            if(stage==null||stage.Avatar!=motionAvatar||Time.unscaledTime-decision.arrived>10)return;
            _=motionAvatar.Motions.Decide(queue.Turn,decision.item.stageRequestId,decision.item.stageAction.action);
        }
        // 互动打断仅取消旧角色发声与迟到台词，麦克风和空间动作保持各自生命周期。
        public void InterruptCharacterVoice()
        {
            if (!Busy && Voice.CurrentKind != VoiceKind.Dialogue) return;
            ++epoch; queue.Cancel(); currentCue=null; writer.Cancel(); motionWriter.Cancel();
            Voice.Stop(); Effects.Stop(); Thinking=false; Busy=false; stageDecisions.Clear();
            if (Connected && session != null && isActiveAndEnabled && !Stopping)
            {
                Stopping=true; StartCoroutine(StopRemote(session,0));
            }
            Notify();
        }
        public void Stop()=>StopGeneration(0);
        private void StopGeneration(long expectedGeneration)
        {
            CancelRecognition();
            if (Stopping) return;
            ++epoch; queue.Cancel(); currentCue=null; writer.Cancel();motionWriter.Cancel(); Voice.Stop(); Effects.Stop(); Thinking=false; Busy = false; Status = "已停止";
            motionAvatar?.Motions.EndTurn();stageDecisions.Clear();motionAvatar=null;
            var spatial=FindAnyObjectByType<FeiyuPreview>();if(spatial!=null&&spatial.Ready)spatial.Avatar.Motions?.CancelSpeech();Notify();
            if (Connected && session != null && isActiveAndEnabled) { Stopping = true; Notify(); StartCoroutine(StopRemote(session,expectedGeneration)); }
        }
        private IEnumerator StopRemote(string id,long expectedGeneration)
        {
            // 等待在途 /turn 返回后再取消，避免旧停止请求误取消下一轮。
            while (pendingTurn) yield return null;
            using (var scope = Request("/stop", JsonUtility.ToJson(new StopRequest { sessionId = id, expectedGeneration = expectedGeneration })))
            {
                var request=scope.Value;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { Connected = false; Status = "本地播放已停，服务连接中断"; }
            }
            Stopping = false; Notify();
        }
        public void SwitchRole(string role) { StartCoroutine(ChangeRole(role)); }
        private IEnumerator ChangeRole(string role)
        {
            Stop(); while (Stopping) yield return null;
            Connected = false; retryTurn = null; Subtitle = "今天想聊些什么？"; yield return Open(role);
        }
        private RequestScope Request(string path, string json = null)
        {
            var r = new UnityWebRequest(connection.endpoint + path, json == null ? "GET" : "POST") { downloadHandler = new DownloadHandlerBuffer(), timeout = 15, redirectLimit = 0 };
            r.SetRequestHeader("Authorization", "Bearer " + connection.token);
            if (json != null) { r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); r.SetRequestHeader("Content-Type", "application/json"); }
            return new RequestScope(r,requests);
        }
        private void Notify() => Changed?.Invoke();
        private void OnApplicationPause(bool paused) { if (paused && Busy) Stop(); }
        private void OnApplicationFocus(bool focused) { if (!focused && Busy) Stop(); }
        private void OnDisable()
        {
            CancelRecognition();AsrConfigured=false;
            ++epoch; queue.Cancel(); currentCue=null;writer.Cancel();motionWriter.Cancel();Voice.Stop();Effects.Stop();Thinking=false; Busy = false;
            StopAllCoroutines();foreach(var request in requests.ToArray())request.Dispose();
            // 脚本域退出后不能再创建带 Unity 完成回调的原生请求。
            // 通知仅用托管 HTTP，延续原来的尽力取消语义，不触碰 Unity 对象或旧台词。
            if (connection != null && session != null)
            {
                _=NotifyStopped(connection.endpoint,connection.token,JsonUtility.ToJson(new StopRequest { sessionId = session }));
            }
        }
        static async System.Threading.Tasks.Task NotifyStopped(string endpoint,string token,string json)
        {
            try
            {
                using var handler=new System.Net.Http.HttpClientHandler{UseProxy=false,AllowAutoRedirect=false};
                using var client=new System.Net.Http.HttpClient(handler){Timeout=TimeSpan.FromSeconds(2)};
                using var message=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,endpoint+"/stop");
                message.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
                message.Content=new System.Net.Http.StringContent(json,Encoding.UTF8,"application/json");
                using var response=await client.SendAsync(message).ConfigureAwait(false);
            }
            catch(System.Net.Http.HttpRequestException) { /* 本地服务已经关闭时不再重试退出通知。 */ }
            catch(System.Threading.Tasks.TaskCanceledException) { /* 退出通知不阻塞脚本域回收。 */ }
        }
        private void OnDestroy() { if (Voice != null) Voice.PlaybackEvent -= OnVoice; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if ((scene == "FeiyuPreview" || scene == "F2ChatPreview" || scene == "Alpha2Preview") && FindAnyObjectByType<XrChatController>() == null)
            { var root = new GameObject("XR Dialogue"); root.AddComponent<XrChatController>(); root.AddComponent<XrChatView>(); }
        }
    }
}
