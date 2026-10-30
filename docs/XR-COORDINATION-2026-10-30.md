# 小肥鱼 / LingChat XR：统筹接手、交付审阅与下一批

更新：2026-10-30，Asia/Shanghai。审阅覆盖 10 月 29 日首批及随后落盘的操作修复。本文件由 XR 统筹维护；施工结果仍由原施工对话写入各批报告。技术检查、实际操作与用户观感分别验收。

> **当前优先级已修正：先做本地 TTS、真实聊天和翻译对话框；物理效果等新 VRM。** 下一批以 [基础施工与验收规格 F2](XR-FOUNDATION-BATCH-2026-10-30.md) 为准，无模型也要能完成双语显示、真实合成和播放/停止。新模型到位后再做缓降、惯性/碰撞校准及角色互动接线；旧鱼和成果全部保留。复杂表情/Agent/生成动作延期，下文旧 Quest 优先排期仅保留历史依据。规格已落盘，本轮未向原施工对话发送任务或修改实现。已读刚完成的 LingChat 10 月 30 日汇总交接及翻译/响应源码；三个源触摸脚本 14/14 通过，不能代替 XR/真实服务验收。

## 1. 分工与固定边界

- 现有施工对话：**交接并推进小肥鱼 XR MR**，ID `01a187a3-e8c8-7780-a22f-7fa7d012fcf4`。继续负责代码、场景、构建及修复。
- 统筹对话：**统筹 LingChat XR 项目交付**，ID `01a187ef-1cec-7a90-afbc-883f7957f89f`。负责来源核对、交付审阅、批次范围和验收结论。本轮没有接管实现或另开工程。
- 工程固定在 `D:/GameCreator/Document/DesktopSmallBlueFish/LingChat-XR`。保留旧蓝色大肥鱼、全部成果、原失败日志及回退资产；等待 DOT 后新增模型包和独立校准，不覆盖旧鱼。
- 兼容 LingChat 角色格式。沿用现有导入器和 manifest v1，分别记录原值保留、可迁移数据、实际运行能力。LingChat 工作树、用户角色和设置只读参考。
- 当前仅 Editor 小样；Quest、实际语音、自然行走、DOT 替换均未完成。临时日语路径继续有效，my-neuro 中文接口保留；没有已验证的独立 TTS 服务。

固定来源：[XR 交接](D:/方的世界/LingChat-3D-prototype/LINGCHAT-XR-FEIYU-MR-HANDOFF-2026-10-29.md)、[共享能力索引](D:/方的世界/LingChat-3D-prototype/LINGCHAT-XR-SHARED-CAPABILITIES.md)、[Q1 施工报告](Q1-EDITOR-BATCH-2026-10-29.md)、[角色契约](character-compatibility.md)、[Unity 工程上下文](AI/UnityProjectContext.md)。新批次还要查看两侧报告目录，不能把这些日期入口当成永远最新。

## 2. 本次审阅结论

**保留 Q1 Editor 技术底座；Q1 整体及用户操作验收仍开放。** 用户在施工对话反馈“无法调整视角，无论怎么提起，提起到哪儿都是落点无效”，优先级高于先前自动测试的体验推断。

| 项目 | 本次实际证据 | 结论与边界 |
| --- | --- | --- |
| Unity 基线 | ProjectVersion、manifest、lock、GraphicsSettings、三份 asmdef、Build Settings 已读 | Unity 6000.4.0f1 / 8cf496087c8f；Built-in RP；UniVRM 0.131.4 / `efd20f62f1bf0ac3fcc34de1e497324c5197d146`；legacy Input。尚无 OpenXR/Meta 包 |
| 模型保留 | 重新计算源 VRM 与包内 model.vrm 的 SHA256，均为 `733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6` | 占位副本一致；未重转或替换。原规约随包保留 |
| 空间核心 | 统筹复跑 `tools/CoreChecks/run-checks.ps1`，C#9 / .NET Standard 2.1 编译和 183 条断言通过 | 放置、足迹、十次抓放、owner/generation、失效空间等纯逻辑通过；不证明实体鼠标或真机手感 |
| 角色导入 | 统筹复跑完整 `importer.test.mjs`，12/12 通过，约 260 秒 | 合成夹具验证 YAML、未知数据记录、路径、SHA、重复导入、校准失效与资产规约；未证明任意角色完整运行 |
| 首批 Unity | 已查 `unity6-smoke-attempt5.log` 的 `XR_SMOKE_PASSED` 与 smoke JSON；查看过首批实际相机图片 | 施工方程序化 PlayMode 证据，统筹没有重新启动 Editor；JSON 明示实体鼠标、Quest、用户观感未验 |
| 用户操作修复 | 当前 `FeiyuPreview.cs` 已加入中键视角、缩放、落点提示、释放帧取点并投到桌面；`PreviewSmoke.cs` 已加相应用例；`unity6-drop-red.log` 失败、`unity6-drop-view-green.log` 通过 | 修复已落盘，有程序化回归证据；仍需用户复玩及施工报告更新。旧 README 的“必须滚轮降低后放回”尚未同步，不能据旧说明判定新行为 |
| Android 环境 | 读取环境报告和已有证据入口 | 工具链准备已有记录；未由本轮重新安装或重跑工具链，未产生 APK、安装或真机证据 |

