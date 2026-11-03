using UnityEngine;

namespace LingChatXR.XR
{
    // 扫描平台和三点手动标定共用一个提交入口，不改变角色物理。
    public sealed class QuestTablePlacement:MonoBehaviour
    {
        QuestEntry entry;int step;float acceptAfter;Vector3 center;GameObject cursor;LineRenderer outline;
        bool pending;float pendingWidth,pendingDepth,yaw;
        public string Hint{get;private set;}="请选择现实平台，或手动标定";
        public int Step=>step;
        public bool HasPendingPlane=>pending;
        public void Configure(QuestEntry value)
        {
            entry=value;cursor=GameObject.CreatePrimitive(PrimitiveType.Sphere);cursor.name="Table contact marker";Destroy(cursor.GetComponent<Collider>());cursor.transform.localScale=Vector3.one*.015f;
            cursor.GetComponent<Renderer>().sharedMaterial=Resources.Load<Material>("QuestControllers/Water");cursor.SetActive(false);
            outline=new GameObject("Table calibration boundary").AddComponent<LineRenderer>();outline.sharedMaterial=Resources.Load<Material>("QuestControllers/Water");outline.numCornerVertices=5;outline.positionCount=4;outline.loop=true;outline.widthMultiplier=.003f;outline.enabled=false;
        }
        public void Begin()
        {
            entry.EnvironmentSurfaces?.StopSelection();entry.Panel.Collapse();
            entry.Bridge.CancelInputs();entry.Preview.ResetPlacement();entry.Bridge.Table.GetComponent<Collider>().enabled=false;
            pending=false;step=1;acceptAfter=Time.unscaledTime+.35f;Hint="① 右手浅蓝色点贴近桌面中心，捏合确认";outline.enabled=false;
        }
        public void Invalidate(){pending=false;step=0;if(cursor!=null)cursor.SetActive(false);if(outline!=null)outline.enabled=false;Hint="空间已变化，请重新选择平台";}
        void Update()
        {
            if(pending&&entry.Preview.Ready)CommitPending();
            bool active=step>0&&entry.RightTracked;cursor.SetActive(active);if(!active)return;
            Vector3 point=entry.RightHand.position;cursor.transform.position=point;
            if(step==2){outline.enabled=true;outline.loop=false;outline.positionCount=2;outline.SetPositions(new[]{center,new Vector3(point.x,center.y,point.z)});}
            if(step==3){var local=Quaternion.Euler(0,-yaw,0)*(point-center);DrawBounds(pendingWidth,Mathf.Abs(local.z));}
            if(Time.unscaledTime<acceptAfter||!entry.RightBareHand.CanPinch||!entry.RightTrigger.WasPressedThisFrame())return;
            if(entry.RightRay.TryGetCurrentUIRaycastResult(out var hit)&&hit.gameObject!=null)return;
            AcceptPoint(point);
        }
        void DrawBounds(float w,float d)
        {
            var rotation=Quaternion.Euler(0,yaw,0);outline.enabled=true;outline.loop=true;outline.positionCount=4;
            outline.SetPositions(new[]{center+rotation*new Vector3(-w,.005f,-d),center+rotation*new Vector3(w,.005f,-d),center+rotation*new Vector3(w,.005f,d),center+rotation*new Vector3(-w,.005f,d)});
        }
        public bool AcceptPoint(Vector3 point)
        {
            if(step==0)return false;
            if(step==1){center=point;step=2;acceptAfter=Time.unscaledTime+.25f;Hint="② 移到桌面右边缘中点，捏合确定宽度和桌边方向";return true;}
            if(Mathf.Abs(point.y-center.y)>.12f){Hint="请让浅蓝色点贴近同一桌面高度";return false;}
            float minimum=entry.Preview.Ready?entry.Preview.Avatar.FootprintRadius+.02f:.02f;
            if(step==2)
            {
                var delta=Vector3.ProjectOnPlane(point-center,Vector3.up);float w=delta.magnitude;
                if(w<minimum||w>1.5f){Hint="请移到右边缘中点，并留足角色足迹";return false;}
                pendingWidth=w;yaw=Mathf.Atan2(-delta.z,delta.x)*Mathf.Rad2Deg;step=3;acceptAfter=Time.unscaledTime+.25f;Hint="③ 移到前后边缘或角点，捏合确定深度";return true;
            }
            float d=Mathf.Abs((Quaternion.Euler(0,-yaw,0)*(point-center)).z);
            if(d<minimum||d>1.5f){Hint="深度不足或过大，请移到前后边缘";return false;}
            return ChooseSurface(center,pendingWidth,d,yaw);
        }
        public bool ChooseSurface(Vector3 position,float halfWidth,float halfDepth,float yawDegrees)
        {
            if(halfWidth<=0||halfDepth<=0||float.IsNaN(yawDegrees)||float.IsInfinity(yawDegrees))return false;
            center=position;pendingWidth=halfWidth;pendingDepth=halfDepth;yaw=yawDegrees;pending=true;step=0;cursor.SetActive(false);DrawBounds(halfWidth,halfDepth);
            Hint="平台已记录 · 角色就绪后放置";
            if(entry.Preview.Ready)return CommitPending();return true;
        }
        bool CommitPending()
        {
            pending=false;float minimum=entry.Preview.Avatar.FootprintRadius+.02f;
            if(pendingWidth<minimum||pendingDepth<minimum){Hint="平台放不下角色，请选择更大平台或重新手动标定";entry.Panel.Expand();return false;}
            if(!entry.ConfirmTable(center,pendingWidth,pendingDepth,yaw)){Hint="放置失败，请重新选择平台";entry.Panel.Expand();return false;}
            outline.enabled=false;Hint="已放置 · 近头抚摸 · 捏合提起，松开放回 · 翻开手掌唤出菜单";return true;
        }
        void OnDestroy(){if(cursor!=null)Destroy(cursor);if(outline!=null)Destroy(outline.gameObject);}
    }
}
