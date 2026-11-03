using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LingChatXR
{
    // 保留未来字段；损坏文件拒绝覆盖。与 LingChat 设置草稿一样，只提交本组已编辑字段。
    public static class PreferenceFiles
    {
        public static JObject Read(string path) => File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        public static T Load<T>(string path) where T : new() => Read(path).ToObject<T>() ?? new T();
        public static void Save(string path, JObject patch)
        {
            var current=Read(path);
            current.Merge(patch,new JsonMergeSettings { MergeArrayHandling=MergeArrayHandling.Replace, MergeNullValueHandling=MergeNullValueHandling.Merge });
            Write(path,current);
        }
        public static void Write(string path,JObject data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,data.ToString(Formatting.Indented));
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        public static bool IsStorageError(Exception e) => e is IOException || e is UnauthorizedAccessException || e is JsonException || e is ArgumentException;
    }
}
