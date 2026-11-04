# 第二批：碰撞体错位修复与发束边界

接续 2692173。复用 UniVRM 的骨骼局部碰撞体与原生求解，没有新增布料引擎。VRM 文件、网格和权重不变；不打包。

## 已确认并修复

当前 VRM 的头部碰撞体 Offset.y=-0.837、髋部=-0.414 等局部偏移抵消了骨骼高度。源码核对：Vrm10Importer 将 collider 挂到指定骨骼，Offset 以该骨骼局部坐标处理。Unity 实际运行进一步确认：原 8 个碰撞球中心的世界 Y 均接近 0，而髋、胸、颈、头部骨骼约位于 0.07、0.11、0.13、0.15 m。这些球没有处在应保护的身体区域。

角色包 springPhysics 增加可选碰撞校准：组序号、组内序号、节点校验、局部偏移和半径。参数放在现有 JSON，不在运行时识别角色或猜骨名。校准后中心位于髋/腿约 0.07–0.08、胸 0.11、颈 0.13、头部约 0.17 m。头球半径和中心是当前角色的候选覆盖范围，位置修正已确认，不表示最优覆盖已验收。

裙摆绑定额外检查上身/头部碰撞组，避免仅检查髋腿。保留原碰撞组和作者角限。配置切换时按实际变化重建一次；普通逐帧更新不改碰撞结构。移除配置时恢复加载时记录的 Offset、Radius 和链碰撞组。不会写回 VRM。

## 发束试验

试过“根部松、末端保形”的候选（root stiffness 0.35、tip 2、gravity 0.95、root angle 145、tip 15）。它能下垂，但画面仍出现发片拉成长条，因此已撤回；交付保留上一批保守发束参数，加上本次正确位置的碰撞体。

只读权重盘点发现侧发主材质的末端骨骼影响中，左侧 1562 个顶点有 1149 个接近单骨刚性权重，右侧 1453 个中有 1085 个；内壁侧发末端还存在 94/94 和 92/92 个近似刚性权重点。它们是需要检查的大角度变形风险，不凭这一统计直接断言作者权重错误。确切的可变形范围、发片内壁和相邻链间的权重连续性仍需结合大角度姿态检查。不能继续只增大重力掩盖问题。

原始只读盘点：evidence/spring-followup-2026-11-04/asset-audit.json。失败大角度候选：evidence/spring-collider-2026-11-04-root-swing。不要把该候选用于交付。

## 证据与边界

最终证据：evidence/spring-collider-2026-11-04-delivery/comparison.json 与图片。

- 18 组：正立/倒挂 × 12/24/45 cm × 30/60/90 Hz。抓取移动、下落、回稳中无非有限关节坐标，结构/碰撞重建计数无增长，残差满足角色身高 1% 以内的断言。
- 额外实际推进 720 帧/12 秒倒挂扑腾动作及 UniVRM 模拟，碰撞重建次数 0；每 2 秒保存一张关键帧，实际抽查第 120、480 帧。
- 已执行恢复配置分支并断言材质绑定清空。源码恢复碰撞属性与原组；未把这项断言夸大成逐一验证所有恢复后像素。
- 查看正立及倒挂候选画面，但没有全角度、连续视频或 Quest 自然度验收。裙层间无自碰撞，发束仍不能保证完全下垂不硬折。

## 使用和回退

本机已应用保守发束加碰撞校准，重新进入 Play Mode 载入模型生效。

```powershell
# 应用本批配置
./tools/prepare-spring-materials.ps1
# 仅回到上一批材质配置，撤掉本批碰撞校准
./tools/prepare-spring-materials.ps1 -Profile ./tools/physics-profiles/smallbluefish-spring-materials-v1.json
# 回到加入物理材质前的默认行为
./tools/prepare-spring-materials.ps1 -RestoreDefaults
```

复验：`./tools/run-m1-check.ps1 -EvidenceDirectory evidence/spring-collider-check -Method LingChatXR.Editor.InvertedPhysicsProbe.Run`。不要在用户打开隔离验证工程时运行复制脚本。当前用户主工程不由本批关闭或操作。
