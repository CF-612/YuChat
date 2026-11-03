import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import struct
import tempfile
from threading import Thread
import unittest

from audit_model import accessor, audit
from download_file import download_file


def glb(path, doc, binary):
    text = json.dumps(doc).encode()
    text += b' ' * (-len(text) % 4)
    binary += b'\0' * (-len(binary) % 4)
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(text) + len(binary))
        + struct.pack('<II', len(text), 0x4E4F534A) + text
        + struct.pack('<II', len(binary), 0x004E4942) + binary)


class ModelAuditTests(unittest.TestCase):
    def fixture(self, path):
        # Two vertices, one normalized nonzero influence. A zero-weight joint must not count.
        binary = bytes([0, 1, 0, 0, 0, 1, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0])
        doc = {'asset': {'version': '2.0'}, 'nodes': [
            {'name': 'Rig', 'children': [1, 2]}, {'name': 'Tail', 'translation': [0, 1, 0]},
            {'name': 'Fin'}, {'name': 'Mesh', 'mesh': 0, 'skin': 0}],
            'skins': [{'joints': [1, 2]}], 'meshes': [{'primitives': [{'attributes': {'JOINTS_0': 0, 'WEIGHTS_0': 1}}]}],
            'bufferViews': [{'buffer': 0, 'byteOffset': 0, 'byteLength': 8}, {'buffer': 0, 'byteOffset': 8, 'byteLength': 8}],
            'accessors': [{'bufferView': 0, 'componentType': 5121, 'type': 'VEC4', 'count': 2},
                {'bufferView': 1, 'componentType': 5121, 'type': 'VEC4', 'count': 2, 'normalized': True}],
            'extensions': {'VRMC_vrm': {'specVersion': '1.0', 'humanoid': {'humanBones': {'hips': {'node': 0}}}},
                'VRMC_springBone': {'springs': [{'name': 'tail', 'joints': [{'node': 1}]}]}}}
        glb(path, doc, binary)
        return doc, binary

    def test_counts_real_weight_and_reports_missing_body_capabilities(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'model.vrm'
            self.fixture(path)
            report = audit(path)
            self.assertEqual(report['nodeInventory'][1]['skinning']['vertexInfluences'], 1)
            self.assertEqual(report['nodeInventory'][2]['skinning']['vertexInfluences'], 1)
            self.assertEqual(report['nodeInventory'][1]['skinning']['weightSum'], 1)
            self.assertEqual(report['nodeInventory'][1]['path'], 'Rig/Tail')
            self.assertIn('head', report['missingRequiredHumanBones'])
            self.assertEqual(report['nodeInventory'][1]['springMembership'], [{'chain': 0, 'order': 0}])
            self.assertFalse(report['visualSkinningVerified'])

    def test_signature_changes_with_reference_pose(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'model.vrm'
            doc, binary = self.fixture(path)
            before = audit(path)['skeletonSignature']
            doc['nodes'][1]['translation'][1] = 2
            glb(path, doc, binary)
            self.assertNotEqual(before, audit(path)['skeletonSignature'])

    def test_invalid_header_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'bad.vrm'
            path.write_bytes(struct.pack('<III', 0, 2, 12))
            with self.assertRaises(ValueError):
                audit(path)

    def test_sparse_weights_are_not_silently_ignored(self):
        doc = {'accessors': [{'sparse': {}, 'componentType': 5121, 'type': 'VEC4'}]}
        with self.assertRaises(ValueError):
            list(accessor(doc, b'', 0))


class DownloadTests(unittest.TestCase):
    def test_exact_range_hash_and_existing_file_protection(self):
        payload = b'checked immutable model weights' * 100
        requests = []
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                requests.append(self.headers['Range'])
                first, last = map(int, self.headers['Range'].split('=')[1].split('-'))
                self.send_response(206)
                self.send_header('Content-Range', f'bytes {first}-{last}/{len(payload)}')
                self.end_headers()
                self.wfile.write(payload[first:last + 1])
            def log_message(self, *args):
                pass
        server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with tempfile.TemporaryDirectory() as folder:
                path = Path(folder) / 'weights'
                url = f'http://127.0.0.1:{server.server_port}/weights'
                digest = hashlib.sha256(payload).hexdigest()
                download_file(url, path, len(payload), digest)
                self.assertEqual(path.read_bytes(), payload)
                download_file(url, path, len(payload), digest)
                self.assertEqual(len(requests), 1)
                path.write_bytes(b'incompatible existing data')
                with self.assertRaises(ValueError):
                    download_file(url, path, len(payload), digest)
                self.assertEqual(path.read_bytes(), b'incompatible existing data')
        finally:
            server.shutdown()
            server.server_close()


if __name__ == '__main__':
    unittest.main()
