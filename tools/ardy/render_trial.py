"""按 Unity 实际采集时间编码连续 PC 视频，叠加同帧记录的控制者和耗时。"""
import argparse
import bisect
import json
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw, ImageFont


def render(folder):
    rows=json.loads((folder/"timeline.json").read_text(encoding="utf-8-sig"))
    times=[r["t"]-rows[0]["t"] for r in rows]
    font=ImageFont.truetype("C:/Windows/Fonts/msyh.ttc",22)
    output=folder/"ARDY-Horizon8-PC-live.mp4"
    rate=24
    cmd=["ffmpeg","-hide_banner","-loglevel","error","-y","-f","rawvideo","-pix_fmt","rgb24",
         "-s","960x720","-r",str(rate),"-i","pipe:0","-c:v","libx264","-preset","fast",
         "-crf","20","-pix_fmt","yuv420p","-movflags","+faststart",str(output)]
    with subprocess.Popen(cmd,stdin=subprocess.PIPE) as process:
        last=-1
        for i in range(round(times[-1]*rate)+1):
            idx=max(0,bisect.bisect_right(times,i/rate)-1)
            if idx!=last:
                r=rows[idx]
                with Image.open(folder/f"live-{idx:04}.png") as source:
                    image=source.convert("RGB")
                draw=ImageDraw.Draw(image)
                draw.rectangle((0,0,960,75),fill=(17,29,46))
                draw.text((16,5),f'YuChat   /   Core8     {times[idx]:05.2f} s     {r["owner"]} · {r["state"]}',font=font)
                target=r["target"] if r["owner"] in ("ARDY","固定走路") else "—"
                draw.text((16,40),f'目标 {target}     上次往返 {r["rtt"]:.0f} ms     缓冲 {r["buffer"]} 帧     拒收 {r["rejected"]}',font=font)
                pixels=image.tobytes()
                last=idx
            process.stdin.write(pixels)
        process.stdin.close()
        if process.wait()!=0:
            raise RuntimeError("视频编码失败")
    print(output)


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder",type=Path)
    render(parser.parse_args().folder)
