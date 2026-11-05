"""Horizon8 离线接步与同种子改向。输入为 ArdyWalkProbe 的实际播放历史。"""
import argparse
import json
import os
from pathlib import Path
import sys
import time

START = time.perf_counter()
ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / ".cache/ardy-horizon8"
os.environ["HF_HUB_OFFLINE"] = "1"
sys.path[:0] = [str(CACHE / "deps"), str(CACHE / "upstream")]
import numpy as np
import psutil
import torch
from scipy.spatial.transform import Rotation
from ardy.constraints import Root2DConstraintSet
from ardy.model.load_model import load_model
from ardy.skeleton.kinematics import fk


# 依官方 Core27 骨序，额外胸椎和手末端在归一化骨架内保持中立。
BONES = ["Hips","Spine","Chest","UpperChest",None,"Neck","Head",
         "RightShoulder","RightUpperArm","RightLowerArm","RightHand",None,"RightThumbProximal",
         "LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand",None,"LeftThumbProximal",
         "RightUpperLeg","RightLowerLeg","RightFoot","RightToes",
         "LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes"]
MIRROR = np.diag([-1.,1.,1.])
HISTORY = 16
CONSUME = 4


def tensor(array):
    return torch.as_tensor(array,dtype=torch.float32,device="cuda")


def make_history(rep, walk):
    frames = walk["frames"][-HISTORY:]
    q = np.array([[f["pose"].get(name,[0,0,0,1]) for name in BONES] for f in frames])
    rot = Rotation.from_quat(q.reshape(-1,4)).as_matrix().reshape(HISTORY,27,3,3)
    rot = tensor(MIRROR @ rot @ MIRROR)
    joints = rep.skeleton.neutral_joints
    leg = ((joints[23]-joints[24]).norm() + (joints[24]-joints[25]).norm()).item()
    scale = leg / walk["leg"]
    pelvis = tensor(np.array([f["hips"] for f in frames]) @ MIRROR * scale)
    pelvis[:,[0,2]] -= pelvis[0,[0,2]]
    # Q 版胯高不能按腿长直接当成人形胯高。以实际姿态骨链的支撑足标定地面。
    _, pos, _ = fk(rot, pelvis, rep.skeleton)
    pelvis[:,1] -= pos[:,[21,22,25,26],1].amin(dim=1).median()
    return rep(rot[None],pelvis[None],to_normalize=True),scale


def sample(model, hist, target, speed, seed, steps):
    rep = model.motion_rep
    n = hist.shape[1]
    previous = rep.unnormalize(hist)
    start = previous[0,-1,:3][[0,2]]
    times = torch.arange(1,17,device="cuda")
    offset = tensor(target) - start
    dist = offset.norm()
    path = start + (times / 20 * speed).clamp(max=dist)[:,None] * offset / dist.clamp_min(1e-5)
    yaw = rep.get_root_heading_angle(previous)[0,-1]
    desired = torch.atan2(offset[0],offset[1])
    turn = torch.atan2(torch.sin(desired-yaw),torch.cos(desired-yaw))
    allowance = times / 20 * np.radians(120)
    heading = yaw + turn.clamp(-allowance,allowance) * (dist > .05)
    constraint = Root2DConstraintSet(rep.skeleton,times+n-1,path,heading)
    obs, mask = rep.create_conditions_from_constraints([constraint],n+16,True,"cuda")
    torch.manual_seed(seed)
    samples = model.autoregressive_step(n+16,steps,mask[None],obs[None],(0.,2.),
        text_feat=torch.zeros((1,1,4096),device="cuda"),
        text_pad_mask=torch.zeros((1,1),dtype=torch.bool,device="cuda"),
        init_history_sequence=hist)
    if samples.shape[1] != n+8 or not samples.isfinite().all():
        raise ValueError(f"非预期返回 {samples.shape}")
    return samples[:,-8:]


def decode(rep, future):
    body = rep.inverse(rep.unnormalize(future),is_normalized=False)
    local = body["local_rot_mats"][0].cpu().numpy()
    q = Rotation.from_matrix((MIRROR @ local @ MIRROR).reshape(-1,3,3)).as_quat()
    return dict(pose=q.reshape(-1,27,4).tolist(),
                root=(body["root_positions"][0].cpu().numpy() @ MIRROR).tolist(),
                feet=(body["posed_joints"][0,:,[21,25]].cpu().numpy() @ MIRROR).tolist())


@torch.inference_mode()
def run(args):
    walk = json.loads(args.walk.read_text(encoding="utf-8-sig"))
    model = load_model("core8",device="cuda",text_encoder=False,checkpoints_dir=str(CACHE/"checkpoints"))
    torch.cuda.synchronize()
    cold = time.perf_counter()-START
    assert model.text_encoder is None
    history,scale = make_history(model.motion_rep,walk)
    speed = walk["speed"]*scale
    # Unity 桌面中心为 0，历史起始的脚下点用于世界坐标与 Core27 坐标互换。
    origin = np.array(walk["frames"][-HISTORY]["hips"])[[0,2]] + [0,-.24]
    targets = {"A": [0,.31], "B": [.44,.14]}
    def step(hist, key, seed):
        torch.cuda.synchronize()
        then = time.perf_counter()
        target = (np.array(targets[key])-origin) * [-scale,scale]
        result = sample(model,hist,target,speed,seed,args.steps)
        torch.cuda.synchronize()
        return result,1000*(time.perf_counter()-then)
    ahead,first = step(history,"A",101)
    # 两条分支共享四帧真正消费的前行结果，再用完全相同的种子重新规划。
    shared = torch.cat([history,ahead[:,:CONSUME]],1)[:,-HISTORY:]
    times = []
    branches = {}
    for key in targets:
        hist = shared
        blocks = []
        for i in range(20):
            future,ms = step(hist,key,102+i)
            times.append(ms)
            used = future[:,:CONSUME]
            blocks.append(used)
            hist = torch.cat([hist,used],1)[:,-HISTORY:]
        branches[key] = decode(model.motion_rep,torch.cat(blocks,1))
    args.output.mkdir(parents=True,exist_ok=True)
    payload = dict(bones=BONES,scale=scale,source=walk["id"],model=walk["model"],targets=targets,
                   history=decode(model.motion_rep,history),first=decode(model.motion_rep,ahead),branches=branches)
    (args.output/"samples.json").write_text(json.dumps(payload),encoding="utf-8")
    p = psutil.Process()
    report = dict(coldSeconds=cold,firstStepMs=first,denoisingSteps=args.steps,historyShape=list(history.shape),
                  warmStepsMs=times,warmMeanMs=np.mean(times),warmP95Ms=np.percentile(times,95),
                  consumedFrames=CONSUME,refillDeadlineMs=200,peakCudaAllocated=torch.cuda.max_memory_allocated(),
                  peakCudaReserved=torch.cuda.max_memory_reserved(),peakWorkingSet=getattr(p.memory_info(),"peak_wset",None),
                  deltas={k:(np.array(v["root"][-1])-np.array(payload["first"]["root"][CONSUME-1])).tolist() for k,v in branches.items()})
    (args.output/"performance.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print(json.dumps(report),flush=True)


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("walk",type=Path)
    parser.add_argument("output",type=Path)
    parser.add_argument("--steps",type=int,default=10)
    run(parser.parse_args())
