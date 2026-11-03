"""Prepare local Unity atlases from LingChat emotion configuration and animated WebP."""
import argparse
import json
import math
import re
from pathlib import Path
from PIL import Image, ImageChops

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("lingchat",type=Path)
args=parser.parse_args()
out=Path(__file__).resolve().parents[1]/"LingChat-XR/Assets/LingChatXR/Resources/LocalLingChatEffects/Stickers"
out.mkdir(parents=True,exist_ok=True)
config=(args.lingchat/"src/controllers/emotion/config.ts").read_text(encoding="utf-8")
mapping=dict(re.findall(r"^  (\w+): \{\s*animation: '[^']*',\s*bubbleImage: '../pictures/animation/([^']+)\.webp'",config,re.M))
mapping.update({k:mapping[v] for k,v in {"开心":"高兴","喜悦":"高兴","愉快":"高兴","疑问":"疑惑","生气":"生气","愤怒":"生气","震惊":"惊讶","悲伤":"哭泣","伤心":"哭泣"}.items()})
clips={}
for index,name in enumerate(sorted(set(mapping.values()))):
    frames=[]; times=[]; mask=None
    with Image.open(args.lingchat/"public/pictures/animation"/(name+".webp")) as src:
        for i in range(src.n_frames):
            src.seek(i)
            frame=src.convert("RGBA").resize((480,270),Image.Resampling.LANCZOS)
            frames.append(frame);times.append(max(1,src.info.get("duration",40)))
            alpha=frame.getchannel("A")
            mask=alpha if mask is None else ImageChops.lighter(mask,alpha)
    bounds=mask.getbbox()
    if not bounds:raise ValueError(name)
    w,h=frames[0].crop(bounds).size
    ratio=128/max(w,h); w,h=max(1,round(w*ratio)),max(1,round(h*ratio))
    cols=math.ceil(math.sqrt(len(frames)));rows=math.ceil(len(frames)/cols)
    sheet=Image.new("RGBA",(1<<(((w+2)*cols-1).bit_length()),1<<(((h+2)*rows-1).bit_length())))
    for i,frame in enumerate(frames):
        sheet.paste(frame.crop(bounds).resize((w,h),Image.Resampling.LANCZOS),((i%cols)*(w+2)+1,(i//cols)*(h+2)+1))
    key=f"atlas{index}"
    sheet.save(out/(key+".png"))
    clips[name]=dict(key=key,w=w,h=h,cols=cols,ms=times)
(out/"index.json").write_text(json.dumps(dict(mapping=mapping,clips=clips),ensure_ascii=False),encoding="utf-8")
print(len(mapping),"moods,",len(clips),"animated atlases")
