using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace LingChatXR.XR
{
    // 仅做 XRI 生命周期接线。关节、捏合与系统手势均由官方 XR Hands / Meta Aim 提供。
    [DefaultExecutionOrder(-90)]
    public sealed class QuestTrackedHand:MonoBehaviour
    {
        bool left;XRDirectInteractor direct;XRRayInteractor ray;XRPokeInteractor poke;QuestEntry entry;
        XRInteractorLineVisual visual;LineRenderer line;Material lineMaterial;
        public bool PointerVisible=>line!=null&&line.enabled;
        public bool Tracked=>Device!=null&&Device.isTracked.isPressed;
        XRHandDevice Device=>left?XRHandDevice.leftHand:XRHandDevice.rightHand;
        MetaAimHand Aim=>left?MetaAimHand.left:MetaAimHand.right;
        public float PinchStrength=>CanPinch?Aim.pinchStrengthIndex.ReadValue():0;
        public bool CanPinch=>Tracked&&(entry==null||entry.InputAvailable)&&Aim!=null&&((MetaAimFlags)Aim.aimFlags.ReadValue()&MetaAimFlags.SystemGesture)==0;
        public void Configure(bool isLeft,XRDirectInteractor near,XRRayInteractor far,XRPokeInteractor finger,QuestEntry owner=null)
        {
            left=isLeft;direct=near;ray=far;poke=finger;entry=owner;
            visual=ray.GetComponent<XRInteractorLineVisual>();line=ray.GetComponent<LineRenderer>();
            lineMaterial=new Material(line.sharedMaterial);lineMaterial.color=Color.white;line.sharedMaterial=lineMaterial;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(.7f,.86f,1),0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.55f,.35f),new GradientAlphaKey(.8f,1)});
            visual.lineWidth=.0015f;visual.validColorGradient=gradient;visual.invalidColorGradient=gradient;visual.blockedColorGradient=gradient;visual.stopLineAtFirstRaycastHit=true;visual.enabled=false;line.enabled=false;
        }
        void Update()
        {
            bool active=CanPinch;
            direct.enabled=active;poke.enabled=active;
            ray.enabled=active&&((MetaAimFlags)Aim.aimFlags.ReadValue()&MetaAimFlags.Valid)!=0;
        }
        void LateUpdate()
        {
            // 保留射线检测供 UI 命中；无 UI 目标不画线，摸头/抓放时也不显示。
            bool ui=ray.TryGetCurrentUIRaycastResult(out var hit)&&hit.gameObject!=null&&hit.gameObject.activeInHierarchy;
            bool platform=entry!=null&&((entry.EnvironmentSurfaces!=null&&entry.EnvironmentSurfaces.Selecting)||(entry.Bridge!=null&&entry.Bridge.SelectingWalk))&&ray.hasHover;
            bool show=CanPinch&&ray.enabled&&(ui||platform);
            visual.enabled=show;if(!show)line.enabled=false;
        }
        void OnDestroy(){if(lineMaterial!=null)Destroy(lineMaterial);}
    }
}
