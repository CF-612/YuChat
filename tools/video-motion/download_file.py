"""Resumable range downloads for large immutable model assets; verify final SHA256."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
from pathlib import Path
import time
import threading
import urllib.request


def download_file(url, destination, size, expected_sha256, workers=12, alternate_urls=()):
    destination = Path(destination)
    def digest(path):
        with path.open('rb') as stream:
            return hashlib.file_digest(stream, 'sha256').hexdigest()
    if destination.exists():
        if destination.stat().st_size == size and (not expected_sha256 or digest(destination) == expected_sha256):
            return
        raise ValueError(f'Existing file differs from lock; refusing overwrite: {destination}')
    destination.parent.mkdir(parents=True, exist_ok=True)
    pieces = destination.with_name(destination.name + '.chunks')
    pieces.mkdir(exist_ok=True)
    block = 8 * 1024 * 1024
    count = (size + block - 1) // block
    transport = threading.local()
    # Reuse TLS connections when requests is available in the worker environment.
    # urllib's Connection: close otherwise repeats expensive redirected handshakes.
    try:
        import requests
    except ImportError:
        requests = None
    def fetch(index):
        start, end = index * block, min(size, (index + 1) * block) - 1
        path = pieces / f'{index:06d}'
        if path.exists() and path.stat().st_size == end - start + 1:
            return path
        # A distinct query prevents intermediaries incorrectly sharing cached Range responses.
        for attempt in range(6):
            try:
                source = ([url] + list(alternate_urls))[attempt % (1 + len(alternate_urls))]
                target = source + ('&' if '?' in source else '?') + f'lc_range={start}-{end}'
                req = urllib.request.Request(target, headers={'Range': f'bytes={start}-{end}', 'User-Agent': 'LingChat-XR-P0'})
                if requests is None:
                    with urllib.request.urlopen(req, timeout=45) as response:
                        if response.status != 206 or response.headers.get('Content-Range') != f'bytes {start}-{end}/{size}':
                            raise ValueError('Server did not honor the exact byte range')
                        data = response.read(end - start + 2)
                else:
                    if not hasattr(transport, 'session'):
                        transport.session = requests.Session()
                    with transport.session.get(target, headers=dict(req.header_items()), timeout=(25, 90), stream=True) as response:
                        if response.status_code != 206 or response.headers.get('Content-Range') != f'bytes {start}-{end}/{size}':
                            raise ValueError('Server did not honor the exact byte range')
                        data = response.raw.read(end - start + 2)
                if len(data) != end - start + 1:
                    raise ValueError('Incomplete chunk')
                path.write_bytes(data)
                return path
            except Exception as error:
                print(f'{destination.name}: chunk {index} attempt {attempt + 1}: {type(error).__name__}', flush=True)
                if attempt == 5:
                    raise
                time.sleep(1 + attempt)
    with ThreadPoolExecutor(max_workers=workers) as pool:
        paths = []
        for i, path in enumerate(pool.map(fetch, range(count))):
            paths.append(path)
            if i % 16 == 0 or i + 1 == count:
                print(f'{destination.name}: {i + 1}/{count} chunks', flush=True)
    part = destination.with_name(destination.name + '.assembled.part')
    with part.open('wb') as stream:
        for path in paths:
            stream.write(path.read_bytes())
    actual = digest(part)
    if part.stat().st_size != size or expected_sha256 and actual != expected_sha256:
        raise ValueError(f'Download integrity failed: {destination}; expected {expected_sha256}, actual {actual}')
    part.replace(destination)
    # Remove only temporary chunks from this completed download.
    for path in paths:
        path.unlink()
    pieces.rmdir()
    print(f'Verified {destination.name}: {actual}', flush=True)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('url')
    p.add_argument('destination', type=Path)
    p.add_argument('--bytes', type=int, required=True)
    p.add_argument('--sha256', required=True)
    args = p.parse_args()
    download_file(args.url, args.destination, args.bytes, args.sha256)
