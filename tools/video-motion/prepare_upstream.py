"""Lock the reviewed GEM checkout and its weight URLs. No floating mirror fallback."""
import argparse
import hashlib
import json
from pathlib import Path
import runpy
import subprocess
import urllib.request
from download_file import download_file

COMMIT = '80755fbac89ba2f65ff846df249507138b622884'
GEM_REV = '5b9a69d5ed4713ac4ba2a63b335e66db87279a3e'
SOMA_CODE = 'e0f8ff0ecfa3edbbb6058b1e0f08822ee2f84ee5'


def api(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'LingChat-XR-P0'}), timeout=60) as response:
        return json.load(response)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def git(root, *args):
    return subprocess.check_output(['git', '-c', 'safe.directory=' + root.resolve().as_posix(), '-C', str(root), *args], text=True).strip()


def prepare(root, lock):
    if git(root, 'rev-parse', 'HEAD') != COMMIT:
        raise ValueError('Checkout does not match the reviewed upstream commit')
    if lock.exists():
        record = json.loads(lock.read_text(encoding='utf-8'))
        soma_rev = record['somaWeightsRevision']
    else:
        soma_rev = api('https://huggingface.co/api/models/nvidia/SOMA-X/revision/main')['sha']
    if len(soma_rev) != 40 or any(c not in '0123456789abcdef' for c in soma_rev):
        raise ValueError('Invalid SOMA weight revision')
    model_file = root / 'comfyui_gem_x/models.py'
    original = git(root, 'show', f'{COMMIT}:comfyui_gem_x/models.py') + '\n'
    patched = original.replace('import hashlib\n', 'import hashlib\nimport os\n')
    patched = patched.replace('HF_GEM = "https://huggingface.co/nvidia/GEM-X/resolve/main/"',
        f'GEM_REVISION = os.environ.get("GEM_X_GEM_REVISION", "{GEM_REV}")\n'
        'HF_GEM = f"https://huggingface.co/nvidia/GEM-X/resolve/{GEM_REVISION}/"')
    patched = patched.replace('HF_SOMA = "https://huggingface.co/nvidia/SOMA-X/resolve/main/"',
        f'SOMA_REVISION = os.environ.get("GEM_X_SOMA_REVISION", "{soma_rev}")\n'
        'HF_SOMA = f"https://huggingface.co/nvidia/SOMA-X/resolve/{SOMA_REVISION}/"')
    patched = patched.replace('[HF_GEM + name, MS_GEM + name]', '[HF_GEM + name]')
    patched = patched.replace('[HF_SOMA + name, MS_SOMA + name]', '[HF_SOMA + name]')
    # Reject mutable/invalid revisions even when the environment overrides the default.
    guard = '\nfor _revision in (GEM_REVISION, SOMA_REVISION):\n'
    guard += '    if len(_revision) != 40 or any(c not in "0123456789abcdef" for c in _revision):\n'
    guard += '        raise ValueError("GEM-X requires immutable 40-character revisions")\n'
    patched = patched.replace('\n\ndef _gem(', guard + '\n\ndef _gem(')
    current = model_file.read_text(encoding='utf-8')
    if current not in (original, patched):
        raise ValueError('models.py has unrelated changes; refusing to overwrite')
    model_file.write_text(patched, encoding='utf-8', newline='\n')
    catalog = runpy.run_path(str(model_file))
    inventories = {}
    for repo, revision in [('GEM-X', GEM_REV), ('SOMA-X', soma_rev)]:
        info = api(f'https://huggingface.co/api/models/nvidia/{repo}/revision/{revision}?blobs=true')
        inventories[repo] = {x['rfilename']: x for x in info['siblings']}
    entries = {}
    for group, values in catalog['GROUPS'].items():
        for dest, urls, size, digest in values:
            if dest not in entries:
                upstream = None
                for repo, revision in [('GEM-X', GEM_REV), ('SOMA-X', soma_rev)]:
                    marker = f'https://huggingface.co/nvidia/{repo}/resolve/{revision}/'
                    if urls[0].startswith(marker):
                        upstream = inventories[repo].get(urls[0][len(marker):])
                        if upstream is None or upstream.get('size') != size:
                            raise ValueError(f'Historical asset missing or incompatible size: {dest}')
                entries[dest] = {'path': dest, 'urls': urls, 'bytes': size,
                    'sha256': digest or ((upstream or {}).get('lfs') or {}).get('sha256'), 'groups': []}
            entries[dest]['groups'].append(group)
    entries['yolox.zip'] = {'path': 'yolox/yolox_x_8xb8-300e_humanart-a39d44ed.zip',
        'urls': [catalog['YOLOX_ZIP']], 'bytes': 368200375, 'sha256': None,
        'groups': list(catalog['GROUPS']), 'integrity': 'first-download hash must be recorded; no upstream SHA supplied'}
    record = {'schemaVersion': 'lingchat.gem-lock.v1', 'repository': 'https://github.com/lkevincc0/ComfyUI-GEM-X',
        'commit': COMMIT, 'pluginVersion': '0.1.0', 'gemWeightsRevision': GEM_REV,
        'somaCodeRevision': SOMA_CODE, 'somaWeightsRevision': soma_rev,
        'modelsPythonSha256': sha(model_file), 'files': list(entries.values()),
        'inferenceValidated': False, 'notes': ['SOMA HF revision resolved once and persisted; MHR compatibility still requires inference.',
            'ModelScope floating mirrors disabled.', 'Environment overrides must be immutable; changing them requires a new lock.']}
    lock.parent.mkdir(parents=True, exist_ok=True)
    lock.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    print(f'Locked {len(entries)} assets: {lock}')


