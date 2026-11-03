"""Convert metric BVH to a body-only VRMA package with external world-root motion."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

import numpy as np
from scipy.spatial.transform import Rotation
from audit_model import REQUIRED


def read_bvh(path):
    hierarchy, motion = path.read_text(encoding='utf-8').split('MOTION', 1)
    tokens = re.findall(r'[{}]|[^\s{}]+', hierarchy)
    cursor, columns, nodes = 0, 0, []

    def take(expected=None):
        nonlocal cursor
        value = tokens[cursor]
        cursor += 1
        if expected is not None and value != expected:
            raise ValueError(f'Expected {expected}, got {value}')
        return value

    def joint(parent):
        nonlocal columns
        kind = take()
        if kind not in ('ROOT', 'JOINT'):
            raise ValueError('Expected BVH joint')
        name = take()
        index = len(nodes)
        node = {'name': name, 'parent': parent, 'offset': [0, 0, 0], 'channels': [], 'column': columns}
        nodes.append(node)
        take('{')
        take('OFFSET')
        node['offset'] = [float(take()) for _ in range(3)]
        take('CHANNELS')
        count = int(take())
        node['channels'] = [take() for _ in range(count)]
        columns += count
        while tokens[cursor] != '}':
            if tokens[cursor] == 'End':
                take('End'); take('Site'); take('{'); take('OFFSET')
                [take() for _ in range(3)]
                take('}')
            else:
                joint(index)
        take('}')

    take('HIERARCHY')
    joint(-1)
    if cursor != len(tokens):
        raise ValueError('Unexpected extra BVH hierarchy')
    lines = [line.strip() for line in motion.splitlines() if line.strip()]
    count = int(lines[0].split(':', 1)[1])
    dt = float(lines[1].split(':', 1)[1])
    values = np.array([[float(v) for v in line.split()] for line in lines[2:]], dtype=np.float64)
    if values.shape != (count, columns) or not np.isfinite(values).all() or not np.isfinite(dt) or dt <= 0 or count < 2:
        raise ValueError('Invalid BVH timebase or samples')
    if len({n['name'] for n in nodes}) != len(nodes):
        raise ValueError('BVH node names must be unique for semantic mapping')
    fps = 1 / dt
    if abs(fps - round(fps)) < 1e-6:
        fps = float(round(fps))
    return nodes, values, fps


def evaluate(nodes, samples):
    count, joints = len(samples), len(nodes)
    rotations = np.empty((count, joints, 3, 3))
    positions = np.empty((count, joints, 3))
    rest = np.zeros((joints, 3))
    for i, node in enumerate(nodes):
        channels = node['channels']
        data = samples[:, node['column']:node['column'] + len(channels)]
        rotation_channels = [j for j, name in enumerate(channels) if name.endswith('rotation')]
        order = ''.join(channels[j][0] for j in rotation_channels)
        if len(rotation_channels) != 3 or len(set(order)) != 3:
            raise ValueError('BVH requires three distinct Euler rotation channels')
        local_rotation = Rotation.from_euler(order, data[:, rotation_channels], degrees=True).as_matrix()
        local_position = np.tile(node['offset'], (count, 1)).astype(np.float64)
        for j, name in enumerate(channels):
            if name.endswith('position'):
                if node['parent'] >= 0 and np.ptp(data[:, j]) > 1e-5:
                    raise ValueError('Animated non-root bone lengths are not supported')
                local_position[:, 'XYZ'.index(name[0])] += data[:, j]
        parent = node['parent']
        if parent < 0:
            rotations[:, i] = local_rotation
            positions[:, i] = local_position
            rest[i] = node['offset']
        else:
            rotations[:, i] = rotations[:, parent] @ local_rotation
            positions[:, i] = positions[:, parent] + np.einsum('fij,fj->fi', rotations[:, parent], local_position)
            rest[i] = rest[parent] + node['offset']
    return rotations, positions, rest


def continuous_quaternions(rotations):
    values = Rotation.from_matrix(rotations).as_quat()
    for i in range(1, len(values)):
        if np.dot(values[i - 1], values[i]) < 0:
            values[i] *= -1
    return values.astype('<f4')


def build_body(nodes, samples, fps, profile):
    names = {n['name']: i for i, n in enumerate(nodes)}
    mapping = profile['humanBones']
    if any(name not in names for name in mapping.values()) or any(name not in mapping for name in REQUIRED):
        raise ValueError('Source skeleton is missing required humanoid semantics')
    expected = {'handedness': 'right', 'up': '+Y', 'forward': '+Z', 'lengthUnit': 'meter'}
    if profile['coordinates'] != expected:
        raise ValueError('This exporter requires explicit right-handed Y-up metric input')
    world, positions, rest = evaluate(nodes, samples)
    semantic = list(mapping)
    source = [names[mapping[role]] for role in semantic]
    inverse = {i: role for role, i in enumerate(source)}
    hips = names[mapping['hips']]
    height = rest[hips, 1] - np.min(rest[:, 1])
    if height <= 0 or rest[names[mapping['head']], 1] <= rest[hips, 1]:
        raise ValueError('Reference skeleton is not Y-up')
    left = rest[names[mapping['leftUpperArm']]] - rest[names[mapping['rightUpperArm']]]
    if left[0] <= 0 or abs(left[1]) > abs(left[0]) * .2:
        raise ValueError('Source reference pose does not match the declared SOMA T-pose basis')
    count = len(samples)
    heading = np.unwrap(np.arctan2(world[:, hips, 0, 2], world[:, hips, 2, 2]))
    remove_heading = Rotation.from_euler('Y', -heading).as_matrix()
    hips_position = np.zeros((count, 3), dtype='<f4')
    hips_position[:, 1] = height + positions[:, hips, 1] - positions[0, hips, 1]
    trajectories = positions[:, hips] - positions[0, hips]
    trajectories[:, 1] = 0
    exported = [{'name': 'MotionRoot', 'children': []}]
    rotations = []
    for target, original in enumerate(source):
        parent = nodes[original]['parent']
        while parent >= 0 and parent not in inverse:
            parent = nodes[parent]['parent']
        parent_target = 0 if parent < 0 else inverse[parent] + 1
        translation = rest[original] - (rest[parent] if parent >= 0 else rest[hips])
        if original == hips:
            translation = np.array([0, height, 0])
            local = remove_heading @ world[:, original]
        else:
            local = world[:, parent].transpose(0, 2, 1) @ world[:, original]
        exported.append({'name': semantic[target], 'translation': translation.tolist()})
        exported[parent_target].setdefault('children', []).append(target + 1)
        rotations.append(continuous_quaternions(local))
    root = {'schemaVersion': 'lingchat.root-trajectory.v1', 'sampleRateHz': fps, 'frameCount': count,
        'positionMeters': trajectories.tolist(), 'yawRadians': (heading - heading[0]).tolist(),
        'sourceOriginPositionMeters': positions[0, hips].tolist(), 'sourceOriginYawRadians': float(heading[0]),
        'verticalPolicy': 'retained as local hips excursion, not applied to world root'}
    return exported, rotations, hips_position, root


def write_glb(path, nodes, rotations, hips_position, fps, profile):
    binary = bytearray()
    views, accessors = [], []

    def accessor(array, kind):
        array = np.asarray(array, dtype='<f4')
        while len(binary) % 4:
            binary.append(0)
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': array.nbytes})
        binary.extend(array.tobytes())
        item = {'bufferView': len(views) - 1, 'componentType': 5126, 'count': len(array), 'type': kind}
        if kind == 'SCALAR':
            item.update({'min': [float(array.min())], 'max': [float(array.max())]})
        accessors.append(item)
        return len(accessors) - 1

    times = accessor(np.arange(len(hips_position)) / fps, 'SCALAR')
    samplers, channels = [], []
    for node, values, kind, channel in [(1 + list(profile['humanBones']).index('hips'), hips_position, 'VEC3', 'translation')] + [
            (i + 1, values, 'VEC4', 'rotation') for i, values in enumerate(rotations)]:
        output = accessor(values, kind)
        channels.append({'sampler': len(samplers), 'target': {'node': node, 'path': channel}})
        samplers.append({'input': times, 'output': output, 'interpolation': 'LINEAR'})
    document = {'asset': {'version': '2.0', 'generator': 'LingChat-XR metric-BVH body exporter/1'},
        'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': nodes,
        'extensionsUsed': ['VRMC_vrm_animation'], 'extensions': {'VRMC_vrm_animation': {'specVersion': '1.0',
            'humanoid': {'humanBones': {role: {'node': i + 1} for i, role in enumerate(profile['humanBones'])}}}},
        'buffers': [{'byteLength': len(binary)}], 'bufferViews': views, 'accessors': accessors,
        'animations': [{'name': 'body', 'samplers': samplers, 'channels': channels}]}
    payload = json.dumps(document, separators=(',', ':')).encode()
    payload += b' ' * (-len(payload) % 4)
    binary.extend(b'\0' * (-len(binary) % 4))
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(payload) + len(binary))
        + struct.pack('<II', len(payload), 0x4E4F534A) + payload + struct.pack('<II', len(binary), 0x004E4942) + binary)


def export(source, folder, profile_path, provenance=None, pose_policy='absolute'):
    if folder.exists():
        raise ValueError('Use a fresh package directory')
    profile = json.loads(profile_path.read_text(encoding='utf-8'))
    nodes, samples, fps = read_bvh(source)
    skeleton, rotations, hips, root = build_body(nodes, samples, fps, profile)
    folder.mkdir(parents=True)
    body = folder / 'body.vrma'
    write_glb(body, skeleton, rotations, hips, fps, profile)
    count = len(samples)
    appendages = {'schemaVersion': 'lingchat.appendages.v1', 'sampleRateHz': fps, 'frameCount': count, 'mode': 'none', 'tracks': []}
    metadata = {'schemaVersion': 'lingchat.motion.v1', 'bodyFile': body.name,
        'bodySha256': hashlib.sha256(body.read_bytes()).hexdigest(), 'appendageFile': 'appendages.json',
        'appendageMode': 'none', 'rootTrajectoryFile': 'root-trajectory.json',
        'sampleRateHz': fps, 'frameCount': count, 'lastSampleTimeSec': (count - 1) / fps,
        'cyclePeriodSec': count / fps, 'loop': False, 'worldRootPolicy': 'external', 'posePolicy': pose_policy,
        'quaternionOrder': 'xyzw', 'coordinates': profile['coordinates'], 'qualityStatus': 'candidate-unaccepted',
        'provenance': provenance or {'sourceBvh': str(source.resolve())},
        'notes': ['Body only; no face, finger or appendage observations exported.',
            'World horizontal translation and heading separated; local hips vertical excursion retained.',
            'Reference hips height inferred from metric rest skeleton; final target calibration requires Unity checks.']}
    for name, value in [('metadata.json', metadata), ('appendages.json', appendages), ('root-trajectory.json', root)]:
        (folder / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'VRMA body package: {folder}; {count} samples at {fps} Hz', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bvh', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--profile', type=Path, default=Path(__file__).with_name('soma-humanoid-map.json'))
    parser.add_argument('--provenance', type=Path)
    parser.add_argument('--pose-policy', choices=['absolute','relativeToCurrentIdle'], default='absolute',
        help='Explicit authoring calibration; relative mode keeps motion changes but does not reproduce the source initial pose')
    args = parser.parse_args()
    export(args.bvh, args.output, args.profile, json.loads(args.provenance.read_text(encoding='utf-8')) if args.provenance else None, args.pose_policy)
