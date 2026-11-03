# PC 混合动作原型

独立实验入口复用现有 UniVRM 播放器，目标角色从当前待机进入生成动作。抓取会使当前动作和未提交请求失效。落稳恢复待机后可以接受新请求。本入口只编入 Editor。

## 运行

1. 启动固定版本 `morelle/T2M-DLC2`（revision `fc4221d11a9c31598b15feece5ab015e41d4f373`）的本地 web.py 服务。默认端口 17880。使用其他端口时，在启动编辑器前设置 `XR_T2M_PORT`。
2. Windows 工程使用现有 `.cache/video-motion/worker311/Scripts/python.exe`（NumPy、SciPy）。在 Unity 6 的预览场景进入 Play Mode，放置已准备好的本地角色并等待落稳。
3. 选 **YuChat → 动作实验 → 生成右手挥手**。可以在等待期间抓起角色，或在动作播放中抓取。放下后可以再次发起。每轮请求结果写在项目私有缓存 `.cache/my-neuro-prototype/manual-*/result.json`。记录的是本地 HTTP 请求耗时，包含服务推理和传输，不含冷启动及 UniVRM 导入。

命令行可将推理和转换分开复现；使用新输出目录以区分重复实验：

```powershell
$py='.cache/video-motion/worker311/Scripts/python.exe'
& $py tools/video-motion/capture_t2m.py 'A person raises the right hand and waves.' .cache/t2m-run --seed 42
& $py tools/video-motion/convert_t2m.py .cache/t2m-run/motion.json .cache/t2m-run/package
$env:XR_HYBRID_PACKAGE=(Resolve-Path .cache/t2m-run/package).Path
$env:XR_HYBRID_CAPTURE='1'
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/t2m-hand-demo -Method LingChatXR.Editor.QuestHandsProbe.Run
```

模拟手演示会依次记录生成动作、抓取打断、放下后失败回退、重新接管。若要逐帧渲染原始生成序列，设置 `XR_VIDEO_MOTION_PACKAGE` 为相同包目录，设置 `XR_VIDEO_MODEL_PACKAGE=local-smallbluefish-alpha2`，运行 `LingChatXR.Editor.VideoMotionBaselineProbe.Run`。

## 验证边界

固定根策略保留局部髋部起伏，角色世界位置由空间 Core 管理。22 关节位置不能观测手指、前臂扭转和面部，动作包只输出身体姿势。合成夹具用于验证状态切换和坏数据拒绝，不能用来报告模型延迟、语义命中或 Q 版动作自然度。

## 2026-11-03 真实权重实测

使用固定 ZIP（4,565,157,207 字节；SHA-256 ad3f083b5b50f28194feaee5c28400716bf2c5115c77265c47acce540bdfb9fa）。本机 RTX 4060 Laptop 8 GB，Python 3.11、PyTorch 2.6.0+cu124、torchvision 0.21.0+cu124，包内 ftfy 和 wcwidth。包附带的 torch 2.5.1+cpu 在本机载入 torch_python.dll 时出现 WinError 126。可从项目根运行 `./tools/video-motion/start-t2m-prototype.ps1` 启动此次验证的 CUDA 路线。

| 动作意图 / 种子 | 服务内生成 | 本机请求至接收 | 输出 |
|---|---:|---:|---:|
| 举右手轻挥后放下 / 42 | 1.125 s | 1.138 s | 100 帧，5.0 s |
| 举右手挥两次 / 43 | 0.438 s | 0.455 s | 128 帧，6.4 s |
| 同上 / 44 | 0.281 s | 0.302 s | 80 帧，4.0 s |
| 双臂向两侧展开再放下 / 45 | 0.840 s | 0.852 s | 108 帧，5.4 s |

这四条来自真实采样，种子 42、45 分别为两次服务启动后的首条请求。计时不含冷启动与模型导入。所有序列为 20 Hz。服务未启动时另实测回环请求被拒绝；0.8 秒级忙时延迟与实际多人排队未作测量。

固定真实种子 42 的结果进入双角色 Quest 模拟手路径。已有姿态切入生成、抓取后打断、在手中释放延迟返回的旧任务、放下后失败保持待机和新任务重新接管均通过。晚到时机与失败由夹具控制，以便稳定复现。视频与模拟 XR 场景均由 Unity 6 在 PC 生成。真实服务生成与上述夹具分段验证，未宣称头显联网端到端通过。

视觉质量暂未通过。种子 42 的源右腕最高仍低于右肩约 1.1 cm；种子 44 源右腕高于肩约 49.7 cm，重定向到 24 cm 小肥鱼后实测最高约 3.5 cm，正面视角挥手轮廓容易被头发和头部遮挡。种子 45 虽要求双臂平展，源躯干最大倾角已达约 76°，小肥鱼也出现明显弯腰。后一问题在生成源中就能观察到。保留现有挥手作为确定性素材，正式演出接入范围待姿势可读性复验后确定。

本机演示文件（相对项目根）：

- `evidence/t2m-real-wave44/local-smallbluefish-alpha2/wave.mp4`：第二个挥手提示词的真实结果。
- `evidence/t2m-real-grab/hybrid-stages.png` 和同目录 `checks.json`：生成、提起、落稳回退和再次接管。
- `evidence/t2m-wide-real/local-smallbluefish-alpha2/wide.mp4`：张臂指令出现明显弯腰的失败案例。
- `.cache/my-neuro-prototype/`：私有模型包、原始响应、每次推理计时以及导出的候选 VRMA。
