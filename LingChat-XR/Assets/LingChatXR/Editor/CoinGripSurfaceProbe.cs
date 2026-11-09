#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 只读绘制用蒙皮。在握区附近分别检查手部、袖口实点，记录带正负号的实心圆盘距离。
    internal sealed class CoinGripSurfaceProbe : IDisposable
    {
        struct Sample
        {
            public int vertex, group;
            public string material;
        }
        sealed class Source
        {
            public SkinnedMeshRenderer renderer;
            public Sample[] samples;
            public Mesh baked=new Mesh();
            public List<Vector3> vertices=new();
        }
        readonly List<Source> sources=new();
        readonly int[] count=new int[4];
        readonly float[] minimum=new float[4],nearest=new float[4];
        public CoinGripSurfaceProbe(FeiyuAvatar actor)
        {
            actor.Instance.TryGetBoneTransform(HumanBodyBones.LeftLowerArm,out var left);
            actor.Instance.TryGetBoneTransform(HumanBodyBones.RightLowerArm,out var right);
            foreach(var renderer in actor.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=renderer.sharedMesh;if(!renderer.enabled || !mesh)continue;
                var weights=mesh.boneWeights;if(weights.Length==0)continue;
                var arm=renderer.bones.Select(b=>b && b.IsChildOf(left)?0:b && b.IsChildOf(right)?1:-1).ToArray();
                var selected=new List<Sample>();
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    string material=renderer.sharedMaterials[sub].name;
                    bool skin=material.IndexOf("Skin_FaceMatched",StringComparison.OrdinalIgnoreCase)>=0;
                    foreach(int vertex in mesh.GetTriangles(sub).Distinct())
                    {
                        var weight=weights[vertex];
                        int side=arm[weight.boneIndex0];if(side<0 || weight.weight0<.45f)continue;
                        selected.Add(new Sample{vertex=vertex,group=side+(skin?0:2),material=material});
                    }
                }
                if(selected.Count>0)sources.Add(new Source{renderer=renderer,samples=selected.ToArray()});
            }
        }
        public string Measure(Transform disk,float radius,Vector3 leftZone,Vector3 rightZone,List<string> profile=null,Quaternion frame=default)
        {
            for(int i=0;i<4;i++){count[i]=0;minimum[i]=nearest[i]=float.PositiveInfinity;}
            foreach(var source in sources)
            {
                source.renderer.BakeMesh(source.baked,true);
                source.baked.GetVertices(source.vertices);
                var matrix=disk.worldToLocalMatrix*source.renderer.transform.localToWorldMatrix;
                var a=disk.InverseTransformPoint(leftZone);var b=disk.InverseTransformPoint(rightZone);
                foreach(var sample in source.samples)
                {
                    var point=matrix.MultiplyPoint3x4(source.vertices[sample.vertex]);
                    if(profile!=null){
                        var local=Quaternion.Inverse(frame)*(disk.rotation*point);
                        profile.Add(FormattableString.Invariant($"{sample.group},{sample.material},{local.x:F5},{local.y:F5},{local.z:F5}"));
                    }
                    float radial=new Vector2(point.x,point.z).magnitude-radius;
                    float axial=Mathf.Abs(point.y)-.003f;
                    float sdf=new Vector2(Mathf.Max(0,radial),Mathf.Max(0,axial)).magnitude+Mathf.Min(0,Mathf.Max(radial,axial));
                    int group=sample.group;
                    minimum[group]=Mathf.Min(minimum[group],sdf);
                    if(sdf<-.0005f)count[group]++;
                    nearest[group]=Mathf.Min(nearest[group],Vector3.Distance(point,(group%2==0)?a:b));
                }
            }
            var fields=new string[4];
            for(int i=0;i<4;i++)fields[i]=FormattableString.Invariant($"{nearest[i]:F5},{minimum[i]:F5},{count[i]}");
            return string.Join(",",fields);
        }
        public void Dispose(){foreach(var source in sources)UnityEngine.Object.DestroyImmediate(source.baked);}
    }
}
#endif
