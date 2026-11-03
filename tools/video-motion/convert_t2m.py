"""Convert a validated T2M joint track into a root-locked humanoid VRMA."""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation
from capture_t2m import validate
from export_body_vrma import continuous_quaternions, write_glb

NAMES = ("hips leftUpperLeg rightUpperLeg spine leftLowerLeg rightLowerLeg chest "
         "leftFoot rightFoot upperChest leftToes rightToes neck leftShoulder rightShoulder "
         "head leftUpperArm rightUpperArm leftLowerArm rightLowerArm leftHand rightHand").split()
PARENT = [-1,0,0,0,1,2,3,4,5,6,7,8,9,9,9,12,13,14,16,17,18,19]
AXES = np.array([[0,1,0],[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,-1,0],[0,1,0],
                 [0,-1,0],[0,-1,0],[0,1,0],[0,0,1],[0,0,1],[0,1,0],[1,0,0],
                 [-1,0,0],[0,1,0],[1,0,0],[-1,0,0],[1,0,0],[-1,0,0],[1,0,0],[-1,0,0]])
CHILD = {p:i for i,p in enumerate(PARENT) if p >= 0}
CHILD.update({0:3,9:12})


def basis(up, left):
    y = up / np.linalg.norm(up)
    x = left - y * np.dot(left,y)
    if np.linalg.norm(x) < 1e-5:
        raise ValueError("Degenerate torso basis")
    x /= np.linalg.norm(x)
    return Rotation.from_matrix(np.column_stack([x,y,np.cross(x,y)]))


def convert(data, path):
    validate(data)
    frames = np.asarray(data["joints"], dtype=float)
    bones = frames[:,1:] - frames[:,PARENT[1:]]
    lengths = np.linalg.norm(bones,axis=2)
    if lengths.min() < 1e-5:
        raise ValueError("Degenerate bone")
    rest = AXES.astype(float).copy()
    rest[1:] *= np.median(lengths,axis=0)[:,None]
    rest[0] = [0,sum(-rest[i,1] for i in (1,4,7)),0]
    nodes = [dict(name="MotionRoot",children=[1])]
    for i,name in enumerate(NAMES):
        nodes.append(dict(name=name,translation=rest[i].tolist()))
        if i:
            nodes[PARENT[i]+1].setdefault("children",[]).append(i+1)
    poses = []
    for frame in frames:
        world = []
        pose = []
        for i,parent in enumerate(PARENT):
            previous = world[parent] if parent >= 0 else Rotation.identity()
            local = Rotation.identity()
            if i in CHILD:
                child = CHILD[i]
                direction = frame[child]-frame[i]
                if i in (0,3,6,9):
                    left = frame[1]-frame[2] if i==0 else frame[13]-frame[14]
                    local = previous.inv()*basis(direction,left)
                else:
                    local,_ = Rotation.align_vectors([previous.inv().apply(direction)], [rest[child]])
            world.append(previous*local)
            pose.append(local.as_matrix())
        poses.append(pose)
    rotations = [continuous_quaternions(track) for track in np.swapaxes(poses,0,1)]
    hips = np.tile(rest[0],(len(frames),1))
    write_glb(path,nodes,rotations,hips,data["fps"],{"humanBones":dict.fromkeys(NAMES)})


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("motion",type=Path)
    parser.add_argument("output",type=Path)
    args=parser.parse_args()
    convert(json.loads(args.motion.read_text(encoding="utf-8")),args.output)
