# Local video-motion tools

Run from the repository root. The approved Chinese specification and current progress are in `docs/VIDEO-MOTION-SPEC-2026-11-02.md` and `docs/VIDEO-MOTION-IMPLEMENTATION-2026-11-02.md`.

The preparation environment and large assets stay under ignored `.cache/video-motion/`. These scripts do not create a network inference service and do not alter the current avatar, physics or chat configuration.

## Preparation

The current local worker is `.cache/video-motion/worker311`, using Python 3.11.14 and read-only inherited PyTorch 2.6.0+cu124 / torchvision 0.21.0+cu124 from the existing voice-tool environment. GEM additions are installed only into the workspace venv. Save `pip freeze --all` after changes; inherited unrelated voice-package requirements are not GEM validation results. The earlier Python 3.12 `worker` and partial wheel downloads are retained but are not the current entry point. `face` is a separate experimental environment.

For a fresh installation, use a Python version supported by the reviewed checkout and a matching CUDA PyTorch / torchvision pair. The upstream install script assumes an existing ComfyUI PyTorch installation, so it is insufficient by itself on a fresh machine. ONNX Runtime must match the actual CUDA/cuDNN libraries; merely listing `CUDAExecutionProvider` is insufficient. This machine's CUDA 12.4 setup is pinned to **onnxruntime-gpu==1.20.2** / cuDNN 9, rather than CUDA 13 builds from ORT 1.27 onward. Actual detector inference and CUDA profile events passed with this combination.

```powershell
git clone --no-checkout https://github.com/lkevincc0/ComfyUI-GEM-X.git .cache/video-motion/ComfyUI-GEM-X
git -C .cache/video-motion/ComfyUI-GEM-X checkout -b codex/lingchat-video-motion 80755fbac89ba2f65ff846df249507138b622884
python tools/video-motion/prepare_upstream.py --checkout .cache/video-motion/ComfyUI-GEM-X --lock tools/video-motion/upstream-lock.json
python tools/video-motion/prepare_upstream.py --checkout .cache/video-motion/ComfyUI-GEM-X --lock tools/video-motion/upstream-lock.json --download onnx_keypoints --models .cache/video-motion/models
```

`python` here denotes the selected usable interpreter; on this machine the global PATH does not expose one. The current `worker311` venv was created from `D:/SoftWare/VoiceSpeechMaker-core-fork/env/python.exe` with `--system-site-packages`, leaving that base environment unchanged.

The lock requires the exact historical asset sizes and LFS SHA256 values. Never replace historical weights with renamed current assets. Large downloads may use hf-mirror with the **same revision and expected digest**, and retain completed chunks for restart. The original floating ModelScope fallback is disabled. YOLOX's first-download hash is measured separately because upstream supplied no expected hash.

## Read-only model report / input preparation

```powershell
python tools/video-motion/audit_model.py <model.vrm> --output <report.json>
python tools/video-motion/prepare_input.py <source.gif> <fresh-output.mp4> --fps 25
python -m unittest discover -s tools/video-motion -p test_video_motion.py -v
```

Input conversion requires Pillow plus FFmpeg/FFprobe (Windows defaults are in the script, overridable by arguments). It retains original frame durations, source hashes and the difference between last sample time and container duration. Do not interpret duplicated CFR frames as interpolated motion.

## Locked raw inference / export

After dependency installation and the verified download receipt exist:

```powershell
.cache/video-motion/worker311/Scripts/python.exe tools/video-motion/run_inference.py --checkout .cache/video-motion/ComfyUI-GEM-X --lock tools/video-motion/upstream-lock.json --models .cache/video-motion/models --video .cache/video-motion/input/reference.mp4 --output .cache/video-motion/runs/reference-01
```

Outputs include the original worker outputs, keypoint overlay, 77/78-joint BVH, and an experiment report. Failed runs are retained. No animation is automatically applied in Unity. The 78th BVH joint is an auxiliary root, not an ear/tail observation.

Once the YOLOX detector is available, check actual CUDA execution before full GEM weights finish downloading:

```powershell
.cache/video-motion/worker311/Scripts/python.exe tools/video-motion/probe_detector.py --model .cache/video-motion/models/yolox/yolox_x_8xb8-300e_humanart-a39d44ed.onnx --output <fresh-report.json>
```

