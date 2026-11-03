using System;
namespace LingChatXR.Chat
{
    [Serializable]
    public sealed class DialoguePreferences
    {
        public float voice=.8f;
        public float effects=.8f;
        public bool effectEnabled=true;
        public bool auto;
        public int speed=80;
        public string role="feiyu";
    }
}
