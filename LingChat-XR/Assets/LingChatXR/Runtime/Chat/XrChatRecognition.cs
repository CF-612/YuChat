using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace LingChatXR.Chat
{
    // 复用原连接、请求生命周期与会话代次；识别只返回草稿，不创建 /turn。
    public sealed partial class XrChatController
    {
        [Serializable] sealed class AsrCapability { public bool configured;public bool cloud; }
        [Serializable] sealed class AsrText { public string text; }
        public bool AsrConfigured {get;private set;}
        public bool CloudRecognition {get;private set;}
        public bool Recognizing {get;private set;}
        public bool VoiceInputActive {get;internal set;}
        public string RecognitionStatus {get;private set;}="语音输入尚未连接";
        Coroutine recognition;
        RequestScope recognitionRequest;
        int recognitionGeneration;
        public int ConversationGeneration=>epoch;
        public bool CanRecord=>CanSend&&AsrConfigured&&!Voice.IsPlaying&&!Voice.IsWaiting&&!IsTyping;
        IEnumerator RefreshAsrAvailability()
        {
            int expected=epoch;
            using(var scope=Request("/asr"))
            {
                var request=scope.Value;yield return request.SendWebRequest();
                if(expected!=epoch)yield break;
                AsrConfigured=false;
                if(request.result==UnityWebRequest.Result.Success)
                {
                    try{var capability=JsonUtility.FromJson<AsrCapability>(request.downloadHandler.text);AsrConfigured=capability?.configured==true;CloudRecognition=capability?.cloud==true;}
                    catch(ArgumentException){ }
                }
                RecognitionStatus=AsrConfigured?(CloudRecognition?"联网识别：录音经 PC 发给已配置服务，可能产生用量":"本地识别：按住说话，松开后填入输入框"):"PC 语音识别未配置或宿主需更新，可继续打字";Notify();
            }
        }
        public bool RecognizeWav(byte[] wav,Action<string> result)
        {
            if(!CanRecord||wav==null||wav.Length<3244||wav.Length>960044)return false;
            Recognizing=true;RecognitionStatus="正在识别…";
            recognition=StartCoroutine(Recognize(wav,result,++recognitionGeneration,epoch));Notify();return true;
        }
        IEnumerator Recognize(byte[] wav,Action<string> result,int generation,int conversation)
        {
            try
            {
                using(var scope=new RequestScope(new UnityWebRequest(connection.endpoint+"/asr","POST")
                {uploadHandler=new UploadHandlerRaw(wav),downloadHandler=new DownloadHandlerBuffer(),timeout=25,redirectLimit=0},requests))
                {
                    recognitionRequest=scope;var request=scope.Value;
                    request.SetRequestHeader("Authorization","Bearer "+connection.token);request.SetRequestHeader("Content-Type","audio/wav");
                    if(CloudRecognition)request.SetRequestHeader("X-XR-ASR-Cloud","1");
                    yield return request.SendWebRequest();
                    if(generation!=recognitionGeneration||conversation!=epoch||!Connected)yield break;
                    if(request.result!=UnityWebRequest.Result.Success){RecognitionStatus=request.responseCode==503?"PC 语音识别未配置，可继续打字":request.responseCode==409?"识别服务忙，请稍后再试":"识别失败，请检查 PC 识别服务与网络；草稿已保留";yield break;}
                    string text=null;
                    try{text=JsonUtility.FromJson<AsrText>(request.downloadHandler.text)?.text?.Trim();}catch(ArgumentException){ }
                    if(string.IsNullOrEmpty(text)){RecognitionStatus="没有识别到文字，请重试";yield break;}
                    if(text.Length>2000){RecognitionStatus="识别文字过长，草稿已保留";yield break;}
                    RecognitionStatus="已填入输入框，检查后点击发送";result?.Invoke(text);
                }
            }
            finally{if(generation==recognitionGeneration){Recognizing=false;recognitionRequest=null;recognition=null;Notify();}}
        }
        public void CancelRecognition()
        {
            ++recognitionGeneration;recognitionRequest?.Dispose();recognitionRequest=null;
            if(recognition!=null)StopCoroutine(recognition);recognition=null;
            if(Recognizing)RecognitionStatus="识别已取消，草稿已保留";
            Recognizing=false;
        }
    }
}
