using UnityEngine;

namespace LingChatXR.Chat
{
    // 逐式适配 LingChat motion-transition.ts + vrma-motion.ts（AGPL-3.0-only）。
    // XR 的世界平移由放置/抓取持有，所以只比较归一化旋转；不导入素材髋部位移。
    public static class XrMotionTransition
    {
        public const float Minimum=.35f,Maximum=1.25f,ExitMinimum=.9f,ExitMaximum=1.8f,Seam=.65f;
        // LingChat native-rig-motion 的相对参考姿态组合：保留动作变化，替换素材基准站姿。
        // 以当前待机为底，不能把动作首帧本身作为必须先到达的姿势。
        public static Quaternion RelativeToIdle(Quaternion sample,Quaternion reference,Quaternion idle)=>sample*Quaternion.Inverse(reference)*idle;
        public static float Weight(float progress){float t=Mathf.Clamp01(progress);return t*t*t*(t*(t*6-15)+10);}
        public static float EntryTime(float age,float duration)
        {float t=Mathf.Clamp01(age/Mathf.Max(.001f,duration));return age<duration?duration*(Mathf.Pow(t,6)-3*Mathf.Pow(t,5)+2.5f*Mathf.Pow(t,4)):age-duration/2;}
        public static float PoseDuration(Quaternion[] from,Quaternion[] to,MotionRegion[] regions,MotionRegion owned,float minimum=Minimum,float maximum=Maximum)
        {
            float duration=0;
            for(int i=0;i<from.Length;i++)if((regions[i]&owned)!=0)duration=Mathf.Max(duration,Quaternion.Angle(from[i],to[i])/90f);
            return Mathf.Clamp(duration,minimum,maximum);
        }
        public static float EnterDuration(float length,float poseDuration)=>Mathf.Min(length*.35f,Maximum,1.875f*poseDuration);
        public static float ExitDuration(float length,float poseDuration)=>Mathf.Min(length/2,ExitMaximum,1.875f*poseDuration);
        public static float CancelDuration(float poseDuration)=>Mathf.Min(.75f,1.875f*poseDuration);
    }
}
