using System;
using UnityEngine;

namespace LingChatXR.Chat
{
    [Flags] public enum MotionRegion { None=0,Head=1,Torso=2,LeftArm=4,RightArm=8,Legs=16,All=31 }

    // LingChat conversation-motion / conversation-signals / semantic-action 的 Unity 适配。
    // 保留语音时钟、宽节奏、预算、延迟平滑；只写未被动作占用的标准人形部位。
    public sealed class XrConversationMotion
    {
        float elapsed,breathPhase,speech,energy,flow,mix,credit=1,lastAction=-100,age,duration,gain,lastTime;
        long voice=-1;int cue,serial,family=-1,lastFamily=-1;bool left;readonly float[] smooth=new float[14],values=new float[14];
        float waitingAge;bool wasWaiting;
        public int GestureSerial=>serial;
        public float Energy=>energy;
        public void Reset(){voice=-1;family=-1;energy=flow=speech=0;Array.Clear(smooth,0,smooth.Length);}
        public static float Smooth(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
        static float Hash(float n){float s=Mathf.Sin(n*12.9898f)*43758.5453f;return s-Mathf.Floor(s);}
        static float Noise(float x,float row){float i=Mathf.Floor(x),a=Hash(i+row*131.7f);return (a+(Hash(i+1+row*131.7f)-a)*Smooth(x-i))*2-1;}
        static float Drift(float x,float row)=>Noise(x,row)*.74f+Noise(x*.31f,row+7)*.4f;
        static float Breath(float p)=>p<.34f?.5f-.5f*Mathf.Cos(Mathf.PI*p/.34f):p<.82f?.5f+.5f*Mathf.Cos(Mathf.PI*(p-.34f)/.48f):0;
        public void Step(float delta,bool enabled,bool playing,bool waiting,long key,float audioTime,XrVoiceRhythm rhythm,string style,MotionRegion occupied,Action<HumanBodyBones,Vector3> add)
        {
            float dt=Mathf.Clamp(delta,0,.05f);elapsed+=dt;
            bool changed=voice!=key,jumped=!changed&&(audioTime<lastTime-.05f||audioTime-lastTime>Math.Max(.3f,delta+.12f));
            if(changed||jumped||delta>.3f)
            {
                if(jumped||delta>.3f)family=-1;voice=key;cue=0;
                while(rhythm!=null&&cue<rhythm.Cues.Length&&rhythm.Cues[cue]<=audioTime+.18f)cue++;
            }
            float advance=changed?dt:Mathf.Clamp(audioTime-lastTime,0,dt*2);lastTime=audioTime;
            float amplitude=style=="reserved"?.5f:style=="lively"?1:.75f,openness=style=="reserved"?.65f:style=="lively"?1:.85f;
            float budget=style=="reserved"?4:style=="lively"?12:10;
            speech+=((playing?1:0)-speech)*(1-Mathf.Exp(-dt/.65f));
            float target=playing&&rhythm!=null?rhythm.At(audioTime):0;
            energy+=(target-energy)*(1-Mathf.Exp(-dt/(target>energy?.16f:.35f)));
            flow+=(energy-flow)*(1-Mathf.Exp(-dt/.6f));mix+=((enabled?1:0)-mix)*(1-Mathf.Exp(-dt/.5f));
            if(playing)credit=Math.Min(1,credit+dt*budget/60);
            if(!enabled||((occupied&MotionRegion.Torso)!=0)||((occupied&(left?MotionRegion.LeftArm:MotionRegion.RightArm))!=0))family=-1;
            if(playing&&rhythm!=null)
            {
                while(cue<rhythm.Cues.Length&&rhythm.Cues[cue]<audioTime+.48f)
                {
                    float lead=rhythm.Cues[cue++]-audioTime;
                    if(lead<.16f||!enabled||family>=0||credit<1||elapsed-lastAction<3||occupied!=MotionRegion.None)continue;
                    family=(lastFamily+1)%4;lastFamily=family;left=serial%2==0;serial++;age=0;
                    duration=family==0?2.4f:family==1?2.8f:family==2?2.6f:1.7f;gain=amplitude*openness;credit--;lastAction=elapsed;
                }
            }
            if(waiting&&!wasWaiting)waitingAge=0;wasWaiting=waiting;float priorWait=waitingAge;if(waiting)waitingAge+=dt;
            if(waiting&&priorWait<2.2f&&waitingAge>=2.2f&&enabled&&family<0&&credit>=1&&elapsed-lastAction>=3&&occupied==MotionRegion.None)
            {family=3;age=0;duration=1.7f;gain=amplitude*openness;credit--;lastAction=elapsed;serial++;}
            float accent=0,body=0,hand=0;
            if(family>=0)
            {
                age+=playing?advance:dt;float p=age/duration;
                accent=(p<.04f?0:p<.36f?Smooth((p-.04f)/.32f):p<.62f?1:Smooth((.96f-p)/.34f))*gain;
                body=(p<.22f?Smooth(p/.22f):p<.65f?1:Smooth((1-p)/.35f))*gain;
                hand=Smooth((p-.18f)/.42f)*Smooth((1-p)/.28f)*gain;
                if(p>=1){family=-1;accent=body=hand=0;}
            }
            breathPhase=(breathPhase+dt/(3.9f-speech*.7f))%1;
            float breath=(Breath(breathPhase)-.35f)*(1-speech*.35f);
            float arm=speech*(.35f+flow*.22f)*amplitude*openness;
            float spread=speech*(.015f+flow*.025f)*amplitude*openness;
            float palm=speech*flow*.1f*amplitude*openness;
            float la=(left||family==1||family==2)?accent:0,ra=(!left||family==1||family==2)?accent:0;
            values[0]=breath*.04f*amplitude+speech*.065f*amplitude+flow*.065f*amplitude+body*.04f;
            values[1]=Drift(elapsed*.035f,7)*.025f*amplitude;values[2]=values[3]=0;values[4]=playing?body*.05f:0;
            values[5]=breath*.018f*amplitude+flow*.035f*amplitude+body*.06f;
            values[6]=spread+la*.14f;values[7]=spread+ra*.14f;values[8]=palm+la*hand*.45f;values[9]=palm+ra*hand*.45f;
            values[10]=Math.Min(1.45f,arm+la);values[11]=Math.Min(1.45f,arm+ra);values[12]=palm*.35f+la*.09f;values[13]=palm*.35f+ra*.09f;
            for(int i=0;i<smooth.Length;i++)smooth[i]+=(values[i]-smooth[i])*(1-Mathf.Exp(-dt/(i==3||i==4?.28f:i>=8?.24f:.16f)));
            void Apply(HumanBodyBones bone,MotionRegion region,Vector3 radians){if((occupied&region)==0)add(bone,radians*(mix*Mathf.Rad2Deg));}
            Apply(HumanBodyBones.Chest,MotionRegion.Torso,new Vector3(smooth[0],smooth[2],-smooth[1]));
            Apply(HumanBodyBones.Spine,MotionRegion.Torso,new Vector3(smooth[0]*.45f,smooth[2]*.35f,-smooth[1]*.35f));
            Apply(HumanBodyBones.Head,MotionRegion.Head,new Vector3(smooth[4],0,smooth[3]));
            Apply(HumanBodyBones.LeftShoulder,MotionRegion.LeftArm,new Vector3(0,0,-smooth[5]));
            Apply(HumanBodyBones.RightShoulder,MotionRegion.RightArm,new Vector3(0,0,smooth[5]));
            Apply(HumanBodyBones.LeftUpperArm,MotionRegion.LeftArm,new Vector3(-smooth[10]*.4f,0,-smooth[6]));
            Apply(HumanBodyBones.RightUpperArm,MotionRegion.RightArm,new Vector3(-smooth[11]*.4f,0,smooth[7]));
            Apply(HumanBodyBones.LeftLowerArm,MotionRegion.LeftArm,new Vector3(-smooth[8],-smooth[10],0));
            Apply(HumanBodyBones.RightLowerArm,MotionRegion.RightArm,new Vector3(-smooth[9],smooth[11],0));
            Apply(HumanBodyBones.LeftHand,MotionRegion.LeftArm,new Vector3(0,0,-smooth[12]));
            Apply(HumanBodyBones.RightHand,MotionRegion.RightArm,new Vector3(0,0,smooth[13]));
        }
        static readonly float[] NodTimes={.26f,.24f,.24f,.32f},ShakeTimes={.28f,.30f,.28f,.26f,.30f},NodValues={0,.18f,-.03f,.13f,0},ShakeValues={0,-.16f,.16f,-.12f,.08f,0};
        public static Vector3 HeadAction(string id,float seconds)
        {
            float[] durations=id=="nod"?NodTimes:ShakeTimes;
            float[] values=id=="nod"?NodValues:ShakeValues;
            for(int i=0;i<durations.Length;i++){if(seconds<=durations[i]){float value=Mathf.Lerp(values[i],values[i+1],Smooth(seconds/durations[i]))*Mathf.Rad2Deg;return id=="nod"?new Vector3(value,0,0):new Vector3(0,value,0);}seconds-=durations[i];}
            return Vector3.zero;
        }
    }
}
