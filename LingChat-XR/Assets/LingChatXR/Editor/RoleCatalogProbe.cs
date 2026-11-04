#if UNITY_EDITOR
using System;
using System.IO;
using LingChatXR.Chat;
using UnityEditor;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class RoleCatalogProbe
    {
        public static void Run()
        {
            int checks=0;
            void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
            try
            {
                var catalog=DialogueCatalog.Legacy();Check(catalog.IsValid(),"旧宿主兼容");
                Array.Resize(ref catalog.roles,3);
                catalog.roles[2]=new DialogueRole{id="fixture-third",label="测试第三角色",voiceProfile="ling-v2",spokenLanguage="ja"};
                var parsed=JsonUtility.FromJson<DialogueCatalog>(JsonUtility.ToJson(catalog));
                Check(parsed.IsValid()&&parsed.roles[2].id=="fixture-third","新增角色传输");
                parsed.roles[2].id="feiyu";Check(!parsed.IsValid(),"重复身份拒绝");
                parsed.roles[2].id="fixture-third";parsed.roles[2].voiceProfile="missing";Check(!parsed.IsValid(),"未知声线拒绝");
                parsed.roles[2].voiceProfile="ling-v2";parsed.roles[2].spokenLanguage="unknown";Check(!parsed.IsValid(),"语种不符拒绝");
                Check(DialogueCatalog.ValidId("fixture-third")&&!DialogueCatalog.ValidId("../private"),"本地角色标识校验");
                var dir=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,"checks.json"),"{\"passed\":"+checks+",\"scope\":\"catalog serialization and validation; no device or visual acceptance\"}");
                Debug.Log("XR_M1_PASSED catalog "+checks);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
#endif
