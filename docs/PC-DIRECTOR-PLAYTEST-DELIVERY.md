# PC 自然语言动作导演试玩交付

## 启动与操作

从工程根运行 `./tools/xr-host/start-host.ps1` 启动 PC 宿主（首次更新二进制时先 stop-host.ps1，再 start-host.ps1 -Build）。另一个 PowerShell 窗口运行 `./tools/video-motion/start-t2m-prototype.ps1` 启动本地生成服务。默认 17880，试玩无需设置环境变量。主项目使用本机私有配置和本地动作包。

Unity 6 打开 LingChat-XR 工程中的 Assets/Scenes/FeiyuPreview.unity，进入 Play Mode；选择「YuChat → 动作实验 → 自然语言导演试玩」，在窗口点「放置到桌面中心」。已有动作许可页需启用动作和导演，并允许要测试的片段。提交中文后看“导演”和“执行”两行。可以选择库内片段直接对照、停止、缓存重播，或对生成结果换种子。

切到 Game 视图，右键按住身体提起，滚轮调高度，松右键放下。抓放消耗旧交互代次，落稳后再提交。生成服务不可用时可继续比较库内动作。第一版对排除项采用保守策略：有显式禁用片段时关闭生成分支，避免借生成绕开许可。这会同时挡住与该排除项无关的生成意图。

## 真实验证与边界

复用原 stage_action 路径使用的 LlmClient.complete_with_tools、宿主用户配置、候选有效性检查，以及 XrMotionPlayer.Capabilities / Play / TryGeneratedPrototype；试玩另设仅本机认证访问的 POST /playtest/direct。没有改现有聊天句队列。新工具协议 direct_motion 输出 action、reason、prompt。action 须是允许目录中的 ID、none 或获得许可的 generate；非法结果作为协议错误返回。LingChat 参考文件核对值为 motion-library-preferences.ts 668CDD6B2D3A，motion-transition.ts 971CDCE690F0，check-motion-library-preferences.mjs 74D47FFBEC3A。

| 中文输入 | 真实导演与执行 |
|---|---|
| 我要走啦，下次见。 | 面板选 wave_normal_right_661 并实际播放。库内分支无生成请求。 |
| 我先回家了，改天见。 | 真实接口另测仍选同一个挥手 ID，未单独渲染这句。 |
| 陪我安静待一会儿，先别动。 | 面板返回 none，保持当前状态。 |
| 请蹲下，双手假装捡起一个大箱子，再慢慢站起来。 | 面板收到英文意图，经真实 CUDA T2M 生成、转换、在小肥鱼上播放。正面能看到身体下降和双手向前，但搬起大箱子的重量感不清楚。 |

隔离 Unity 试玩夹具只在本次运行开放可比较的候选，不写玩家的许可。使用真实导演与推理服务，验证缓存重播、同意图新种子和新包，以及请求期间进入 Held 后旧结果被拒绝。抓取事件由测试驱动，完整鼠标操作的易用性由玩家试玩。证据见本机 evidence/pc-director-live-final，生成成片见 evidence/director-box-visual/local-smallbluefish-alpha2/director-box.mp4。未制作 APK。
