using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UniVRM10;
using UnityEngine;

namespace LingChatXR.Editor
{
    internal static class V21MaterialAbProbe
    {
        internal static IEnumerator Check(FeiyuAvatar avatar,Camera camera,string output)
        {
            var renderers=avatar.Instance.GetComponentsInChildren<Renderer>(true);
            var originals=renderers.ToDictionary(r=>r,r=>r.sharedMaterials);
            AnimePresentation.Apply(avatar);
            var mapped=renderers.ToDictionary(r=>r,r=>r.sharedMaterials);
            var bridge=avatar.GetComponent<LilToonExpressionBridge>();
            var expression=avatar.Instance.Runtime.Expression;
            if(System.Environment.GetEnvironmentVariable("XR_V21_FRONT_PHOTO")=="1")
            {
                foreach(var key in expression.ExpressionKeys)expression.SetWeight(key,0);
                expression.SetWeight(ExpressionKey.CreateCustom("eyeCircleShock"),1);
                avatar.Instance.Runtime.Process();
                bridge.Synchronize();
                // 固定同一姿态、同一帧与相机，仅切换整套实际材质实例。
                V21ModelProbe.Capture(camera,Path.Combine(output,"front-shock-liltoon-synced.png"));
                var alphas=new JArray();
                foreach(var r in renderers)for(int i=0;i<originals[r].Length;i++)
                    if(Selected(originals[r][i]))alphas.Add(new JObject{["name"]=originals[r][i].name,["sourceAlpha"]=originals[r][i].GetColor("_Color").a,["targetAlpha"]=mapped[r][i].GetColor("_Color").a});
                foreach(var r in renderers)r.sharedMaterials=originals[r];
                V21ModelProbe.Capture(camera,Path.Combine(output,"front-shock-mtoon.png"));
                File.WriteAllText(Path.Combine(output,"front-photo.json"),new JObject{["frame"]=Time.frameCount,["cameraPosition"]=camera.transform.position.ToString("F5"),["cameraRotation"]=camera.transform.rotation.ToString("F5"),["orthographicSize"]=camera.orthographicSize,["samePoseSameFrame"]=true,["explicitSyncBeforeCapture"]=true,["alphas"]=alphas}.ToString());
                yield break;
            }
            var rows=new JArray();
            foreach(string mode in new[]{"A-auto","A-sync","B-original"})
            {
                foreach(var r in renderers)
                {
                    var materials=(Material[])mapped[r].Clone();
                    if(mode=="B-original")for(int i=0;i<materials.Length;i++)if(Selected(originals[r][i]))materials[i]=originals[r][i];
                    r.sharedMaterials=materials;
                }
                foreach(string state in new[]{"neutral","shock","reset"})
                {
                    foreach(var key in expression.ExpressionKeys)expression.SetWeight(key,0);
                    expression.SetWeight(ExpressionKey.CreateCustom("eyeCircleShock"),state=="shock"?1:0);
                    avatar.Instance.Runtime.Process();
                    Snapshot("after-process");
                    // 批处理可能不推进游戏帧；记录真实帧号，不把编辑器回调当作 LateUpdate 已执行。
                    for(int tick=0;tick<4;tick++){UnityEditor.EditorApplication.QueuePlayerLoopUpdate();yield return null;}
                    Snapshot("before-explicit-sync");
                    if(mode=="A-sync")bridge.Synchronize();
                    Snapshot("after-sync-stage");
                    Camera.CameraCallback callback=c=>{if(c==camera)Snapshot("onPreRender");};
                    Camera.onPreRender+=callback;
                    try{V21ModelProbe.Capture(camera,Path.Combine(output,mode+"-"+state+".png"));}
                    finally{Camera.onPreRender-=callback;}
                    Snapshot("after-render");
                    void Snapshot(string stage)
                    {
                        var materials=new JArray();var seen=new HashSet<Material>();
                        foreach(var r in renderers)for(int i=0;i<originals[r].Length;i++)
                        {
                            var source=originals[r][i];if(!Selected(source)||!seen.Add(source))continue;
                            var target=mapped[r][i];var active=r.sharedMaterials[i];
                            materials.Add(new JObject{["name"]=source.name,["sourceId"]=source.GetInstanceID(),["targetId"]=target.GetInstanceID(),["drawId"]=active.GetInstanceID(),["sourceAlpha"]=source.GetColor("_Color").a,["targetAlpha"]=target.GetColor("_Color").a,["activeShader"]=active.shader.name,["queue"]=active.renderQueue});
                        }
                        rows.Add(new JObject{["mode"]=mode,["state"]=state,["stage"]=stage,["frame"]=Time.frameCount,["materials"]=materials});
                    }
                }
            }
            File.WriteAllText(Path.Combine(output,"material-ab.json"),rows.ToString());
        }
        static bool Selected(Material m)=>m!=null&&(m.name.StartsWith("RestoredCircle_V26_")||m.name.StartsWith("SpecialHidden_")&&m.name.Contains("ContinuousLash"));
    }
}