本轮复跑日志位于 [core-checks.txt](../evidence/review-2026-10-29/core-checks.txt) 与 [importer-tests.txt](../evidence/review-2026-10-29/importer-tests.txt)。首轮文件指纹见 [source-baseline.json](../evidence/review-2026-10-29/source-baseline.json)，审阅中差异见 [source-recheck.json](../evidence/review-2026-10-29/source-recheck.json)，交付前刷新见 [source-final-2026-10-30.json](../evidence/review-2026-10-29/source-final-2026-10-30.json)。这些是本次检查版本，不是冻结工作树或后续自动采用旧版的指令。

## 3. LingChat 来源核对与复用对照

LingChat 根目录：`D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp`。实际只读 Git 查询得到 HEAD `0478d5dd010aaad3c33f1ff89ff81b0593928b58`，相关文件有未提交修改和未跟踪文件。XR 当前没有本地 `.git`，以文件 SHA256 记录版本。

已阅读最新落盘的 [交互报告](D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp/docs/vrm/INTERACTION-STAGE1-2026-10-29.md)，顶部结论时间为 **10 月 29 日 21:14**。报告确认用户看见眼睛运动、恢复延迟头颈辅助，头眼自然度仍待验；先前摸头和两段摩根短音的接受只覆盖对应角色与场景。

**报告晚于旧交接，但部分源码又晚于报告。** 共享索引中的 pointer-attention 指纹以 `2e5c7ce3` 开头，本次读取版本以 `01a3d7d6` 开头；VrmStage、vrm-runtime 和输入测试在审阅窗口内继续变化。还发现 head-touch-session、pending-head-touch、touch-sway、touch-regions、touch-reply-policy、touch-sound 及对应测试入口。它们涉及持续接触、部位、局部摆动、短音与待处理语义，属于正在施工的候选能力；本轮只做范围盘点，不把它们当作已验收交付。

本轮读了对应源码关键路径和测试用例，并核对历史检查日志：session-checks 为 9 项、checks-final 为 11 项通过；报告另记更晚组合 21 项通过。本轮未运行 LingChat、未重跑其测试、未调用聊天或 TTS，旧日志不能证明变化后的源码通过。表现类派工前必须重新取得匹配版本的报告与验证。

下表源码路径相对 LingChat 根；XR 路径相对本工作区。完整 SHA256 均在上述 JSON。**本次只采用复用方向与验收约束，没有移植源代码。**

