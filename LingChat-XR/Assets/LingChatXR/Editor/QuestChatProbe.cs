using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using LingChatXR.Chat;
using LingChatXR.XR;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 真实 HTTP 边界的本机夹具，不调用云端模型或本地推理，不使用用户历史。
    public static class QuestChatProbe
    {
        static Fixture server;static IEnumerator work;static bool started;static double deadline;static JArray checks=new JArray();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        public static void Run()
        {
            Environment.SetEnvironmentVariable("XR_SKIP_LOADER","1");Environment.SetEnvironmentVariable("XR_HOST_CONNECTION",Path.Combine(Output,"absent-private-connection.json"));
            server=new Fixture();EditorSceneManager.OpenScene(QuestProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick(){if(EditorApplication.timeSinceStartup>deadline){Finish(new Exception("timeout"));return;}if(EditorApplication.isPlaying&&!started){started=true;new GameObject("Chat probe").AddComponent<QuestChatProbeRunner>();}}
        public static void GameTick(){try{var entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();if(entry!=null&&!entry.InputAvailable){entry.SendMessage("OnApplicationPause",false);entry.SendMessage("OnApplicationFocus",true);}work??=Checks();if(!work.MoveNext())Finish(null);}catch(Exception error){Finish(error);}}
        static void Check(bool value,string label){checks.Add(new JObject{["name"]=label,["passed"]=value});File.WriteAllText(Path.Combine(Output,"progress.json"),checks.ToString());if(!value)throw new Exception(label);}
        static IEnumerator Checks()
        {
            var entry=UnityEngine.Object.FindAnyObjectByType<QuestEntry>();var chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();
            yield return null;yield return null;
            Check(!XrChatController.ValidEndpoint("http://8.8.8.8:1234",true)&&!XrChatController.ValidEndpoint("http://example.com",true)&&!XrChatController.ValidEndpoint("http://192.168.1.2/token",true),"reject-public-hostname-and-path");
            Check(XrChatController.ValidEndpoint("http://192.168.1.2:17862",true)&&!XrChatController.ValidEndpoint("http://192.168.1.2:17862",false),"private-only-with-pairing");
            chat.Pair(server.Url,"99999999");while(chat.Pairing)yield return null;Check(!chat.Connected&&chat.PairedEndpoint=="","invalid-pair-preserves-state");
            chat.Pair(server.Url,"12345678");while(chat.Pairing)yield return null;Check(chat.Connected&&chat.PairedEndpoint==server.Url,"pair-connects-through-http");
            entry.SendMessage("OnApplicationPause",false);entry.SendMessage("OnApplicationFocus",true);
            while(!chat.AsrConfigured)yield return null;
            // 本夹具只注入网络和 UI 指针，不提供左手关节；显式固定模拟掌面供 uGUI 派发事件。
            Check(entry.Panel.Canvas.transform.IsChildOf(entry.Panel.PalmCanvas.transform),"menu-is-attached-to-palm");
            entry.Panel.PalmCanvas.transform.SetParent(entry.TrackingSpace,false);
            entry.Panel.PalmCanvas.gameObject.SetActive(true);
            var voiceChecks=VoiceChecks(entry,chat);while(voiceChecks.MoveNext())yield return voiceChecks.Current;
            entry.Panel.ShowDialogue();
            entry.Panel.MessageInput.GetComponent<XrFieldClick>().OnPointerClick(null);
            Check(entry.Panel.ComposerInput.gameObject.activeInHierarchy,"palm-input-opens-chest-composer");
            var keys=entry.Panel.ComposerInput.transform.parent.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard.XRKeyboardKey>();
            var key=Array.Find(keys,k=>k.keyCode==KeyCode.A||k.gameObject.name=="A");
            Check(key!=null&&key.gameObject.activeInHierarchy,"胸前实体键盘显示字母键");
            var press=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            key.OnPointerDown(press);key.OnPointerUp(press);key.OnPointerClick(press);
            Check(string.Equals(entry.Panel.ComposerInput.text,"a",StringComparison.OrdinalIgnoreCase),"实体键写入胸前草稿");
            var view=new GameObject("胸前键盘实物布局检查").AddComponent<Camera>();
            view.transform.position=entry.HeadCamera.transform.position;
            var fieldRect=(RectTransform)entry.Panel.ComposerInput.transform;Vector3 across=Vector3.zero;
            foreach(var k in keys){var kr=(RectTransform)k.transform;across+=kr.TransformPoint(kr.rect.center);}
            view.transform.LookAt(Vector3.Lerp(fieldRect.TransformPoint(fieldRect.rect.center),across/keys.Length,.5f));
            view.nearClipPlane=.02f;view.fieldOfView=74;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.08f,.12f,.16f);
            Check(Vector3.Distance(view.transform.position,key.transform.position)<.9f&&key.transform.position.y<view.transform.position.y,"实体按键在胸前伸手范围");
            Canvas.ForceUpdateCanvases();Alpha2Probe.Capture(view,Path.Combine(Output,"chest-keyboard.png"));UnityEngine.Object.Destroy(view.gameObject);
            entry.Panel.ComposerInput.text="你好，测试中文消息。";yield return null;
            entry.Panel.ComposerSend.onClick.Invoke();entry.Panel.ComposerSend.onClick.Invoke();
            float sendDeadline=Time.realtimeSinceStartup+12;while((server.Turns==0||entry.Panel.ComposerInput.text!="")&&Time.realtimeSinceStartup<sendDeadline)yield return null;
            Check(server.Turns==1&&entry.Panel.ComposerInput.text=="","胸前输入被接收后清空 草稿长度="+entry.Panel.ComposerInput.text.Length+" turns="+server.Turns+" ready="+chat.CanSend);
            Check(server.Turns==1,"double-send-does-not-duplicate-turn");
            Check(chat.History.Count==1&&chat.History[0].text=="你好，测试中文消息。","accepted-text-recorded-and-draft-cleared");
            while(!chat.Voice.IsPlaying)yield return null;
            Check(chat.FullSubtitle=="收到，这是中文回复。","reply-reuses-audio-typewriter-gate");
            chat.ContinueDialogue();yield return null;yield return null;
            Check(entry.Panel.DisplaySubtitle==chat.Subtitle&&chat.Subtitle=="收到，这是中文回复。","placement-state-does-not-overwrite-chat-subtitle");
            chat.ContinueDialogue();chat.Stop();while(chat.Stopping)yield return null;
            Check(!chat.Voice.IsPlaying&&!chat.Busy,"stop-clears-original-voice-queue");
            int turns=server.Turns;chat.Connect();while(!chat.Connected)yield return null;yield return null;
            Check(server.Turns==turns&&chat.History.Count==2&&!chat.Voice.IsPlaying,"reconnect-restores-history-without-resend-or-replay");
            entry.Panel.ShowHistory();yield return null;QuestSizeProbe.CapturePanel(entry.Panel.Canvas,"history-ui.png");
            Check(chat.History[1].text=="收到，这是中文回复。","history-only-chinese-reply");
            server.FailTurn=true;entry.Panel.ShowDialogue();entry.Panel.MessageInput.text="失败时保留这段草稿";yield return null;entry.Panel.SendButton.onClick.Invoke();while(chat.Busy)yield return null;
            Check(entry.Panel.MessageInput.text=="失败时保留这段草稿","failed-send-retains-editable-draft");
            QuestSizeProbe.CapturePanel(entry.Panel.Canvas,"chat-ui.png");entry.Panel.ShowPairing();yield return null;QuestSizeProbe.CapturePanel(entry.Panel.Canvas,"pairing-ui.png");
            chat.ClearPairing();while(chat.Connected||chat.Stopping)yield return null;yield return null;
            Check(chat.PairedEndpoint==""&&chat.History.Count==2,"clear-pairing-preserves-history-and-disconnects");
            Check(server.Turns==1,"no-background-or-reconnect-cloud-turn");
        }
        static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;server?.Dispose();if(error!=null)File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["checks"]=checks,["deviceTested"]=false,["cloudRequests"]=0,["audioIsFixture"]=true}.ToString());Debug.Log(error==null?"XR_M1_PASSED":"XR_CHAT_FAILED");EditorApplication.Exit(error==null?0:1);
        }
        sealed class CaptureFixture:IXrVoiceCapture
        {
            public bool Denied;public int Begins,Cancels;
            public bool Begin(out string error){Begins++;error=Denied?"麦克风权限未允许，可继续打字":null;return !Denied;}
            public byte[] Finish()=>UnityVoiceCapture.Encode(new float[3200],1);
            public void Cancel(){Cancels++;}
        }
        static IEnumerator VoiceChecks(QuestEntry entry,XrChatController chat)
        {
            entry.Panel.ShowDialogue();var input=entry.Panel.MessageInput;var voice=entry.Panel.VoiceInput;var capture=new CaptureFixture();voice.SetCaptureForProbe(capture);
            Check(chat.CanRecord,"configured-asr-is-ready-without-opening-microphone");
            yield return null;
            Check(chat.CloudRecognition&&Array.Exists(entry.Panel.Canvas.GetComponentsInChildren<UnityEngine.UI.Text>(),t=>t.text.Contains("联网识别")),"cloud-recognition-visible-before-recording");
            var pointer=entry.Panel.Canvas.GetComponentInChildren<XrPushToTalkButton>();
            var pointerEvent=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){pointerId=987};
            UnityEngine.EventSystems.ExecuteEvents.Execute(pointer.gameObject,pointerEvent,UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            Check(voice.Recording,"ugui-pointer-down-starts-capture");
            UnityEngine.EventSystems.ExecuteEvents.Execute(pointer.gameObject,pointerEvent,UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
            Check(!voice.Recording&&server.AsrRequests==0,"pointer-exit-discards-without-upload");
            capture.Denied=true;Check(!voice.Begin()&&!voice.Recording&&chat.CanSend,"permission-failure-keeps-text-fallback");capture.Denied=false;
            input.text="保留草稿";Check(voice.Begin()&&!voice.Begin()&&!chat.CanSend,"hold-starts-once-and-gates-send");
            yield return null;QuestSizeProbe.CapturePanel(entry.Panel.Canvas,"ptt-recording.png");
            voice.Release();voice.Release();while(chat.Recognizing)yield return null;
            File.WriteAllText(Path.Combine(Output,"ptt-result.json"),new JObject{["text"]=input.text,["requests"]=server.AsrRequests,["turns"]=server.Turns,["status"]=chat.RecognitionStatus,["captureStatus"]=voice.Status,["inputAvailable"]=entry.InputAvailable,["expanded"]=entry.Panel.IsExpanded}.ToString());
            Check(input.text=="保留草稿 语音识别测试"&&server.AsrRequests==1&&server.Turns==0,"release-fills-draft-once-without-auto-send");
            var reaction=AudioClip.Create("PTT playback gate fixture",32000,1,16000,false);chat.Voice.PlayReaction(reaction);yield return null;
            Check(chat.Voice.IsPlaying&&!voice.Begin(),"tts-reaction-playback-blocks-microphone");chat.Voice.Stop();UnityEngine.Object.Destroy(reaction);
            Check(voice.Begin(),"can-record-again-after-result");voice.SendMessage("OnApplicationFocus",false);yield return null;
            Check(!voice.Recording&&server.AsrRequests==1&&chat.CanSend,"focus-loss-discards-recording");
            Check(voice.Begin(),"record-after-focus-recovery");voice.Release();float postedDeadline=Time.realtimeSinceStartup+5;while(server.AsrRequests<2&&Time.realtimeSinceStartup<postedDeadline)yield return null;
            Check(server.AsrRequests==2,"second-recording-reaches-asr");
            entry.Panel.ShowHistory();while(chat.Recognizing)yield return null;
            float until=Time.realtimeSinceStartup+.5f;while(Time.realtimeSinceStartup<until)yield return null;
            Check(input.text=="保留草稿 语音识别测试","page-close-rejects-late-asr-text");entry.Panel.ShowDialogue();
            server.FailAsr=true;Check(voice.Begin(),"record-before-provider-failure");voice.Release();while(chat.Recognizing)yield return null;
            Check(input.text=="保留草稿 语音识别测试"&&chat.RecognitionStatus.Contains("失败"),"asr-failure-preserves-draft");server.FailAsr=false;
            Check(voice.Begin(),"record-before-stop");voice.Release();chat.Stop();while(chat.Stopping)yield return null;
            until=Time.realtimeSinceStartup+.5f;while(Time.realtimeSinceStartup<until)yield return null;
            Check(input.text=="保留草稿 语音识别测试"&&!chat.Recognizing,"stop-rejects-recognition-result");
            var pcm=UnityVoiceCapture.Encode(new[]{.5f,-.5f},2);Check(pcm==null,"short-recording-not-uploaded");
            var stereo=new float[6400];for(int n=0;n<stereo.Length;n+=2){stereo[n]=.5f;stereo[n+1]=-.5f;}var wav=UnityVoiceCapture.Encode(stereo,2);
            Check(wav.Length==6444&&BitConverter.ToInt16(wav,22)==1&&BitConverter.ToInt32(wav,24)==16000&&BitConverter.ToInt16(wav,44)==0,"pcm-averaged-to-mono-16k-wav");
            input.text="";voice.SetStatus("");QuestSizeProbe.CapturePanel(entry.Panel.Canvas,"ptt-ready.png");
        }
        sealed class Fixture:IDisposable
        {
            readonly HttpListener listener=new HttpListener();readonly string token=Guid.NewGuid().ToString();readonly List<object> history=new List<object>();readonly string audio=Guid.NewGuid()+".wav";
            public readonly string Url;public volatile int Turns,AsrRequests;public volatile bool FailTurn,FailAsr;long generation=1;int cursor;string turn;
            public Fixture(){var port=new TcpListener(IPAddress.Loopback,0);port.Start();int n=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();Url="http://127.0.0.1:"+n;listener.Prefixes.Add(Url+"/");listener.Start();_ = Serve();}
            async Task Serve(){try{while(listener.IsListening){var context=await listener.GetContextAsync();_ = Reply(context);}}catch(HttpListenerException){}catch(ObjectDisposedException){}}
            async Task Reply(HttpListenerContext c)
            {
                try
                {
                    string path=c.Request.Url.AbsolutePath;object response;
                    if(path=="/asr"&&c.Request.HttpMethod=="POST")
                    {
                        if(c.Request.Headers["Authorization"]!="Bearer "+token){c.Response.StatusCode=401;c.Response.Close();return;}
                        if(c.Request.Headers["X-XR-ASR-Cloud"]!="1"){c.Response.StatusCode=412;c.Response.Close();return;}
                        using var audio=new MemoryStream();await c.Request.InputStream.CopyToAsync(audio);
                        if(audio.Length<3244||c.Request.ContentType!="audio/wav")throw new Exception("Invalid recorded WAV");
                        System.Threading.Interlocked.Increment(ref AsrRequests);await Task.Delay(200);
                        if(FailAsr){c.Response.StatusCode=502;c.Response.Close();return;}
                        byte[] recognized=Encoding.UTF8.GetBytes("{\"text\":\"语音识别测试\"}");c.Response.ContentType="application/json";await c.Response.OutputStream.WriteAsync(recognized,0,recognized.Length);c.Response.Close();return;
                    }
                    string text=await new StreamReader(c.Request.InputStream).ReadToEndAsync();JObject body=string.IsNullOrEmpty(text)?new JObject():JObject.Parse(text);
                    if(path=="/pair")
                    {
                        if((string)body["code"]!="12345678"){c.Response.StatusCode=401;c.Response.Close();return;}response=new{token};
                    }
                    else
                    {
                        if(c.Request.Headers["Authorization"]!="Bearer "+token){c.Response.StatusCode=401;c.Response.Close();return;}
                        switch(path)
                        {
                            case "/asr":response=new{configured=true,cloud=true};break;
                            case "/session":response=new{sessionId="fixture",roleId="feiyu",generation,cursor,history};break;
                            case "/turn":
                                if(FailTurn){c.Response.StatusCode=503;c.Response.Close();return;}
                                Turns++;generation++;turn=Guid.NewGuid().ToString();history.Add(new{speaker="user",text=(string)body["text"]});history.Add(new{speaker="assistant",text="收到，这是中文回复。"});response=new{turnId=turn,generation};break;
                            case "/events":
                                var events=new List<object>();if(Turns>0&&cursor==0){foreach(string kind in new[]{"text","audio","streamEnd"})events.Add(new{cursor=++cursor,sessionId="fixture",roleId="feiyu",turnId=turn,generation,index=0,kind,message="收到，这是中文回复。",audioFile=audio});}response=new{events};break;
                            case "/stop":response=new{generation=++generation};break;
                            default:
                                if(path=="/audio/"+audio){byte[] wave=Wave();c.Response.ContentType="audio/wav";c.Response.ContentLength64=wave.Length;await c.Response.OutputStream.WriteAsync(wave,0,wave.Length);c.Response.Close();return;}c.Response.StatusCode=404;c.Response.Close();return;
                        }
                    }
                    byte[] bytes=Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(response));c.Response.ContentType="application/json";c.Response.ContentLength64=bytes.Length;await c.Response.OutputStream.WriteAsync(bytes,0,bytes.Length);c.Response.Close();
                }
                catch(Exception){try{c.Response.StatusCode=500;c.Response.Close();}catch{}}
            }
            static byte[] Wave(){using var memory=new MemoryStream();using var writer=new BinaryWriter(memory);int samples=16000;writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);writer.Write(new byte[samples*2]);return memory.ToArray();}
            public void Dispose(){listener.Close();}
        }
    }
    public sealed class QuestChatProbeRunner:MonoBehaviour{void Update()=>QuestChatProbe.GameTick();}
}
