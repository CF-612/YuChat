"""Independent face feasibility experiment. Failed observations stay null, not fabricated curves."""
import argparse
import hashlib
import json
from pathlib import Path
import time


def probe(video, model, output):
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    output.mkdir(parents=True, exist_ok=False)
    with model.open('rb') as stream:
        model_hash = hashlib.file_digest(stream, 'sha256').hexdigest()
    cap = cv2.VideoCapture(str(video))
    if not cap.isOpened():
        raise ValueError('Cannot open source video')
    fps = cap.get(cv2.CAP_PROP_FPS)
    if fps <= 0:
        raise ValueError('Video must have valid FPS')
    width, height = int(cap.get(3)), int(cap.get(4))
    writer = cv2.VideoWriter(str(output / 'face-observations.mp4'), cv2.VideoWriter_fourcc(*'mp4v'), fps, (width, height))
    if not writer.isOpened():
        raise ValueError('Cannot open preview encoder')
    options = vision.FaceLandmarkerOptions(base_options=python.BaseOptions(model_asset_path=str(model)),
        running_mode=vision.RunningMode.VIDEO, num_faces=1,
        min_face_detection_confidence=.5, min_face_presence_confidence=.5,
        min_tracking_confidence=.5, output_face_blendshapes=True)
    records = []
    started = time.perf_counter()
    try:
        with vision.FaceLandmarker.create_from_options(options) as detector:
            while True:
                ok, frame = cap.read()
                if not ok:
                    break
                i = len(records)
                result = detector.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB,
                    data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)), round(i * 1000 / fps))
                seen = bool(result.face_landmarks and result.face_blendshapes)
                scores = {c.category_name: float(c.score) for c in result.face_blendshapes[0]} if seen else None
                records.append({'frame': i, 'timeSec': i / fps, 'observed': seen,
                    'provenance': 'mediapipe-estimate-unreviewed' if seen else 'not-detected',
                    'blendshapeScores': scores, 'detectionConfidence': None,
                    'candidateVrm': {'blinkLeft': scores.get('eyeBlinkLeft'), 'blinkRight': scores.get('eyeBlinkRight'),
                        'mouthOpen': scores.get('jawOpen'),
                        'mouthSmile': (scores.get('mouthSmileLeft', 0) + scores.get('mouthSmileRight', 0)) / 2} if seen else None})
                if seen:
                    for landmark in result.face_landmarks[0]:
                        cv2.circle(frame, (round(landmark.x * width), round(landmark.y * height)), 1, (0, 255, 0), -1)
                cv2.putText(frame, f'{i / fps:.2f}s ' + ('ESTIMATE' if seen else 'NOT DETECTED'),
                    (8, 20), cv2.FONT_HERSHEY_SIMPLEX, .4, (0, 255, 0) if seen else (0, 0, 255), 1)
                writer.write(frame)
    finally:
        cap.release()
        writer.release()
    if not records:
        raise ValueError('No frames decoded')
    seen_count = sum(r['observed'] for r in records)
    gaps, start = [], None
    for i, r in enumerate(records + [{'observed': True}]):
        if not r['observed'] and start is None:
            start = i
        if r['observed'] and start is not None:
            gaps.append({'startFrame': start, 'endFrameInclusive': i - 1, 'startSec': start / fps,
                'endSecInclusive': (i - 1) / fps, 'fill': None})
            start = None
    report = {'schemaVersion': 'lingchat.face-experiment.v1', 'source': str(video.resolve()),
        'backend': 'MediaPipe FaceLandmarker', 'modelSha256': model_hash,
        'sampleRateHz': fps, 'frameCount': len(records), 'lastSampleTimeSec': (len(records) - 1) / fps,
        'observedFrames': seen_count, 'observationRate': seen_count / len(records),
        'undetectedIntervals': gaps, 'elapsedSec': time.perf_counter() - started,
        'acceptedForPlayback': False, 'manualCorrections': [], 'frames': records,
        'notes': ['General human-face detector; stylized-face correctness not guaranteed.',
            'Blendshape coefficient is not detection confidence. API exposes no per-frame detection confidence here.',
            'No missing frames filled; no TTS/expression owner changes made.',
            'mouthSmile is not a validated full-face happy expression.']}
    (output / 'face-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f'Face observations: {seen_count}/{len(records)} ({seen_count / len(records):.1%}); preview requires review')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('video', type=Path)
    p.add_argument('--model', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    probe(args.video, args.model, args.output)
