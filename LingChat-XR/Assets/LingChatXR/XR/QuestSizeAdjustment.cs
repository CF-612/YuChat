using LingChatXR.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LingChatXR.XR
{
    // 将官方手部输入适配到已有尺寸事务；不写角色 Transform、不另建抓取或动画执行器。
    public sealed class QuestSizeAdjustment:MonoBehaviour
    {
        QuestEntry entry;InputAction rightPinch;int generation;
        bool editing,dragging,armed,waitForRelease,farDrag;float startDistance,startHeight;Vector3 startPoint,dragDirection;Plane dragPlane;
        public bool Active=>editing&&entry!=null&&entry.Preview.SizeEditing&&entry.Preview.SizeEditGeneration==generation;
        public bool BlocksGrab=>Active||waitForRelease;
        public bool Dragging=>dragging;
        public string Hint{get;private set;}="放稳角色后可调整大小";
        public void Configure(QuestEntry value)
        {
            entry=value;rightPinch=entry.InputActions.FindAction("XRI Right Interaction/Select",true);
        }
        public bool Begin()
        {
            var preview=entry.Preview;
            if(Active)return false;
            if(!preview.Ready||!preview.Interaction.PlaneConfirmed||(preview.Interaction.State!=PetState.Idle&&preview.Interaction.State!=PetState.Touched))
            {Hint="请先放稳角色，再调整大小";return false;}
            entry.Bridge.CancelInputs();
            foreach(var interactor in entry.Controllers)entry.Interactions.CancelInteractorSelection((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)interactor);
            if(!preview.BeginSizeEdit(out generation)){Hint=preview.Status;return false;}
            editing=true;dragging=false;armed=false;Hint="右手捏住右上角箭头，往外拉大或往内缩小";entry.Panel.OpenScaleFrame();return true;
        }
        public bool PreviewHeight(float height)
        {
            if(!Active||!float.IsFinite(height))return false;
            if(!entry.Preview.PreviewSize(generation,height)){Hint=entry.Preview.Status;return false;}
            Hint="当前 "+(entry.Preview.Avatar.HeightMeters*100).ToString("0.0")+" cm · 尚未保存";return true;
        }
        void Update()
        {
            bool pressed=rightPinch.IsPressed();
            if(!pressed)waitForRelease=false;
            if(!editing)return;
            if(!Active){editing=dragging=false;entry.Panel.HideScaleFrame();return;}
            if(!entry.RightBareHand.CanPinch)
            {
                dragging=armed=false;Hint="右手暂歇。重新追踪后松手，再拖动箭头";return;
            }
            if(!pressed){armed=true;dragging=false;return;}
            if(!dragging)
            {
                if(!armed)return;
                armed=false;
                Vector3 handle=entry.Panel.ScaleHandlePosition;
                bool near=Vector3.Distance(entry.RightHand.position,handle)<=.025f;
                if(!near&&!entry.Panel.RightRayHitsScaleHandle())return;
                var foot=entry.Preview.Avatar.transform.position;
                dragDirection=(handle-foot).normalized;startDistance=Vector3.Distance(handle,foot);
                if(startDistance<.06f)return;
                startHeight=entry.Preview.Avatar.HeightMeters;
                farDrag=!near;dragPlane=new Plane(entry.Panel.ScaleFrame.transform.forward,handle);
                if(!TryReadPoint(out startPoint))return;
                dragging=true;
            }
            if(!TryReadPoint(out var point))return;
            float ratio=1+Vector3.Dot(point-startPoint,dragDirection)/startDistance;
            PreviewHeight(startHeight*ratio);
        }
        bool TryReadPoint(out Vector3 point)
        {
            point=entry.RightHand.position;
            if(!farDrag)return true;
            var ray=new Ray(entry.RightRay.transform.position,entry.RightRay.transform.forward);
            if(!dragPlane.Raycast(ray,out float distance)||distance>3)return false;
            point=ray.GetPoint(distance);return true;
        }
        public bool Commit()
        {
            if(!Active)return false;
            if(!entry.Preview.CommitSizeEdit(generation)){Hint=entry.Preview.Status;return false;}
            editing=false;dragging=false;waitForRelease=true;Hint="大小已保存";entry.Panel.HideScaleFrame();return true;
        }
        public void Cancel()
        {
            bool wasActive=Active;if(wasActive)entry.Preview.CancelSizeEdit();editing=false;dragging=false;armed=false;waitForRelease=true;Hint="已恢复调整前的大小";if(entry!=null&&entry.Panel!=null)entry.Panel.HideScaleFrame();
        }
        void OnDisable(){Cancel();}
    }
}