| 能力 / 来源 | XR 现状与缺口 | 采用方式 / 不采用项 | 目标验证与本次状态 |
| --- | --- | --- | --- |
| 头眼：`src/components/game/vrm/pointer-attention.ts`；`scripts/check-pointer-relative-pose.mjs` | `FeiyuAvatar.cs` 有限幅视线，但 headRest 来自加载时，未实现每帧作者姿态上的增量恢复与动作让位 | 复用相对姿态、先眼后头、残差限幅及恢复算法，改为 Unity 骨骼执行与头/手目标输入；旧鱼/DOT 各自验证真实可见瞳孔，不复制摩根 iris 名或毫米参数 | Unity 验证侧头不被拉正、变化中的作者姿态无累计漂移、快速反向、动作占用、可见瞳孔。待适配；源自然度未最终接受 |
| 输入与触摸：`VrmStage.vue`、`vrm-runtime.ts`；`check-vrm-pointer-input.mjs`；新增 touch 模块 | `FeiyuPreview.cs` 直接测头/身体代理；Core 已有触摸、抓取优先及失效处理，但代理命中不证明真实表面/遮挡正确 | 保留 XR 状态机；对照有效接触、结束/取消、UI 排除与去重。手追碰撞及抓取由 XR 实现；新增持续触摸语义等源批次收口后选择性移植 | 空白、UI、遮挡、离开、重入、取消、旧输入失效。源文件变化中，需重对照；本批不扩成全身触摸任务 |
| 会话视线：`pointer-gaze-session.ts`；`check-pointer-gaze-session.mjs` | XR 当前没有对应会话开关工作流 | 未来新增开关时复用“未设置/显式关闭/持久偏好变化”的语义，不移植 sessionStorage | 仅在安排开关时验禁用、重入、保存变化；本批无需新增一套设置系统 |
| 表情：`face-channel-composer.ts` 及 runtime；`check-face-channel-composer.mjs` | XR happyBase/blinkBase 在加载时采样，当前仅 happy/blink；触摸释放不能宣称恢复动态聊天情绪 | 复用当前基础脸与临时效果分离、明确通道所有者、口型/眨眼独立的语义；保留单执行器。复杂作者通道按角色能力逐步接 | 触摸中情绪改变，释放回当前情绪；口型与眨眼不中断。待适配，不能以“VRM 表情可显示”代替完整兼容 |
| 音频：`src/composables/role/useVoicePlayback.ts`、`reaction-pack.ts`；`check-reaction-playback.mjs` | `XrSpeechController.cs` 已有唯一人声、kind、generation、忙时跳过和晚到保护；SpeakDialogue 先 Stop，会打断 reaction；尚无口型/ASR 接线或音包版本落实 | 保留已有控制器；适配“正式请求可先准备、播放等短音结束”，取消与失败分别处理。复用音包指纹思想；源报告也承认全局 Provider 指纹覆盖不全，不照搬这个缺口 | 固定缓存/模拟返回先验证等待、失败、取消后晚到、reaction 不推进 dialogue、换声线失效；真实试听另验。待适配，禁止把摩根接受音包直接当小肥鱼声线 |
| 角色格式：`src/types/vrm.ts` 与对应元数据 | `tools/character-import/import.mjs` 已有 YAML→manifest v1，未知原值保存在私有记录；可迁移包过滤字段，Unity 仅执行基础能力 | 继续现有导入器；补字段只在已有契约中演进。桌面构图参数不充当 XR 米制校准，不能将“保留”标成“执行” | 12 项导入测试本轮通过；第二配置/换模型运行与不串校准另验 |
| 空间 / 抓放：XR `Core/PetInteraction.cs` | 已有 owner/generation、完整足迹、安全点、失效重确认；不需要从桌面重建 | 继续直接复用本工程 Core；Editor 的松手投桌面属于输入适配，不能放宽 Quest 实际接触校验 | 183 断言本轮通过；新鼠标修复单独复核，未来 Quest 十次抓放和追踪丢失真机验 |
| 原生持物 / 行走 | 空手旧鱼，animations=0；未接原生动作 | Q1 当前不搬摩根杖/原生附件特殊实现；安排对应角色时再读附件源码与回归。行走先找最小有来源片段，不重建整个动作库 | 未实施；静态站姿平移不得记作自然行走 |

## 4. 当前收口与下一批候选

### Q1-Editor 操作修复：现有施工正在负责

本项已有用户反馈和施工，不另派一份重复任务。统筹验收以下交付后再确认操作问题关闭：

1. 从未放置开始，放置、摸头、右键提起；不滚轮降高，在有效区域直接松开能落回。包括快速移动后立即释放，位置使用释放帧数据。
2. 越界、完整足迹碰边、UI 上释放仍回最近安全点；落点标记与实际结果一致；十次重复操作无漂移、无旧 owner 恢复。
3. 中键转视角、非抓取时滚轮缩放、视角复位可用；UI 不误触；抓取期间滚轮仍有明确语义；换视角后触摸和放回仍准确。
4. 保留原 Core 的 MR 接触高度约束；鼠标投桌面的便捷操作只放在 Editor 输入适配中。
5. 将红/绿回归日志、实际操作证据、更新后的操作说明写入批次报告；记录用户是否接受，不能只写自动通过。当前 README/Q1 报告仍需由施工方同步。

