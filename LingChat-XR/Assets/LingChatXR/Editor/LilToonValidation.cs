using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UniVRM10;

namespace LingChatXR.Editor
{
    public static class LilToonValidation
    {
        public static void Begin(FeiyuPreview preview,string evidence,Action<int> finish)
        {
            var check=Check(preview,evidence);int lastFrame=-1;
            double deadline=EditorApplication.timeSinceStartup+60;
            void Step()
            {
                try
                {
                    if(EditorApplication.timeSinceStartup>deadline)throw new Exception("表情分帧验收超时");
                    if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                    if(check.MoveNext())return;
                    EditorApplication.update-=Step;finish(0);
                }
                catch(Exception e){EditorApplication.update-=Step;Debug.LogWarning(e);finish(1);}
            }
            EditorApplication.update+=Step;
        }
        static IEnumerator Check(FeiyuPreview preview,string evidence)
        {
            var a=preview.Avatar;var bridge=a.GetComponent<LilToonExpressionBridge>();
            a.FreezePresentation=true;a.Instance.UpdateType=Vrm10Instance.UpdateTypes.None;
            // 缩放回归：同一相机/姿态，先恢复旧半径重现，再恢复适配半径。
            var springService=UniVRM10.FastSpringBones.FastSpringBoneService.Instance;
            var springUpdate=springService.UpdateType;
            springService.UpdateType=UniVRM10.FastSpringBones.FastSpringBoneService.UpdateTypes.Manual;
            var joints=a.Instance.GetComponentsInChildren<VRM10SpringBoneJoint>();
            var radii=joints.Select(j=>j.m_jointRadius).ToArray();
            var side=joints.Where(j=>j.name.StartsWith("HairSide")).ToArray();
            float scale=a.Instance.transform.localScale.x;
            var hairRenderer=a.Instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMaterials.Any(m=>m.name.StartsWith("HairCap_ContinuousUV")));
            var bakedHair=new Mesh();Vector3[] restVertices=null;
            float HairDeviation(){hairRenderer.BakeMesh(bakedHair);var vertices=bakedHair.vertices;double total=0;for(int i=0;i<vertices.Length;i++)total+=(vertices[i]-restVertices[i]).sqrMagnitude;return Mathf.Sqrt((float)(total/vertices.Length));}
            Quaternion[] rest=null;
            float SideAngle()=>side.Select((j,i)=>Quaternion.Angle(rest[i],j.transform.localRotation)).Max();
            void Simulate(){a.Instance.Runtime.SpringBone.ReconstructSpringBone();a.ResetSecondaryMotion();for(int i=0;i<180;i++)springService.ManualUpdate(1f/60f);}
            a.ResetSecondaryMotion();yield return null;yield return null;
            rest=side.Select(j=>j.transform.localRotation).ToArray();
            hairRenderer.BakeMesh(bakedHair);restVertices=bakedHair.vertices;
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"hair-rest-liltoon.png"));
            for(int i=0;i<joints.Length;i++)joints[i].m_jointRadius=radii[i]/scale;
            Simulate();yield return null;yield return null;float before=SideAngle(),beforeMesh=HairDeviation();
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"hair-radius-before.png"));
            for(int i=0;i<joints.Length;i++)joints[i].m_jointRadius=radii[i];
            Simulate();yield return null;yield return null;float after=SideAngle(),afterMesh=HairDeviation();
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"hair-radius-after.png"));
            UnityEngine.Object.Destroy(bakedHair);
            File.WriteAllText(Path.Combine(evidence,"hair-geometry-validation.json"),"{\"beforeRms\":"+beforeMesh.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"afterRms\":"+afterMesh.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            if(beforeMesh<.001f||afterMesh>=beforeMesh*.5f)throw new Exception($"头发蒙皮回归未改善：{beforeMesh} -> {afterMesh}");
            var frames=Resources.LoadAll<Texture2D>("LocalLingChatEffects/Thinking");
            if(frames.Length!=101||frames.Any(f=>f.width!=405||f.height!=314))throw new Exception("思考气泡帧尺寸被缩放");
            File.WriteAllText(Path.Combine(evidence,"hair-radius-validation.json"),"{\"result\":\"passed\",\"beforeMaxSideDegrees\":"+before.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"afterMaxSideDegrees\":"+after.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"modelScale\":"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"bubbleFrames\":101,\"bubbleWidth\":405,\"bubbleHeight\":314}");
            springService.UpdateType=springUpdate;
            var materials=a.Instance.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            if(materials.Length!=104||bridge==null||bridge.MaterialCount!=104)throw new Exception("lilToon 材质数量不完整");
            foreach(var m in materials)
                if(!m.shader.name.Contains("lilToon")||!m.shader.isSupported||ShaderUtil.ShaderHasError(m.shader))throw new Exception("lilToon Shader 无效："+m.name);
            Material Find(string prefix)=>materials.First(m=>m.name.StartsWith(prefix));
            var skin=Find("tripo_part_1_material");var hair=Find("HairCap_ContinuousUV_-1");
            var cloth=Find("WhiteCloth_CleanBalanced_v20");var blush=Find("SharedNaturalBlush_Image2");
            if(skin.GetColor("_OutlineColor").r<=skin.GetColor("_OutlineColor").b||hair.GetColor("_OutlineColor").b<=hair.GetColor("_OutlineColor").r)throw new Exception("皮肤/头发线色分层失败");
            if(!(skin.GetFloat("_OutlineWidth")<hair.GetFloat("_OutlineWidth")&&hair.GetFloat("_OutlineWidth")<cloth.GetFloat("_OutlineWidth")))throw new Exception("线宽分层失败");
            var weights=a.Instance.Runtime.Expression.GetWeights().ToArray();
            var key=ExpressionKey.CreateCustom("blush");
            void Reset(){foreach(var w in weights)a.Instance.Runtime.Expression.SetWeight(w.Key,0);a.Instance.Runtime.Process();bridge.Synchronize();}
            Reset();float hidden=blush.GetColor("_Color").a;
            yield return null;yield return null;
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"liltoon-neutral.png"));
            File.WriteAllLines(Path.Combine(evidence,"outline-renderer-audit.txt"),a.Instance.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r=>r.name+" | scale="+r.transform.lossyScale.ToString("F6")+" | bounds="+r.sharedMesh.bounds.size.ToString("F4")+" | "+string.Join(", ",r.sharedMaterials.Select(m=>m.name+" width="+m.GetFloat("_OutlineWidth")))));
            var auditCamera=preview.ViewCamera;
            auditCamera.transform.position=new Vector3(0,.88f,.50f);auditCamera.transform.LookAt(new Vector3(0,.86f,0));auditCamera.fieldOfView=32;
            yield return null;yield return null;Alpha2Probe.Capture(auditCamera,Path.Combine(evidence,"outline-balance-current.png"));
            yield return null;yield return null;Alpha2Probe.Capture(auditCamera,Path.Combine(evidence,"outline-balance-final.png"));
            auditCamera.transform.position=new Vector3(0,.83f,.30f);auditCamera.transform.LookAt(new Vector3(0,.81f,0));auditCamera.fieldOfView=24;
            yield return null;yield return null;Alpha2Probe.Capture(auditCamera,Path.Combine(evidence,"outline-clothing-front.png"));
            auditCamera.transform.position=new Vector3(.15f,.83f,.26f);auditCamera.transform.LookAt(new Vector3(0,.81f,0));
            yield return null;yield return null;Alpha2Probe.Capture(auditCamera,Path.Combine(evidence,"outline-clothing-side.png"));
            // 刘海描边分离：同一特写比较偏移、宽度和关闭描边，区分几何与材质。
            var camera=preview.ViewCamera;var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;float fov=camera.fieldOfView;
            a.Instance.TryGetBoneTransform(HumanBodyBones.Head,out var head);
            var target=head.position+Vector3.up*.025f;
            camera.transform.position=target+new Vector3(0,.015f,.30f);camera.transform.LookAt(target);camera.fieldOfView=16;
            yield return null;yield return null;Alpha2Probe.Capture(camera,Path.Combine(evidence,"outline-gap-after.png"));
            foreach(var m in hairRenderer.sharedMaterials)
            {m.SetFloat("_StencilPass",0);m.SetFloat("_OutlineStencilComp",8);m.SetFloat("_OutlineCull",1);m.SetFloat("_OutlineZBias",.003f);}
            yield return null;yield return null;Alpha2Probe.Capture(camera,Path.Combine(evidence,"outline-gap-before.png"));
            foreach(var m in hairRenderer.sharedMaterials)AnimePresentation.ConfigureHairOutline(m);
            camera.fieldOfView=34;
            yield return null;yield return null;Alpha2Probe.Capture(camera,Path.Combine(evidence,"outline-complete-front.png"));
            foreach(float angle in new[]{-30f,30f})
            {
                camera.transform.position=target+Quaternion.Euler(0,angle,0)*new Vector3(0,.015f,.30f);camera.transform.LookAt(target);
                yield return null;yield return null;Alpha2Probe.Capture(camera,Path.Combine(evidence,"outline-complete-side-"+angle+".png"));
            }
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);camera.fieldOfView=fov;
            a.Instance.Runtime.Expression.SetWeight(key,1);a.Instance.Runtime.Process();bridge.Synchronize();
            float shown=blush.GetColor("_Color").a;
            if(hidden>.01f||shown<.99f)throw new Exception("lilToon 腮红透明度绑定丢失");
            yield return null;yield return null;
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"liltoon-blush.png"));
            Reset();a.Instance.Runtime.Expression.SetWeight(ExpressionKey.Blink,1);a.Instance.Runtime.Process();bridge.Synchronize();
            yield return null;yield return null;
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"liltoon-blink.png"));
            Reset();a.Instance.Runtime.Expression.SetWeight(ExpressionKey.Aa,1);a.Instance.Runtime.Process();bridge.Synchronize();
            yield return null;yield return null;
            Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(evidence,"liltoon-aa.png"));
            Reset();foreach(var w in weights)a.Instance.Runtime.Expression.SetWeight(w.Key,w.Value);a.Instance.Runtime.Process();bridge.Synchronize();
            File.WriteAllText(Path.Combine(evidence,"liltoon-validation.json"),"{\"result\":\"passed\",\"version\":\"2.4.1\",\"materials\":104,\"skinBrownRed\":true,\"lineWidthHierarchy\":true,\"blushAlphaBinding\":true,\"blinkAndAaCaptured\":true,\"questTested\":false}");
        }
    }
}
