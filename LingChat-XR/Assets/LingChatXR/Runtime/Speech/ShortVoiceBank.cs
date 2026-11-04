using System;
using UnityEngine;

namespace LingChatXR.Speech
{
    public enum CharacterVoiceMode { Clips, Muted, Tts }

    [Serializable]
    public sealed class ShortVoiceEntry
    {
        public string id, group;
        public AudioClip clip;
        [Range(0, 1)] public float volume = 1;
        [Min(0)] public float weight = 1;
        [Min(0)] public float cooldown = 2;
    }

    [CreateAssetMenu(menuName = "YuChat/互动短声配置")]
    public sealed class ShortVoiceBank : ScriptableObject
    {
        public ShortVoiceEntry[] entries = Array.Empty<ShortVoiceEntry>();
    }
}
