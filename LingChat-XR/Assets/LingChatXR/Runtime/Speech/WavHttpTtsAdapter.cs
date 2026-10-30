using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LingChatXR.Speech
{
    public interface ITtsRequestFactory
    {
        UnityWebRequest Create(XrTtsSettings settings, string text);
    }

    /// <summary>Builds requests only; Create does not send or start a TTS service.</summary>
    public sealed class WavHttpTtsAdapter : ITtsRequestFactory
    {
        [Serializable]
        private sealed class MyNeuroRequest
        {
            public string text;
            public string text_language = "zh";
        }

        [Serializable]
        private sealed class Sbv2Request
        {
            public string ident;
            public float length_scale;
            public float sdp_ratio;
            public int speaker_id;
            public int style_id;
            public string text;
        }

        public UnityWebRequest Create(XrTtsSettings settings, string text)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.TryValidate(out string reason))
                throw new ArgumentException(reason, nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.endpoint) || string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("An explicit endpoint and nonempty text are required.");

            string json = CreateJson(settings, text);
            var request = new UnityWebRequest(settings.endpoint.Trim(), UnityWebRequest.kHttpVerbPOST);
            try
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerAudioClip(settings.endpoint.Trim(), AudioType.WAV)
                {
                    streamAudio = false,
                    compressed = false
                };
                request.timeout = settings.timeoutSeconds;
                request.redirectLimit = 0;
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.SetRequestHeader("Accept", "audio/wav, audio/x-wav, application/octet-stream");
                return request;
            }
            catch
            {
                request.Dispose();
                throw;
            }
        }

        public static string CreateJson(XrTtsSettings settings, string text)
        {
            if (settings.provider == XrTtsProvider.MyNeuroChinese)
                return JsonUtility.ToJson(new MyNeuroRequest { text = text });
            if (settings.provider != XrTtsProvider.Sbv2Japanese)
                throw new ArgumentOutOfRangeException(nameof(settings.provider));
            return JsonUtility.ToJson(new Sbv2Request
            {
                ident = settings.sbv2Ident,
                length_scale = settings.lengthScale,
                sdp_ratio = settings.sdpRatio,
                speaker_id = settings.speakerId,
                style_id = settings.styleId,
                text = text
            });
        }
    }
}
