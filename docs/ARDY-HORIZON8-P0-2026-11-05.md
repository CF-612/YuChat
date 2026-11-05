# Horizon8 PC 实验，11 月 5 日 P0 交付

**阻塞，性能未测。** 指定动作模型的 12 个文件已下载并校验。首测三个英文提示仍需有权访问基础模型的外部编码环境计算。仓库内的输入清单标记为待生成，真实文件 SHA 等待编码后填写。

|项目|现场结果|
|---|---|
|YuChat / LingChat 源码|1199f821 / 0478d5dd。动作过渡和许可测试的指纹分别为 971CDCE6、74D47FFB，原许可测试 5/5 通过。后续接入复用固定动画许可、模型代次、持有者及旧结果失效。|
|占位 VRM|BlueBigFatFish，SHA256 733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6|
|设备 / worker|RTX 4060 Laptop，驱动 616.56，显存 8188 MiB，初检已用 1526 MiB。D 盘已有 worker，Python 3.11.14，PyTorch 2.6.0+cu124，CUDA 可用。|
|代码及许可|nv-tlabs/ardy e706e27acc88070918308a10922b8f7174d0825e，Apache-2.0|
|权重及许可|nvidia/ARDY-Core-RP-20FPS-Horizon8 b2a1749ab60ffca324bff11ea3012d4d8e1ac90b，NVIDIA Open Model Agreement，LICENSE 已随模型取得|
|下载|765,105,005 字节。denoiser 的 SHA256 为 7c309b6026bf9440f2fbf83194b6040f2ffb6cd3a1d63b0b61557b27a4248614，tokenizer 为 58a887e299a3a6779b5b4ff361b452c4349d45b513b5957f2a961d393623abcd。两个与固定 revision 的 LFS SHA256 相符，其余文件按 Git blob 校验。|
|PC 磁盘|空闲字节 C 9,752,854,528 → 9,759,469,568；D 35,175,792,640 → 34,329,219,072。复核时 ARDY 缓存文件逻辑大小 845,085,137 字节，含源码及 Git。盘符总量会受其他进程影响。|
|依赖差异|上游指定 transformers 5.8.1、peft ≥0.19、huggingface_hub ≥1.0。现有 worker 分别为 4.57.3、缺失、0.36.0。文本输入就绪后再验证最小隔离依赖；真实模型加载、显存峰值、暖机均值/P95、重规划与抓取仍待测。|

## 向量来源与生成步骤

目标机需要三个文件，各是单条提示的 1×4096 float32 数组，原始数据各 16 KiB。生成包还包含 manifest.json，逐条登记原提示、形状、dtype、文件 SHA 和回退。八个意图、编码器和三份依赖的精确 revision 见 [输入清单](../tools/ardy/intent-inputs.json)。

1. 在已有外部 Linux CUDA 机器使用具有 Meta-Llama-3-8B-Instruct 访问权的 HF 登录，并准备上述固定提交的官方 ARDY Python 环境。已有环境可沿用。源码可用 `git clone https://github.com/nv-tlabs/ardy.git` 和 `git -C ardy checkout e706e27acc88070918308a10922b8f7174d0825e` 取得。依赖按该提交的 README 安装。
2. 将 tools/ardy 内的 encode_external.py、intent_pack.py 和 intent-inputs.json 放在同一目录。在外部机器执行以下命令。首个参数是 ARDY 源码目录，第二个参数是尚未创建的输出目录。它只在外部机取得固定编码器依赖，复用 HF 缓存，再离线调用官方 LLM2VecEncoder。每条提示单独编码，内部 batch size 为 1，计算 dtype 为 bfloat16，保存 float32。

   `python encode_external.py /path/to/ardy intent_embeddings`

3. 将生成的 intent_embeddings 整个目录回传。目标机执行 `.cache/video-motion/worker311/Scripts/python.exe tools/ardy/intent_pack.py verify <目录>`。校验项包括三个精确提示、编码器版本、文件摘要、形状、有限数值和意图完整性。该检查验证交接格式；语义以接下来实际生成的画面验收。
4. 已有离线 snapshot 的机器也可用 `python intent_pack.py export intent_embeddings --upstream <官方源码> --llama <Llama snapshot> --base <MNTP snapshot> --adapter <supervised snapshot>`。该入口从本机已有 Hugging Face snapshot 加载。

## 获取尝试与证据边界

官方 `load_model("core8", text_encoder=False)` 留空编码器。Demo 使用 `text_embedding` 作为后续生成输入。官方缓存按原提示 UTF-8 的 SHA256 命名。固定权重树和官方源码没有附带三条指定提示的缓存。本机默认编码端口 9550 无监听，也未配置编码服务 URL。

现场查询 HF 的公开 API。`hugging-apps/ardy` 公开 /generate，`cs686/ardy-motion-api` 公开 /generate 和 /generate_blender。NVIDIA 的 Kimodo Space 将编码器监听在容器内部 9550，Docker 对外提供 7860 演示。HF 在指定 supervised 模型上返回空的 inferenceProviderMapping。另一个公开 LLM2Vec Space 使用 Sheared-LLaMA unsupervised。上述来源均未取得可核验的三个目标向量。

输入校验器已用临时测试数据验证格式成功以及篡改提示、摘要、形状和来源状态时的拒收。测试数据随临时目录清除。外部生成入口尚未在具备底模访问权的 GPU 上实跑。P1–P4 保留待测，录制随对应关卡通过后进行。

## PC 复现

```powershell
. ./tools/ardy/pc-cache.ps1
& .cache/video-motion/worker311/Scripts/python.exe tools/ardy/fetch_core8.py
```

缓存位于 `.cache/ardy-horizon8/`，包括 upstream、checkpoints 和 checkpoint-verification.json。下载程序复用已核验文件；源码、权重和用户凭据均未随本批代码上传。

依据：[固定权重树](https://huggingface.co/nvidia/ARDY-Core-RP-20FPS-Horizon8/tree/b2a1749ab60ffca324bff11ea3012d4d8e1ac90b)、[固定加载入口](https://github.com/nv-tlabs/ardy/blob/e706e27acc88070918308a10922b8f7174d0825e/ardy/model/load_model.py)、[官方外部编码客户端](https://github.com/nv-tlabs/ardy/blob/e706e27acc88070918308a10922b8f7174d0825e/ardy/model/text_encoder_api.py)、[NVIDIA 演示启动配置](https://huggingface.co/spaces/nvidia/Kimodo/blob/main/start.sh)。
