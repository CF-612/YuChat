using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LingChatXR.Chat
{
    public sealed partial class XrChatController
    {
        DialogueCatalog catalog=DialogueCatalog.Legacy();
        bool catalogReady;
        public IReadOnlyList<DialogueRole> AvailableRoles=>catalog.roles;
        public string RoleLabel=>catalog.roles.FirstOrDefault(r=>r.id==Role)?.label??Role;
        public string VoiceLabel=>catalog.voices.FirstOrDefault(v=>v.id==catalog.roles.FirstOrDefault(r=>r.id==Role)?.voiceProfile)?.label??"声线配置不可用";
        IEnumerator FetchCatalog(int revision)
        {
            catalogReady=false;
            using(var scope=Request("/catalog"))
            {
                yield return scope.Value.SendWebRequest();
                if(revision!=epoch)yield break;
                if(scope.Value.responseCode==404){catalog=DialogueCatalog.Legacy();catalogReady=true;yield break;}
                if(scope.Value.result!=UnityWebRequest.Result.Success){Status="角色清单读取失败 · 请重新连接";yield break;}
                DialogueCatalog next=null;
                try{next=JsonUtility.FromJson<DialogueCatalog>(scope.Value.downloadHandler.text);}catch(System.ArgumentException){}
                if(next==null||!next.IsValid()){Status="服务端角色清单无效 · 原角色选择已保留";yield break;}
                catalog=next;catalogReady=true;
            }
        }
    }
}
