using UnityEngine;
namespace LingChatXR {
    public sealed class PetTouchSound : MonoBehaviour
    {
        readonly AudioSource[] pool=new AudioSource[2];
        static readonly float[] rates={1,1.1225f,1.2599f,1.4983f,1.2599f,1.1225f};
        AudioSource pressure, middle, release;
        public bool PlayingMiddle=>pressActive && AudioSettings.dspTime>=middleAt && middle.isPlaying;
        double middleAt;
        bool pressActive;
        public int PressureReleases {get;private set;}
        public int PressureStarts {get;private set;}
        AudioClip clip;bool active;float lastAt;int step;
        public int Pulses {get;private set;}
        public bool Ready=>clip!=null&&clip.loadState==AudioDataLoadState.Loaded;
        public bool Playing=>pressActive||(release!=null&&release.isPlaying)||(pressure!=null&&pressure.isPlaying)||(pool[0]!=null&&pool[0].isPlaying)||(pool[1]!=null&&pool[1].isPlaying);
        void Awake()
        {
            pressure=gameObject.AddComponent<AudioSource>();
            pressure.playOnAwake=false;pressure.loop=false;
            pressure.clip=Resources.Load<AudioClip>("LocalLingChatEffects/squeeze");
            middle=gameObject.AddComponent<AudioSource>();
            middle.playOnAwake=false;middle.loop=true;
            middle.clip=Resources.Load<AudioClip>("LocalLingChatEffects/squeezeMiddle");
            release=gameObject.AddComponent<AudioSource>();
            release.playOnAwake=false;
            release.clip=Resources.Load<AudioClip>("LocalLingChatEffects/squeezeRelease");
            clip=Resources.Load<AudioClip>("LocalLingChatEffects/pet");if(clip!=null)clip.LoadAudioData();
            for(int i=0;i<2;i++){var child=new GameObject("Pet touch effect "+i);child.transform.SetParent(transform,false);pool[i]=child.AddComponent<AudioSource>();pool[i].playOnAwake=false;pool[i].loop=false;pool[i].clip=clip;}
        }
        public void Tick(bool contact,float now,float volume,bool enabled,bool pressed=false)
        {
            volume=float.IsFinite(volume)?Mathf.Clamp01(volume):0;
            foreach(var source in pool)if(source!=null)source.volume=enabled?volume:0;
            release.volume=volume;
            if(!enabled){Cancel();return;}
            if(contact&&pressed&&volume>0&&pressure.clip!=null)
            {
                if(!pressActive)
                {
                    foreach(var source in pool)source.Stop();
                    pressActive=true;release.Stop();
                    double start=AudioSettings.dspTime+.02;
                    middleAt=start+(double)pressure.clip.samples/pressure.clip.frequency;
                    pressure.volume=middle.volume=volume;
                    pressure.PlayScheduled(start);
                    if(middle.clip!=null)middle.PlayScheduled(middleAt);
                    PressureStarts++;
                }
                pressure.volume=middle.volume=volume;
                active=true;lastAt=now;return;
            }
            if(pressActive)
            {
                pressActive=false;pressure.Stop();middle.Stop();
                if(volume>0&&release.clip!=null){release.Play();PressureReleases++;}
            }

            if(!contact){active=false;return;} // 正常释放保留已开始的尾音。
            if(!active){active=true;step=0;lastAt=float.NegativeInfinity;}
            if(now-lastAt<.3f)return;lastAt=now;
            if(!Ready||volume<=0)return;
            foreach(var source in pool)if(!source.isPlaying){source.pitch=rates[step++%rates.Length];source.Play();Pulses++;break;}
        }
        public void Cancel(){active=false;pressActive=false;if(release!=null)release.Stop();if(pressure!=null)pressure.Stop();if(middle!=null)middle.Stop();foreach(var source in pool)if(source!=null)source.Stop();}

        void OnDisable()=>Cancel();
    }
}
