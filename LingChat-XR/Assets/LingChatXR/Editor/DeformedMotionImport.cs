using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LingChatXR.Editor
{
    // 只在隔离工程读用户付费 .anim，借用已授权 Humanoid Avatar 完成官方重定向导出。
    public static class DeformedMotionImport
    {
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/.r1-unity-validation/Assets"))throw new InvalidOperationException("只允许隔离导入");
                string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
                var inventory=JArray.Parse(File.ReadAllText(Path.Combine(root,".cache/motion-source/deformed-catalog.json")));
                string prefix="Assets/LocalMotionAudit/Deformed/";Directory.CreateDirectory(prefix);
                foreach(var item in inventory)
                {
                    string name=Path.GetFileName((string)item["Path"]);
                    if(!new[]{"walkcycle.anim","skip.anim","run.anim"}.Contains(name))throw new InvalidDataException("非预期素材");
                    File.Copy(Path.Combine((string)item["Folder"],"asset"),prefix+name,true);
                    File.Copy(Path.Combine((string)item["Folder"],"asset.meta"),prefix+name+".meta",true);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LocalMotionAudit/Kevin Iglesias/Human Animations/Models/HumanF_Model.fbx");
                if(template==null)throw new InvalidDataException("先运行 LocomotionAssetProbe 导入既有标准 Humanoid 依赖");
                string catalogPath=Path.Combine(root,"LingChat-XR/Assets/StreamingAssets/Motions/local-locomotion/catalog.json");
                var catalog=JObject.Parse(File.ReadAllText(catalogPath));var actions=(JArray)catalog["actions"];var report=new JArray();
                foreach(string name in new[]{"walkcycle","skip","run"})
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(prefix+name+".anim");
                    if(clip==null||!clip.humanMotion||clip.legacy||!AnimationUtility.GetAnimationClipSettings(clip).loopTime)throw new InvalidDataException("需要原生循环 Humanoid 动作: "+name);
                    string id="locomotion_"+name;
                    var result=LocomotionExport.Convert(template,clip,root,id,"User-purchased Deformed Character Animation 01; local-only; redistribution not verified",true);
                    result["preserveVertical"]=true;result["sourceSHA256"]=PackageFiles.Hash(File.ReadAllBytes(prefix+name+".anim"));
                    actions.Where(a=>(string)a["id"]==id).ToList().ForEach(a=>a.Remove());actions.Add(result);report.Add(result.DeepClone());
                }
                catalog["schemaVersion"]=2;catalog["defaultMovement"]="skip";catalog["fastMovement"]="run";catalog["license"]="Mixed licensed local resources; do not redistribute source or derived library";
                File.WriteAllText(catalogPath,catalog.ToString());File.WriteAllText(Path.Combine(output,"import.json"),new JObject{["assets"]=report,["sourceModelChanged"]=false,["questTested"]=false}.ToString());
                Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
