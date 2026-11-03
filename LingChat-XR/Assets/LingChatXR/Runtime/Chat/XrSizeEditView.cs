using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Chat
{
    // PC 控制点适配器。VR 双手距离以后只需调用同一代次化尺寸编辑入口。
    public sealed class XrSizeEditView
    {
        readonly VisualElement root,frame,handle,bar;
        readonly Label status;
        readonly Label dragHint;
        FeiyuPreview preview;
        Vector2 down,direction;
        float startHeight,baselinePixels;
        int pointer=-1,generation;
        public bool Active=>preview!=null&&preview.SizeEditing;
        public XrSizeEditView(VisualElement parent)
        {
            root=new VisualElement{pickingMode=PickingMode.Ignore};root.AddToClassList("size-overlay");parent.Add(root);
            frame=new DashedFrame{pickingMode=PickingMode.Ignore};frame.AddToClassList("size-frame");root.Add(frame);
            bar=new VisualElement{name="size-toolbar"};bar.AddToClassList("size-toolbar");root.Add(bar);
            // 控制点单独放在最上层；框可以出屏，实际可拖动控件始终留在安全区域。
            handle=new ResizeHandle{pickingMode=PickingMode.Position,name="size-handle"};handle.AddToClassList("size-handle");root.Add(handle);
            dragHint=new Label("拖动缩放"){pickingMode=PickingMode.Ignore};dragHint.AddToClassList("size-drag-hint");handle.Add(dragHint);
            status=new Label();status.AddToClassList("size-status");bar.Add(status);
            var save=new Button(()=>{if(preview!=null)preview.CommitSizeEdit(preview.SizeEditGeneration);}){text="确认大小"};save.AddToClassList("settings-primary");bar.Add(save);
            var cancel=new Button(()=>preview?.CancelSizeEdit()){text="取消"};cancel.AddToClassList("settings-secondary");bar.Add(cancel);
            handle.RegisterCallback<PointerDownEvent>(e=>
            {
                if(e.button!=0||!Active)return;
                pointer=e.pointerId;generation=preview.SizeEditGeneration;down=e.position;startHeight=preview.Avatar.HeightMeters;
                Vector2 anchor=new Vector2(frame.worldBound.center.x,frame.worldBound.yMax);direction=(down-anchor).normalized;baselinePixels=Mathf.Max(40,Vector2.Distance(down,anchor));
                handle.CapturePointer(pointer);e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e=>
            {
                if(e.pointerId!=pointer||!handle.HasPointerCapture(pointer)||!Active)return;
                float ratio=1+Vector2.Dot((Vector2)e.position-down,direction)/baselinePixels;
                preview.PreviewSize(generation,startHeight*ratio);e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e=>{if(e.pointerId==pointer){handle.ReleasePointer(pointer);pointer=-1;e.StopPropagation();}});
            handle.RegisterCallback<PointerCaptureOutEvent>(_=>pointer=-1);
            root.style.display=DisplayStyle.None;
        }
        public void Update()
        {
            if(preview==null)preview=Object.FindAnyObjectByType<FeiyuPreview>();
            root.style.display=Active?DisplayStyle.Flex:DisplayStyle.None;
            if(!Active){if(pointer>=0){handle.ReleasePointer(pointer);pointer=-1;}return;}
            if(Input.GetKeyDown(KeyCode.Escape)){preview.CancelSizeEdit();return;}
            var camera=preview.ViewCamera;Vector3 foot=camera.WorldToViewportPoint(preview.Avatar.transform.position);
            Vector3 top=camera.WorldToViewportPoint(preview.Avatar.transform.position+Vector3.up*preview.Avatar.HeightMeters);
            float width=root.resolvedStyle.width,height=root.resolvedStyle.height;
            bool visible=foot.z>0&&top.z>0;frame.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            float pixels=Mathf.Max(70,Mathf.Abs(top.y-foot.y)*height),half=pixels*.44f;
            frame.style.left=foot.x*width-half;frame.style.top=(1-top.y)*height-12;frame.style.width=half*2;frame.style.height=pixels+12;
            float toolbarWidth=Mathf.Min(620,width-24);bar.style.width=toolbarWidth;bar.style.left=(width-toolbarWidth)*.5f;
            float toolbarHeight=float.IsFinite(bar.resolvedStyle.height)?Mathf.Max(64,bar.resolvedStyle.height):80;
            var bounds=HandleBounds(new Vector2(width,height),new Vector2(foot.x*width+half,(1-top.y)*height-12),toolbarHeight);
            handle.style.left=bounds.x;handle.style.top=bounds.y;handle.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            dragHint.style.left=bounds.xMax+100>width?-100:44;
            status.text="调整尺寸   "+(preview.Avatar.HeightMeters*100).ToString("0.0")+" cm\n拖动箭头等比缩放 · Esc 取消";
        }
        public static Rect HandleBounds(Vector2 viewport,Vector2 corner,float toolbarHeight)
        {
            const float size=36,padding=12;
            return new Rect(Mathf.Clamp(corner.x-size*.5f,padding,Mathf.Max(padding,viewport.x-padding-size)),
                Mathf.Clamp(corner.y-size*.5f,padding,Mathf.Max(padding,viewport.y-24-toolbarHeight-padding-size)),size,size);
        }
        sealed class DashedFrame:VisualElement
        {
            public DashedFrame(){generateVisualContent+=context=>
            {
                var p=context.painter2D;p.strokeColor=resolvedStyle.color;p.lineWidth=1.5f;
                float w=contentRect.width,h=contentRect.height;if(w<2||h<2)return;
                var points=new[]{new Vector2(0,0),new Vector2(w,0),new Vector2(w,h),new Vector2(0,h),new Vector2(0,0)};
                for(int i=0;i<4;i++)
                {
                    Vector2 delta=points[i+1]-points[i];float length=delta.magnitude;
                    for(float at=8;at<length-8;at+=15){p.BeginPath();p.MoveTo(points[i]+delta*(at/length));p.LineTo(points[i]+delta*(Mathf.Min(at+7,length-8)/length));p.Stroke();}
                }
            };}
        }
        sealed class ResizeHandle:VisualElement
        {
            public ResizeHandle(){generateVisualContent+=context=>
            {
                var p=context.painter2D;p.strokeColor=resolvedStyle.color;p.lineWidth=1.7f;
                void Line(params Vector2[] points){p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.Stroke();}
                Line(new Vector2(11,25),new Vector2(25,11));
                Line(new Vector2(17,11),new Vector2(25,11),new Vector2(25,19));
                Line(new Vector2(11,17),new Vector2(11,25),new Vector2(19,25));
            };}
        }
    }
}
