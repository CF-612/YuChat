# Q1 第一批：Unity 6 Editor 占位互动

日期：2026-10-29，Asia/Shanghai。目标依据用户提供的 XR 交接与本轮补充：Quest 未到货，先做 Editor；使用 Unity 6，补装 Android 模块；my-neuro 未安装，可临时使用 LingChat 默认日语 TTS。

## 本批结果

独立工程位于 `D:/GameCreator/Document/DesktopSmallBlueFish/LingChat-XR`。交接建议的 `D:/方的世界/LingChat-XR` 未使用，也未覆盖该路径。初次盘点先找到旧 Unity，后经 Unity CLI 发现已有 Unity 6，最终工程与验证全部使用 **6000.4.0f1 / 8cf496087c8f**，可执行文件 `D:/GameCreator/Unity/Editor/Unity.exe`。本批不升级、删除或修改原有其他 Unity 项目。

| 范围 | 已实现与证据 | 验收边界 |
|---|---|---|
| 工程/模型 | Unity 6 编译通过；UniVRM 0.131.4；真实旧鱼运行时加载、SHA 验证、实际渲染 | Editor Built-in RP；不是透视、Quest APK 或 DOT 资产验收 |
| Q1-A 前置 | 模拟桌面、手动放置/重置、0.24 m 身高、独立空间根 | 原模型未转换、未覆盖；真实世界稳定放置未测 |
| Q1-B 前置 | 有界头眼朝向、头部碰撞代理、触摸 happy 淡入淡出与眨眼；UI/空白射线不误触 | 只实现基础 VRM 表情；触摸手感/自然度待用户观看，未完成 Quest 手追 |
| Q1-C 前置 | 身体代理抓取、拖动/升降、有效放回、无效位置回安全点、单所有者与 generation、失焦/禁用恢复 | 模拟指针及规则通过；无真实手追、控制器、头显摘戴实测 |
| Q1-D | 完整足迹限区检查、独立高度/区域偏好保存、失效空间必须重新确认；自主移动默认关闭 | **自主走—转—停未实现**，模型 animations=0；当前 UI 只保存已有数值，校准编辑/自然行走另批 |
| 角色兼容 | 合成 LingChat YAML 导入→相对模型包；保留未知原值的本机记录、缺模型提示、SHA、重复导入/换模型处理 | 不声称任意角色无损；原生动作、作者复杂通道未执行；Q1-E完整换模型演练未验 |
| 语音 | 单人声适配器、SBV2 日语/my-neuro 中文请求格式、显式取消/晚到隔离、缓存 reaction 接口 | **未连接真实服务、未生成或试听音频**；默认静音；原 LingChat 进程内 TTS 不是现成 HTTP 端口 |

## 资产和原项目边界

源模型：`D:/方的世界/vrm-conversion-lab/blue-fat-fish/output/BlueBigFatFish.vrm`。14,061,476 B，SHA256：

`733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6`

本机开发包是 `Assets/StreamingAssets/Characters/blue-placeholder`，来自合成格式测试配置，而非私人 DeepSeek 配置。包内 `model.vrm` 与源 SHA 相同，`source-rules.txt` 保留原规约/署名。没有重新转换 PMX/FBX，没有调用 DOT，没有下载 TTS 权重。

只读盘点得到 65,083 三角、43 材质、42 贴图、155 表情、70 SpringBone 链。贴图按 RGBA8 解码约 231.25 MiB、不计 mip，属于估算；没有测得 Quest 显存或性能。当前限区用保守圆形足迹，实测适配高度 0.24 m，足迹半径约 0.1075 m，含源姿势肢体范围。

原 LingChat 代码/资源/设置未由本批写入；所核 DeepSeek settings 的 SHA 在前后相同。不能据此声称其他会话正在施工的整个工作树完全没变化。未知字段的保留、敏感文本筛选与路径规则见 `character-compatibility.md`。

## 验证记录

