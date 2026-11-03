using UnityEngine;
using UnityEngine.UI;

namespace LingChatXR.XR
{
    // 一张 uGUI 网格共用 Canvas 材质，保持世界空间线条宽度与按钮字号一致。
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class QuestPanelGlyph : MaskableGraphic
    {
        public enum Shape { Frame, Resize, Chat, Table, Check, Fish, Border }
        public Shape kind;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect=rectTransform.rect;
            if(kind==Shape.Border){PetBadgeGraphic.Border(mesh,rect,6,1.5f);return;}
            if(kind==Shape.Frame)
            {
                var a=rect.min;var b=rect.max;
                Dash(mesh,a,new Vector2(b.x,a.y));Dash(mesh,new Vector2(b.x,a.y),b);
                Dash(mesh,b,new Vector2(a.x,b.y));Dash(mesh,new Vector2(a.x,b.y),a);return;
            }
            var center=rect.center;float unit=Mathf.Min(rect.width,rect.height)*.4f;
            void Stroke(float x,float y,float endX,float endY)=>Line(mesh,center+new Vector2(x,y)*unit,center+new Vector2(endX,endY)*unit,unit*.18f);
            if(kind==Shape.Fish){PetBadgeGraphic.DrawFish(mesh,rect,color);return;}
            switch(kind)
            {
                case Shape.Resize:
                    Stroke(-.8f,-.8f,.8f,.8f);Stroke(.8f,.8f,.05f,.8f);Stroke(.8f,.8f,.8f,.05f);
                    Stroke(-.8f,-.8f,-.05f,-.8f);Stroke(-.8f,-.8f,-.8f,-.05f);break;
                case Shape.Chat:
                    Stroke(-.85f,.65f,.85f,.65f);Stroke(.85f,.65f,.85f,-.4f);Stroke(.85f,-.4f,-.15f,-.4f);
                    Stroke(-.15f,-.4f,-.65f,-.8f);Stroke(-.65f,-.8f,-.65f,-.4f);Stroke(-.65f,-.4f,-.85f,-.4f);Stroke(-.85f,-.4f,-.85f,.65f);break;
                case Shape.Table:
                    Stroke(-.95f,.35f,.95f,.35f);Stroke(-.65f,.35f,-.65f,-.75f);Stroke(.65f,.35f,.65f,-.75f);break;
                case Shape.Check: Stroke(-.9f,0,-.27f,-.6f);Stroke(-.27f,-.6f,.9f,.73f);break;
            }
        }
        void Dash(VertexHelper mesh,Vector2 a,Vector2 b)
        {
            float distance=Vector2.Distance(a,b);if(distance<1)return;
            var dir=(b-a)/distance;
            for(float t=0;t<distance;t+=20)Line(mesh,a+dir*t,a+dir*Mathf.Min(distance,t+11),2.5f);
        }
        void Line(VertexHelper mesh,Vector2 a,Vector2 b,float width)
        {
            var normal=Vector2.Perpendicular((b-a).normalized)*width*.5f;int index=mesh.currentVertCount;
            mesh.AddVert(a-normal,color,Vector2.zero);mesh.AddVert(a+normal,color,Vector2.zero);
            mesh.AddVert(b+normal,color,Vector2.zero);mesh.AddVert(b-normal,color,Vector2.zero);
            mesh.AddTriangle(index,index+1,index+2);mesh.AddTriangle(index,index+2,index+3);
        }
    }
}
