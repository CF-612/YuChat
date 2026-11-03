using System;
using System.Globalization;

namespace LingChatXR.Core
{
    // 移植 LingChat TypeWriter 的速度公式；传输文本不触发 Start，音频 Started 才触发。
    public sealed class DialoguePresentation
    {
        private string target = "";
        private int[] elements = Array.Empty<int>();
        private int count;
        private double remaining;
        private readonly Random random = new Random();
        public string Text { get; private set; } = "";
        public string Target => target;
        public bool Typing => count < elements.Length;
        public int Speed { get; set; } = 80;
        public void Start(string text, bool append = false)
        {
            string prefix = append ? target + " " : "";
            target = prefix + (text ?? ""); elements = StringInfo.ParseCombiningCharacters(target);
            count = append ? StringInfo.ParseCombiningCharacters(prefix).Length : 0;
            Text = prefix; remaining = 0; Step(0);
        }
        public bool Step(double seconds)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)return false;
            string before=Text; remaining-=seconds;
            while(Typing && remaining<=0)
            {
                count++; int end=count==elements.Length ? target.Length : elements[count]; Text=target.Substring(0,end);
                int speed=Math.Max(1,Math.Min(100,Speed));
                remaining+=(200-((speed-1)/99d)*190+random.NextDouble()*speed*.2)/1000d;
            }
            return before!=Text;
        }
        public void Finish() { count=elements.Length;Text=target; }
        public void Cancel() { target=Text="";elements=Array.Empty<int>();count=0;remaining=0; }
    }
}
