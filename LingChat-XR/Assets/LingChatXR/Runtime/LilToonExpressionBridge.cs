using System.Collections.Generic;
using UnityEngine;

namespace LingChatXR
{
    // UniVRM 继续驱动原始材质；在它完成 LateUpdate 后映射到 lilToon。
    // 不复制骨骼或表情权重，不改写 VRM，不向原项目回写。
    [DefaultExecutionOrder(32000)]
    public sealed class LilToonExpressionBridge : MonoBehaviour
    {
        struct Binding { public Material source,target; public Color originalShade,shade; }
        readonly List<Binding> bindings=new List<Binding>();
        public int MaterialCount=>bindings.Count;
        public void Add(Material source,Material target,Color shade)=>bindings.Add(new Binding{source=source,target=target,originalShade=source.GetColor("_ShadeColor"),shade=shade});
        void LateUpdate()=>Synchronize();
        public void Synchronize()
        {
            foreach(var b in bindings)
            {
                if(b.source==null||b.target==null)continue;
                b.target.SetColor("_Color",b.source.GetColor("_Color"));
                Color delta=b.source.GetColor("_ShadeColor")-b.originalShade;
                b.target.SetColor("_ShadowColor",b.shade+delta);
                b.target.SetTextureScale("_MainTex",b.source.GetTextureScale("_MainTex"));
                b.target.SetTextureOffset("_MainTex",b.source.GetTextureOffset("_MainTex"));
            }
        }
        void OnDestroy(){foreach(var b in bindings)if(b.target!=null)Destroy(b.target);bindings.Clear();}
    }
}
