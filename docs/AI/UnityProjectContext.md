# LingChat XR Unity 工程上下文

2026-10-29，本机源码与 Unity 6 PlayMode 已核。

- 工程根：`D:/GameCreator/Document/DesktopSmallBlueFish/LingChat-XR`；当前工作区起初为空，没有继承旧项目。
- 编辑器：Unity 6000.4.0f1 (8cf496087c8f)，`D:/GameCreator/Unity/Editor/Unity.exe`；用户要求使用 Unity 6。
- 渲染：Built-in RP。UniVRM 0.131.4（Git efd20f62f1bf0ac3fcc34de1e497324c5197d146）运行时导入；必要传递依赖见 `Packages/packages-lock.json`。
- 输入：legacy Input 鼠标模拟；`FeiyuPreview.HandlePointer` 接收模拟射线意图，尚无 XR 输入/Meta/OpenXR 包。
- 核心：`Assets/LingChatXR/Core` 纯 C#，noEngineReferences；唯一空间状态/所有者/generation。`Runtime` 负责模型适配、碰撞代理、显示、语音；`Editor` 负责场景构建和检查。无DI框架、多人或网络状态同步。
- 场景：`Assets/Scenes/FeiyuPreview.unity`。运行时创建角色空间根，读取 `StreamingAssets/Characters/blue-placeholder/manifest.json` 和 VRM 副本。
- 配置：XR persistentDataPath，按模型 SHA 独立保存；不恢复旧物理空间。只读导入工具位于 `tools/character-import`，原始配置记录在 `.cache`，不打入 APK。
- 原项目边界：LingChat 工作树/配置/资源只读；DOT 新模型不阻断占位框架；不重转旧鱼。暂未支持自然走路，不能平移静止姿势冒称完成。
- 语音：用户允许临时 LingChat 默认日语；其现有 default 是进程内 localsbv2api，不是已运行的HTTP端口。适配器默认静音，保留 SBV2 API 和 my-neuro 中文契约。
- 测试：纯核心183断言、Node导入器测试、Unity真模型/模拟射线/抓放/表达Smoke。实体鼠标、自然度、Quest真机/性能、真实TTS未验。
- MCP：当前无Unity MCP桥，也未安装；文件和官方命令行完成本批。无需为继续源码工作额外安装桥。
- 已知Editor环境处理：本工程关闭启动搜索索引以避开6.4空项目SearchDatabase异常；不改全局配置。失败证据与最终通过分开留存。
- 当前无Git提交/PR；独立工程文件及证据均在当前工作区。详细范围和失败史见 `docs/Q1-EDITOR-BATCH-2026-10-29.md`。

## 统筹审阅入口（2026-10-30）

最新用户优先级：先接不依赖模型的本地 TTS、真实聊天、翻译对话框和播放/停止；新 VRM 即将到位，物理调校等待新模型。见 [基础施工与验收规格](../XR-FOUNDATION-BATCH-2026-10-30.md)。复杂表情/生成动作及旧 Quest 优先排期后置；规格已准备，本轮未改实现或发出跨对话任务。

[统筹接手、复用对照与下一批](../XR-COORDINATION-2026-10-30.md)记录后续状态。统筹独立复跑核心 183 断言和导入器 12 项均通过；首批之后已有鼠标落点/视角修复源码及红绿 Smoke 日志，用户重新试玩与报告同步仍待收口。该修复不等于 Quest 交付。

源码结构已核：Runtime 引用 Core；Editor 引用 Runtime、Core 和 VRM10；Core 无 Unity 引用，Editor 仅编辑器平台。启用构建场景为 `Assets/Scenes/FeiyuPreview.unity`；Unity Test Framework 为传递依赖，当前主要证据来自外部核心/导入检查及自定义 PlayMode Smoke。当前会话没有可用 Unity MCP 工具；本轮未操作 Editor、改包或构建。
