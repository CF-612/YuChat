using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LingChatXR
{
    public sealed class MoodSticker : MonoBehaviour
    {
        const string Folder="LocalLingChatEffects/Stickers/";
        sealed class Clip { public string key; public int w,h,cols; public int[] ms; }
        sealed class Catalog { public Dictionary<string,string> mapping; public Dictionary<string,Clip> clips; }
        static Catalog catalog;
        SpriteRenderer display;
        Clip clip;
        Sprite[] sprites;
        string signal;
        float elapsed;
        int lastFrame;
        public int Starts {get;private set;}
        void Awake()
        {
            display=gameObject.AddComponent<SpriteRenderer>();display.enabled=false;
            var index=Resources.Load<TextAsset>(Folder+"index");
            if(catalog==null&&index!=null)catalog=JsonConvert.DeserializeObject<Catalog>(index.text);
        }
        public void Present(string mood,Vector3 anchor,Vector3 viewer,float height)
        {
            string next=null;
            if(catalog!=null)catalog.mapping.TryGetValue(mood??"",out next);
            if(next!=signal)
            {
                Clear();signal=next;
                if(next!=null)
                {
                    clip=catalog.clips[next];
                    var texture=Resources.Load<Texture2D>(Folder+clip.key);
                    if(texture!=null){
                        sprites=new Sprite[clip.ms.Length];
                        for(int i=0;i<sprites.Length;i++){
                            var rect=new Rect(i%clip.cols*(clip.w+2)+1,texture.height-(i/clip.cols*(clip.h+2)+1+clip.h),clip.w,clip.h);
                            sprites[i]=Sprite.Create(texture,rect,new Vector2(.5f,.5f),Mathf.Max(clip.w,clip.h),0,SpriteMeshType.FullRect);
                        }
                        elapsed=0;lastFrame=Time.frameCount;Starts++;
                    }
                }
            }
            if(sprites==null)return;
            if(lastFrame!=Time.frameCount){elapsed+=Mathf.Min(Time.unscaledDeltaTime,.1f);lastFrame=Time.frameCount;}
            float age=elapsed*1000;int frame=0;
            while(frame<clip.ms.Length&&age>=clip.ms[frame])age-=clip.ms[frame++];
            display.enabled=frame<sprites.Length;
            if(!display.enabled)return;
            display.sprite=sprites[frame];
            var forward=anchor-viewer;if(forward.sqrMagnitude<.00001f)return;
            var rot=Quaternion.LookRotation(forward,Vector3.up);
            transform.SetPositionAndRotation(anchor+rot*new Vector3(height*.34f,height*.24f,-height*.08f),rot);
            transform.localScale=Vector3.one*height*.28f;
        }
        void Clear(){
            display.enabled=false;display.sprite=null;
            if(sprites!=null)foreach(var sprite in sprites)Destroy(sprite);
            sprites=null;
        }
        void OnDestroy(){if(display!=null)Clear();}
    }
}
