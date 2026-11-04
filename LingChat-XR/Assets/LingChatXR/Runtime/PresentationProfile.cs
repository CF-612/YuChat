using System;
using System.Collections.Generic;
using System.Linq;

namespace LingChatXR
{
    [Serializable] public sealed class ExpressionBinding { public string semantic; public string key; }
    [Serializable] public sealed class MaterialRoleRule
    {
        public string[] exact, contains, prefix, excludePrefix;
        public bool ignoreCase;
        public bool Matches(string name)
        {
            var mode=ignoreCase?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            return !(excludePrefix?.Any(x=>name.StartsWith(x,mode))??false) &&
                ((exact?.Any(x=>string.Equals(name,x,mode))??false)||(contains?.Any(x=>name.IndexOf(x,mode)>=0)??false)||(prefix?.Any(x=>name.StartsWith(x,mode))??false));
        }
        public bool Valid=>new[]{exact,contains,prefix,excludePrefix}.All(a=>a==null||a.Length<=100&&a.All(x=>!string.IsNullOrEmpty(x)&&x.Length<=160));
    }
    [Serializable] public sealed class AnimeMaterialRoles
    {
        public MaterialRoleRule skin,detail,cloth,gold,hairSheets,outlineExcluded;
        public bool Valid=>new[]{skin,detail,cloth,gold,hairSheets,outlineExcluded}.All(r=>r!=null&&r.Valid);
    }
    [Serializable] public sealed class PresentationProfile
    {
        public int schemaVersion;
        public string profileId;
        public bool interactionFace;
        public bool secondaryBodyFeedback;
        public ExpressionBinding[] expressions;
        public AnimeMaterialRoles animeMaterials;
        public bool Valid=>schemaVersion==1&&!string.IsNullOrWhiteSpace(profileId)&&profileId.Length<=80
            &&(expressions==null||expressions.Length<=64&&expressions.All(x=>x!=null&&!string.IsNullOrWhiteSpace(x.semantic)&&!string.IsNullOrWhiteSpace(x.key)&&x.key.Length<=160)
                &&expressions.Select(x=>x.semantic).Distinct().Count()==expressions.Length)
            &&(!interactionFace||new[]{"eyeSmile","eyeCircleShock","mouthOpenCircle","eyeTroubled"}.All(s=>expressions?.Any(x=>x.semantic==s)==true))
            &&(animeMaterials==null||animeMaterials.Valid);
        public string Key(string semantic)=>expressions?.FirstOrDefault(x=>x.semantic==semantic)?.key??"__unbound_"+semantic;
        public string[] MissingKeys(IEnumerable<string> actualKeys)
        {
            var available=new HashSet<string>(actualKeys);
            return (expressions??Array.Empty<ExpressionBinding>()).Where(x=>!available.Contains(x.key)).Select(x=>x.semantic+" -> "+x.key).ToArray();
        }
    }
}
