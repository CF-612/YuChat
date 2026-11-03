using System;
using UnityEngine;
namespace LingChatXR
{
    // LingChat head-touch-session / touch-sound 的 Unity 适配（AGPL-3.0）。
    // 会话只记录接触，不触发聊天或占用角色人声。
    public sealed class PetTouchSession
    {
        public struct Summary { public float duration,distance; public bool duringSpeech; }
        public bool Active {get;private set;}
        public bool Inside {get;private set;}
        public float Duration {get;private set;}
        public float Distance {get;private set;}
        public bool DuringSpeech {get;private set;}
        public int CompletedCount {get;private set;}
        public Summary LastCompleted {get;private set;}
        public event Action<Summary> Completed;
        float lastAt;Vector3 previous;bool hasPoint;
        public void Begin(float now,bool speaking)
        {if(Active)return;Active=Inside=true;Duration=Distance=0;DuringSpeech=speaking;lastAt=now;hasPoint=false;}
        public void Tick(float now,bool speaking)
        {if(!Active)return;if(Inside){Duration+=Mathf.Max(0,now-lastAt);DuringSpeech|=speaking;}lastAt=Mathf.Max(lastAt,now);}
        public void Contact(bool inside,float now,bool speaking){if(!Active||Inside==inside)return;Tick(now,speaking);Inside=inside;hasPoint=false;}
        public void Move(Vector3 point,float height)
        {
            if(!Active||!Inside)return;
            if(hasPoint){float distance=Vector3.Distance(point,previous);if(distance<height*.5f)Distance+=distance;}
            previous=point;hasPoint=true;
        }
        public void End(float now,bool speaking,bool cancelled)
        {
            if(!Active)return;Tick(now,speaking);Active=Inside=false;hasPoint=false;
            if(cancelled||Duration<.04f)return;
            LastCompleted=new Summary{duration=Duration,distance=Distance,duringSpeech=DuringSpeech};CompletedCount++;Completed?.Invoke(LastCompleted);
        }
    }
}
