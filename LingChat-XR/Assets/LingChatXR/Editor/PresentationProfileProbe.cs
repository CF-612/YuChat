#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class PresentationProfileProbe
    {
        public static void Run()
        {
            int checks=0;
            void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
            try
            {
                var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../tools/character-import/profiles/smallbluefish-face-v1.json"));
                var json=File.ReadAllText(path);var p=JsonUtility.FromJson<PresentationProfile>(json);
                Check(p.Valid,"版本配置有效");
                var keys=p.expressions.Select(x=>x.key).ToArray();Check(p.MissingKeys(keys).Length==0,"完整语义绑定");
                var missing=p.MissingKeys(keys.Where(x=>x!="eyeSpiral"));Check(missing.Length==1&&missing[0]=="eyeSpiral -> eyeSpiral","仅晕眼缺失可定位");
                Check(p.Key("missing").StartsWith("__unbound_"),"未声明能力不冒用同名键");
                var roles=p.animeMaterials;
                Check(roles.skin.Matches("Skin_Face")&&!roles.skin.Matches("Sock_Skin"),"皮肤排除袜子");
                Check(roles.skin.Matches("tripo_part_30_material.001")&&!roles.skin.Matches("tripo_part_2_material"),"原始皮肤编号等价");
                Check(roles.detail.Matches("FOUNDATION_OralCavity")&&roles.detail.Matches("EyeCircleShock")&&!roles.detail.Matches("Cloth"),"细节大小写规则");
                Check(roles.cloth.Matches("Ruffle")&&roles.cloth.Matches("tripo_part_4_material"),"衣物规则");
                Check(roles.gold.Matches("GoldTrim")&&roles.gold.Matches("tripo_part_19_material"),"金属装饰规则");
                Check(roles.hairSheets.Matches("HairCap_ContinuousUV (Instance)")&&!roles.hairSheets.Matches("OtherHair"),"薄发片规则");
                Check(roles.outlineExcluded.Matches("Face_InnerWalls"),"内壁描边排除");
                p.schemaVersion=99;Check(!p.Valid,"未知版本拒绝");
                p=JsonUtility.FromJson<PresentationProfile>(json);p.expressions[0].semantic="typo";Check(!p.Valid,"必要语义缺失拒绝");
                var old=JsonUtility.FromJson<CharacterPackage>("{\"schemaVersion\":1,\"characterId\":\"legacy\"}");Check(old.presentationProfile==null,"旧包无配置仍可读取");
                var active=JsonUtility.FromJson<CharacterPackage>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Characters/local-smallbluefish-alpha2/manifest.json")));
                Check(active.presentationProfile=="presentation.json","本地角色引用配置");
                Check(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Characters/local-smallbluefish-alpha2/presentation.json"))==json,"本地配置与示例一致");
                var dir=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,"checks.json"),"{\"passed\":"+checks+",\"visualAccepted\":false,\"modelGeometryModified\":false}");
                Debug.Log("XR_M1_PASSED presentation profile "+checks);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
#endif
