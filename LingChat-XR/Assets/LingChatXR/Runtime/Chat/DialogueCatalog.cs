using System;
using System.Linq;

namespace LingChatXR.Chat
{
    [Serializable] public sealed class DialogueRole { public string id; public string label; public string voiceProfile; public string spokenLanguage; }
    [Serializable] public sealed class DialogueVoice { public string id; public string label; public string language; }
    [Serializable] public sealed class DialogueCatalog
    {
        public int schemaVersion;
        public DialogueRole[] roles;
        public DialogueVoice[] voices;
        public bool IsValid() => schemaVersion==1 && roles!=null && roles.Length>0 && roles.Length<=32
            && voices!=null && voices.Length>0 && voices.All(v=>v!=null && ValidId(v.id) && !string.IsNullOrWhiteSpace(v.label))
            && voices.Select(v=>v.id).Distinct().Count()==voices.Length
            && roles.All(r=>r!=null && ValidId(r.id) && !string.IsNullOrWhiteSpace(r.label) && voices.Any(v=>v.id==r.voiceProfile && v.language==r.spokenLanguage))
            && roles.Select(r=>r.id).Distinct().Count()==roles.Length;
        public static bool ValidId(string id)=>!string.IsNullOrEmpty(id)&&id.Length<=64&&id.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'||c=='_');
        // 仅用于未提供清单接口的旧宿主兼容；成功连接新宿主后，以它的清单为准。
        public static DialogueCatalog Legacy()=>new DialogueCatalog{schemaVersion=1,
            roles=new[]{new DialogueRole{id="feiyu",label="小肥鱼",voiceProfile="ling-v2",spokenLanguage="ja"},new DialogueRole{id="ling-demo",label="Ling 演示角色",voiceProfile="ling-v2",spokenLanguage="ja"}},
            voices=new[]{new DialogueVoice{id="ling-v2",label="LingChat 默认日语",language="ja"}}};
    }
}