def download(root, lock, models, group, prefer_mirror=False):
    record = json.loads(lock.read_text(encoding='utf-8'))
    if git(root, 'rev-parse', 'HEAD') != record['commit'] or sha(root / 'comfyui_gem_x/models.py') != record['modelsPythonSha256']:
        raise ValueError('Source differs from lock')
    catalog = runpy.run_path(str(root / 'comfyui_gem_x/models.py'))
    # Download using upstream resumable transport, with LFS hashes enforced from lock.
    measured = []
    for entry in record['files']:
        if group not in entry['groups']:
            continue
        if entry['bytes'] > 100_000_000:
            mirrors = []
            if entry['sha256'] and entry['urls'][0].startswith('https://huggingface.co/'):
                mirrors = [entry['urls'][0].replace('https://huggingface.co/', 'https://hf-mirror.com/', 1)]
            urls = [entry['urls'][0]] + mirrors
            if prefer_mirror and mirrors:
                urls.reverse()
            download_file(urls[0], models / entry['path'], entry['bytes'], entry['sha256'], alternate_urls=urls[1:])
        else:
            catalog['_download']((entry['path'], entry['urls'], entry['bytes'], entry['sha256']), models)
        actual = sha(models / entry['path'])
        measured.append({**entry, 'actualSha256': actual})
    # Upstream extracts exactly the expected detector file from the checked archive.
    catalog['_download_yolox'](models)
    detector = models / catalog['YOLOX_ONNX']
    measured.append({'path': catalog['YOLOX_ONNX'], 'bytes': detector.stat().st_size, 'actualSha256': sha(detector)})
    (models / 'download-receipt.json').write_text(json.dumps({'group': group, 'preferredTransport': 'hf-mirror' if prefer_mirror else 'official', 'files': measured}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--checkout', type=Path, required=True)
    parser.add_argument('--lock', type=Path, required=True)
    parser.add_argument('--download', choices=['onnx_keypoints', 'onnx_sam3d', 'pytorch'])
    parser.add_argument('--models', type=Path)
    parser.add_argument('--prefer-hf-mirror', action='store_true', help='Prefer exact-revision, SHA-verified mirror for large HF assets')
    args = parser.parse_args()
    if args.download:
        if args.models is None:
            parser.error('--models is required for download')
        download(args.checkout, args.lock, args.models, args.download, args.prefer_hf_mirror)
    else:
        prepare(args.checkout, args.lock)
