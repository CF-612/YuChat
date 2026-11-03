"""只读复用 LingChat 原音效/思考气泡到本机资源，不将素材公开提交。"""
from pathlib import Path
import hashlib, json
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path('D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp/public')
OUT = ROOT / 'LingChat-XR/Assets/LingChatXR/Resources/LocalLingChatEffects'
OUT.mkdir(parents=True, exist_ok=True)
pet = (SOURCE/'audio/pet.mp3').read_bytes()
(OUT/'pet.mp3').write_bytes(pet)
sources = {'pet.mp3': hashlib.sha256(pet).hexdigest()}
for name, target in [('无语.wav','thinking.wav'), ('对话.wav','typing.wav')]:
    data = (SOURCE/'audio_effects'/name).read_bytes()
    (OUT/target).write_bytes(data)
    sources[name] = hashlib.sha256(data).hexdigest()
path = SOURCE/'pictures/animation/AI思考.webp'
sources['AI思考.webp'] = hashlib.sha256(path.read_bytes()).hexdigest()
im = Image.open(path)
frames, durations, union = [], [], None
for i in range(im.n_frames):
    im.seek(i); frame = im.convert('RGBA'); frames.append(frame)
    durations.append(im.info.get('duration', 17))
    box = frame.getbbox()
    if box: union = box if union is None else (min(union[0],box[0]),min(union[1],box[1]),max(union[2],box[2]),max(union[3],box[3]))
folder = OUT/'Thinking'; folder.mkdir(exist_ok=True)
for i, frame in enumerate(frames):
    cropped = frame.crop(union)  # 保留原始有效画幅，不先缩小再被 Unity 拉伸。
    cropped.save(folder/f'{i:03}.png')
(OUT/'thinking-frames.json').write_text(json.dumps({'durations':durations,'width':union[2]-union[0],'height':union[3]-union[1]}),encoding='utf-8')
evidence = ROOT/'evidence/experience-repair';evidence.mkdir(parents=True,exist_ok=True)
(evidence/'effect-sources.json').write_text(json.dumps({'sources':sources,'frameCount':len(frames),'crop':union,'sourceUnmodified':True},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Prepared original effects and {len(frames)} thinking frames; originals unchanged.')
