using System;

namespace LingChatXR.Speech
{
    public enum XrTtsProvider
    {
        MyNeuroChinese,
        Sbv2Japanese
    }

    [Serializable]
    public sealed class XrTtsSettings
    {
        public int version = 1;
        public XrTtsProvider provider = XrTtsProvider.MyNeuroChinese;
        // Full configured URL: my-neuro's TTS endpoint, or sbv2api's /synthesize.
        // Empty means silent. Quest must use the PC's LAN address, not its own localhost.
        public string endpoint = "";
        public int timeoutSeconds = 60;
        public string sbv2Ident = "Ling v2";
        public float lengthScale = 1f;
        public float sdpRatio = 0f;
        public int speakerId = 0;
        public int styleId = 0;

        public XrTtsSettings Copy() => (XrTtsSettings)MemberwiseClone();

        public bool TryValidate(out string reason)
        {
            reason = "";
            if (version != 1 || !Enum.IsDefined(typeof(XrTtsProvider), provider))
                reason = "Unsupported XR TTS settings version or provider.";
            else if (timeoutSeconds < 1 || timeoutSeconds > 300)
                reason = "TTS timeout must be between 1 and 300 seconds.";
            else if (!string.IsNullOrWhiteSpace(endpoint) &&
                (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                 !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)))
                reason = "Set a full HTTP(S) endpoint without embedded credentials or a fragment.";
            else if (provider == XrTtsProvider.Sbv2Japanese &&
                (string.IsNullOrWhiteSpace(sbv2Ident) || !Finite(lengthScale) || lengthScale <= 0f ||
                 !Finite(sdpRatio) || sdpRatio < 0f || sdpRatio > 1f || speakerId < 0 || styleId < 0))
                reason = "Invalid sbv2api voice parameters.";
            return reason.Length == 0;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
