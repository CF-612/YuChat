using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LingChatXR.Chat
{
    // PC 私有配置；头显只用配对信息，此对象不进入角色包或 StreamingAssets。
    public static class XrServiceSettings
    {
        public static bool Editable => Application.platform==RuntimePlatform.WindowsEditor || Application.platform==RuntimePlatform.WindowsPlayer;
        public static string FilePath => Environment.GetEnvironmentVariable("XR_SERVICE_SETTINGS") ??
            (Application.isEditor?Path.GetFullPath(Path.Combine(Application.dataPath,"../../.cache/xr-service-settings.json")):Path.Combine(Application.persistentDataPath,"xr-service-settings.json"));
        public static JObject Read()
        {
            if(!Editable)throw new InvalidOperationException("服务设置请在 PC 端编辑；头显只管理配对");
            var data=PreferenceFiles.Read(FilePath);
            if(!(data["llm.providers"] is JArray))data["llm.providers"]=new JArray();
            return data;
        }
        public static string Validate(JObject data)
        {
            var providers=data["llm.providers"] as JArray;
            foreach(string slot in new[]{"llm.chat_provider_id","llm.translate_provider_id"})
            {
                string id=(string)data[slot];if(string.IsNullOrEmpty(id))id=(string)data["llm.chat_provider_id"];
                var p=providers?.OfType<JObject>().FirstOrDefault(x=>(string)x["id"]==id);
                if(p==null)return "请选择对话和翻译服务";
                if(!new[]{"openai","deepseek","gemini","lmstudio"}.Contains(((string)p["provider"])?.ToLowerInvariant()))return "当前宿主支持 OpenAI、DeepSeek、Gemini 和 LM Studio";
                if(string.IsNullOrWhiteSpace((string)p["model"]))return "模型名称不能为空";
                string url=(string)p["base_url"];
                if(!string.IsNullOrEmpty(url)&&(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||(uri.Scheme!="https"&&uri.Scheme!="http")||!string.IsNullOrEmpty(uri.UserInfo)))return "服务地址需要有效的 http/https URL，不在地址里填写密钥";
            }
            return null;
        }
        public static string Save(JObject draft)
        {
            if(!Editable)return "请在 PC 端修改服务配置";
            string error=Validate(draft);if(error!=null)return error;
            try
            {
                // 从最新文件合并，保留未编辑字段；当前界面负责同一草稿内的全部 provider 字段。
                PreferenceFiles.Save(FilePath,new JObject{{"llm.providers",draft["llm.providers"].DeepClone()},{"llm.chat_provider_id",draft["llm.chat_provider_id"]?.DeepClone()},{"llm.translate_provider_id",draft["llm.translate_provider_id"]?.DeepClone()}});
                return null;
            }
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){return "XR 服务配置保存失败，请检查存储权限或文件格式；原配置已保留";}
        }
    }
}
