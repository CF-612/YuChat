# 来源声明

2026-10-31 Q2-A 续批：普通一次性动作新增当前待机相对组合，适配 LingChat native-rig-motion.ts 的参考姿态乘法规则；保留 AGPL-3.0-only。此规则用于 Unity VRMA 的适配差异、源码指纹和用户反馈修正见 docs/Q2-A-XR-ENTRY-2026-10-31.md。

2026-10-31 自然过渡修复：XrMotionTransition 与 XrMotionPlayer 的进入时钟、姿态角差时长、收势、取消及循环接缝逐式适配 LingChat motion-transition.ts、vrma-motion.ts，沿用 AGPL-3.0-only。当前源码指纹、Unity 差异和原函数对照结果见 docs/M1-NATURAL-TRANSITIONS-2026-10-31.md。

2026-10-31 M1 续批：XrVoiceRhythm、XrConversationMotion、XrPoseLoop、播放器所有权及导演接线适配 LingChat voice-rhythm.ts、conversation-motion.ts、conversation-signals.ts、semantic-action.ts、native-rig-motion.ts、daily-motion.ts、stage-action-gate.ts 和舞台导演工具/提示词。仍按本模块 AGPL-3.0-only 条款。上游再复用的 Rexclaw 数值曲线和 MotionEngine 点头/摇头原 MIT 许可完整保留于 tools/xr-host/licenses/motion/PHASE-1-SOURCES.md、PHASE-2-SOURCES.md。对应指纹与测试边界见 docs/M1-COMPLETION-2026-10-31.md。

2026-10-31 M1：XrMotionWindow、XrMotionCatalog、XrMotionPlayer 和本批 UI 继续按 LingChat 的 VrmActionPreview.vue、VrmMotionLibrary.vue、VrmPreview.vue、SettingsPanel.vue、SettingsNav.vue、MenuPage.vue、MenuItem.vue、action-library.ts、motion-library-preferences.ts、motion-transition.ts、vrma-motion.ts 等源码适配，沿用本模块 AGPL-3.0-only 条款。16 个来源指纹见 evidence/m1-2026-10-31/source-start.json 和 source-final.json；迁移差异见 docs/M1-MOTION-LIBRARY-2026-10-31.md。UniVRM 保留原许可。本地动作素材不由本模块代码许可证授权公开，原许可和批准记录不变。

后续时序适配还参考 TypeWriter.ts、charHtml.ts、dialogue-text.css、useRoleAvatar.ts、useDialogAdvance.ts、useAutoAdvance.ts 和 emotion/config.ts。原音效和思考气泡仅按本机授权范围复制到 Git 忽略目录，由 tools/prepare-dialogue-effects.py 记录指纹；未将素材纳入公开源代码许可声明。详见 docs/EXPERIENCE-REPAIR-2026-10-30.md。

UI 的视觉结构与交互布局改写自 LingChat 的 GameDialog.vue、Button.vue 和历史对话组件（https://github.com/CF-612/LingChat，源 HEAD 0478d5dd010aaad3c33f1ff89ff81b0593928b58 加本机工作树版本）。Unity UI Toolkit 适配文件为本项目新增。此 Chat 适配模块按 AGPL-3.0-only 提供，完整条款见工作区 tools/xr-host/licenses/LingChat-AGPL-3.0.txt。实际采用版本和差异见 docs/F2-CHAT-TTS-2026-10-30.md。
