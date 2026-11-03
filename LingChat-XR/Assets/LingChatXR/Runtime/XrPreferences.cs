using System;

namespace LingChatXR
{
    [Serializable]
    public sealed class XrPreferences
    {
        public int version = 2;
        public string modelSha256;
        public float heightMeters = 0.24f;
        public float halfWidth = 0.60f;
        public float halfDepth = 0.40f;
        public bool gazeEnabled = true;
        public bool touchEnabled = true;
        public float movementSpeed = 1f;
    }
}
