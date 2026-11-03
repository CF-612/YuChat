using UnityEngine;
using UnityEngine.UI;

namespace LingChatXR
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PetBadgeGraphic : MaskableGraphic
    {
        public enum Part { Panel, Particles, Fish }
        public Part part;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect=rectTransform.rect;
            if(part==Part.Fish) { DrawFish(mesh,rect,color); return; }
            if(part==Part.Panel)
            {
                // 圆角轮廓与渐变共用网格，透明四角可透出真实房间。
                float r=Mathf.Min(rect.width,rect.height)*.5f;
                Color Tint(Vector2 p)=>Color.Lerp(new Color(.045f,.08f,.14f,.72f),new Color(.14f,.28f,.40f,.72f),(.68f*(1-Mathf.InverseLerp(rect.xMin,rect.xMax,p.x))+.32f*Mathf.InverseLerp(rect.yMin,rect.yMax,p.y)));
                mesh.AddVert(rect.center,Tint(rect.center),Vector2.zero);
                for(int corner=0;corner<4;corner++)
                {
                    var origin=new Vector2(corner==0||corner==3?rect.xMax-r:rect.xMin+r,corner<2?rect.yMax-r:rect.yMin+r);
                    for(int n=0;n<=16;n++)
                    {
                        float angle=(90*corner+n*90f/16)*Mathf.Deg2Rad;
                        var p=origin+r*new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                        mesh.AddVert(p,Tint(p),Vector2.zero);
                    }
                }
                for(int i=0;i<68;i++)mesh.AddTriangle(0,1+i,1+(i+1)%68);
                Border(mesh,rect,r,6.4f,true);
                return;
            }
            DrawParticles(mesh,rect);
        }
        const int ParticleCount=10;
        int phase;
        public void Animate(float time)
        {
            int next=Mathf.FloorToInt(time*9)%ParticleCount;
            if(next==phase)return;
            phase=next;
            SetVerticesDirty();
        }
        void DrawParticles(VertexHelper mesh,Rect rect)
        {
            float unit=Mathf.Min(rect.width,rect.height);
            for(int i=0;i<ParticleCount;i++)
            {
                float angle=2*Mathf.PI*i/ParticleCount;
                var axis=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
                var tangent=Vector2.Perpendicular(axis);
                float tail=(phase-i+ParticleCount)%ParticleCount;
                float glow=Mathf.Exp(-tail*.48f);
                Color ink=Color.Lerp(new Color(.28f,.49f,.71f,.81f),new Color(.70f,.95f,1),glow);
                Vector2 middle=rect.center+axis*unit*.385f;
                float radius=unit*.034f;
                float halfStem=unit*(.038f+.022f*glow);
                int center=mesh.currentVertCount;
                mesh.AddVert(middle,ink,Vector2.zero);
                for(int cap=0;cap<2;cap++)
                {
                    float sign=cap==0?1:-1;
                    for(int step=0;step<=8;step++)
                    {
                        float arc=-Mathf.PI*.5f+step*Mathf.PI/8;
                        var offset=axis*(halfStem+radius*Mathf.Cos(arc))+tangent*radius*Mathf.Sin(arc);
                        mesh.AddVert(middle+sign*offset,ink,Vector2.zero);
                    }
                }
                for(int n=0;n<18;n++)mesh.AddTriangle(center,center+1+n,center+1+(n+1)%18);
            }
        }
        public static void Border(VertexHelper mesh,Rect rect,float radius,float width,bool gradient=false)
        {
            int first=mesh.currentVertCount;
            var ink=new Color(.56f,.81f,.98f,1);
            for(int corner=0;corner<4;corner++)
            {
                Vector2 center=new Vector2(corner==0||corner==3?rect.xMax-radius:rect.xMin+radius,corner<2?rect.yMax-radius:rect.yMin+radius);
                for(int n=0;n<=16;n++)
                {
                    float a=(90*corner+90f*n/16)*Mathf.Deg2Rad;
                    var normal=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                    if(gradient)ink=Color.Lerp(new Color(.51f,.91f,1),new Color(.45f,.60f,.99f),Mathf.InverseLerp(rect.xMin,rect.xMax,center.x+normal.x*radius));
                    mesh.AddVert(center+normal*radius,ink,Vector2.zero);
                    mesh.AddVert(center+normal*(radius-width),ink,Vector2.zero);
                }
            }
            for(int n=0;n<68;n++){int a=first+n*2,b=first+(n+1)%68*2;mesh.AddTriangle(a,b,b+1);mesh.AddTriangle(a,b+1,a+1);}
        }
        public static void DrawFish(VertexHelper mesh, Rect rect, Color color)
        {
            var center=rect.center;float unit=Mathf.Min(rect.width,rect.height)*.4f;
                Vector2 P(float x,float y)=>center+new Vector2(x,y)*unit;
                void Triangle(Vector2 a,Vector2 b,Vector2 c,Color tint)
                {
                    int first=mesh.currentVertCount;
                    mesh.AddVert(a,tint,Vector2.zero);mesh.AddVert(b,tint,Vector2.zero);mesh.AddVert(c,tint,Vector2.zero);
                    mesh.AddTriangle(first,first+1,first+2);
                }
            void Oval(float x,float y,float w,float h,Color tint)
            {
                for(int i=0;i<32;i++)
                {
                    float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;
                    Triangle(P(x,y),P(x+w*Mathf.Cos(a),y+h*Mathf.Sin(a)),P(x+w*Mathf.Cos(b),y+h*Mathf.Sin(b)),tint);
                }
            }
            Triangle(P(.32f,0),P(1.05f,.46f),P(1.05f,-.46f),color);
            Oval(-.18f,0,.81f,.49f,color);
            Oval(-.64f,.085f,.074f,.082f,new Color(.06f,.18f,.31f));
        }
    }
}