### 下一批候选：Q1-Quest 构建与输入接线

目标是在同一个工程接平台能力，复用现有角色包、空间状态机和恢复行为。它是下一批计划，尚未由本统筹发消息启动，也不将任何未做项写成已批准交付。

- 开工先重新核对本表涉及文件与最新修复；Quest 平台工作不必等待 LingChat 所有自然度问题收口。表现代码变化中时，保留当前可用适配并注明差异，不混合移植。
- 先核对 Unity 6.4 与 OpenXR/Meta 必要组件的官方兼容资料，写出具体版本和最小依赖，再锁入 manifest/lock。本轮未替施工选择或安装包版本。
- 保留 FeiyuPreview；新增 Quest 场景宿主与头/手输入适配。手动确认桌面、空间根和米制校准沿现有 Core；不为本批先做房间导航、多人或整套 MRUK 空间系统。
- 透视、放置/重置、单手抓放、触摸、追踪丢失/摘戴、控制器或调试兜底分别提供接线证据。Editor 相机操作不带到头显视图。
- 验证 Android/IL2CPP、包内资源读取与角色加载；产出 APK 时记录哈希、构建日志、安装/恢复步骤。工具链 ARM64 测试库不替代 APK。
- 头显未到时以构建与模拟输入收口，真机栏留未验。到货后做真实桌面稳定放置、连续十次抓放、摘戴与重开、关闭 PC 后核心互动；72 Hz / 约 13.9 ms 和约 20 分钟运行按交接作为测量目标，未达标就记录实测与瓶颈。

### 随后分开交付的能力

| 批次 | 进入条件 | 交付 / 验收 |
| --- | --- | --- |
| 表现复用补齐 | 来源相关代码与报告稳定匹配；列新复用表 | 相对头眼姿态、当前表情恢复、触摸生命周期的小范围 Unity 适配；技术和自然度分开，不一次移植全部作者系统 |
| 语音 | 明确可调用的独立服务或小肥鱼缓存；来源/声线/语言已核 | 先把仲裁与取消用固定输入测通，再实际日语试听；my-neuro 中文仍为后续目标。不改 LingChat 声音、不自动换云服务 |
| 自然走—转—停 | 找到适配当前骨架且有来源的最小动作片段 | 根运动仅一个所有者，触摸/抓取立即接管；从新落点恢复、脚底不滑、限区和关闭自主移动有效。无片段就保持未完成 |
| Q1-E / DOT | 第二独立配置可先验证；DOT 文件与能力清单到位后替换 | 复用已完成框架；按新 SHA 校准骨骼、眼睛、表情、触摸/抓取、脚底与比例；旧包可回退。新默认资产需实际观看与目标平台验证 |

## 5. 每次派工和验收的固定记录

派工前由统筹更新一张表：**源能力 → XR 现状 → 缺口 → 采用方式 → 目标用例**。已有等价能力直接保留；需要自写时说明具体平台差异。只读来源，避免两个会话改同一工作树。

每批报告必须包含以下字段，不再只写“参考 LingChat”：

| 字段 | 最少内容 |
| --- | --- |
| 来源基线 | 最新报告路径/结论时间、HEAD、相关源文件与测试 SHA256；脏树不能只记 HEAD |
| 采用决定 | 保留行为、直接复用/算法适配/语义与用例移植、不采用项及理由；模型专有参数单列 |
| XR 交付版本 | 修改文件及哈希、Unity/包锁、模型与音包指纹、构建产物哈希（若有） |
| 验证结果 | 命令、退出码、日志和用例输入/预期；分别填脚本、Editor 模拟、实体操作、Quest、用户观感 |
| 验收前源差异 | 再核对相关来源；逐项标已覆盖/待迁移/不适用。源码更新但报告未覆盖时标需重对照 |
| 状态与失败 | 已实现 / 程序化验证 / 用户接受分别记录；保留失败记录与修复因果，列下一步和负责人 |

本轮只新增统筹文档和独立检查证据，并在工程入口补链接；没有改产品实现、Unity 场景/设置、资产、服务或其他项目。下一次审阅以新交付和用户反馈为准，不把本文件当永久状态快照。
