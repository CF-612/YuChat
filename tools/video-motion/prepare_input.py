"""Convert a GIF to fixed-rate video while retaining the original timing evidence."""
import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import subprocess
from PIL import Image


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def prepare(source, output, fps, ffmpeg, ffprobe):
    if fps <= 0:
        raise ValueError('FPS must be positive')
    frames, at = [], 0
    with Image.open(source) as image:
        for i in range(image.n_frames):
            image.seek(i)
            ms = image.info.get('duration', 0)
            if ms <= 0:
                raise ValueError(f'Frame {i} has no positive duration; specify a reviewed timing repair first')
            frames.append({'frame': i, 'timeSec': at / 1000, 'durationMs': ms})
            at += ms
        width, height = image.size
    output.parent.mkdir(parents=True, exist_ok=True)
    # -n deliberately refuses to overwrite a previous conversion or the user's source.
    args = [str(ffmpeg), '-v', 'error', '-nostdin', '-n', '-ignore_loop', '1', '-i', str(source),
        '-vf', f'fps={fps},pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p',
        '-c:v', 'libx264', '-crf', '16', '-an', '-movflags', '+faststart', str(output)]
    subprocess.run(args, check=True)
    probe = json.loads(subprocess.check_output([str(ffprobe), '-v', 'error', '-show_entries',
        'format=duration:stream=width,height,avg_frame_rate,nb_frames', '-of', 'json', str(output)], text=True))
    video = probe['streams'][0]
    count = int(video['nb_frames'])
    if Fraction(video['avg_frame_rate']) != fps or abs(float(probe['format']['duration']) - at / 1000) > 1 / fps + .001:
        raise ValueError('Converted video does not preserve the expected duration/FPS')
    record = {'schemaVersion': 'lingchat.video-source.v1', 'sourcePath': str(source.resolve()),
        'sourceSha256': digest(source), 'width': width, 'height': height,
        'sourceFrameCount': len(frames), 'sourceDurationSec': at / 1000,
        'sourceFrames': frames, 'videoPath': str(output.resolve()), 'videoSha256': digest(output),
        'sampleRateHz': fps, 'frameCount': count, 'lastSampleTimeSec': (count - 1) / fps,
        'containerDurationSec': float(probe['format']['duration']),
        'conversion': args, 'note': 'CFR duplicates source frames; no optical-flow interpolation or extra motion information.',
        'sourceRights': 'user-provided; redistribution permission not established'}
    output.with_suffix('.source.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{len(frames)} source frames -> {count} CFR frames, {at / 1000}s; last sample {(count - 1) / fps}s')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('source', type=Path)
    p.add_argument('output', type=Path)
    p.add_argument('--fps', type=int, default=25)
    p.add_argument('--ffmpeg', type=Path, default=Path('D:/SoftWare/ffmpeg/bin/ffmpeg.exe'))
    p.add_argument('--ffprobe', type=Path, default=Path('D:/SoftWare/ffmpeg/bin/ffprobe.exe'))
    args = p.parse_args()
    prepare(args.source, args.output, args.fps, args.ffmpeg, args.ffprobe)
