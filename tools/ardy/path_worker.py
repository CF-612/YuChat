"""仅供 PC 试玩。固定本机进程的标准输入输出承载控制数据，每次输出八帧。"""
import json
import sys
import time

from path_walk import CACHE, MIRROR, HISTORY, load_model, make_history, sample, decode, torch, np


def reply(**data):
    print("YUCHAT " + json.dumps(data,separators=(",",":")),flush=True)


@torch.inference_mode()
def generate(model, request):
    frames = request["frames"]
    leg, speed = request["leg"], request["speed"]
    if len(frames) != HISTORY or not 0 < leg < 2 or not 0 < speed < 2:
        raise ValueError("步态历史、尺寸或速度无效")
    history, scale = make_history(model.motion_rep, request)
    first_hips = np.array(frames[0]["hips"])
    target = (np.array(request["target"])-first_hips[[0,2]]) * [-scale,scale]
    future = sample(model,history,target,speed*scale,request["seed"],6)
    result = decode(model.motion_rep,future)
    previous = model.motion_rep.unnormalize(history)[0,-1,:3].cpu().numpy() @ MIRROR
    last = frames[-1]
    parents = model.motion_rep.skeleton.joint_parents.tolist()
    rows = []
    for i in range(8):
        delta = (np.array(result["root"][i]) - previous)/scale
        root = np.array(last["root"]) + delta * [1,0,1]
        rows.append(dict(root=root.tolist(),hips=(np.array(last["hips"])+delta).tolist(),
                         y=last["y"]+delta[1],
                         pose=collapse(result["pose"][i],last["pose"],parents)))
    return rows


def collapse(quaternions, available, parents):
    from path_walk import BONES, Rotation
    result = {}
    for i,name in enumerate(BONES):
        if name not in available:
            continue
        rotation = Rotation.from_quat(quaternions[i])
        parent = parents[i]
        while parent >= 0 and BONES[parent] not in available:
            rotation = Rotation.from_quat(quaternions[parent]) * rotation
            parent = parents[parent]
        result[name] = rotation.as_quat().tolist()
    return result


if __name__ == "__main__":
    model = load_model("core8",device="cuda",text_encoder=False,checkpoints_dir=str(CACHE/"checkpoints"))
    reply(ready=True)
    for line in sys.stdin:
        request = json.loads(line)
        started = time.perf_counter()
        try:
            rows = generate(model,request)
            reply(ticket=request["ticket"],rows=rows,ms=1000*(time.perf_counter()-started))
        except Exception as error:
            reply(ticket=request["ticket"],error=str(error))
