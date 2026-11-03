using UnityEngine;

namespace LingChatXR
{
    // 固定单位圆只创建一次。扩散只改变子物体尺度、线宽和透明度。
    public sealed class TableWaterRings : MonoBehaviour
    {
        readonly LineRenderer[] rings=new LineRenderer[2];
        Material ink;
        float started,radius;
        bool repeat,doublePulse;
        const float Duration=.64f;
        public void Show(Vector3 point,float outerRadius,bool paired,bool looping=false)
        {
            if(ink==null)
            {
                ink=new Material(Shader.Find("Sprites/Default"));
                for(int i=0;i<rings.Length;i++)
                {
                    var child=new GameObject("水纹");child.transform.SetParent(transform,false);
                    var line=child.AddComponent<LineRenderer>();rings[i]=line;line.sharedMaterial=ink;
                    line.useWorldSpace=false;line.loop=true;line.positionCount=48;line.enabled=false;
                    for(int n=0;n<48;n++){float a=n*Mathf.PI/24;line.SetPosition(n,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}
                }
            }
            if(!looping||!enabled||!repeat)started=Time.unscaledTime;
            repeat=looping;doublePulse=paired;radius=outerRadius;
            transform.position=point;enabled=true;Update();
        }
        public void Hide(){enabled=false;}
        void Update()
        {
            float elapsed=Time.unscaledTime-started;
            if(repeat)elapsed%=1.25f;
            for(int i=0;i<rings.Length;i++)
            {
                float t=(elapsed-i*.15f)/Duration;
                var line=rings[i];if(line==null)continue;
                line.enabled=(i==0||doublePulse)&&t>=0&&t<1;
                if(!line.enabled)continue;
                float spread=1-(1-t)*(1-t);
                line.transform.localScale=Vector3.one*(radius*Mathf.Lerp(.25f,1,spread));
                line.widthMultiplier=Mathf.Lerp(.0022f,.001f,t);
                var tint=Color.Lerp(new Color(.61f,.90f,1,.95f),new Color(.27f,.60f,1,0),t);
                line.startColor=line.endColor=tint;
            }
            if(!repeat&&elapsed>=Duration+.15f)Hide();
        }
        void OnDisable(){foreach(var line in rings)if(line!=null)line.enabled=false;}
        void OnDestroy(){if(ink!=null)Destroy(ink);}
    }
}
