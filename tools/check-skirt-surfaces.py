"""检查 V21WalkDiagnostic 导出的裙摆与腿部蒙皮三角形是否真正交叉。"""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import struct

import numpy as np
from scipy.spatial import cKDTree


def load(path):
    data = path.read_bytes()
    offset = 0
    groups = defaultdict(list)
    while offset < len(data):
        kind, length = data[offset:offset+2]
        offset += 2
        if length >= 128:
            length = (length & 127) + (data[offset] << 7)
            offset += 1
        material = data[offset:offset+length].decode()
        offset += length
        groups[kind, material].append(struct.unpack_from("<9f", data, offset))
        offset += 36
    return {key: np.array(values).reshape(-1, 3, 3) for key, values in groups.items()}


def edges_cross(a, b):
    """开区间内的边与三角面相交，排除相切和仅共顶点。"""
    e1, e2 = b[:, 1]-b[:, 0], b[:, 2]-b[:, 0]
    hit = np.zeros(len(a), dtype=bool)
    for j in range(3):
        ray = a[:, (j+1)%3]-a[:, j]
        h = np.cross(ray, e2)
        det = (e1*h).sum(axis=1)
        valid = abs(det) > 1e-15
        inv = np.divide(1., det, where=valid, out=np.zeros_like(det))
        s = a[:, j]-b[:, 0]
        u = inv*(s*h).sum(axis=1)
        q = np.cross(s, e1)
        v = inv*(ray*q).sum(axis=1)
        t = inv*(e2*q).sum(axis=1)
        eps = 1e-5
        hit |= valid & (u>eps) & (v>eps) & (u+v<1-eps) & (t>eps) & (t<1-eps)
    return hit


def count_crossings(a, b):
    ca, cb = a.mean(axis=1), b.mean(axis=1)
    ra = np.linalg.norm(a-ca[:, None], axis=2).max(axis=1)
    rb = np.linalg.norm(b-cb[:, None], axis=2).max(axis=1)
    neighbors = cKDTree(cb).query_ball_point(ca, ra+rb.max())
    ids = [(i, j) for i, group in enumerate(neighbors) for j in group]
    if not ids:
        return 0
    ia, ib = np.array(ids).T
    keep = np.all(a.max(axis=1)[ia]>=b.min(axis=1)[ib], axis=1) & np.all(b.max(axis=1)[ib]>=a.min(axis=1)[ia], axis=1)
    x, y = a[ia[keep]], b[ib[keep]]
    return int(np.count_nonzero(edges_cross(x,y) | edges_cross(y,x)))


def report(path):
    meshes = load(path)
    if {kind for kind, _ in meshes} != {1, 2}:
        raise ValueError(f"{path} 缺少已蒙皮的裙面或腿面")
    result = Counter()
    for (kind, cloth), a in meshes.items():
        if kind != 1:
            continue
        for (other, leg), b in meshes.items():
            if other == 2 and cloth != leg:
                n = count_crossings(a, b)
                if n:
                    result[f"{cloth} / {leg}"] = n
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--verify-pair", nargs=2, metavar=("BASE", "TUNED"), help="核对两组姿态相同、基线能复现且修复组零相交")
    args = parser.parse_args()
    rows = {p.stem: report(p) for p in sorted(args.directory.glob("mesh-*.bin"))}
    if not rows:
        parser.error("没有裙摆三角面记录")
    (args.directory/"intersections.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
    totals = defaultdict(Counter)
    for name, row in rows.items():
        totals[name.split("-")[1]].update(row)
    for key, row in sorted(totals.items()):
        print(key, sum(row.values()), row.most_common(5))

    if args.verify_pair:
        baseline, tuned = args.verify_pair
        def samples(group):
            prefix = f"mesh-{group}-"
            return {name[len(prefix):] for name in rows if name.startswith(prefix)}
        if not samples(baseline) or samples(baseline) != samples(tuned):
            parser.exit(1, "基线与修复组的采样帧缺失或不一致\n")
        if not totals[baseline] or totals[tuned]:
            parser.exit(1, "穿插回归未通过，请检查 intersections.json\n")