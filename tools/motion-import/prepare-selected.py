"""Import a local selection of licensed GLB clips into the existing YuChat catalog."""
import argparse, copy, hashlib, json, struct, zipfile
from pathlib import Path

KAY = {"Jump_Full_Short":"原地小跳并落地","Jump_Full_Long":"大跳并落地","PickUp":"弯腰拾起物品",
       "Sneaking":"踮脚潜行","Crouching":"蹲伏","Idle_A":"轻松站立待机","Idle_B":"警觉站立待机"}
UAL = {"Walk_Carry_Loop":"双手搬着重物走路","Idle_FoldArms_Loop":"抱臂站立待机"}
IDLES={"Idle_A","Idle_B","Idle_FoldArms_Loop"}


def write_clip(doc, blob, animation, kay):
    names={n.get("name"):i for i,n in enumerate(doc["nodes"])}
    roles={"hips":"hips","head":"head","spine":"spine","chest":"chest"} if kay else {
        "hips":"pelvis","spine":"spine_01","chest":"spine_02","upperChest":"spine_03","neck":"neck_01","head":"Head"}
    for side,short in [("left","l"),("right","r")]:
        for role,k,u in [("UpperArm","upperarm","upperarm"),("LowerArm","lowerarm","lowerarm"),("Hand","hand","hand"),("UpperLeg","upperleg","thigh"),("LowerLeg","lowerleg","calf"),("Foot","foot","foot"),("Toes","toes","ball")]:
            roles[side+role]=(k+"."+short if kay else u+"_"+short)
        if not kay:roles[side+"Shoulder"]="clavicle_"+short
    bones={k:{"node":names[v]} for k,v in roles.items()}
    nodes=[{k:v for k,v in n.items() if k in ("name","children","rotation","translation","scale","matrix")} for n in doc["nodes"]]
    out={"asset":{"version":"2.0"},"scene":doc["scene"],"scenes":doc["scenes"],"nodes":nodes,"animations":[],
         "extensionsUsed":["VRMC_vrm_animation"],"extensions":{"VRMC_vrm_animation":{"specVersion":"1.0","humanoid":{"humanBones":bones}}},
         "accessors":[],"bufferViews":[]}
    raw=bytearray();cache={}
    def accessor(idx):
        if idx in cache:return cache[idx]
        a=copy.deepcopy(doc["accessors"][idx]);v=dict(doc["bufferViews"][a["bufferView"]])
        offset=v.get("byteOffset",0);raw.extend(bytes(-len(raw)%4))
        v["byteOffset"]=len(raw);raw.extend(blob[offset:offset+v["byteLength"]])
        a["bufferView"]=len(out["bufferViews"]);out["bufferViews"].append(v)
        cache[idx]=len(out["accessors"]);out["accessors"].append(a);return cache[idx]
    channels=[];samplers=[]
    for c in animation["channels"]:
        target=c["target"]
        if target["path"]!="rotation" and not(target["node"]==bones["hips"]["node"] and target["path"]=="translation"):continue
        s=dict(animation["samplers"][c["sampler"]]);s["input"]=accessor(s["input"]);s["output"]=accessor(s["output"])
        channels.append(dict(target=target,sampler=len(samplers)));samplers.append(s)
    out["animations"]=[dict(name=animation["name"],channels=channels,samplers=samplers)]
    raw.extend(bytes(-len(raw)%4));out["buffers"]=[dict(byteLength=len(raw))]
    js=json.dumps(out,separators=(",",":")).encode();js+=b" "*(-len(js)%4)
    return struct.pack("<III",0x46546c67,2,28+len(js)+len(raw))+struct.pack("<II",len(js),0x4e4f534a)+js+struct.pack("<II",len(raw),0x004e4942)+raw


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("downloads",type=Path);args=parser.parse_args()
    folder=Path(__file__).resolve().parents[2]/"LingChat-XR/Assets/StreamingAssets/Motions/local-lingchat"
    catalog=folder/"catalog.json";data=json.loads(catalog.read_text(encoding="utf-8-sig"))
    for archive,selection,kay in [("KayKit_Character_Animations_1.1.zip",KAY,True),("Universal Animation Library 2[Standard].zip",UAL,False)]:
        found=set()
        with zipfile.ZipFile(args.downloads/archive) as z:
            for name in z.namelist():
                if not(name.endswith(".glb") and ("/Rig_Medium/Rig_Medium_" in name if kay else name.endswith("/UAL2_Standard.glb"))):continue
                b=z.read(name);size=struct.unpack_from("<I",b,12)[0];doc=json.loads(b[20:20+size]);blob=b[28+size:]
                for a in doc["animations"]:
                    n=a["name"]
                    if n not in selection or n in found:continue
                    found.add(n);id=("kay_" if kay else "ual2_")+n.lower();vrma=write_clip(doc,blob,a,kay)
                    (folder/(id+".vrma")).write_bytes(vrma)
                    data["actions"]=[x for x in data["actions"] if x["id"]!=id]
                    data["actions"].append(dict(id=id,label=selection[n],kind="scene",style="normal",asset=id+".vrma",
                        loop=n in IDLES,sha256=hashlib.sha256(vrma).hexdigest(),license=archive,preserveHeight=n.startswith("Jump_Full")))
        if found!=set(selection):raise ValueError(f"Missing clips: {set(selection)-found}")
    temp=catalog.with_suffix(".tmp");temp.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding="utf-8");temp.replace(catalog)

if __name__=="__main__":main()
