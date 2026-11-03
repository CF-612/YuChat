using UnityEngine;
using UnityEngine.UI;

namespace LingChatXR
{
    // Desktop 与 Quest 共用一个只显示状态的世界空间加载提示。
    public sealed class PetLoadingIndicator : MonoBehaviour
    {
        FeiyuPreview preview;
        Canvas canvas;
        PetBadgeGraphic particles;
        public bool Visible => canvas != null && canvas.enabled;
        public void Configure(FeiyuPreview owner, Camera camera)
        {
            preview = owner;
            var root = new GameObject("角色加载提示", typeof(RectTransform));
            root.transform.SetParent(camera.transform, false);
            root.transform.localPosition = new Vector3(0, -.07f, .65f);
            root.transform.localScale = Vector3.one * .0005f;
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 6;
            var rect=(RectTransform)root.transform;
            rect.sizeDelta=new Vector2(290,84);
            Art(rect,PetBadgeGraphic.Part.Panel,Color.white);
            var badge=Child(rect,"小鱼徽章",new Vector2(-98,0),new Vector2(61,61));
            particles=Art(badge,PetBadgeGraphic.Part.Particles,Color.white);
            Art(Child(badge,"小蓝鱼",Vector2.zero,new Vector2(33,33)),PetBadgeGraphic.Part.Fish,new Color(.57f,.84f,1));
            var font=Resources.Load<Font>("Fonts/MaokenAssortedSans");
            Label(rect,font,"YuChat",new Vector2(35,17),14,new Color(.60f,.83f,.96f));
            Label(rect,font,"小肥鱼加载中",new Vector2(35,-10),25,new Color(.93f,.98f,1));
        }
        static PetBadgeGraphic Art(RectTransform rect,PetBadgeGraphic.Part part,Color color)
        {
            var graphic=rect.gameObject.AddComponent<PetBadgeGraphic>();
            graphic.part=part;graphic.color=color;graphic.raycastTarget=false;return graphic;
        }
        static void Label(RectTransform parent,Font font,string value,Vector2 position,int size,Color color)
        {
            var text=Child(parent,value,position,new Vector2(180,size*1.55f)).gameObject.AddComponent<Text>();
            text.font=font;text.text=value;text.fontSize=size;text.color=color;text.alignment=TextAnchor.MiddleLeft;text.raycastTarget=false;
        }
        static RectTransform Child(Transform parent,string name,Vector2 pos,Vector2 size)
        {
            var child=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;
            child.SetParent(parent,false);child.anchoredPosition=pos;child.sizeDelta=size;return child;
        }
        void Update()
        {
            if(canvas==null)return;
            canvas.enabled=preview.Loading;
            if(canvas.enabled)particles.Animate(Time.unscaledTime);
        }
        void OnDisable(){if(canvas!=null)canvas.enabled=false;}
        void OnDestroy(){if(canvas!=null)Destroy(canvas.gameObject);}
    }
}
