using System;
using System.Collections.Generic;
using UnityEngine;

namespace LingChatXR.Speech
{
    // 所有本地互动与 Agent 发声意图共用此入口，无待播队列。
    [DisallowMultipleComponent]
    public sealed class ShortVoiceController : MonoBehaviour
    {
        [SerializeField] ShortVoiceBank bank;
        XrSpeechController voice;
        readonly Dictionary<string, string> lastClip = new Dictionary<string, string>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        int serial;
        long ownedGeneration;
        bool held, requiresHeld;
        int activePriority;
        public CharacterVoiceMode Mode { get; private set; } = CharacterVoiceMode.Clips;
        public string Status { get; private set; } = "短声就绪";
        public string CurrentClip { get; private set; } = "";
        public string CurrentEvent { get; private set; } = "";
        public ShortVoiceBank Bank => bank;

        void Awake()
        {
            voice = GetComponent<XrSpeechController>();
            bank = bank != null ? bank : Resources.Load<ShortVoiceBank>("LocalInteractionVoice/Bank");
            if (bank == null) Status = "短声素材未配置，互动仍可使用";
            else Preload();

        }

        public void Configure(ShortVoiceBank value) { bank = value; Preload(); }
        void Preload()
        {
            if(bank==null)return;
            foreach(var entry in bank.entries)
                if(entry?.clip!=null && entry.clip.loadState==AudioDataLoadState.Unloaded)entry.clip.LoadAudioData();
        }
        public void SetMode(CharacterVoiceMode value)
        {
            if (Mode == value) return;
            Mode = value;
            if (value == CharacterVoiceMode.Muted) StopVoice();
        }

        public void SetHeld(bool value)
        {
            held = value;
            if (!held && requiresHeld && voice.Generation == ownedGeneration) StopVoice();
        }

        public bool Play(string group, string eventId, int priority, bool onlyWhileHeld = false,
            float requestedAt = -1)
        {
            if (Mode == CharacterVoiceMode.Muted || voice == null || bank == null ||
                (onlyWhileHeld && !held) || (requestedAt >= 0 && Time.unscaledTime - requestedAt > 1)) return false;
            if (string.IsNullOrEmpty(eventId)) return false;
            bool occupied = voice.CurrentKind != VoiceKind.None;
            bool highPriority = occupied && priority > (occupied && voice.Generation == ownedGeneration ? activePriority : 0);
            if (!highPriority && lastPlayed.TryGetValue(group, out var at))
            {
                foreach (var entry in bank.entries)
                    if (entry.group == group && Time.unscaledTime - at < entry.cooldown) return false;
            }
            lastClip.TryGetValue(group, out var previous);
            float total = 0;
            foreach (var entry in bank.entries)
                if (Eligible(entry, group) && entry.id != previous) total += entry.weight;
            bool allowRepeat = total <= 0;
            if (allowRepeat)
                foreach (var entry in bank.entries) if (Eligible(entry, group)) total += entry.weight;
            if (total <= 0) { Status = "没有可用的 " + group + " 短声"; return false; }
            float pick = UnityEngine.Random.value * total;
            ShortVoiceEntry selected = null;
            foreach (var entry in bank.entries)
            {
                if (!Eligible(entry, group) || (!allowRepeat && entry.id == previous)) continue;
                selected = entry;
                pick -= entry.weight;
                if (pick <= 0) break;
            }
            if (selected == null || !voice.PlayReaction(selected.clip, priority, eventId)) return false;
            voice.ReactionGain = selected.volume;
            ownedGeneration = voice.Generation;
            requiresHeld = onlyWhileHeld;
            activePriority = priority;
            lastClip[group] = selected.id;
            lastPlayed[group] = Time.unscaledTime;
            CurrentClip = selected.id; CurrentEvent = eventId;
            Status = "播放 " + selected.id;
            return true;
        }

        static bool Eligible(ShortVoiceEntry entry, string group) =>
            entry != null && entry.group == group && entry.clip != null &&
            entry.clip.loadState == AudioDataLoadState.Loaded && entry.weight > 0;

        public bool Preview(string group) => Play(group, "preview-" + (++serial), 5);
        public void StopVoice()
        {
            voice?.Stop();
            CurrentClip = ""; CurrentEvent = ""; requiresHeld = false; activePriority = 0;
            Status = Mode == CharacterVoiceMode.Muted ? "角色静音" : "已停止角色声音";
        }
        [ContextMenu("试听/开心")] void Happy() => Preview("happy");
        [ContextMenu("试听/疑惑")] void Curious() => Preview("curious");
        [ContextMenu("试听/惊讶")] void Surprised() => Preview("surprised");
        [ContextMenu("试听/不满")] void Protest() => Preview("protest");
    }
}
