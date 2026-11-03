using UnityEngine;
namespace LingChatXR.Chat
{
    // 与 LingChat 相同：气泡音效独立于唯一人声源，不驱动口型/语音队列。
    public sealed class DialogueEffects : MonoBehaviour
    {
        private AudioSource effect;
        private AudioClip thinking, typing;
        public float Volume { get => effect == null ? .8f : effect.volume; set { if(effect!=null)effect.volume=Mathf.Clamp01(value); } }
        public bool Enabled { get; set; } = true;
        public int ThinkingStarts { get; private set; }
        public bool Ready => thinking != null && typing != null;
        void Awake()
        {
            var child=new GameObject("Dialogue effect audio");child.transform.SetParent(transform,false);
            effect=child.AddComponent<AudioSource>();effect.playOnAwake=false;effect.loop=false;effect.volume=.8f;
            thinking=Resources.Load<AudioClip>("LocalLingChatEffects/thinking");typing=Resources.Load<AudioClip>("LocalLingChatEffects/typing");
        }
        public void Thinking() { if(Enabled && thinking!=null){effect.Stop();effect.PlayOneShot(thinking);ThinkingStarts++;} }
        public void TypeTick() { if(Enabled && typing!=null)effect.PlayOneShot(typing,.5f); }
        public void Stop() { if(effect!=null)effect.Stop(); }
        void OnDisable() => Stop();
    }
}
