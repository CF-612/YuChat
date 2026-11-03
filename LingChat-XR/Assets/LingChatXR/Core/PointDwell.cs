using System;

namespace LingChatXR.Core
{
    // 识别由 XR Hands 负责；这里只定义稳定确认和必须先松开手势的交互时序。
    public sealed class PointDwell
    {
        Vec3 anchor;bool pending,latched;float held,released;
        public float Progress=>latched?1:Math.Min(1,held/.65f);
        public bool Latched=>latched;
        public bool Step(bool valid,Vec3 point,float dt,out Vec3 confirmed)
        {
            confirmed=default;if(!Vec3.Finite(dt)||dt<=0)return false;dt=Math.Min(dt,.05f);
            if(!valid||!point.IsFinite){pending=false;held=0;released+=dt;if(released>=.2f)latched=false;return false;}
            released=0;if(latched)return false;
            float dx=point.X-anchor.X,dz=point.Z-anchor.Z;
            if(!pending||dx*dx+dz*dz>.0004f){anchor=point;pending=true;held=0;}
            held+=dt;if(held<.65f)return false;
            latched=true;confirmed=anchor;return true;
        }
        public void Reset(){pending=latched=false;held=released=0;}
    }
}
