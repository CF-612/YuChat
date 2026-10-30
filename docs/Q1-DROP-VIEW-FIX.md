# Editor 抓放与视角修复

用户试玩报告无法调整视角、提起后放回无效。原因：初版无相机输入；抓起默认升高 0.12 米，但核心接触高度只接受 0.04 米内；释放还沿用上一帧位置。旧检查用滚轮降高掩盖了直接松手的问题。

本批只改独立 XR 工程的 Editor 模拟输入、提示和回归检查。鼠标释放取当帧射线投影到桌面；MR 核心接触规则保持严格。抓取时增加绿/红落点圆圈，整个角色占地必须处于安全区内。中键拖动绕桌面旋转，未抓取时滚轮缩放，F 或按钮重置；抓取期间视角锁定。

## 复现与验证

关闭此 XR 工程的 Editor 后，在工作区运行：

```powershell
& ./tools/run-unity.ps1 -Mode Smoke -LogName unity6-drop-view-recheck.log
```

- 修复前 `evidence/unity6-drop-red.log`：无滚轮降高的抓放检查失败，记录 `mouse release without wheel lowering`。
- 修复后 `evidence/unity6-drop-view-green.log`：`XR_SMOKE_PASSED`。
- 覆盖直接释放、只在松手帧改变位置、越界恢复、UI 遮挡、落点提示状态、旋转缩放后抓放、抓取期间相机锁定、视角重置；保留模型、表情、十轮抓放和追踪失效恢复检查。
- 操作说明见根 README。实际试玩先确认中心，再右键身体拖动，绿色圆圈时松手；中键拖动旋转、滚轮缩放、F 复位。

验收边界：以上是 Unity 6000.4.0f1 图形 PlayMode 的模拟输入检查，不代表实体鼠标手感或用户视觉验收。Quest 尚未到货，未验证手追、真实桌面接触或 MR 性能。未改原 LingChat、资产转换、语音服务或配置。
