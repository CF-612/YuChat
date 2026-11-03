"""Exercise the downloaded detector on a blank image and prove CUDA nodes executed."""
import argparse
import json
from pathlib import Path
import time
import traceback


def probe(model, output):
    import torch
    import numpy as np
    import onnxruntime as ort
    if output.exists():
        raise ValueError('Use a fresh report path')
    output.parent.mkdir(parents=True, exist_ok=True)
    report = {'stage': 'detector-only CUDA preflight; not GEM body inference or detection-quality validation',
        'torch': torch.__version__, 'cudaRuntime': torch.version.cuda,
        'ortVersion': ort.__version__, 'passed': False}
    started = time.perf_counter()
    session = None
    try:
        report['gpu'] = torch.cuda.get_device_name(0)
        if hasattr(ort, 'preload_dlls'):
            ort.preload_dlls()
        options = ort.SessionOptions()
        options.enable_profiling = True
        options.profile_file_prefix = str(output.with_suffix('.ort-profile'))
        session = ort.InferenceSession(str(model.resolve()), sess_options=options, providers=['CUDAExecutionProvider'])
        report['sessionProviders'] = session.get_providers()
        if 'CUDAExecutionProvider' not in session.get_providers():
            raise RuntimeError('CUDA provider could not load; CPU fallback is not accepted')
        source = session.get_inputs()[0]
        report['input'] = {'name': source.name, 'shape': source.shape, 'type': source.type}
        shape = [n if isinstance(n, int) and n > 0 else (1 if i == 0 else 640) for i, n in enumerate(source.shape)]
        values = session.run(None, {source.name: np.zeros(shape, dtype=np.float32)})
        report['outputs'] = [{'shape': list(x.shape), 'finite': bool(np.isfinite(x).all())} for x in values]
        profile = Path(session.end_profiling())
        events = json.loads(profile.read_text(encoding='utf-8'))
        counts = {}
        for event in events:
            provider = event.get('args', {}).get('provider')
            if provider:
                counts[provider] = counts.get(provider, 0) + 1
        report['profile'] = str(profile)
        report['executedNodeEvents'] = counts
        if counts.get('CUDAExecutionProvider', 0) == 0:
            raise RuntimeError('No CUDA node execution recorded')
        report['passed'] = all(x['finite'] for x in report['outputs'])
    except Exception:
        report['error'] = traceback.format_exc()
    finally:
        report['elapsedSec'] = time.perf_counter() - started
        output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(report, indent=2), flush=True)
    if not report['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    probe(args.model, args.output)
