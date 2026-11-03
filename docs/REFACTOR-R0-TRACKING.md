# R0 第一批：空间事件基线

施工指南与本机施工起点均为 YuChat 5de5999。Unity 6000.4.0f1。包锁 SHA-256 为 0934A45A153A71DAC9EB8C1B1FDAB48DFCB3DC925A2F65730275DEA25E37E84C；现有 GitHub 预发布 APK 为 DC79C7775471D0E8F8092F2AE37F4DFB312DEB95798804C5EF1855939F73FB39，该 APK 早于停留选桌面的源码。

复现：在已准备本地模型与动作的工程根目录执行以下命令。

```powershell
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/audit-r0-tracking -Method LingChatXR.Editor.QuestHandsProbe.Run
```

隔离 Unity 回归通过。测试从已锁定桌面出发，分别调用 QuestEntry 实际注册的原点更新与头部失追回调，校验桌面仍锁定。显式 Suspend 清除桌面另行断言。模拟回调不能证明物理桌面经过真实 recenter 后仍然对齐。此批仅覆盖 R0 的空间入口；持续姿势与旧请求竞态、语音失败和后续 R1–R3 实施仍待推进。
