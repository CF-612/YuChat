using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace LingChatXR.XR
{
    // Built-in / OpenXR 的独立画质入口；必须早于创建眼缓冲，不依赖桌面舞台设置。
    public static class QuestRenderQuality
    {
        public const int Msaa=4;
        public const float ResolutionScale=1.15f;
        public static void Configure(Camera camera)
        {
            QualitySettings.antiAliasing=Msaa;QualitySettings.globalTextureMipmapLimit=0;QualitySettings.anisotropicFiltering=AnisotropicFiltering.ForceEnable;
            camera.allowHDR=false;camera.allowMSAA=true;camera.allowDynamicResolution=false;camera.renderingPath=RenderingPath.Forward;
            XRSettings.eyeTextureResolutionScale=ResolutionScale;XRSettings.renderViewportScale=1;
        }
        public static void ConfigureDisplays()
        {
            var displays=new List<XRDisplaySubsystem>();SubsystemManager.GetSubsystems(displays);
            foreach(var display in displays)if(display.running){display.SetMSAALevel(Msaa);display.scaleOfAllRenderTargets=ResolutionScale;display.scaleOfAllViewports=1;display.foveatedRenderingLevel=0;}
        }
        public static System.Collections.IEnumerator ReportActualBuffers()
        {
            // 无设备的 Editor 没有眼缓冲；运行时首帧分配后再记录实际尺寸，不把请求值当实际值。
            for(int i=0;i<15;i++)yield return null;
            var displays=new List<XRDisplaySubsystem>();SubsystemManager.GetSubsystems(displays);
            foreach(var display in displays)if(display.running)
            {
                string buffer="not-ready";
                if(display.GetRenderPassCount()>0){display.GetRenderPass(0,out var pass);var descriptor=pass.renderTargetDesc;buffer=$"{descriptor.width}x{descriptor.height} samples={descriptor.msaaSamples}";}
                Debug.Log($"XR_RENDER_QUALITY requestedMSAA={Msaa} qualityMSAA={QualitySettings.antiAliasing} scale={display.scaleOfAllRenderTargets} viewport={display.scaleOfAllViewports} buffer={buffer} graphics={SystemInfo.graphicsDeviceType} foveation={display.foveatedRenderingLevel}");
            }
        }
    }
}
