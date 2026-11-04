# 占位模型加载失败与设置恢复

本批继续通用加载生命周期，不修改 VRM、物理算法、表情配方、动作或语音播放，不打包。

## 修复

- 预先取消的切换在修改加载代次、清理角色和停止会话之前返回，现有角色与平台保持有效。
- 单独记录最近一次有效切换目标。失败或取消后重试同一目标，不悄悄回到上一个成功角色；成功选择才进入原有保存流程。
- 每模型校准文件无法读取/解析时保留原文件，给出日志诊断，本次使用默认校准。保存仍通过原来的合并写入保护，损坏原件不会被默认值覆盖。

## 复现与结果

```powershell
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/placeholder-load-recovery-2026-11-04 -Method LingChatXR.Editor.PlaceholderRegressionProbe.Run
```

隔离 Unity 6 Play Mode 35 项通过。包括之前的放置、触摸、抓取、多帧下降、大小确认/取消与连续加载，以及新增取消不清场、损坏设置不覆盖、缺资源失败、重试仍失败、补齐资源后原入口恢复。补齐资源使用隔离副本中的已有占位 VRM；未触碰源项目模型或用户设置。

本地证据：`evidence/placeholder-load-recovery-2026-11-04/checks.json`、`M1-20261104-211226.log`。下降记录为真实 Play Mode 多帧采样，但不是 Quest 视觉或自然度验收。未操作用户 Unity 窗口、未重启用户服务。