This runs a blank image, records finite output and CUDA node events in the ORT profile. It checks runtime compatibility, not the quality of character detection.

## Unity baseline

```powershell
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/video-motion-2026-11-02/unity-baseline-02 -Method LingChatXR.Editor.VideoMotionBaselineProbe.Run
```

Use a fresh evidence directory for each attempt. This copies the project to the existing validation workspace and tests existing local VRMA playback. It does **not** validate generated GEM motion or invoke LLM/TTS services. Never run two Unity validation jobs against the same isolated project concurrently.

Set `XR_VIDEO_MODEL_PACKAGE` to an absolute local test-package folder to check only that package. The Eye-Tail5 TEST package currently resides in `.cache/video-motion/model-eye-tail5-test`; it is not registered as the user's default character.

Set `XR_VIDEO_MOTION_PACKAGE` to a generated body-package folder to validate its import, common source clock, hips vertical excursion, external root ownership, pause, cancellation and malformed metadata rejection. `XR_VIDEO_COMPARE_REFERENCE=1` also checks the old MMD/VRM package. `XR_VIDEO_CAPTURE_FRAMES=0` skips repeated screenshot capture during a technical regression. On this Windows host, give `TEMP` and `TMP` an existing workspace directory before launching Unity; the host's sandbox temp location can cause early native startup failure.

## Body VRMA package authoring (P1)

```powershell
.cache/video-motion/worker311/Scripts/python.exe tools/video-motion/export_body_vrma.py <metric-motion_77j.bvh> <fresh-package-folder> --provenance <source.json>
```

The source-name mapping and declared coordinate convention live in `soma-humanoid-map.json`, not in target-model patches. The exporter preserves collapsed-ancestor rotation, separates horizontal world-root translation and heading, retains local hips vertical excursion, and writes continuous xyzw quaternions. It emits standard body VRMA plus `metadata.json`, `root-trajectory.json` and an explicit empty `appendages.json` (`mode=none`). It does not produce face, finger or appendage observations.

`posePolicy=absolute` retains source pose. The optional `--pose-policy relativeToCurrentIdle` records a separate authoring mode that reuses the player's existing relative motion rule against current idle. This keeps motion changes while replacing the source initial pose; it must be described as an approximation, not an accurate source reconstruction.

The runtime entry is `XrMotionPlayer.PreviewPackage(folder)` in `XrMotionPackage.cs`. It is restricted to preview mode; candidate packages are not added to the automatic chat catalog. Validation checks SHA256, numeric samples, normalized quaternions, frame count/rate/timebase, and the external horizontal-root contract. Cached packages must agree on sample count/rate even when their durations coincide. Generated-package cache capacity is three; old model references are released with the existing avatar/player lifecycle.

Current selected reference is [BV14oa961ELQ, 28.5–31.5 seconds](https://www.bilibili.com/video/BV14oa961ELQ/), author Super_Pinkcat. Absolute reconstruction has an incorrect bowed pose, so fidelity is not accepted. The comparative relative draft resides in `.local/video-motion/packages/bili-gentle-relative-draft-01`; see `docs/VIDEO-MOTION-P0-P1-2026-11-02.md`. P2/P3 video appendage integration is paused by the original specification's body-quality gate.

## Optional face feasibility probe

```powershell
.cache/video-motion/face/Scripts/python.exe tools/video-motion/face_probe.py .cache/video-motion/input/reference.mp4 --model .cache/video-motion/models/face_landmarker.task --output <fresh-evidence-folder>
```

The tested stylized GIF produced zero detections with MediaPipe. Missing observations stay null. Candidate blendshape mappings, when any exist, require review and calibration before export; mouth animation must later arbitrate with TTS.

## Sources and licensing

GEM plugin code is Apache 2.0; its `LICENSE` and `ATTRIBUTIONS.md` are retained with the checkout and copied in `upstream-notices/`. GEM model weights, SOMA assets, Meta SAM 3D Body components, optional FBX SDK and character models each retain their own terms. The keypoints-only experiment does not download SAM3D weights. Do not distribute model files, reference video or derived assets merely because tool code is open source. The existing old MMD model rules and credits remain beside its local package.
