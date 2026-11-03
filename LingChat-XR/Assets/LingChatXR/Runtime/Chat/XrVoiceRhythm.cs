using System;
using System.Collections.Generic;
using UnityEngine;

namespace LingChatXR.Chat
{
    // 逐式迁移 LingChat voice-rhythm.ts：分析已解码的人声，不另建音源或解码器。
    public sealed class XrVoiceRhythm
    {
        public const int Rate=40;
        public float[] Energy {get;private set;}
        public float[] Cues {get;private set;}
        public float Duration {get;private set;}
        public static XrVoiceRhythm Build(AudioClip clip)
        {
            var pcm=new float[clip.samples*clip.channels];
            if(!clip.GetData(pcm,0))return null;
            return Build(pcm,clip.frequency,clip.channels);
        }
        public static XrVoiceRhythm Build(float[] pcm,int sampleRate,int channels)
        {
            if(pcm==null||channels<1||sampleRate<1)return null;
            int frames=pcm.Length/channels;float duration=(float)frames/sampleRate;
            var energy=new float[Mathf.CeilToInt(duration*Rate)];
            for(int i=0;i<energy.Length;i++)
            {
                int start=i*sampleRate/Rate,end=Math.Min(frames,(i+1)*sampleRate/Rate),count=0;double sum=0;
                for(int c=0;c<channels;c++)for(int j=start;j<end;j+=4){float v=pcm[j*channels+c];sum+=v*v;count++;}
                energy[i]=(float)Math.Sqrt(sum/Math.Max(1,count));
            }
            var sorted=(float[])energy.Clone();Array.Sort(sorted);
            float floor=sorted.Length==0?0:Math.Min(.015f,sorted[(int)(sorted.Length*.15f)]);
            float high=sorted.Length==0?.025f:Math.Max(.025f,sorted[(int)(sorted.Length*.9f)]);
            for(int i=0;i<energy.Length;i++)energy[i]=Mathf.Clamp01((energy[i]-Math.Max(.004f,floor*1.5f))/high);
            var cues=new List<float>();int quiet=0;float last=-10;
            for(int i=0;i<energy.Length;i++)
            {
                if(energy[i]<.12f){quiet++;continue;}
                float time=(float)i/Rate;
                if(quiet>=8&&time>.8f&&time-last>=1.8f&&energy[i]>.28f){cues.Add(time);last=time;}
                quiet=0;
            }
            if(cues.Count==0)
            {
                int first=Array.FindIndex(energy,e=>e>.12f),end=energy.Length-1;
                while(end>=0&&energy[end]<=.12f)end--;
                int best=-1;float peak=0,low=1;
                for(int i=Math.Max(36,first+24);first>=0&&i<=end-12;i++)
                {
                    float sum=0;for(int j=i-7;j<=i+7;j++)if(j>=0&&j<energy.Length)sum+=energy[j];
                    float mean=sum/15;low=Math.Min(low,mean);if(mean>peak){peak=mean;best=i;}
                }
                if(best>=0&&peak>.28f&&peak-low>.12f)cues.Add((float)best/Rate);
            }
            return new XrVoiceRhythm{Duration=duration,Energy=energy,Cues=cues.ToArray()};
        }
        public float At(float time)
        {
            if(time<0||time>=Duration)return 0;
            float at=time*Rate;int i=(int)at;
            return Mathf.Lerp(i<Energy.Length?Energy[i]:0,i+1<Energy.Length?Energy[i+1]:0,at-i);
        }
    }
}
