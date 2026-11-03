"""Run the locked GEM worker and raw BVH export, recording measured success or failure."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import runpy
import subprocess
import sys
import threading
import time
import traceback


def run(args):
    root, models, source, output = [p.resolve() for p in (args.checkout, args.models, args.video, args.output)]
    lock = json.loads(args.lock.read_text(encoding='utf-8'))
    commit = subprocess.check_output(['git', '-c', 'safe.directory=' + root.as_posix(), '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    with (root / 'comfyui_gem_x/models.py').open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if commit != lock['commit'] or digest != lock['modelsPythonSha256']:
        raise ValueError('Worker source does not match lock')
    if not source.is_file():
        raise ValueError('Input video missing')
    if output.exists():
        raise ValueError('Use a fresh output directory; failed runs are retained for diagnosis')
    receipt = models / 'download-receipt.json'
    if not receipt.is_file() or json.loads(receipt.read_text(encoding='utf-8'))['group'] != 'onnx_keypoints':
        raise ValueError('Verified onnx_keypoints download receipt missing')
    # Inherited packages are read-only. Numba's writable-path probe can loop on
    # Windows sandbox ACL denials, so keep its cache beside the isolated worker.
    numba_cache = root.parent / 'numba-cache'
    numba_cache.mkdir(exist_ok=True)
    os.environ.setdefault('NUMBA_CACHE_DIR', str(numba_cache))
    output.mkdir(parents=True)
    (output / 'process.json').write_text(json.dumps({'pid': os.getpid(), 'source': str(source)}, indent=2), encoding='utf-8')
    started = time.perf_counter()
    report = {'schemaVersion': 'lingchat.gem-experiment.v1', 'source': str(source),
        'upstreamCommit': commit, 'backend': 'onnx_keypoints + CUDA PyTorch decoder',
        'postprocess': False, 'seed': args.seed, 'modelLoadingIncluded': True,
        'dependencyInstallIncluded': False, 'python': sys.version, 'platform': platform.platform(),
        'bodyInferencePassed': False, 'rawBvhExportPassed': False, 'unityPlaybackPassed': False,
        'inputQualityAccepted': False, 'peakVramProcessBytes': None}
    readings, stop = [], threading.Event()
    def sample_gpu():
        while not stop.is_set():
            try:
                reading = subprocess.check_output(['nvidia-smi', '--query-gpu=memory.used', '--format=csv,noheader,nounits'],
                    timeout=5, text=True)
                readings.append([int(x.strip()) for x in reading.strip().splitlines()])
            except (OSError, ValueError, subprocess.SubprocessError):
                pass
            stop.wait(1)
    sampler = threading.Thread(target=sample_gpu, daemon=True)
    sampler.start()
    original_argv, original_cwd = sys.argv, Path.cwd()
    try:
        print('GEM_STAGE importing runtime', flush=True)
        import numpy as np
        import torch
        import onnxruntime as ort
        if not torch.cuda.is_available():
            raise ValueError('CUDA PyTorch unavailable; CPU is not an accepted GEM backend')
        report['gpu'] = torch.cuda.get_device_name(0)
        report['gpuCapacityBytes'] = torch.cuda.get_device_properties(0).total_memory
        report['torch'] = torch.__version__
        report['cudaRuntime'] = torch.version.cuda
        report['onnxRuntime'] = ort.__version__
        # An advertised CUDA provider does not prove its DLLs can load. Reject silent CPU fallback.
        detector = models / 'yolox/yolox_x_8xb8-300e_humanart-a39d44ed.onnx'
        print('GEM_STAGE creating CUDA detector preflight session', flush=True)
        detector_session = ort.InferenceSession(str(detector), providers=['CUDAExecutionProvider'])
        print('GEM_STAGE CUDA detector session ready', flush=True)
        report['detectorSessionProviders'] = detector_session.get_providers()
        if 'CUDAExecutionProvider' not in detector_session.get_providers():
            raise ValueError('ONNX CUDA provider could not load; CPU fallback is not accepted')
        del detector_session
        print('GEM_STAGE CUDA detector session released', flush=True)
        torch.cuda.reset_peak_memory_stats()
        os.environ['GEM_X_MODEL_ROOT'] = str(models)
        if args.subject_box is not None:
            import cv2
            sys.path.insert(0, str(root))
            from gem.utils.geo_transform import get_bbx_xys_from_xyxy
            capture = cv2.VideoCapture(str(source))
            try:
                width, height = int(capture.get(3)), int(capture.get(4))
                frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
            finally:
                capture.release()
            x0,y0,x1,y1 = args.subject_box
            if not all(np.isfinite(args.subject_box)) or not (0 <= x0 < x1 < width and 0 <= y0 < y1 < height) or frames < 2:
                raise ValueError('Manual subject box must lie within the source video')
            boxes = torch.tensor(args.subject_box, dtype=torch.float32).repeat(frames, 1)
            preprocess = output / 'preprocess'
            preprocess.mkdir(exist_ok=True)
            torch.save({'bbx_xyxy': boxes, 'bbx_xys': get_bbx_xys_from_xyxy(boxes, base_enlarge=1.2).float()}, preprocess / 'bbx.pt')
            report['subjectTracking'] = {'mode': 'manual-fixed-subject-box', 'boxXYXY': args.subject_box,
                'note': 'Manual subject confirmation, not successful automatic detection; keypoints remain network predictions.'}
        sys.argv = [str(root / 'scripts/comfyui_infer.py'), '--video', str(source), '--output', str(output),
            '--models-dir', str(models), '--backend', 'onnx', '--no-imgfeat', '--no-postprocess',
            '--preview', 'keypoints', '--seed', str(args.seed)]
        print('GEM_STAGE starting upstream worker', flush=True)
        runpy.run_path(sys.argv[0], run_name='__main__')
        with np.load(output / 'motion.npz', allow_pickle=False) as motion:
            for name in motion.files:
                if not np.isfinite(motion[name]).all():
                    raise ValueError(f'Nonfinite body output: {name}')
            report['frameCount'] = int(motion['frame_count'])
            report['sampleRateHz'] = float(motion['fps'])
            report['lastSampleTimeSec'] = (report['frameCount'] - 1) / report['sampleRateHz']
        report['bodyInferencePassed'] = True
        sys.path.insert(0, str(root / 'scripts'))
        sys.argv = [str(root / 'scripts/comfyui_export_bvh.py'), '--motion', str(output / 'motion.npz'), '--models-dir', str(models)]
        runpy.run_path(sys.argv[0], run_name='__main__')
        report['rawBvhExportPassed'] = True
        report['torchPeakAllocatedBytes'] = torch.cuda.max_memory_allocated()
    except BaseException:
        report['error'] = traceback.format_exc()
        print(report['error'], flush=True)
    finally:
        sys.argv = original_argv
        os.chdir(original_cwd)
        stop.set()
        sampler.join(timeout=6)
        report['elapsedSec'] = time.perf_counter() - started
        report['gpuTotalUsedSamplesMiB'] = readings
        report['gpuTotalUsedPeakMiB'] = max((max(x) for x in readings), default=None)
        report['memoryMeasurementNote'] = 'nvidia-smi samples total GPU usage, including other processes; torch peak excludes ONNX allocations. Exact process peak is unmeasured.'
        (output / 'experiment-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if not report['rawBvhExportPassed']:
        raise SystemExit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--checkout', type=Path, required=True)
    parser.add_argument('--lock', type=Path, required=True)
    parser.add_argument('--models', type=Path, required=True)
    parser.add_argument('--video', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--seed', type=int, default=0)
    parser.add_argument('--subject-box', type=float, nargs=4, metavar=('X0','Y0','X1','Y1'),
        help='Optional manually confirmed fixed subject box, in source pixels; recorded as manual provenance')
    run(parser.parse_args())
