using System;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;

namespace LingChatXR
{
    // UniGLTF 上传网格时会跨帧返回；退出 Play、换模型或销毁角色后禁止继续上传。
    internal sealed class ImportAwaitCaller : IAwaitCaller
    {
        readonly RuntimeOnlyAwaitCaller inner;
        readonly CancellationToken token;
        public ImportAwaitCaller(float budget,CancellationToken token)
        {
            inner=new RuntimeOnlyAwaitCaller(budget);
            this.token=token;
        }
        public async Task NextFrame(){token.ThrowIfCancellationRequested();await inner.NextFrame();token.ThrowIfCancellationRequested();}
        public async Task NextFrameIfTimedOut(){token.ThrowIfCancellationRequested();await inner.NextFrameIfTimedOut();token.ThrowIfCancellationRequested();}
        public async Task Run(Action action){token.ThrowIfCancellationRequested();await inner.Run(action);token.ThrowIfCancellationRequested();}
        public async Task<T> Run<T>(Func<T> action){token.ThrowIfCancellationRequested();var result=await inner.Run(action);token.ThrowIfCancellationRequested();return result;}
    }
}
