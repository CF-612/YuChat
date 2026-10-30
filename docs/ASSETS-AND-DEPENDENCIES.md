# 本批资产与依赖

2026-10-29，仅记录本机小样实际使用内容。

| 内容 | 版本/来源 | 用途与许可记录 |
|---|---|---|
| Unity Editor | 6000.4.0f1 / 8cf496087c8f，既有本机安装 | 独立 XR 工程；Unity 自身许可，不复制编辑器到交付包 |
| UniVRM / UniGLTF | 0.131.4 / efd20f62f1bf0ac3fcc34de1e497324c5197d146，[官方发布](https://github.com/vrm-c/UniVRM/releases/tag/v0.131.4) | MIT；仅所需 VRM1/glTF 包，未导入演示角色或完整样例 |
| Unity 传递依赖 | Mathematics 1.3.3、Timeline 1.8.11、Test Framework 1.6.0 等 | 锁定实际解析结果在 `LingChat-XR/Packages/packages-lock.json`；使用各包附带许可证 |
| yaml | 2.8.1，[官方包](https://www.npmjs.com/package/yaml) | ISC；仅本机角色导入器，依赖锁在工具子目录 |
| BlueBigFatFish.vrm | 现有转换产物，14,061,476 B；SHA `733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6` | 本机占位；未重转或覆盖。模型制作：水原镜；MMD 转换绑定：耶栗Yaki。原规约逐字节随包保留，不据此批准公开分发 |
| 源规约 | `source-rules.txt`；SHA `5845d04c64f835b9b28f4d68d60c52ca7885b0bf3cfec9a7abce69b105bef24a` | 由原 `input/使用规约.txt` 复制，见开发包 |
| LingChat 格式 fixture | 本工程合成测试内容 | 不含用户私人角色配置，保留占位来源状态与缺项 |
| 日语/中文语音资产 | 本批无新增音频、无下载权重 | 仅接口；尚未连接服务。用户允许临时日语，未来 my-neuro 中文可替换 |
| 行走/原生动作 | 本批无动作片段 | 不把转换报告中的预览动作当作 VRM 内置资源；自主行走未完成 |

模型副本在 `LingChat-XR/Assets/StreamingAssets/Characters/blue-placeholder`；转换报告只放在工具 fixture 的本机来源记录中。原 LingChat 工作树与全局凭据不在这个资源清单中，也未随包迁移。
