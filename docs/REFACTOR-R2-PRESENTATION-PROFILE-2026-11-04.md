# R2 表现能力声明与材质绑定迁移（2026-11-04）

## 范围与保留边界

仅迁移能力开启条件、语义键绑定和材质分类。V21 VRM 二进制、形状键权重、已有互动配方、动画、物理参数、lilToon 模板与描边数值均不改。暂停中的震惊眼白片、动态星星/晕圈效果排查不在本批重新启动。

## 改动

- CharacterPackage 增加可选相对路径 presentationProfile；独立 JSON 包含 schemaVersion / profileId、interactionFace、secondaryBodyFeedback、语义键与材质角色规则。
- FeiyuAvatar 加载时解析/验证一次，将语义绑定转换为 UniVRM ExpressionKey。文件哈希仅用于文件完整性、资源共享和原用户偏好，不再用 SHA 决定脸部、材质或身体反馈能力。
- 特殊互动配方须显式声明，且实际存在四个必要键（eyeSmile、eyeCircleShock、mouthOpenCircle、eyeTroubled）；缺少 eyeSpiral 仅关闭晕眼触发，其他互动保留。所有声明但缺失的键记录 profileId / semantic / key 诊断。
- 无配置旧包仍可导入，走标准 VRM/通用行为；不因恰好存在同名键而默认承诺支持整套特殊配方。配置错误警告并回退，取消加载仍正常传播。
- AnimePresentation 消费配置声明的皮肤、细节、衣物、金饰、薄发片和描边排除角色；素材名称只存在于绑定 JSON。共享赛璐璐模板算法及数值保持原样。
- 示例 tools/character-import/profiles/smallbluefish-face-v1.json。当前本地 V21 包 manifest.json 引用 presentation.json；配置复制自该示例。旧 manifest 已保存在 .cache/model-backups/pre-presentation-profile/manifest.json，未改模型文件。
- 导入工具新增 --presentation-profile，复制配置并记录 SHA/大小；重复导入必须显式继续提供已有配置，不能静默丢弃。拒绝未知字段、无效语义、危险路径/链接。V21 超过旧 256 MiB 上限，模型有界上限提高为 512 MiB，不改源模型字节。

## 验证

- Unity 6000.4.0f1 隔离编译及 16 项检查通过：声明、缺键定位、未声明能力、各材质分类、错误版本/必要语义、旧包解析、本地包引用。
- 读取 V21 GLB 元数据核对 147 个材质，旧规则与新规则差异 0；609 个 custom 键中所有本批声明键存在。没有重做 CPU 蒙皮/权重调查。
- VRM SHA256 仍为 cf331c0f5c62f818b05bb7f132bd34424bdd99784bb908d5f4651d901f855514。
- 导入全量 13 项通过；随后收紧未知字段和资产登记，最终针对表现配置的 2 项补测通过。初次受限测试环境出现 EPERM 改名失败，不作为通过证据；通过结果来自 D 盘独立临时目录。

复现：

1. `./tools/run-m1-check.ps1 -EvidenceDirectory evidence/refactor-presentation-profile-2026-11-04 -Method LingChatXR.Editor.PresentationProfileProbe.Run`
2. 将 TEMP/TMP 指向 D 盘独立临时目录，`node --test tools/character-import/importer.test.mjs`。
3. 用原导入参数加 `--presentation-profile tools/character-import/profiles/smallbluefish-face-v1.json`；旧包不提供参数则继续通用模式。不要将不同语义模型随意标为此示例配置。

证据：evidence/refactor-presentation-profile-2026-11-04 下 checks.json、model-metadata-parity.json、importer-tests.txt、profile-final-tests.txt 和 Unity 日志。

## 边界与回退

未做本批真实模型绘制、正面/侧面视觉或 Quest 验收，不声称白片解决。相同语义新 SHA 不依赖代码哈希条件；实际重新导出模型的完整视觉验收仍需后续样本。完整脸部状态升级与动态表情时序不包含在这个等价配置迁移中。

回退须同时恢复本批前代码与缓存中的旧 manifest；仅移除配置会按设计切换为通用表现，不能当作视觉等价回退。用户设置、当前未提交的其他物理调查、音效和 APK 均保留。私有角色包不提交 GitHub，公开示例不包含 VRM 或贴图。
