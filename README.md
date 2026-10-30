# LingChat XR · 小肥鱼 MR 小样

GitHub 项目名：**Desktop-SmallBlueFish**。当前为开发中的公开源码备份；代码开源许可证尚待确定，第三方资源分别遵循原规约。参见 [公开备份与恢复说明](docs/PUBLIC-BACKUP.md)。

**仓库不附带旧鱼 VRM、模型截图或本机原始日志。** 克隆后需自行提供具有相应用途授权的模型；本机原占位模型仍保留。使用同一旧模型恢复本地开发时，可执行 `./tools/restore-placeholder.ps1 -SourcePath '<自己的 BlueBigFatFish.vrm 路径>'`，脚本会核对指纹且不覆盖不同模型。没有模型时，目前角色场景不能完成加载；后续 F2 将提供独立于模型的聊天语音入口。

这是独立 Unity 工程。当前批次先做 **Unity 6 Editor 模拟桌面**，为 Quest 的放置、触摸、提起、放回提供可复现的前置实现。当前不等同于可安装的 Quest MR 成品。

工程：`LingChat-XR/`。Unity：`6000.4.0f1 (8cf496087c8f)`，本机路径 `D:/GameCreator/Unity/Editor/Unity.exe`。使用 Built-in Render Pipeline、UniVRM `0.131.4`（提交由 packages-lock.json 锁定）。不依赖 DOT 新模型。

本批 Android Build Support、SDK 34–36、NDK r27c 与 OpenJDK 17 已补齐，并通过 Android ARM64 编译链接小检查；尚未构建 Quest APK。环境证据见 `docs/ANDROID-ENVIRONMENT-2026-10-29.md`，完整批次结果见 `docs/Q1-EDITOR-BATCH-2026-10-29.md`。

## 本机复现

1. 使用 Unity 6 打开 `LingChat-XR`，等待依赖解析与编译。
2. 菜单 `LingChat XR > Create Editor Preview Scene` 创建场景；已有场景会保留。
3. 打开 `Assets/Scenes/FeiyuPreview.unity`，进入 Play Mode，等待旧鱼加载。
4. 点击模拟桌面或“确认桌面并放在中心”。左键按住头部抚摸；右键按住身体提起并拖动，落点圆圈为绿色时直接松开右键即可放回桌面，无需滚轮降高。提起时滚轮仅调整展示高度。
5. 中键拖动旋转视角，未提起时滚轮缩放，F 或“重置视角”恢复初始视角。提起期间锁住视角。落点圆圈超出活动范围变红；越界、无桌面射线或界面上松手会回最近安全点。Esc / 模拟追踪丢失 / 窗口失焦后必须重新确认桌面。
6. “重置放置”清除本次空间；“保存身高与活动范围”仅保存当前校准数值，重新启动仍需确认桌面。当前 UI 不提供身高/区域编辑，不能视为完整校准工作流。

右键抓取要求命中身体代理；头部与空白区域不触发抓取。左上角按钮区域屏蔽场景输入。当前只有键鼠模拟输入，尚无 Quest 手追、透视、锚点或控制器接线。

桌面为 140×100 厘米，默认活动区为 120×80 厘米。落点圈按模型底部 15% 高度内、主要绑定腿脚骨骼的鞋脚几何计算，中心校准到双脚之间，额外留 5 毫米余量；当前 24 厘米旧鱼的圈半径约 2.75 厘米。不再把头发、裙摆和原始张臂范围当成脚底占地。旧版默认区域会在加载时升级，身高与自定义区域保留；只有点击保存才写入 XR 自己的配置。

## 命令行验证

从此目录执行 PowerShell。命令行 Unity 检查前关闭这个 XR 工程的 Editor 实例，避免项目锁冲突；其他工程无需关闭。

```powershell
& '.\tools\CoreChecks\run-checks.ps1'
& 'D:\SoftWare\Nodejs\node.exe' --test '.\tools\character-import\importer.test.mjs'
& '.\tools\run-unity.ps1' -Mode Prepare
& '.\tools\run-unity.ps1' -Mode Smoke
```

Smoke 使用 Unity 图形设备，加载真实 VRM，并通过碰撞代理的模拟指针射线验证触摸表情、抓放、UI 屏蔽与恢复，输出到 `evidence/`。它不操作实体鼠标，也不代表用户已认可画面；具体结果以 `docs/Q1-EDITOR-BATCH-2026-10-29.md` 为准。失败日志与后续修复日志分开保存。Unity 6.4 空项目的启动搜索索引异常采用本工程 `UserSettings/Search.settings` 内 `indexOnEditorStartup=false` 处理，未改全局偏好。

## 模型和兼容范围

使用 `BlueBigFatFish.vrm` 的副本，不重做 PMX/FBX 转换。源 SHA256：

`733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6`

这是 VRM 1.0，内置动画为零。本批有静态休息姿势、基础表情/眨眼、受限头眼朝向，**尚无自主行走**。限区移动规则已预留，但不会把静止站姿平移当作走路。

角色导入器保留 LingChat 来源格式、原值与未知字段的本机记录；可迁移包只包含经过筛选的资料和明确选择的模型资源。开发包来自合成的格式测试配置，不包含用户 DeepSeek 的私人提示词、全局设置或凭据。细节见 `docs/character-compatibility.md`。

XR 身高/脚底/抓取点独立于 LingChat 的桌面缩放与偏移。换模型 SHA 后需要重新校准；目前代理尺寸由模型几何和人形骨推算，仍需用户观看确认。

旧鱼来源规约与署名随包保留。仅供本机占位开发，不据此宣称可公开分发。

## 语音

按用户最新决定，本批可临时采用 LingChat 默认日语，后续保留 my-neuro 中文接口。当前 LingChat 默认 `localsbv2api` 在应用进程内执行，没有已核实的独立 HTTP 服务。

XR 的语音适配器默认不联网、不发声。可配置独立的 SBV2 API 或 my-neuro 服务；需要实际服务或缓存短音后才能验收。不会改 LingChat 声音设置，不会自动切云服务。停止网络等待不证明远端 GPU 推理停止。

## 交付边界

- 本批：Editor 工程、旧鱼副本、米制放置、触摸与抓放规则、无效空间恢复、只读角色导入、语音接口。
- 当前下一批（2026-10-30 用户更新）：先完成不依赖模型的本地 TTS、真实聊天与翻译对话框；物理效果等待新 VRM 后校准。视线与自然触摸继续按 LingChat 来源适配。详见 [基础施工与验收规格](docs/XR-FOUNDATION-BATCH-2026-10-30.md)。Quest 接线和复杂表现后续分别验收。
- 未验：真实手感、自然度、DOT 新资产、TTS 试听、Quest 72 Hz / 20 分钟性能、关闭 PC 后运行。

原 LingChat 工作树、角色配置和资源不由本工程写入。

官方依赖资料：[UniVRM 0.131.4](https://github.com/vrm-c/UniVRM/releases/tag/v0.131.4)、[Unity CLI](https://docs.unity.com/en-us/hub/cli-overview)。本项目不安装 Unity MCP；当前文件、命令行与渲染验证已足够推进本批。

## 统筹与后续验收

[统筹接手、复用对照与下一批（2026-10-30）](docs/XR-COORDINATION-2026-10-30.md)。首批后已出现鼠标落点与视角修复，旧操作说明待施工方同步；程序化检查通过与用户实际接受分别记录。每批派工前刷新 LingChat 相关报告、源码与测试，记录采用版本和目标平台验证。
