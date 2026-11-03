"""Read-only VRM/GLB skeleton, skinning, reference-pose and spring capability audit."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

REQUIRED = ['hips', 'spine', 'head', 'leftUpperLeg', 'leftLowerLeg', 'leftFoot',
            'rightUpperLeg', 'rightLowerLeg', 'rightFoot', 'leftUpperArm',
            'leftLowerArm', 'leftHand', 'rightUpperArm', 'rightLowerArm', 'rightHand']
FORMATS = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2),
           5125: ('I', 4), 5126: ('f', 4)}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def read_glb(path):
    with path.open('rb') as stream:
        magic, version, length = struct.unpack('<III', stream.read(12))
        if magic != 0x46546C67 or version != 2 or length != path.stat().st_size:
            raise ValueError('Invalid GLB header')
        doc, binary = None, b''
        while stream.tell() < length:
            size, kind = struct.unpack('<II', stream.read(8))
            chunk = stream.read(size)
            if kind == 0x4E4F534A:
                doc = json.loads(chunk)
            elif kind == 0x004E4942:
                binary = chunk
        if doc is None:
            raise ValueError('Missing glTF document')
        return doc, binary


def accessor(doc, binary, index):
    a = doc['accessors'][index]
    if 'sparse' in a:
        raise ValueError('Sparse skin accessors require a separate audit; no guessed weights')
    view = doc['bufferViews'][a['bufferView']]
    if view.get('buffer', 0) != 0:
        raise ValueError('External buffer not supported by this GLB audit')
    code, size = FORMATS[a['componentType']]
    width = WIDTHS[a['type']]
    stride = view.get('byteStride', size * width)
    start = view.get('byteOffset', 0) + a.get('byteOffset', 0)
    for i in range(a['count']):
        value = struct.unpack_from('<' + code * width, binary, start + i * stride)
        if a.get('normalized') and code != 'f':
            limit = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}[a['componentType']]
            value = tuple(max(-1, v / limit) for v in value)
        yield value


def audit(path):
    doc, binary = read_glb(path)
    nodes = doc['nodes']
    ext = doc.get('extensions', {})
    vrm = ext.get('VRMC_vrm', {})
    human = vrm.get('humanoid', {}).get('humanBones', {})
    if not human:
        human = {x['bone']: {'node': x['node']} for x in ext.get('VRM', {}).get('humanoid', {}).get('humanBones', [])}
    parents = {child: i for i, n in enumerate(nodes) for child in n.get('children', [])}
    def node_path(i):
        n = nodes[i].get('name', f'node_{i}')
        return node_path(parents[i]) + '/' + n if i in parents else n
    weighted = {}
    # Count real nonzero mesh influences, not merely membership in skin.joints.
    for mesh_node in nodes:
        if 'mesh' not in mesh_node or 'skin' not in mesh_node:
            continue
        joints = doc['skins'][mesh_node['skin']]['joints']
        for primitive in doc['meshes'][mesh_node['mesh']]['primitives']:
            attrs = primitive['attributes']
            for key in attrs:
                if not key.startswith('JOINTS_'):
                    continue
                weight_key = key.replace('JOINTS_', 'WEIGHTS_')
                if weight_key not in attrs:
                    raise ValueError('Skin has joints without matching weights')
                for js, ws in zip(accessor(doc, binary, attrs[key]), accessor(doc, binary, attrs[weight_key]), strict=True):
                    for j, w in zip(js, ws):
                        if w > 1e-6:
                            joint = joints[j]
                            item = weighted.setdefault(joint, {'vertexInfluences': 0, 'weightSum': 0.0})
                            item['vertexInfluences'] += 1
                            item['weightSum'] += w
    springs = ext.get('VRMC_springBone', {})
    ownership = {}
    for chain, spring in enumerate(springs.get('springs', [])):
        for order, joint in enumerate(spring['joints']):
            ownership.setdefault(joint['node'], []).append({'chain': chain, 'order': order})
    inventory = []
    for i, n in enumerate(nodes):
        inventory.append({'node': i, 'name': n.get('name'), 'path': node_path(i),
            'parent': parents.get(i), 'children': n.get('children', []),
            'referenceTranslation': n.get('translation', [0, 0, 0]),
            'referenceRotationXyzw': n.get('rotation', [0, 0, 0, 1]),
            'referenceScale': n.get('scale', [1, 1, 1]), 'referenceMatrix': n.get('matrix'),
            'skinning': weighted.get(i, {'vertexInfluences': 0, 'weightSum': 0}),
            'springMembership': ownership.get(i, []),
            'humanoid': next((k for k, v in human.items() if v['node'] == i), None)})
    # Candidate names are evidence for manual semantic assignment, never runtime rules.
    candidates = [x for x in inventory if re.search(r'ear|tail|fin|耳|尾|鰭|鳍', x['name'] or '', re.I)]
    signature_data = [{'path': x['path'], 'parent': x['parent'], 't': x['referenceTranslation'],
        'r': x['referenceRotationXyzw'], 's': x['referenceScale'], 'm': x['referenceMatrix']} for x in inventory]
    signature = hashlib.sha256(json.dumps(signature_data, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    # Rest-space extent is explicitly not claimed to be final skinned runtime bounds.
    positions = [a for a in doc['accessors'] if a.get('type') == 'VEC3' and 'min' in a and 'max' in a]
    report = {'schemaVersion': 'lingchat.model-capabilities.v1', 'modelFile': str(path.resolve()),
        'modelSha256': digest, 'bytes': path.stat().st_size, 'skeletonSignature': signature,
        'vrmVersion': vrm.get('specVersion', ext.get('VRM', {}).get('specVersion')),
        'coordinateConvention': 'glTF right-handed, +Y up, meters; Unity conversion not yet verified',
        'humanoid': human, 'missingRequiredHumanBones': [k for k in REQUIRED if k not in human],
        'humanoidValidation': 'presence-only; Unity Avatar and reference calibration require runtime check',
        'referencePose': 'raw glTF local TRS/matrix in nodeInventory; no inferred T-pose correction',
        'positionAccessorBounds': [{'min': a['min'], 'max': a['max']} for a in positions],
        'springs': springs, 'vrm0SecondaryAnimation': ext.get('VRM', {}).get('secondaryAnimation'),
        'expressions': vrm.get('expressions', ext.get('VRM', {}).get('blendShapeMaster', {})),
        'appendageCandidates': candidates, 'nodeInventory': inventory,
        'semanticBindingStatus': 'draft: names alone do not identify ear fins, tail branches, or motion axes',
        'runtimeHeightMeters': None, 'visualSkinningVerified': False}
    return report


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('model', type=Path)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    report = audit(args.model)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ['modelSha256', 'vrmVersion', 'missingRequiredHumanBones', 'skeletonSignature']}, ensure_ascii=False))
    print('Appendage candidates:', [(x['node'], x['name'], x['skinning']['vertexInfluences'], x['springMembership']) for x in report['appendageCandidates']])
