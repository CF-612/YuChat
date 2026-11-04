# R2 角色清单配置化（2026-11-04）

## 复用基线

只读核对 LingChat vrm-mvp 0478d5dd（2026-10-28）：SettingsCharacter.vue 按角色身份遍历清单，role 数据实体区分身份和资源目录。采用清单驱动 UI、身份稳定与外观解耦的行为；Unity 独立宿主没有桌面数据库上下文，沿用现有 JSON/HTTP 与 SessionStore，不复制桌面数据库。不修改 LingChat。

## 已实现

- 宿主统一 RoleCatalog：内置 roles.default.json 保持 feiyu / ling-demo 的名称、人设、历史身份和默认日语声线。XR_HOST_DATA/roles.json 可提供版本 1 清单，重启宿主生效；缺文件使用内置值，配置错误明确失败，不静默替代。
- GET /catalog 经鉴权返回角色和已注册声线；不返回私有人设。/session 从配置验证身份，中文对话提示使用同一配置的人设。
- 配置校验版本、数量、重复身份、文本长度与实际注册的声线/语种。当前只注册 ling-v2 / ja，不假装支持任意新声线。沿用现有 TtsAdapter，不改播放、推理和翻译算法。
- Unity 连接时读取清单，设置页按返回值构建角色选项和显示名称/声线。仅旧宿主明确返回 404 时使用旧双角色兼容清单；超时、无权限、错误清单不会伪装成功。
- 已保存的自定义角色标识不会在 Awake 被强制改成 feiyu。缺失角色显示暂不可用，保留选择，用户可换成当前服务注册的角色；历史不合并、不删除。

## 复现及证据

宿主 38 项测试通过，含新增第三角色仅改配置、重复开同角色保持同一会话、原角色会话保留、错误角色/声线/语种拒绝和未鉴权拒绝。

`C:/Users/fangcheng612/.cargo/bin/cargo.exe test --offline --locked --target-dir tools/xr-host/target/c0 --manifest-path tools/xr-host/Cargo.toml`

隔离 Unity 6000.4.0f1 编译及 6 项序列化/清单校验通过：

`./tools/run-m1-check.ps1 -EvidenceDirectory evidence/refactor-role-catalog-2026-11-04 -Method LingChatXR.Editor.RoleCatalogProbe.Run`

证据为上述目录 checks.json / M1 日志。没有操作用户编辑器、修改私有 roles.json、重启真实服务、打包或真机测试。没有声线听感或实际 UI 观感验收。

## 配置方式

将 tools/xr-host/roles.default.json 复制到私有 XR_HOST_DATA/roles.json，保留现有 id（避免改动历史身份），添加新 id / label / persona，voiceProfile 与 spokenLanguage 暂沿用 ling-v2 / ja。仅改名字不会改变 id。此配置只管理对话身份，不宣称完整 LingChat 角色包/第三方声线导入已完成。

R2 的模型能力配置与完整脸部合成仍单列，不以此角色配置提交宣称整个 R2 完成。
