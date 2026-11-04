using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 在目标相机真正开始绘制时读取 CPU 蒙皮结果；不宣称读取了 GPU 深度缓冲。
    internal sealed class V21EyeDrawAudit
    {
        readonly Dictionary<string,Vector3[]> baseline=new();
        internal void Capture(FeiyuAvatar avatar,Camera camera,string output,string state)
        {
            var rows=new JArray();var vertices=new JObject();
            foreach(var renderer in avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var materials=renderer.sharedMaterials;
                if(!materials.Any(m=>m!=null&&(m.name.Contains("TrueLid")||m.name.Contains("AVolumeEye")||m.name.Contains("ScleraSkinFeather"))))continue;
                var mesh=new Mesh();renderer.BakeMesh(mesh);var local=mesh.vertices;
                var world=local.Select(v=>renderer.transform.TransformPoint(v)).ToArray();
                for(int sub=0;sub<mesh.subMeshCount&&sub<materials.Length;sub++)
                {
                    var m=materials[sub];if(m==null||!(m.name.Contains("TrueLid")||m.name.Contains("AVolumeEye")||m.name.Contains("ScleraSkinFeather")))continue;
                    string id=renderer.name+"/"+sub;
                    var indices=mesh.GetIndices(sub).Distinct().ToArray();
                    float maxDelta=0;int changed=0;
                    if(baseline.TryGetValue(id,out var rest))foreach(int i in indices){float delta=Vector3.Distance(world[i],rest[i]);maxDelta=Mathf.Max(maxDelta,delta);if(delta>1e-6f)changed++;}
                    else baseline[id]=world;
                    var props=new JObject();foreach(string p in new[]{"_ZTest","_ZWrite","_M_ZWrite","_Cull","_M_CullMode","_SrcBlend","_DstBlend","_AlphaToMask","_Cutoff","_TransparentMode","_OutlineZTest","_OutlineZWrite","_OutlineWidth"})if(m.HasProperty(p))props[p]=m.GetFloat(p);
                    var weights=new JObject();for(int i=0;i<renderer.sharedMesh.blendShapeCount;i++){string n=renderer.sharedMesh.GetBlendShapeName(i);if(n=="blink"||n=="SpecialClosed_Correction03")weights[n]=renderer.GetBlendShapeWeight(i);}
                    var depths=indices.Select(i=>camera.worldToCameraMatrix.MultiplyPoint(world[i]).z).ToArray();
                    rows.Add(new JObject{["renderer"]=renderer.name,["submesh"]=sub,["material"]=m.name,["shader"]=m.shader.name,["queue"]=m.renderQueue,["renderType"]=m.GetTag("RenderType",false),["color"]=m.HasProperty("_Color")?m.GetColor("_Color").ToString():"",["sortingLayer"]=renderer.sortingLayerID,["sortingOrder"]=renderer.sortingOrder,["enabled"]=renderer.enabled,["properties"]=props,["weights"]=weights,["vertices"]=indices.Length,["changedVertices"]=changed,["maxWorldDelta"]=maxDelta,["cameraZMin"]=depths.Min(),["cameraZMax"]=depths.Max()});
                    vertices[id]=new JArray(indices.Select(i=>new JArray(i,world[i].x,world[i].y,world[i].z)));
                }
                UnityEngine.Object.Destroy(mesh);
            }
            File.WriteAllText(Path.Combine(output,state+"-draw.json"),new JObject{["frame"]=Time.frameCount,["captureStage"]="Camera.onPreRender / BakeMesh CPU skinning",["gpuDepthReadback"]=false,["rows"]=rows}.ToString());
            File.WriteAllText(Path.Combine(output,state+"-vertices.json"),vertices.ToString());
        }
    }
}
