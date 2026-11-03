using UnityEngine;
using UnityEngine.UI;
namespace LingChatXR.XR
{
    public sealed class QuestPanelGradient : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper mesh)
        {
            if(!IsActive())return;
            var r=((RectTransform)transform).rect;var v=UIVertex.simpleVert;
            for(int i=0;i<mesh.currentVertCount;i++)
            {
                mesh.PopulateUIVertex(ref v,i);
                float t=.65f*Mathf.InverseLerp(r.yMin,r.yMax,v.position.y)+.35f*(1-Mathf.InverseLerp(r.xMin,r.xMax,v.position.x));
                Color c=v.color;
                c.r+=t*.075f;c.g+=t*.115f;c.b+=t*.17f;v.color=c;
                mesh.SetUIVertex(v,i);
            }
        }
    }
}
