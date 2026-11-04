// 表现声明独立于文件哈希；实际键存在性由 Unity 导入后检查。
export function validatePresentationProfile(p) {
  const text=x=>typeof x==='string'&&x.trim().length>0&&x.length<=160&&!/[\\/:]/.test(x);
  const fields=(o,allowed)=>o&&typeof o==='object'&&!Array.isArray(o)&&Object.keys(o).every(k=>allowed.includes(k));
  if(!fields(p,['schemaVersion','profileId','interactionFace','secondaryBodyFeedback','expressions','animeMaterials']))throw new Error('Unknown profile field');
  if(!p||p.schemaVersion!==1||!text(p.profileId)||typeof p.interactionFace!=='boolean'||typeof p.secondaryBodyFeedback!=='boolean')throw new Error('Invalid presentation profile');
  const bindings=p.expressions??[];
  if(!Array.isArray(bindings)||bindings.length>64||bindings.some(x=>!fields(x,['semantic','key'])||!text(x.semantic)||!text(x.key))||new Set(bindings.map(x=>x.semantic)).size!==bindings.length)throw new Error('Invalid expression bindings');
  if(p.interactionFace&&['eyeSmile','eyeCircleShock','mouthOpenCircle','eyeTroubled'].some(s=>!bindings.some(x=>x.semantic===s)))throw new Error('Missing interaction semantics');
  const roleNames=['skin','detail','cloth','gold','hairSheets','outlineExcluded'];
  if(p.animeMaterials!=null&&!fields(p.animeMaterials,roleNames))throw new Error('Unknown material role');
  if(p.animeMaterials!=null)for(const role of roleNames){
    const rule=p.animeMaterials[role];if(!fields(rule,['exact','contains','prefix','excludePrefix','ignoreCase'])||(rule.ignoreCase!=null&&typeof rule.ignoreCase!=='boolean'))throw new Error('Invalid material role');
    for(const field of ['exact','contains','prefix','excludePrefix'])if(rule[field]!=null&&(!Array.isArray(rule[field])||rule[field].length>100||rule[field].some(x=>!text(x))))throw new Error('Invalid material selector');
  }
  return p;
}
