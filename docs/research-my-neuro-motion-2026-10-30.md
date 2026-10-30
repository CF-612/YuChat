# my-neuro 动作路线复核：供 XR 方案讨论

日期：2026-10-30，Asia/Shanghai。只读核查官方公开源码、发布元数据与小型文本；没有下载权重、安装、推理、启动服务或修改工程实现。本文件是研究结论，不是施工启动令。

## 核心结论

my-neuro 确实有**训练模型生成新动作**的公开路线，不能将它解释成随机抽取预制动画。当前 `body-actions` 插件让 LLM 给出英文单动作描述，再请求本地 T2M 服务；服务使用 CLIP 文本条件、Transformer 自回归采样动作 token、VQ-VAE 解码，返回整段 22 关节位置序列。另有第一方训练工程。[插件说明](https://github.com/morettt/my-neuro/blob/23e5f996091f89f34d9d5d8b2c2e8752a98f626d/live-2d/plugins/community/body-actions/README.md)、[推理源码](https://modelscope.cn/models/morelle/T2M/resolve/78dab047495cbcd24a171ee8ac44e311015258a0/infer.py)、[训练工程](https://github.com/morettt/Tunbao-Omni/blob/eab3f592d6f75c9b8fe8156e336117f8bed2985f/README.md)

当前可复用的是动作意图协议、服务边界、生成结果格式、重定向思路和取消生命周期；Unity/Quest 执行需要适配。模型生成身体片段不代表已解决现实桌面上的导航、足底接触、碰撞或拿起后恢复。

## 本次固定的来源版本

| 来源 | 2026-10-30 重新核验结果 |
| --- | --- |
| my-neuro main | `23e5f996091f89f34d9d5d8b2c2e8752a98f626d`，与前一天研究一致；API 提交时间 2026-10-28 07:21:27 UTC |
| GitHub latest release | `v6.8.2`，2026-10-28 05:19:41 UTC，发布说明为优化文本动作同步 |
| body-actions metadata | `1.2.0`；`download_dlc` 仍指向 `morelle/T2M-DLC2` |
| T2M-DLC2 模型包 | API 仍列 `T2M.zip`，revision `fc4221d11a9c31598b15feece5ab015e41d4f373`，4,565,157,207 字节；声明 SHA256 `ad3f083b5b50f28194feaee5c28400716bf2c5115c77265c47acce540bdfb9fa` |
| Tunbao-Omni main | `eab3f592d6f75c9b8fe8156e336117f8bed2985f`，提供数据准备、VQ/GPT 训练、编码和推理入口 |

来源：[GitHub commit API](https://api.github.com/repos/morettt/my-neuro/commits/main)、[发布页](https://github.com/morettt/my-neuro/releases/tag/v6.8.2)、[插件 metadata](https://github.com/morettt/my-neuro/blob/23e5f996091f89f34d9d5d8b2c2e8752a98f626d/live-2d/plugins/community/body-actions/metadata.json)、[DLC 文件元数据](https://modelscope.cn/api/v1/models/morelle/T2M-DLC2/repo/files?Revision=master&Recursive=true)、[Tunbao commit API](https://api.github.com/repos/morettt/Tunbao-Omni/commits/main)。ZIP 哈希为发布方声明，本轮未重算整包。

本轮重新读取官方散文件版本 `78dab047495cbcd24a171ee8ac44e311015258a0` 的 web.py、infer.py 和 manifest.json；未重新读取 DLC2 ZIP 内部。前一天已读 ZIP 目录与内部文本的证据保存在 [既有研究](D:/方的世界/LingChat-3D-prototype/research-my-neuro-motion-release-search-2026-10-29.md)，本轮不把那次内部检查说成重新下载验证。

## 实际机制及限制

| 要区分的能力 | 源码支持的结论 | XR 含义 |
| --- | --- | --- |
| 预制动作选择 | 可在 XR 保留已有动作库；这不是 body-actions 服务的生成机制 | 稳定待机、走停、拿起等确定性行为继续用已审片段 |
| 程序化随机变化 | 呼吸、视线、小幅摆动可在客户端参数化；不是这份 T2M 权重输出的全部含义 | 适合立即响应和无服务时维持生命感，另记实现与验收 |
| 神经生成新动作 | web.py 以随机 seed 自回归分类采样，最多 49 token，每 token 4 帧，20 fps，最多约 9.8 秒；一次性返回 JSON | 适合异步生成短身体表现，不能承诺流式逐帧或持续状态条件生成 |
| 上下文控制 | LLM 根据对话写英文单动作；服务只收 text/seed，不直接理解整个聊天状态 | 上帝 Agent 输出意图与风格，由适配器构造动作文本；运行时不让 Agent 逐帧写骨骼 |
| 随机性与缓存 | 服务可变 seed；插件按小写动作文本缓存最近 16 项，同文命中时复用旧结果 | 想看多样性需显式记录 seed、缓存命中、来源版本；不要把重复缓存播放当新采样 |
| 身体与空间移动 | retarget 会把关节坐标转成骨骼动作并应用根部变化；未见桌面导航/真实空间避障控制器 | 小鱼去哪个位置、活动区、抓取优先级仍归 XR 空间控制；不能直接允许生成根轨迹越界 |
| 脸与手指 | 返回 22×3 身体关节位置，未包含表情键、独立手指姿态或完整骨旋转 | 情绪脸、口型和手指需要独立执行/适配，不能靠该模型替代 |
| 取消与服务忙 | 插件可中止等待和取消旧播放；web.py 全局锁忙时 409，源码无推理取消 endpoint | 超时、打断、抓取、换角色后拒绝晚到结果；客户端停止不等于 GPU 计算已停 |

直接依据：[web.py](https://modelscope.cn/models/morelle/T2M/resolve/78dab047495cbcd24a171ee8ac44e311015258a0/web.py)、[插件 service.js](https://github.com/morettt/my-neuro/blob/23e5f996091f89f34d9d5d8b2c2e8752a98f626d/live-2d/plugins/community/body-actions/service.js)、[index.js](https://github.com/morettt/my-neuro/blob/23e5f996091f89f34d9d5d8b2c2e8752a98f626d/live-2d/plugins/community/body-actions/index.js)、[retarget.js](https://github.com/morettt/my-neuro/blob/23e5f996091f89f34d9d5d8b2c2e8752a98f626d/live-2d/plugins/community/body-actions/retarget.js)。

## 建议的 XR 接入顺序（待讨论）

1. 上帝 Agent 统一给出表现意图；动作来源可选预制库或生成器，共用打断、占用与完成事件。空间位置控制继续使用现有 PetInteraction。
2. 先让预制动作、局部程序化变化和表情/口型/眨眼协作稳定。生成结果走同一个动作执行入口，避免第二套执行器争抢骨骼。
3. 单列 my-neuro T2M 的 PC 隔离实验：固定动作意图、记录不同 seed 输出，在旧鱼上做 Unity 重定向与过渡。先测有限值、时长、关节异常、足底/根位移、打断和迟到结果，再由用户看语义与自然度。
4. 生成作为异步候选：未就绪时保持本地表现；过期/不合适结果丢弃，服务不可用时回到预制库。可把用户认可的生成片段缓存为可复用资产，但区分“本次生成”和“缓存播放”。
5. Quest 初期只运行渲染、输入、控制与动作播放；PC 生成服务是可选依赖。端侧推理没有当前实测证据，另评算子、内存、热量和渲染并发，不作为第一版互动前置。

## 尚不能下结论的项目

没有运行此次生成服务，因此不知道本机冷暖启动、首段延迟、显存、与 TTS 并发时的成本；CPU/CUDA 代码路径存在不等于 Quest 可直接跑。没有真实 Unity 重定向或头显测试；模型稳定性、动作语义命中、跨段连续性和手指观感均待测。主页“对话 1 秒以下”不用于动作延迟承诺。

公开推理代码和权重发布入口已经明确，无需再把“寻找是否公开”作为前置任务。训练仓要求自备数据和权重，NOTICE 分别保留权重、数据、评估资产与 CLIP 的条款；源码根许可不能替代全部资产来源记录。[Tunbao NOTICE](https://github.com/morettt/Tunbao-Omni/blob/eab3f592d6f75c9b8fe8156e336117f8bed2985f/NOTICE)、[manifest](https://modelscope.cn/models/morelle/T2M/resolve/78dab047495cbcd24a171ee8ac44e311015258a0/manifest.json)
