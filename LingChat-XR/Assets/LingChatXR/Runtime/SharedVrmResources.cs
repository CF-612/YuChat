using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;

namespace LingChatXR
{
    // 通过 UniGLTF 官方外部资源表复用不可变贴图；材质、骨骼、表情和物理仍独立创建。
    // 引用覆盖异步导入：主角色切换/销毁也不会提前释放正在被预览导入使用的贴图。
    public sealed class SharedVrmResources : IDisposable
    {
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        readonly Dictionary<SubAssetKey,UnityEngine.Object> textures=new Dictionary<SubAssetKey,UnityEngine.Object>();
        int references=1;bool ownerReleased;
        public string ModelSha {get;}
        public IReadOnlyDictionary<SubAssetKey,UnityEngine.Object> Textures=>textures;
        public string LastPreviewImportProfile {get;private set;}
        public SharedVrmResources(RuntimeGltfInstance instance,string sha)
        {
            ModelSha=sha;
            instance.TransferOwnership((key,value)=>{owned.Add(value);if(value is Texture)textures.Add(key,value);});
        }
        public sealed class Lease:IDisposable
        {
            public SharedVrmResources Resources {get;private set;}
            internal Lease(SharedVrmResources resources){Resources=resources;}
            public void Dispose(){var value=Resources;Resources=null;value?.Release();}
        }
        public Lease Acquire()
        {
            if(references==0)throw new ObjectDisposedException(nameof(SharedVrmResources));
            references++;return new Lease(this);
        }
        void Release()
        {
            if(--references!=0)return;
            foreach(var resource in owned)if(resource!=null)UnityEngine.Object.Destroy(resource);
            owned.Clear();textures.Clear();
        }
        public void Dispose(){if(ownerReleased)return;ownerReleased=true;Release();}

        public async Task<Vrm10Instance> ImportPreview(byte[] bytes,CancellationToken token)
        {
            // 沿用 Vrm10.LoadBytesAsync 的解析/0.x 迁移/释放顺序，只传入官方 externalObjectMap。
            // 大块 JSON 解析仍放在线程；小网格使用官方分帧同步策略，避免每块网格往返线程各等一帧。
            var parser=new RuntimeOnlyAwaitCaller(.004f);
            var caller=new RuntimeOnlyNoThreadAwaitCaller(.004f);
            using var data=await parser.Run(()=>GlbLowLevelParser.Parse("",bytes));
            token.ThrowIfCancellationRequested();
            var vrm=await parser.Run(()=>Vrm10Data.Parse(data));
            GltfData migrated=null;
            try
            {
                if(vrm==null)migrated=await parser.Run(()=>Vrm10Data.Migrate(data,out vrm,out _));
                token.ThrowIfCancellationRequested();
                if(vrm==null)throw new InvalidOperationException("无法读取预览模型");
                using var loader=new Vrm10Importer(vrm,externalObjectMap:textures,useControlRig:true);
                var timing=new ImporterContextSpeedLog();
                var imported=await loader.LoadAsync(caller,timing.MeasureTime);
                LastPreviewImportProfile=timing.GetSpeedLog();
                if(token.IsCancellationRequested){imported.Dispose();token.ThrowIfCancellationRequested();}
                var instance=imported.GetComponent<Vrm10Instance>();
                if(instance==null){imported.Dispose();throw new InvalidOperationException("预览模型缺少 VRM 实例");}
                imported.ShowMeshes();return instance;
            }
            finally{migrated?.Dispose();}
        }
    }
}
