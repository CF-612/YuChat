# XR 本地会话宿主（F2）

复用 LingChat 的 LlmClient / GenaiProvider / Translator / LocalTtsEngine / LocalTtsAdapter。源文件指纹在 `source-snapshot.json`；直接复制、裁剪和适配范围见 `../../docs/F2-CHAT-TTS-2026-10-30.md`。本派生宿主按 AGPL-3.0-only 提供，完整条款在 `licenses/LingChat-AGPL-3.0.txt`。第三方 esaxx-rs 保留 Apache-2.0 条款。

本机从项目根运行：

```powershell
./tools/xr-host/start-host.ps1 -Build
# 已启动时不要重复启动。
./tools/xr-host/verify-live.ps1 -Conversation
./tools/run-f2-check.ps1
./tools/xr-host/stop-host.ps1
```

`start-host.ps1` 默认只读本机 LingChat 的 settings.json、已安装的 ling-v2 / DeBERTa ONNX 及 onnxruntime.dll。可用 `-LingChatRoot`、`-SettingsPath` 指定各自路径。不下载模型，不修改 LingChat 配置、角色、数据库或桌面会话。首次构建需 Rust/MSVC；依赖锁定在 Cargo.lock。源模型和 ONNX DLL 不包含在仓库，克隆后需另行准备有授权的依赖。

宿主仅监听 `127.0.0.1:17861`，每次启动生成令牌；Unity Editor 读取工程外 `.cache/xr-host-connection.json`。全部会话、正文、内部日语和音频留在 `.cache/xr-host-data`。令牌不进入 Assets/场景/APK；本版本不可直接从 Quest 连接，也不是 Quest 独立推理。LLM 与补翻译使用设置所选 provider，可能联网并产生用量；本地 TTS 不是全链路离线。

`/session` 打开角色独立历史；`/turn` 提交、试听或重试已有句；`/events` 按 cursor 轮询；`/audio/:uuid.wav` 下载已生成音频；`/stop` 递增代次并屏蔽旧回调。所有请求需要 Bearer，拒绝浏览器 Origin。中文 `message` 和音频 UUID 与会话、角色、轮次、句序、代次绑定。日语只在宿主合成及私有缓存使用，不传给 UI。

当前使用明确的临时小肥鱼人设和 ling-v2 声线，并非已移植桌面角色完整人设、剧情、分析链或长期记忆。现有角色导入器及 manifest v1 保持不变。会话历史最多取最近 30 条作为下一轮上下文，历史面板保留已有记录。重启服务会恢复历史，但不恢复播放队列。停止屏蔽旧结果，不保证中断已开始的本地推理。

故障回归使用隔离夹具：`cargo test --locked --manifest-path tools/xr-host/Cargo.toml`。真实服务脚本另记证据，不把夹具当真实语音。`snapshot-source.ps1` 是来源重建工具，会重新抽取已适配文件；升级来源前检查差异，不能随意覆盖此批适配。

## 2026-11-01：可选 Quest 配对网关

原宿主仍只监听 loopback。新增 `start-pairing.ps1 -BindAddress <本机私网IPv4>` / `stop-pairing.ps1`，独立进程显式绑定单一私网接口，通过一次性 8 位码获取独立随机令牌，受限转接原接口；不开放设置接口，不输出宿主令牌，不把模型/API key 放入设备。配对码 5 分钟、5 次错误上限、一次使用；重启网关使旧令牌失效。只适用于可信 LAN，HTTP 不提供加密；不自动修改防火墙。完整复现和验收边界见 `../../docs/QUEST-UI-AND-PAIRING-2026-11-01.md`。旧文“本版本不可直接从 Quest 连接”指仅 loopback 的首批；跨设备实际网络和语音仍未验收。

## 按住说话与联网边界

新增受同一 Bearer 保护的 `/asr`：按住录音，松开识别并填入草稿，由用户确认发送。沿用 LingChat 的本地 llama-ASR 与云端 Qwen 整句协议；`start-host.ps1 -AsrSettingsPath <配置路径>` 可指定只读来源，默认使用 `-SettingsPath`，并尊重关闭语音输入的偏好。云端识别需要客户端明确允许；未配置识别服务时不会自动下载模型。

Quest APK 不内置本地 TTS/ASR 模型，需通过可信局域网连接电脑宿主。本地 TTS 推理不必访问互联网；聊天、翻译或云端识别是否需要互联网取决于所选服务。放置、抓放、缩放和行走不依赖这些服务。电脑端夹具验证不等于真实头显麦克风、网络或语音服务验收；详见 `../../docs/P1-VOICE-AND-P2-ASSETS-2026-11-01.md`。
