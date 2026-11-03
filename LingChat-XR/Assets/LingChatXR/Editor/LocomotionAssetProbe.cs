using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LingChatXR.Editor
{
    // 仅在隔离副本审计作者 FBX；不把受限原文件放进公开资源目录，不改角色。
    public static class LocomotionAssetProbe
    {
        const string Prefix="Assets/LocalMotionAudit/";
        static readonly string[] Names={"HumanF@Idle01.fbx","HumanF@Walk01_Forward.fbx","HumanF@Turn01_Left.fbx","HumanF@Turn01_Right.fbx"};
        public static void Run()=>Execute(false);
        public static void RunAndExport()=>Execute(true);
        public static void RunAndWalk()=>Execute(true,true);
        static void Execute(bool export,bool walk=false)
        {
            string output=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.r1-unity-validation/Assets"))throw new InvalidOperationException("动作审计只允许隔离副本");
                var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
                var catalog=JArray.Parse(File.ReadAllText(Path.Combine(root,".cache/motion-source/kevin-catalog.json")));
                var byGuid=catalog.ToDictionary(x=>Path.GetFileName((string)x["Folder"]));var included=new HashSet<string>();
                Action<JToken> copy=null;
                copy=item=>
                {
                    string directory=(string)item["Folder"],id=Path.GetFileName(directory);if(!included.Add(id))return;
                    string original=(string)item["Path"];if(!original.StartsWith("Assets/",StringComparison.Ordinal)||original.Contains(".."))throw new InvalidDataException("Asset path rejected");
                    string destination=Prefix+original.Substring(7);Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    foreach(string filename in new[]{"asset.meta","asset"})
                    {
                        string source=Path.Combine(directory,filename);if(!File.Exists(source))continue;
                        File.Copy(source,destination+(filename=="asset.meta"?".meta":""),true);
                        // 元数据及 YAML 依赖保持作者 GUID；不重建骨名映射或导入设置。
                        if(filename=="asset.meta"||original.EndsWith(".mask")||original.EndsWith(".mat"))
                            foreach(Match m in Regex.Matches(File.ReadAllText(source),@"guid: ([a-f0-9]{32})"))if(byGuid.TryGetValue(m.Groups[1].Value,out var dependency))copy(dependency);
                    }
                };
                var selected=Names.Select(name=>catalog.Single(x=>Path.GetFileName((string)x["Path"])==name)).ToArray();foreach(var item in selected)copy(item);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var avatarItem=catalog.Single(x=>Path.GetFileName((string)x["Path"])=="HumanF_Model.fbx");
                string avatarPath=Prefix+((string)avatarItem["Path"]).Substring(7);var template=AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
                if(template==null)throw new Exception("Source avatar dependency missing");
                var report=new JArray();var exported=new JArray();
                foreach(var item in selected)
                {
                    string sourcePath=(string)item["Path"],assetPath=Prefix+sourcePath.Substring(7);
                    var importer=(ModelImporter)AssetImporter.GetAtPath(assetPath);
                    var clip=AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().First(x=>!x.name.StartsWith("__preview__"));
                    var go=UnityEngine.Object.Instantiate(template);var animator=go.GetComponent<Animator>();PlayableGraph graph=default;
                    try
                    {
                        if(animator==null||animator.avatar==null||!animator.avatar.isValid||!animator.avatar.isHuman||!clip.humanMotion)throw new Exception("Not a valid Humanoid animation: "+sourcePath);
                        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=true;animator.Rebind();
                        graph=PlayableGraph.Create("Source locomotion audit");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable=AnimationClipPlayable.Create(graph,clip);var sink=AnimationPlayableOutput.Create(graph,"Humanoid",animator);sink.SetSourcePlayable(playable);graph.Play();
                        playable.SetTime(0);graph.Evaluate(0);
                        var left=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var right=animator.GetBoneTransform(HumanBodyBones.RightFoot);
                        Vector3 first=go.transform.position,last=first,leftStart=left.position,rightStart=right.position;Quaternion rotation=go.transform.rotation;
                        float travel=0,leftRange=0,rightRange=0,maxBoneChange=0;var thigh=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var thighStart=thigh.localRotation;
                        int frames=Mathf.CeilToInt(clip.length*60);var samples=new JArray();
                        for(int i=1;i<=frames;i++)
                        {
                            graph.Evaluate(clip.length/frames);travel+=Vector3.Distance(last,go.transform.position);last=go.transform.position;
                            leftRange=Mathf.Max(leftRange,Vector3.Distance(left.position-go.transform.position,leftStart-first));rightRange=Mathf.Max(rightRange,Vector3.Distance(right.position-go.transform.position,rightStart-first));
                            maxBoneChange=Mathf.Max(maxBoneChange,Quaternion.Angle(thighStart,thigh.localRotation));
                            if(i%6==0)samples.Add(new JObject{["time"]=i*clip.length/frames,["root"]=Vector(go.transform.position-first),["leftFoot"]=Vector(left.position-go.transform.position),["rightFoot"]=Vector(right.position-go.transform.position)});
                        }
                        var settings=AnimationUtility.GetAnimationClipSettings(clip);
                        report.Add(new JObject{["source"]=sourcePath,["fileSHA256"]=PackageFiles.Hash(File.ReadAllBytes(Path.Combine((string)item["Folder"],"asset"))),["metadataSHA256"]=PackageFiles.Hash(File.ReadAllBytes(Path.Combine((string)item["Folder"],"asset.meta"))),["clip"]=clip.name,["format"]="FBX / Unity Humanoid",["length"]=clip.length,["frameRate"]=clip.frameRate,["loopTime"]=settings.loopTime,["loopBlend"]=settings.loopBlend,["importerType"]=importer.animationType.ToString(),["rootDisplacement"]=Vector(go.transform.position-first),["rootTravel"]=travel,["rootRotationDegrees"]=Quaternion.Angle(rotation,go.transform.rotation),["averageSpeed"]=Vector(clip.averageSpeed),["averageAngularSpeed"]=clip.averageAngularSpeed,["leftFootRange"]=leftRange,["rightFootRange"]=rightRange,["leftUpperLegChangeDegrees"]=maxBoneChange,["footSeamMeters"]=Mathf.Max(Vector3.Distance(left.position-go.transform.position,leftStart-first),Vector3.Distance(right.position-go.transform.position,rightStart-first)),["samples"]=samples});
                        if(!float.IsFinite(travel)||clip.length<=0||maxBoneChange<=.01f)throw new Exception("Animation did not sample: "+clip.name);
                    }
                    finally{if(graph.IsValid())graph.Destroy();UnityEngine.Object.DestroyImmediate(go);}
                    if(export)exported.Add(LocomotionExport.Convert(template,clip,root,Array.IndexOf(Names,Path.GetFileName(sourcePath))));
                }
                if(export)File.WriteAllText(Path.Combine(root,"LingChat-XR/Assets/StreamingAssets/Motions/local-locomotion/catalog.json"),new JObject{["schemaVersion"]=1,["license"]="Kevin Iglesias single entity; local derived assets; no source redistribution",["actions"]=exported}.ToString());
                File.WriteAllText(Path.Combine(output,"motion-assets.json"),new JObject{["assets"]=report,["dependencies"]=new JArray(included.OrderBy(x=>x)),["sourceAvatarOnly"]=true,["targetModelsTested"]=false,["deviceTested"]=false,["walkStartStopIncluded"]=false}.ToString());
                if(walk)
                {
                    string source=Path.Combine(root,"LingChat-XR/Assets/StreamingAssets/Motions/local-locomotion"),destination=Path.Combine(Application.streamingAssetsPath,"Motions/local-locomotion");Directory.CreateDirectory(destination);
                    foreach(string file in Directory.GetFiles(source))if(!file.EndsWith(".meta"))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)),true);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);LocomotionProbe.Run();
                }
                else{Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);}
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static JArray Vector(Vector3 v)=>new JArray(v.x,v.y,v.z);
    }
}
