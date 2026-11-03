using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace LingChatXR.Chat
{
    public interface IXrVoiceCapture
    {
        bool Begin(out string error);
        byte[] Finish();
        void Cancel();
    }
    // Quest 使用 Unity Microphone；测试注入 PCM 夹具，不打开用户麦克风。
    public sealed class UnityVoiceCapture:IXrVoiceCapture
    {
        AudioClip clip;string device;
        public bool Begin(out string error)
        {
            error=null;
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                error="请允许麦克风权限，然后再次按住说话";return false;
            }
#endif
            if(Microphone.devices.Length==0){error="未找到麦克风，可继续打字";return false;}
            device=Microphone.devices[0];clip=Microphone.Start(device,false,30,16000);
            if(clip==null||clip.frequency!=16000){Cancel();error="麦克风无法使用 16kHz 录音，可继续打字";return false;}
            return true;
        }
        public byte[] Finish()
        {
            if(clip==null)return null;
            try
            {
                int frames=Microphone.GetPosition(device);if(frames<=0)return null;
                frames=Math.Min(frames,clip.samples);float[] pcm=new float[frames*clip.channels];
                if(!clip.GetData(pcm,0))return null;
                return Encode(pcm,clip.channels);
            }
            finally{Cancel();}
        }
        public void Cancel(){if(clip!=null){Microphone.End(device);UnityEngine.Object.Destroy(clip);clip=null;}device=null;}
        public static byte[] Encode(float[] pcm,int channels)
        {
            if(pcm==null||channels<1||pcm.Length%channels!=0)throw new ArgumentException("Invalid PCM");
            int frames=pcm.Length/channels;if(frames<1600||frames>16000*30)return null;
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+frames*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);
            writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(frames*2);
            for(int i=0;i<frames;i++){float mono=0;for(int c=0;c<channels;c++){float sample=pcm[i*channels+c];if(!float.IsFinite(sample))throw new ArgumentException("Non-finite PCM");mono+=sample/channels;}writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(mono,-1,1)*32767));}
            return stream.ToArray();
        }
    }
    public sealed class XrPushToTalk:MonoBehaviour
    {
        XrChatController chat;IXrVoiceCapture capture=new UnityVoiceCapture();float started;int conversation;
        public bool Recording {get;private set;}
        public string Status {get;private set;}="";
        public event Action<string> Recognized;
        public void SetStatus(string value){Status=value;}
        public void Configure(XrChatController controller){chat=controller;}
#if UNITY_EDITOR
        public void SetCaptureForProbe(IXrVoiceCapture value){Cancel();capture=value;}
#endif
        public bool Begin()
        {
            if(chat==null||Recording||!chat.CanRecord)return false;
            try{if(!capture.Begin(out string error)){Status=error;return false;}}
            catch(Exception){capture.Cancel();Status="麦克风启动失败，可继续打字";return false;}
            conversation=chat.ConversationGeneration;started=Time.realtimeSinceStartup;Recording=true;chat.VoiceInputActive=true;Status="正在听…松开后识别";return true;
        }
        public void Release()
        {
            if(!Recording)return;
            Recording=false;chat.VoiceInputActive=false;
            byte[] wav=null;try{wav=capture.Finish();}catch(Exception){Status="录音读取失败，草稿已保留";capture.Cancel();return;}
            if(conversation!=chat.ConversationGeneration)return;
            if(wav==null){Status="录音太短，请按住说完再松开";return;}
            Status="";chat.RecognizeWav(wav,text=>Recognized?.Invoke(text));
        }
        public void Cancel()
        {
            capture.Cancel();if(chat!=null){chat.VoiceInputActive=false;chat.CancelRecognition();}
            if(Recording)Status="录音已取消，草稿已保留";Recording=false;
        }
        void Update()
        {
            if(!Recording)return;
            if(!chat.Connected||chat.Busy||chat.Stopping||chat.Pairing||chat.Voice.IsPlaying||conversation!=chat.ConversationGeneration){Cancel();return;}
            // 提前一帧级余量，避免非循环录音达到末尾后 GetPosition 归零。
            if(Time.realtimeSinceStartup-started>=29)Release();
        }
        void OnApplicationFocus(bool focused){if(!focused)Cancel();}
        void OnApplicationPause(bool paused){if(paused)Cancel();}
        void OnDisable(){Cancel();}
    }
}
