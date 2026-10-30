# 公开备份与恢复

2026-10-30。仓库名 `Desktop-SmallBlueFish`，按用户要求使用公开可见性。

## 本次包含

- Unity 6 工程源码、场景、项目与依赖设置。
- 导入工具、合成测试配置、测试及批次文档。
- 角色 manifest、来源署名与原资源规约、可公开的测试结果和源码指纹记录。

## 仅留本地

- 第三方 `BlueBigFatFish.vrm` 及其 Unity meta；未经额外授权不随公开源码分发。
- 模型截图、原始 Unity/安装日志、二进制检查产物。文档中的这些证据链接指向原开发机留存文件，公开克隆不包含它们。
- Unity Library/Temp/UserSettings、下载缓存、node_modules、编译产物、私人配置与凭据。
- 工作区中与项目无关的 `SafeNet Sentinel` 目录。

本次不是整个硬盘或完整资产库的备份。上述本地文件未删除，也未改原 LingChat 项目。旧模型指纹为 `733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6`，14,061,476 字节。

## 克隆后恢复

1. 安装 Unity 6000.4.0f1 与所需 Android 模块，用 Unity 6 打开 `LingChat-XR`。依赖由 manifest 与 lock 文件恢复。
2. 若本机已有同一旧模型且具备用途授权，运行 `tools/restore-placeholder.ps1 -SourcePath <模型路径>`。脚本核对大小与 SHA，避免误覆盖其他资产。不同模型应走现有角色导入器并重新校准。
3. 导入器在 `tools/character-import`，安装锁定依赖后运行其测试；纯核心检查见 README。
4. 现有角色 Smoke 需要本机模型；没有模型时不能把载入失败视为通过。F2 的无模型语音/聊天入口尚未实现。

## 开源状态

公开可见与授予开源许可是两件事。本轮先完成公开备份，未擅自为代码或第三方资源指定 MIT/其他许可。后续采用 LingChat 源模块和 UI 时，须核对上游许可并保留所需声明。当前用户决定：F2 先做本地 TTS、真实聊天，UI 复用 LingChat 风格，只显示中文对话与历史，不显示日文原文；备份完成后继续该批。
