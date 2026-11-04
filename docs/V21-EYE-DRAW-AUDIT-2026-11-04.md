# V21 震惊眼绘制帧检查

本批只增加隔离检查，没有修改 VRM、运行时渲染参数或用户设置，没有打包。

## 权重覆盖检查

`eyeCircleShock` 包含 27 条 morph 绑定。单独触发、额外输入 blink=1 并移动 LookAtTarget、执行现有提起 ComposeFace 三个阶段中，全部绑定的实际 Unity 权重均为预期 100；无 mismatch。头部 blink / SpecialClosed_Correction03、两侧 AVolumeEye 与 ScleraSkinFeather 的 blink 均包含在内。本有限检查未发现眨眼或视线把权重清零，不代表所有交互时序已遍历。

## 绘制时采样

在 Camera.onPreRender 对实际 renderer 执行 BakeMesh，导出所选子网格全部顶点索引和世界坐标，并读取材质属性。分别跑原生 MToon 与正式 AnimePresentation.Apply 后的 lilToon；表情 Process 后跨两个编辑器更新，再绘制截图。这里是绘制回调中的 CPU 蒙皮结果，不是 GPU 顶点缓冲或逐像素深度回读。

TrueLidSkin_positive_x 是 donor_tripo_part_1 的 submesh 1（零基），并非独立对象。

- 眼皮 1,931 个顶点，1,837 个相对中立位移超过 1 微米，最大世界位移 1.952 毫米（24cm 角色）。
- 正侧眼白 6,522 个顶点全部变化，反侧 4,188 个全部变化；最大位移均 1.484 毫米。
- 过渡层各带全部采样顶点变化，最大约 1.33–1.47 毫米。
- 两种 shader 路径采样的形变统计相同；绘制回调时相关形态键仍为 100。

## 正式 lilToon 材质结果

| 层 | ZTest | ZWrite | 队列 | 混合 | alpha |
|---|---|---|---|---|---|
| TrueLidSkin_positive_x | LEqual (4) | 1 | 2000 | One / Zero | 1 |
| 两侧眼白 | LEqual (4) | 1 | 2000 | One / Zero | 1 |
| 两侧过渡层四带 | LEqual (4) | 0 | 2999 | One / OneMinusSrcAlpha (10) | .957 / .684 / .316 / .043 |

全部 sortingLayer=0、sortingOrder=0，双面绘制。过渡层使用 Hidden/lilToonTransparent，RenderType 标签为 TransparentCutout；实际有透明混合且不写深度，不能仅凭标签把它当作不透明裁切。眼皮和眼白同为不透明队列，遮挡主要依赖深度测试；没有采集 GPU draw-call trace，不能宣称同队列内部提交先后已确认。过渡层在队列上晚于不透明层。

原生 MToon 眼皮/眼白同为 2000、写深度，过渡层 2999、不写深度；源码主 Pass 的 ZTest 固定 LEqual。

## 观察与尚未证明的部分

原生 MToon 的震惊截图可见白色圈眼，正式 lilToon 截图仍出现普通眼睛。CPU 形变一致而两条材质路径画面不同，说明还必须核对特殊眼层的透明/显隐材质映射与 GPU 最终遮挡，不能将问题归为“没有调用完整 eyeCircleShock”或直接认定眼皮模型损坏。上一轮给用户的静态截图只经过原生 MToon，应以本批正式渲染对照补充其边界。

相机深度区间：眼皮距离约 .6411–.6458m，正侧眼白 .6425–.6480m，过渡层约 .6425–.6453m。区间重叠只能说明需要逐像素核对，不能据此判定整层谁挡住谁。

## 复现与证据

设置 `XR_V21_BIND_AUDIT=1`，执行 `tools/run-m1-check.ps1 -Method LingChatXR.Editor.V21ModelProbe.Run` 可复现权重检查。

绘制检查另起进程，只设置 `XR_V21_DRAW_AUDIT=1`；正式渲染再加 `XR_V21_LILTOON=1`。不要同时保留 BIND_AUDIT，否则先进入权重检查分支。

- `evidence/v21-shock-binding-2026-11-04/binding-audit.json`
- `evidence/v21-eye-draw-2026-11-04/`：原生 MToon，neutral/shock 的 draw.json、vertices.json 和截图。
- `evidence/v21-eye-liltoon-draw-2026-11-04/`：正式 lilToon 同类证据。

两次绘制检查与权重检查均运行完成。原始顶点与模型画面仅本地保存，提交仅包含检查代码与本文。
