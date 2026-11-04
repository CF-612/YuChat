# V21 圆环材质 A/B 与截图同步纠正

## 已确认结果

用户要求同绘制帧比较原材质与 lilToon 的 alpha，并尝试保留震惊圆环、隐藏睫毛的原始 MToon 实例。

隔离测试三组均执行中立 → eyeCircleShock=1 → 清零复位：

| 组 | 震惊圆环 source/target alpha | 隐藏睫毛 source/target alpha | 截图 |
|---|---|---|---|
| A-auto，不显式同步 | 1 / 0 | 0 / 1 | 普通眼，复现旧截图 |
| A-sync，截图前 Synchronize | 1 / 1 | 0 / 0 | 震惊圈眼可见 |
| B-original，绑定原 MToon 实例绘制 | 原实例 1 | 原实例 0 | 震惊圈眼可见 |

复位后各组原圆环 alpha=0、原睫毛 alpha=1；A-sync 的目标材质同步一致，B-original 的绘制实例 ID 与源实例相同。A-sync 与 B-original 截图都恢复普通眼；不是像素逐字节相同验收。

观测到 4 个圆环白/棕材质及 2 个隐藏睫毛材质实际被 renderer 使用，其他配方材质不一定有可见 primitive 消费者。B 仅将这些槽替回表情系统原始材质实例，未克隆或重新导入；其他槽保持 lilToon。

## 对前批结论的纠正

本次所有手动截图的 Time.frameCount 都是 31628。隔离工具从 EditorApplication.update 驱动手动 Process/Camera.Render，编辑器回调推进不等于 Unity 游戏帧推进，不能假定桥接器 LateUpdate 已执行。两次等待实际帧的尝试停滞后，改为有界回调并记录 frame。

因此此前“原生 MToon 可见、lilToon 不可见”首先由**测试截图漏执行桥接同步**解释。显式调用已有 LilToonExpressionBridge.Synchronize 后原/目标 alpha 一致，onPreRender 到 after-render 仍保持，未发现这段绘制过程中被覆盖。没有证据证明转换算法无法表现震惊眼，也没有证据据此指认模型损坏。

本批没有证明正常 Play/Quest 中 LateUpdate 自动同步失效；要判断真实运行时问题，仍需正常 PlayerLoop 连续帧采样。B 组是用户指定的可还原诊断候选，未改为正式默认材质策略。没有修改 VRM、渲染队列、深度设置或材质 alpha 数据。

## 复现

只设置 `XR_V21_MATERIAL_AB=1`，运行：

`./tools/run-m1-check.ps1 -EvidenceDirectory evidence/v21-material-ab-2026-11-04-r3 -Method LingChatXR.Editor.V21ModelProbe.Run`

不要同时设置 XR_V21_BIND_AUDIT。

`material-ab.json` 保存 after-process、before-explicit-sync、after-sync-stage、onPreRender、after-render 的材质实例 ID、alpha、shader、队列和帧号。九张 PNG 为三组各三状态；原始截图与数据本地保存。r1/r2 是未完成的帧等待尝试，不能作为通过证据。

未打包；未上传模型、控制包或原始顶点。只同步诊断代码和报告。
