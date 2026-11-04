# 临时占位模型与基础回归（2026-11-04）

用户要求暂停待大修模型，改用本地另一个占位继续测试。复用 local-physics-reference 中的 BlueBigFatFish VRM（14,061,476 字节），SHA256 733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6；未重做转换或复制到 V21 包内。

Characters/index.json 增加可选 startupOverride=local-physics-reference，普通桌面/Quest 共用启动入口在读取旧模型选择后应用该临时策略。保留原 xr-application.json 与各 SHA 的设置文件；明确 ConfigureCharacter 的定向测试仍可以选择其他包。未删除/覆盖 V21。删除 startupOverride 字段即可恢复用户原来的启动选择。

隔离 Unity 6 实际加载占位模型，27 项通过：旧选择保留、通用能力回退、UniVRM 物理、放置、触摸、持有者仲裁、多帧下落、旧输入失效、缩放取消/确认且不重载、重新放置、连续重载只采纳最新请求，以及无效启动配置拒绝。证据 evidence/placeholder-regression-2026-11-04-final/checks.json 包含实际帧间下降采样。

独立 Core 2698 项断言通过。原 net8.0 检查因本机缺指定 8.0.22 引用包失败；用 .cache/corecheck-net10 临时工程引用完全相同的 Core 源文件和 Program.cs，在本机已安装 .NET 10 上完成。原 csproj 目标未变。这不是 Unity 运行或 Quest 验收的替代。

复现 Unity：`./tools/run-m1-check.ps1 -EvidenceDirectory evidence/placeholder-regression-2026-11-04-final -Method LingChatXR.Editor.PlaceholderRegressionProbe.Run`。

本轮不测 V21 专属眼型/尾巴绑定，不打包、不操作用户编辑器，不声称手追、真实桌面对齐、下落观感或性能已通过。后续继续使用该占位验证非模型依赖的配置、会话和加载生命周期。
