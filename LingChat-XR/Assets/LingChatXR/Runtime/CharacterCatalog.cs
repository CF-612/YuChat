using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LingChatXR
{
    [Serializable] public sealed class CharacterCatalog
    {
        public int version=1;
        public string[] folders=Array.Empty<string>();
        public string startupOverride;
        public static async Task<(string[] folders,string[] labels,string startupOverride)> Load(string root,CancellationToken token)
        {
            var catalog=JsonUtility.FromJson<CharacterCatalog>(Encoding.UTF8.GetString(await PackageFiles.ReadAsync(root,"index.json",token)));
            if(catalog==null||catalog.version!=1||catalog.folders==null||catalog.folders.Distinct().Count()!=catalog.folders.Length)throw new InvalidDataException("角色目录索引无效");
            if(!string.IsNullOrEmpty(catalog.startupOverride)&&!catalog.folders.Contains(catalog.startupOverride))throw new InvalidDataException("启动占位模型未登记在角色目录中");
            var labels=new string[catalog.folders.Length];
            for(int i=0;i<labels.Length;i++)
            {
                string folder=catalog.folders[i];
                FeiyuPreview.ValidateFolder(folder);
                var manifest=JsonUtility.FromJson<CharacterPackage>(Encoding.UTF8.GetString(await PackageFiles.ReadAsync(root,folder+"/manifest.json",token)));
                if(manifest?.model==null)throw new InvalidDataException("角色索引指向无效清单");
                labels[i]=string.IsNullOrEmpty(manifest.displayName)?folder:manifest.displayName;
            }
            return (catalog.folders,labels,catalog.startupOverride);
        }
    }
}
