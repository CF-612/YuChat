// 在只读 LingChat 工作树执行原过渡函数，生成 Unity 对照数据。
const fs=require('node:fs'),path=require('node:path'),{createRequire}=require('node:module');
const root=process.argv[2],output=process.argv[3];
if(!root||!output)throw new Error('Expected LingChat root and output file');
const resolve=createRequire(path.join(root,'package.json')),ts=resolve('typescript'),T=resolve('three');
const source=path.join(root,'src/components/game/vrm/motion-transition.ts');
const code=ts.transpileModule(fs.readFileSync(source,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText;
const moduleScope={exports:{}};new Function('require','exports','module',code)(resolve,moduleScope.exports,moduleScope);
const m=moduleScope.exports,fixtures=[];
for(const progress of [-1,0,.1,.25,.5,.75,.9,1,2])fixtures.push({kind:'weight',a:progress,expected:m.motionReturnWeight(progress)});
for(const duration of [.2,.65,1.25])for(const age of [0,.05,.2,.5,1,2,5])fixtures.push({kind:'clock',a:age,b:duration,expected:m.motionEntryTime(age,duration)});
for(const angle of [0,10,45,90,170])for(const limits of [[.35,1.25],[.9,1.8],[.35,.75]])fixtures.push({kind:'pose',a:angle,b:limits[0],c:limits[1],expected:m.motionPoseDuration([{from:new T.Quaternion(),to:new T.Quaternion().setFromAxisAngle(new T.Vector3(1,0,0),angle*Math.PI/180)}],.6,...limits)});
fs.writeFileSync(output,JSON.stringify({source,fixtures},null,2));console.log(`LingChat reference: ${fixtures.length} fixtures`);
