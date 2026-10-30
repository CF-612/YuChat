# Q1 角色导入器：兼容与验收边界

批次：2026-10-29。脚本入口为 `tools/character-import/import.mjs`，仅用于本机准备独立 XR 角色包；不调用 LingChat API、不写原角色、不读全局设置/数据库、不启动语音或联网服务。默认运行时并不表示已经支持所有 LingChat 表情/动作。

## 输入与输出

要求 Node.js 22，依赖固定 `yaml@2.8.1`（ISC）；解析行为参考 [yaml 2 官方文档](https://eemeli.org/yaml/)。使用 YAML 1.2，保留 `false`、`null`、`none`、`[]` 与缺省的差异。重复键、递归别名、无法解析的标签及非有限数字拒绝导入。

```powershell
Set-Location 'D:\GameCreator\Document\DesktopSmallBlueFish'
$node = 'D:\SoftWare\Nodejs\node.exe'
$npm = 'D:\SoftWare\Nodejs\node_modules\npm\bin\npm-cli.js'
& $node $npm ci --prefix .\tools\character-import --ignore-scripts
& $node .\tools\character-import\import.mjs `
  --source '.\tools\character-import\fixtures\blue-placeholder' `
  --output '.\LingChat-XR\Assets\StreamingAssets\Characters' `
  --records '.\.cache\character-records' `
  --character-id blue-placeholder `
  --placeholder 'D:\方的世界\vrm-conversion-lab\blue-fat-fish\output\BlueBigFatFish.vrm' `
  --source-rules 'D:\方的世界\vrm-conversion-lab\blue-fat-fish\input\使用规约.txt' `
  --expected-model-sha256 733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6
& $node --test .\tools\character-import\importer.test.mjs
```

`--source` 必须指向包含 `settings.yml` 的目录。`--output` 和 `--records` 必须分别指定为独立目录；三者不能重合或彼此嵌套。实际开发包请用合成 fixture，**不把真实 DeepSeek 的私人配置、人设和原文放入 Unity Assets/APK**。

每个包写入 `<output>/<characterId>/manifest.json` 和可选 `model.vrm`。源文件原文、注释和全部未知字段保存在 `<records>/<characterId>/source/<settingsSHA>/settings.yml` 与 `parsed.json`。这个 records 目录是私人本机记录，不能作为 Unity StreamingAssets、APK 资源或公开提交内容。导入前后校验源文件 SHA；复制后再校验模型 SHA。文件被其他进程同时修改会报告失败，需用稳定输入重新导入。

只跟随 `vrm.model` 一个明确的角色相对资源引用；禁止路径越界、绝对/盘符相对/ADS 路径、symlink、junction 和 Windows reparse point。未知字段中的路径不会被跟随。VRM 需自包含，外部图片/缓冲区引用拒绝；这只是 GLB/VRM 基础格式校验，并不等同完整 VRM schema 或运行时导入成功。

旧鱼包必须显式传入 `--source-rules`，其原规约/署名逐字节复制为包内 `source-rules.txt`，和 `model.vrm` 一起列入 manifest 的 `assets`（`path/sha256/bytes/kind`）。这份文本会随本机 APK 保留；不把含本机路径的 `DELIVERY.md` 放进可迁移包。转换报告仅保存在工具 fixture 的 `source-notices`，供本机复核。已有规约的包重复导入时必须再次显式给出规约参数，防止换模型后意外沿用或丢弃旧规约。

没有 VRM 绑定或绑定文件缺失时保留 `missing`。仅显式 `--placeholder` 才复制指定旧鱼，且记录 `isPlaceholder: true`，不会给原 settings.yml 添加绑定。若原绑定可用则优先原绑定。错误的相对路径不允许靠 placeholder 掩盖。

## Manifest v1

```json
{
  "schemaVersion": 1,
  "generator": "lingchat-xr-character-import/0.1.0",
  "sourceFormat": "lingchat-settings-yaml",
  "source": {
    "sourceId": "sha256-of-canonical-local-source-directory",
    "settingsSha256": "sha256-of-source-bytes",
    "vrmSettingsVersion": null
  },
  "characterId": "blue-placeholder",
  "displayName": "合成测试角色",
  "character": { "ai_name": "合成测试角色" },
  "voiceReference": {},
  "model": {
    "path": "model.vrm",
    "sha256": "actual-model-sha256",
    "bytes": 14061476,
    "format": "vrm-1.0",
    "isPlaceholder": true,
    "sourceBinding": "missing"
  },
  "assets": [
    { "path": "model.vrm", "sha256": "actual-model-sha256", "bytes": 14061476, "kind": "model" },
    { "path": "source-rules.txt", "sha256": "actual-source-rules-sha256", "bytes": 2680, "kind": "source-rules" }
  ],
  "lingchatVrm": null,
  "extensions": {},
  "capabilities": {
    "hasModel": true,
    "basicExpressions": ["happy", "blink"],
    "animationsEmbedded": 0,
    "lingchatExpressionsExecution": "basic-vrm-only",
    "nativeMotionsExecution": "not-implemented",
    "ttsExecution": "separately-configured"
  },
  "missing": [{ "code": "CHARACTER_HAS_NO_3D_MODEL", "message": "The source character has no VRM model binding." }],
  "xrCalibration": { "status": "required", "modelSha256": "actual-model-sha256" },
  "omittedFields": []
}
```

Unity 先读 `displayName`、`model.path/sha256/format/isPlaceholder`。`model:null` 是资料包，没有可加载模型。运行时必须拒绝 SHA 不匹配的资源；`hasModel` 仅说明文件通过基础检查，不能直接作为已呈现或已验收的标志。

`sourceId` 默认由规范化来源目录计算，包内不包含该目录明文。可用 `--character-id` 选择短 ID；相同 ID 不允许偷换成另一个源目录，避免串角色。第一批没有双向同步、资产目录扫描或任意文件打包。

## 保留与省略

| 字段 | 处理 |
|---|---|
| `ai_name/ai_subtitle/title/info/system_prompt/system_prompt_example/system_prompt_example_old/thinking_message` | 白名单角色资料；原文保留，带机器路径、URL/凭据标记的字符串不带入可迁移包，原文仍在私人记录 |
| `tts_type/voice_lang/voice_dialect` | 仅来源引用，不启用服务、不带端点、不覆盖 XR 的独立声音选择 |
| `vrm` 已有表情/动作数据 | 保留安全标量、稳定 ID、显式关闭/空数组；模型路径只由 `model.path` 重写为固定包内路径 |
| 根级未知扩展 | 仅迁移 null、布尔、有限数字及由它们组成的对象/数组；未知字符串保留于私人记录，等待明确 schema 后支持 |
| 路径/凭据/端点键、瞬态 ID、角色文件夹、玩家身份、`voice_models`、Live2D/服装/2D 数据 | 不进入首批可迁移包，在 `omittedFields` 中记录字段路径 |
| 桌面 scale/offset/bubble 等 | 私人记录保留，不当作 XR 参数；`lingchatVrm` 中原始构图值仅供兼容参考，运行时不得解释为米制空间 |

省略筛选是保守 schema 和文本模式，不是任意自然语言秘密的自动识别器。导出前仍应审阅白名单文本，正式公开包另做内容与许可审查。本批不生成公开资源包。

`none` 与关闭值不能替换成系统默认；未知动作、作者表情和退休 HAST ID 可以保留，但运行时必须明确不支持。记录数据不等于执行适配已完成。源格式与版本、模型指纹、缺项已入包；完整动作 catalog、原生骨架动画和特殊作者通道留后续。

## XR 校准与重复导入

本机校准文件是 `<records>/<characterId>/calibration.json`：

```json
{
  "schemaVersion": 1,
  "modelSha256": "actual-model-sha256",
  "heightMeters": 0.18,
  "areaRadiusMeters": 0.22
}
```

导入器不创建、重写或合并这个文件。相同模型 SHA 且字段有效时，manifest 的 `xrCalibration.status` 为 `valid`，只迁移已知数值字段；再次导入不抹掉未知本机校准数据。模型 SHA 改变时状态为 `invalidated`，不输出旧高度/区域值，旧文件留作回退证据。没有有效记录时为 `required`。本批未定义把头显校准同步回 PC 的协议；Quest 自己仍需按模型 SHA 存储并在重开后重新确认失效空间。

已存在的输出目录只能含本导入器生成的 manifest/model/source-rules 及其 Unity `.meta` 旁文件；发现其他用户文件就拒绝覆盖。Unity `.meta` 原样保留，重复导入不会改变 GUID。写模型后最后提交 manifest；遇中断必须让运行时 SHA 检查失败关闭加载，不能拿旧 manifest 继续使用错模型。导入锁留在私人 records 下；异常退出后的旧锁必须先检查进程与现场，不能无条件自动删除。

## 当前资产和声音事实

旧鱼源 SHA 为 `733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6`，VRM 1.0，源文件 14,061,476 B；43 材质、65,083 三角、42 贴图、70 SpringBone 链。155 表情，内置动画为 0。转换报告的 Three.js 预览没有证明 UniVRM/Quest 性能。源规约和署名须随本机资产保留，元数据不是追加授权。

本机只读核对到 DeepSeek 和诺一钦灵的声音引用均是 `localsbv2api` / `ja` / `ling-v2` / speaker 0；当前实现是 LingChat 进程内 `LocalTtsEngine`，不能从这个提供方名称推断出可供 Quest 请求的 HTTP 端口。源码另有 SBV2 HTTP 适配器，但默认 URL 不等于正在运行的服务。最新用户允许临时日语 TTS；它与未来 my-neuro 中文接口分别记录，导入器不改原声音设置、不搬运端点或凭据。

## 验证层级

Node 测试覆盖 YAML 值语义、私人原文保留、占位缺项、显式模型路径、路径越界/reparse 拒绝、SHA、无模型导入、重复校准、模型替换失效和用户文件保护。测试使用合成 YAML 与最小 GLB 夹具，不代表可渲染角色。

2026-10-29 的本机记录：初始 11 项测试全部通过；加入规约资产、Unity `.meta` 保护和更保守的未知字符串筛选后，相关 3 项复测全部通过（见 `tools/character-import/validation-2026-10-29.txt`）。Windows reparse 属性检查会调用系统 PowerShell，整组测试约数分钟；这不是运行时角色交互开销。另有针对白名单文本中机器路径/典型凭据标记的复测记录 `validation-sensitive-values-2026-10-29.txt`。

真实合成 fixture 已生成 `LingChat-XR/Assets/StreamingAssets/Characters/blue-placeholder`，只含合成人设、现成 VRM 副本及原规约。模型 SHA 与源文件相同；规约 SHA 为 `5845d04c64f835b9b28f4d68d60c52ca7885b0bf3cfec9a7abce69b105bef24a`。重复核对 DeepSeek 的源 settings SHA 仍为 `b0d64121e619bfadf52561348c278b086a7368c58e7b1520fff6adc868a91d1d`；这仅证明本批检查的该文件未变，不宣称冻结了其他会话正在施工的整个 LingChat 工作树。

真实旧鱼的包导入、Unity 编译/呈现、Quest 安装、抓放十次、72 Hz/20 分钟性能、源角色自然聊天与用户视觉验收必须由对应批次另留证据；该文档不将这些标为通过。
