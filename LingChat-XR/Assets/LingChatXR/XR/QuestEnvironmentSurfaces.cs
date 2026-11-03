using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if UNITY_ANDROID || UNITY_EDITOR
using UnityEngine.XR.OpenXR.Features.Meta;
#endif
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace LingChatXR.XR
{
    // 复用 Quest Space Setup / AR Foundation 平面；不读取相机图像，不上传房间数据。
    public sealed class QuestEnvironmentSurfaces:MonoBehaviour
    {
        const string ScenePermission="com.oculus.permission.USE_SCENE";
        QuestEntry entry;GameObject origin;ARSession session;ARPlaneManager planes;
        readonly Dictionary<TrackableId,GameObject> candidates=new Dictionary<TrackableId,GameObject>();
        TrackableId? hover;
        float elapsed;
        bool scanPending,scanRequested,permissionPending;
        public bool Selecting{get;private set;}
        public string Hint{get;private set;}="正在连接房间平台";
        public int SurfaceCount=>candidates.Count;
        public void Configure(QuestEntry value,GameObject xrOrigin){entry=value;origin=xrOrigin;BeginSelection();}
        public void Connect(ARSession arSession){session=arSession;EnsurePermission();}
        public void BeginSelection()
        {
            hover=null;elapsed=0;Selecting=true;entry.Placement.Invalidate();entry.Bridge.CancelInputs();entry.Preview.ResetPlacement();entry.Bridge.Table.GetComponent<Collider>().enabled=false;entry.Panel.Collapse();
            foreach(var candidate in candidates.Values)if(candidate!=null)candidate.SetActive(true);
            UpdateHint();if(session!=null)EnsurePermission();
        }
        public void StopSelection(){Selecting=false;foreach(var candidate in candidates.Values)if(candidate!=null)candidate.SetActive(false);}
        void UpdateHint(){Hint=candidates.Count>0?"将指尖或手掌停在浅蓝色平台附近片刻即可选择":"未找到平台 · 点扫描房间，在系统中标记桌面；也可手动标定";}
        void EnsurePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                if(permissionPending)return;permissionPending=true;Hint="请允许访问空间数据，以显示现实平台";
                var callbacks=new PermissionCallbacks();
                callbacks.PermissionGranted+=_=>{permissionPending=false;if(this!=null)StartPlanes();};
                callbacks.PermissionDenied+=_=>{permissionPending=false;scanPending=false;if(this!=null)Hint="空间权限未允许，可手动标定；授权后再扫描";};
                callbacks.PermissionDeniedAndDontAskAgain+=_=>{permissionPending=false;scanPending=false;if(this!=null)Hint="请在系统应用权限中允许空间数据，或手动标定";};
                Permission.RequestUserPermission(ScenePermission,callbacks);return;
            }
#endif
            StartPlanes();
        }
        void StartPlanes()
        {
            if(planes==null){planes=origin.AddComponent<ARPlaneManager>();planes.requestedDetectionMode=PlaneDetectionMode.Horizontal;planes.trackablesChanged.AddListener(PlanesChanged);}
            UpdateHint();
            if(scanPending){scanPending=false;RequestCapture();}
        }
        public void RequestScan(){BeginSelection();scanPending=true;if(session==null){Hint="XR 连接尚未就绪，可先手动标定";scanPending=false;return;}EnsurePermission();}
        void RequestCapture()
        {
#if UNITY_ANDROID || UNITY_EDITOR
            if(session.subsystem is MetaOpenXRSessionSubsystem meta&&meta.TryRequestSceneCapture()){scanRequested=true;Hint="请在 Quest 系统中扫描房间并标记桌面，然后返回";Debug.Log("XR_ROOM_SCAN_REQUESTED");return;}
#endif
            Hint="系统扫描暂不可用，请完成 Quest 空间设置后重试，或手动标定";
        }
        void OnApplicationPause(bool paused)
        {
            if(!paused&&scanRequested){scanRequested=false;if(planes!=null){planes.enabled=false;planes.enabled=true;}BeginSelection();}
        }
        void PlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            foreach(var removed in args.removed)Remove(removed.Key);
            foreach(var added in args.added)Upsert(added);
            foreach(var updated in args.updated)Upsert(updated);
            if(Selecting)UpdateHint();
        }
        static bool Suitable(ARPlane plane)=>plane.alignment==PlaneAlignment.HorizontalUp&&Vector3.Dot(plane.transform.up,Vector3.up)>.97f&&plane.size.x>.08f&&plane.size.y>.08f&&(plane.classifications&(PlaneClassifications.Floor|PlaneClassifications.Ceiling))==0;
        void Remove(TrackableId id){if(candidates.TryGetValue(id,out var obj)){Destroy(obj);candidates.Remove(id);}}
        void Upsert(ARPlane plane)
        {
            Remove(plane.trackableId);if(!Suitable(plane))return;
            var obj=new GameObject("Selectable real platform");obj.SetActive(false);obj.transform.SetParent(plane.transform,false);obj.transform.localPosition=new Vector3(plane.centerInPlaneSpace.x,0,plane.centerInPlaneSpace.y);
            var collider=obj.AddComponent<BoxCollider>();collider.size=new Vector3(plane.size.x,.025f,plane.size.y);
            collider.isTrigger=true;

            var line=obj.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.numCornerVertices=5;line.positionCount=4;line.widthMultiplier=.004f;line.sharedMaterial=Resources.Load<Material>("QuestControllers/Water");float w=plane.size.x*.5f,d=plane.size.y*.5f;line.SetPositions(new[]{new Vector3(-w,.006f,-d),new Vector3(w,.006f,-d),new Vector3(w,.006f,d),new Vector3(-w,.006f,d)});
            candidates.Add(plane.trackableId,obj);obj.SetActive(Selecting);
        }
        void Update()
        {
            if(!Selecting)return;
            TrackableId? nearest=null;
            float best=.08f;
            if(entry.InputAvailable && entry.WalkGestures!=null && entry.Placement.Step==0)
                foreach(var pair in candidates)
                {
                    var distance=entry.WalkGestures.SurfaceDistance(pair.Value.GetComponent<BoxCollider>());
                    if(distance>=best)continue;
                    nearest=pair.Key;best=distance;
                }
            if(!System.Nullable.Equals(hover,nearest))
            {
                if(hover.HasValue && candidates.TryGetValue(hover.Value,out var previous))
                    previous.GetComponent<LineRenderer>().widthMultiplier=.004f;
                elapsed=0;hover=nearest;UpdateHint();
            }
            if(!nearest.HasValue)return;
            var target=candidates[nearest.Value];
            elapsed+=Mathf.Min(Time.unscaledDeltaTime,.1f);
            float progress=Mathf.Clamp01(elapsed/.8f);
            target.GetComponent<LineRenderer>().widthMultiplier=Mathf.Lerp(.004f,.014f,progress);
            Hint="保持在平台附近 · "+Mathf.RoundToInt(progress*100)+"%";
            if(progress>=1){elapsed=0;SelectPlane(target.GetComponentInParent<ARPlane>());}
        }
        void SelectPlane(ARPlane plane)
        {
            if(!Selecting||plane==null||!Suitable(plane))return;
            Vector3 right=plane.transform.right;float yaw=Mathf.Atan2(-right.z,right.x)*Mathf.Rad2Deg;
            if(entry.Placement.ChooseSurface(plane.center,plane.size.x*.5f,plane.size.y*.5f,yaw)){StopSelection();Debug.Log("XR_ROOM_PLATFORM_SELECTED yaw="+yaw);}
            else Hint=entry.Placement.Hint;
        }
        void OnDestroy(){if(planes!=null)planes.trackablesChanged.RemoveListener(PlanesChanged);foreach(var obj in candidates.Values)if(obj!=null)Destroy(obj);}
    }
}
