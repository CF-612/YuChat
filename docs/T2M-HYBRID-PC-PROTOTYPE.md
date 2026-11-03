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
