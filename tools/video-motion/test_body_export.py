import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
from scipy.spatial.transform import Rotation
from audit_model import REQUIRED, read_glb
from export_body_vrma import build_body, continuous_quaternions, read_bvh, write_glb


def fixture():
    parents = {'hips': None, 'spine': 'hips', 'head': 'spine',
        'leftUpperLeg': 'hips', 'leftLowerLeg': 'leftUpperLeg', 'leftFoot': 'leftLowerLeg',
        'rightUpperLeg': 'hips', 'rightLowerLeg': 'rightUpperLeg', 'rightFoot': 'rightLowerLeg',
        'leftUpperArm': 'spine', 'leftLowerArm': 'leftUpperArm', 'leftHand': 'leftLowerArm',
        'rightUpperArm': 'spine', 'rightLowerArm': 'rightUpperArm', 'rightHand': 'rightLowerArm'}
    nodes, lookup, column = [], {}, 0
    for role in REQUIRED:
        parent = parents[role]
        if role == 'hips': offset = [0, 0, 0]
        elif role in ('spine', 'head'): offset = [0, .5, 0]
        elif 'Leg' in role: offset = [0, -.5, 0]
        elif 'Foot' in role: offset = [0, -.1, .1]
        else: offset = [.3 if role.startswith('left') else -.3, 0, 0]
        channels = (['Xposition', 'Yposition', 'Zposition'] if parent is None else []) + ['Zrotation', 'Xrotation', 'Yrotation']
        nodes.append({'name': role, 'parent': lookup[parent] if parent is not None else -1,
            'offset': offset, 'channels': channels, 'column': column})
        column += len(channels)
        lookup[role] = len(nodes) - 1
    values = np.zeros((3, column))
    values[:, :3] = [[4, 1, 3], [5, 1.2, 2], [6, .9, 1]]
    values[:, 5] = [45, 65, 85]
    profile = {'coordinates': {'handedness': 'right', 'up': '+Y', 'forward': '+Z', 'lengthUnit': 'meter'},
        'humanBones': {role: role for role in REQUIRED}}
    return nodes, values, profile


class BodyExportTests(unittest.TestCase):
    def test_external_root_retains_local_hips_bob_and_separates_turn(self):
        nodes, samples, profile = fixture()
        skeleton, quaternions, hips, root = build_body(nodes, samples, 30, profile)
        np.testing.assert_allclose(hips[:, [0, 2]], 0)
        np.testing.assert_allclose(hips[:, 1] - hips[0, 1], [0, .2, -.1], atol=1e-6)
        np.testing.assert_allclose(root['positionMeters'][1], [1, 0, -1])
        np.testing.assert_allclose(root['yawRadians'], np.deg2rad([0, 20, 40]), atol=1e-6)
        np.testing.assert_allclose(Rotation.from_quat(quaternions[0]).magnitude(), 0, atol=1e-6)

    def test_unmapped_ancestor_rotation_reaches_mapped_child(self):
        nodes, samples, profile = fixture()
        # Insert a non-humanoid helper at the end; make Head its child.
        head = next(n for n in nodes if n['name'] == 'head')
        helper = {'name': 'DifferentRig_NeckHelper', 'parent': head['parent'], 'offset': [0, 0, 0],
            'channels': ['Zrotation', 'Xrotation', 'Yrotation'], 'column': samples.shape[1]}
        head['parent'] = len(nodes)
        nodes.append(helper)
        # Evaluation requires hierarchy order; move the helper immediately before Head.
        head_index = 2
        order = [0, 1, len(nodes) - 1] + list(range(2, len(nodes) - 1))
        reordered = [dict(nodes[i]) for i in order]
        for node in reordered:
            if node['parent'] >= 0: node['parent'] = order.index(node['parent'])
        samples = np.pad(samples, ((0, 0), (0, 3)))
        samples[1, helper['column']] = 30
        _, quaternions, _, _ = build_body(reordered, samples, 30, profile)
        angle = Rotation.from_quat(quaternions[list(profile['humanBones']).index('head')][1]).as_euler('ZXY', degrees=True)[0]
        self.assertAlmostEqual(angle, 30, places=4)

    def test_quaternion_hemisphere_is_continuous(self):
        matrices = Rotation.from_euler('Y', [170, 179, 181, 190], degrees=True).as_matrix()
        q = continuous_quaternions(matrices)
        self.assertTrue(np.all(np.sum(q[1:] * q[:-1], axis=1) > 0))
        np.testing.assert_allclose(np.linalg.norm(q, axis=1), 1, atol=1e-6)

    def test_glb_contains_standard_humanoid_extension_and_matching_timebase(self):
        nodes, samples, profile = fixture()
        skeleton, q, hips, _ = build_body(nodes, samples, 30, profile)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'body.vrma'
            write_glb(path, skeleton, q, hips, 30, profile)
            document, binary = read_glb(path)
            self.assertEqual(document['extensions']['VRMC_vrm_animation']['specVersion'], '1.0')
            self.assertEqual(set(document['extensions']['VRMC_vrm_animation']['humanoid']['humanBones']), set(REQUIRED))
            self.assertEqual(document['accessors'][0]['count'], 3)
            self.assertAlmostEqual(document['accessors'][0]['max'][0], 2 / 30, places=6)

    def test_bvh_parser_rejects_nonfinite_samples(self):
        text = 'HIERARCHY\nROOT Hips\n{\nOFFSET 0 0 0\nCHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation\n}\nMOTION\nFrames: 2\nFrame Time: 0.04\n0 0 0 0 0 0\n0 nan 0 0 0 0\n'
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'bad.bvh'; path.write_text(text)
            with self.assertRaises(ValueError): read_bvh(path)


if __name__ == '__main__':
    unittest.main()
