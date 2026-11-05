# 场景角色检查

原 Quest 掌边工具提供空间、速度、聊天和配对。原 PC 动作窗口会在后台准备第二份角色实例、相机和 RenderTexture。现在两个平台的手动试播都使用已经放置的场景角色。

- Quest：左手翻掌 → 角色检查。上一项 / 下一项选择动作，点“播放动作”；选择模型表情，点“显示表情”。长表情清单可以按十项跳转。恢复或收起将结束试播。
- PC：设置 → 互动与动作 → 场景角色检查 · 动作与表情。控制栏靠近角色的另一侧，设置草稿保留。下拉框列出当前模型实际提供的表情键，素材库仍可单独保存许可。
- 试播复用当前角色的 UniVRM 表情与动作播放器。手动选用动作不更改聊天许可，结束检查只撤销检查动作和表情覆盖。提起、触摸等互动会释放被检查的表情；尚未放稳的角色拒绝新试播。

## 复现

在仓库根运行 tools/run-m1-check.ps1，分别指定下列方法及独立的 -EvidenceDirectory。

|方法|本轮证据|
|---|---|
|LingChatXR.Editor.SceneInspectionProbe.Pc|evidence/scene-review-final-pc：13 项，包含实际设置按钮、当前实例播放点头、happy 权重 1、抓取立即交还、返回草稿和场景合成截图。|
|LingChatXR.Editor.SceneInspectionProbe.Quest|evidence/scene-review-final-quest：9 项，通过 Quest 场景的掌上 uGUI 按钮调用验证动作、表情和收起。|
|LingChatXR.Editor.QuestAndroidScriptProbe.Run|evidence/scene-review-final-android：Android 玩家程序集编译通过。|

头显实际按钮命中、文字可读性和手掌遮挡仍需随下次允许的 APK 在 Quest 上验收。PC 与 Quest 场景检查均使用临时旧鱼占位。模型作者的多键、动态表情配方仍属独立模型验收。
