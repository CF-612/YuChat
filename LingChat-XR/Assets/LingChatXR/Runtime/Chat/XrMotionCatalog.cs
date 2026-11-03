using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LingChatXR.Chat
{
    // 目录字段与分类规则适配 LingChat action-library / motion-library-preferences（AGPL-3.0）。
    public sealed class XrMotionAsset
    {
        public string id,label,kind,style,asset,sha256,license;
        public bool loop;
        public string[] approvedModels;
        public JObject daily;
        public XrPoseLoop poseLoop;
        public bool builtin;
        public bool Speaking=>daily!=null;
        public bool MatchesStyle(string value)=>daily?["styles"] is JArray styles?styles.Values<string>().Contains(value):style==value;
    }
    public static class XrMotionCatalog
    {
        public static string Folder=>Application.streamingAssetsPath+"/Motions/local-lingchat";
        public static async Task<XrMotionAsset[]> Load(CancellationToken token)
        {
            var bytes=await PackageFiles.ReadAsync(Folder,"catalog.json",token);
            var catalog=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            var items=catalog["actions"].ToObject<XrMotionAsset[]>();
            if(items.Any(a=>string.IsNullOrEmpty(a.id)||a.id.Any(c=>!char.IsLetterOrDigit(c)&&c!='_')||a.sha256?.Length!=64)||items.Select(a=>a.id).Distinct().Count()!=items.Length)
                throw new InvalidDataException("动作目录无效");
            return items.Concat(new[]{new XrMotionAsset{id="nod",label="点头",kind="nod",style="normal",builtin=true},new XrMotionAsset{id="shake",label="摇头",kind="shake",style="normal",builtin=true}}).ToArray();
        }
    }
    public sealed class XrMotionPreferences
    {
        public bool enabled,automatic;
        public bool conversation=true,director;
        public string style="normal",idle="none";
        public Dictionary<string,bool> items=new Dictionary<string,bool>();
        public bool Allowed(string id)=>enabled&&items!=null&&items.TryGetValue(id,out bool value)&&value;
        public static string PathFor(string sha)=>Path.Combine(Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY")??Application.persistentDataPath,"xr-motion-"+sha+".json");
        public static XrMotionPreferences Read(string sha)
        {
            var p=PreferenceFiles.Load<XrMotionPreferences>(PathFor(sha));p.items??=new Dictionary<string,bool>();
            if(p.style!="reserved"&&p.style!="lively")p.style="normal";p.idle??="none";return p;
        }
        public void Save(string sha)=>PreferenceFiles.Save(PathFor(sha),JObject.FromObject(this));
    }
    // 复用 LingChat NativePoseLoop 范围语义；没有声明的短片不能自动成为持续姿势。
    public sealed class XrPoseLoop
    {
        public float from,to,exitFrom,enterEnd,overlapSeconds;
        public bool Valid(float length)=>float.IsFinite(from)&&float.IsFinite(to)&&float.IsFinite(exitFrom)&&float.IsFinite(enterEnd)&&float.IsFinite(overlapSeconds)&&from>=0&&to>from&&to<=length&&enterEnd>=0&&enterEnd<=to&&exitFrom>=to&&exitFrom<length&&overlapSeconds>0&&overlapSeconds<(to-from)/2;
        public void Sample(float age,out float source,out float incoming,out float weight)
        {
            float start=to-overlapSeconds;incoming=0;weight=0;
            if(age<start){source=Math.Max(0,age);return;}
            float phase=(age-start)%(to-from-overlapSeconds);
            if(phase<overlapSeconds){source=start+phase;incoming=from+phase;weight=XrConversationMotion.Smooth(phase/overlapSeconds);}
            else source=from+phase;
        }
    }
}
