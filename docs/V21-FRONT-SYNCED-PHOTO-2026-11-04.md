# 已同步正面震惊眼同帧截图

按用户要求仅重拍同机位并比较材质路径，不重复权重或 CPU 蒙皮检查。

## 结果

圆环外侧靠近头发的白片在 **lilToon 与原生 MToon 两张图中均可见**，位置和轮廓相近。明暗及圆环颜色随 shader 改变，不能把这些色调差别当作几何消失。

截图前已执行 `LilToonExpressionBridge.Synchronize()`，实际使用的四个白/棕圆环材质 source/target alpha 均为 1；两侧隐藏睫毛 source/target alpha 均为 0。

两次手动 Camera.Render 在同一个 Unity 帧 30303 内完成，没有在中间推进动画、物理或再次 Process。只将 renderer 全套材质从映射后的 lilToon 切回表情绑定的原 MToon 实例，排除跨次加载姿态差异。

结论边界：白片仍存在，不能归因于漏同步或 lilToon 独有问题。本轮没有定位具体是哪一层贡献白片，不直接宣称模型损坏，也不更改材质透明度或深度设置遮盖问题。

## 机位与文件

- 相机位置 `(0, 0.14, 0.65)`，LookAt `(0, 0.13, 0)`，正交尺寸 0.15，640×720；与前批机位一致。
- `evidence/v21-front-photo-2026-11-04/front-shock-liltoon-synced.png`
- `evidence/v21-front-photo-2026-11-04/front-shock-mtoon.png`
- 同目录 `front-photo.json` 记录相机、帧号与 alpha。

复现：只设置 `XR_V21_MATERIAL_AB=1` 和 `XR_V21_FRONT_PHOTO=1`，运行 `tools/run-m1-check.ps1 -EvidenceDirectory evidence/v21-front-photo-2026-11-04 -Method LingChatXR.Editor.V21ModelProbe.Run`。

隔离运行通过。只更新检查工具和说明；未改正式渲染默认策略，未改 VRM，未打 APK。原始截图仅本地保留。