- 纯 C# 核心：C#9 / .NET Standard 2.1 编译，183 条行为断言通过；十次连续有效抓放、边界/非有限输入、错误 owner/generation、追踪丢失、旧自主指令拒绝。
- Node 导入器：初始 11 项通过；后续 3 项针对性回归通过；额外敏感文本筛选专项通过。日志在 `tools/character-import/validation-*.txt`。
- Unity 最终验证：`evidence/unity6-smoke-attempt5.log` 退出 0、`XR_SMOKE_PASSED`；`evidence/unity6-smoke.json` 明确测试范围。无第一方 C# 编译警告/错误。
- Unity PlayMode：真实 VRM、实际米制测量、头部射线触摸、空白/UI屏蔽、身体射线抓取、滚轮降低/放回、基础表情淡化、十次抓放、越界安全恢复、晚到输入、禁用/重新确认均通过程序化检查。
- 渲染：`evidence/unity6-preview.png`、`unity6-touch.png`、`unity6-held.png` 来自 Unity 相机，不含 OnGUI 面板，也不是 Quest/透视截图。已做模型与材质可见性检查；用户的视觉接受仍未取得。
- 未验证实体鼠标手感、头显操作、实际音频、帧时/延迟、20 分钟持续运行、关闭 PC 后运行。

## 失败和修复保留

1. 初次编译缺少 `UniGLTF.Utils` 引用。补齐独立 Runtime/Editor asmdef 后通过；保留 `unity6-import.log` 与 `unity6-import-fix1.log`。
2. Smoke 1/2 被 Unity 6.4 的 `UnityEditor.Search.SearchDatabase` 启动索引异常中断。仅在**本新工程** `UserSettings/Search.settings` 设置 `indexOnEditorStartup=false` 后复测通过；未屏蔽运行时错误或修改全局 EditorPrefs。复现脚本在缺少该文件时建立相同设置。原失败日志仍在。
3. 第一次成功运行后，审查发现 BakeMesh 缩放重复计入，足迹半径被压到最小值。改为显式补偿缩放、空间根局部测量及保守范围；实际测得高度 0.24 m。也修复禁用组件时状态已暂停但模型还留在旧位置的问题。旧图保留于 `evidence/smoke-attempt3`。
4. Smoke 4 的“指针降低后放回”失败。鼠标调高度原先同时改变射线投影平面，造成水平位置意外偏移。改为固定桌面平面计算水平拖动、滚轮独立控制高度，Smoke 5 通过。失败日志与旧图保留于 `unity6-smoke-attempt4.log` / `smoke-attempt4`。
5. 编辑器日志仍包含 UniVRM 的 URP fallback shader 不存在提示；本场景使用 Built-in RP，实际材质可见。未把这个结果扩展为 URP/Android 材质已验收。启动期间 Unity 授权客户端也有重试信息，最终成功取得授权并完成运行。

## 复现与下一批

打开工程后运行 `Assets/Scenes/FeiyuPreview.unity`。具体鼠标操作与命令在仓库根 `README.md`。复现入口：

```powershell
& '.\tools\CoreChecks\run-checks.ps1'
& 'D:\SoftWare\Nodejs\node.exe' --test '.\tools\character-import\importer.test.mjs'
& '.\tools\run-unity.ps1' -Mode Smoke
```

下一批优先把这个稳定的空间/角色/交互边界接到 Quest OpenXR、透视与手追；头显未到时继续模拟，但不冒称真机通过。行走片段、可编辑校准和用户自然度验收分别推进。语音采用临时日语时，先提供独立可调用的服务或明确缓存音频；保留 my-neuro 中文替换点，不改原 LingChat 配置。

Android Build Support、SDK 34–36、NDK r27c（27.2.12479018）与 OpenJDK 17.0.9+9 已补齐。NDK 首次安装归位失败，复用已解压内容修复后，官方 CLI 重新探测成功；adb/java/aapt2/CMake/clang 与 Android ARM64 共享库编译链接均通过。完整失败/修复和版本证据另见 `ANDROID-ENVIRONMENT-2026-10-29.md`；此批没有产出 Quest APK。
