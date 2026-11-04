# V21 测试模型替换与接入边界

## 输入及替换

- 输入：用户 Google Drive 本机挂载路径 `G:\我的云端硬盘\Model\SmallBlueFish-V21-AllExpressions-Acceptance.vrm.part000` 至 `part003`。
- 按编号二进制连接；前三片各 94,371,840 字节，末片 65,882,620 字节。读取第三片时云盘返回“函数不正确”，重读该片后合并成功。
- 合并结果：348,998,140 字节，SHA256 `cf331c0f5c62f818b05bb7f132bd34424bdd99784bb908d5f4651d901f855514`。
- 本地合并副本：`.cache/SmallBlueFish-V21-AllExpressions-Acceptance.vrm`。
- 实际替换：`LingChat-XR/Assets/StreamingAssets/Characters/local-smallbluefish-alpha2/model.vrm`。
- 同目录 `manifest.json` 更新模型及 assets 的哈希、大小、显示副标题和校准模型哈希；保留角色 ID、语音设置、用户设置、资源路径。
- 替换前 model.vrm 与 manifest.json 备份：`.cache/model-backups/pre-v21-5d576214/`。恢复时一并复制这两个文件回原目录。
- 原 VRM、Google Drive 分片与作者控制包不改写；未下载 Blender 文件，未构建 APK。

## 物理兼容

逐项比较旧模型与 V21 的 VRMC_springBone 数据及 hips/head/neck/chest/双腿/Hair/skirt/tail 相关节点的局部位移、旋转、缩放，结果完全一致。保留原有 springPhysics 配置和尾部抓取绑定。这不是 V21 所有姿态的物理视觉验收，旧模型暴露的发片权重接缝问题也不能据此判定修复。

## 表情边界

| 表情 | 当前接入状态 |
|---|---|
| 摸头：eyeSmile + browHappy + mouthSmile 0.5 | 原有 UniVRM 直接权重入口保留，V21 具备全部名称 |
| 挤压：eyeTroubled + mouthOpenCircle | 原有直接权重入口保留 |
| 提起：eyeCircleShock + mouthOpenCircle | 原有直接权重入口保留 |
| cheekSlashes | 按用户要求仍保持关闭 |
| eyeSpiral | 原有晕眩交互会写入静态权重；**没有 V21 逐帧旋转驱动** |
| 星星眼 | 模型具备配方；**尚未接入时间驱动及软件触发入口** |
| 新双侧眼泪 | **尚未接入**。需双侧 phase/strength/enabled 控制，只允许作者验证的中立范围，不能任意与其他表情混合 |
| 12 个旧眼泪别名及隐藏层 | 作者保留旧语义；部分材质无可见几何消费者，不承诺可见效果 |

控制 ZIP 已读并在 `.cache/v21-acceptance-tools` 解包。`controller627` 返回完整 627 权重快照，需宿主提供时钟、统一写入和复位；旧 615 writer 不能混用。当前没有将 JS 控制器伪装成已经在 Unity 执行的代码。后续接入需使用现有脸部唯一写入入口，处理眨眼、口型和互动抢占，验证停止后所有 owned 通道清零。

## 可复现验证

1. 合并文件后 `Get-FileHash -Algorithm SHA256`，必须与上述 SHA 一致，大小也必须一致。
2. 在控制包运行 `node controller627/test-candidate627-planner.mjs` 和 `node controller627/verify-candidate-contract.mjs`：21 个有效场景、11 个原 gate 拒绝、10 个新增范围拒绝、4 个合同篡改拒绝通过；627 合同通过。
3. Python 运行 `verification/verify_vrm.py <合并VRM> <报告目录>`：实际文件全部表情绑定与网格键名校验通过，缺失绑定 0。
4. `./tools/run-m1-check.ps1 -EvidenceDirectory evidence/v21-model-import-2026-11-04 -Method LingChatXR.Editor.V21ModelProbe.Run`：隔离 Unity 项目实际加载，检查所用表情存在，写入中立、摸头、挤压、提起、复位，输出截图及 result.json。
5. 主项目重新进入 Play 后检查 V21；此前运行中的模型不会通过替换磁盘文件自动热换。实际观感由用户验收，Quest 体积、耗时、内存及帧率没有实测。

模型和控制包均为本地资源，不随源码提交上传。模型约 349 MB，不能将电脑导入成功等同 Quest 性能通过。

## 本次实际结果

隔离 Unity 6 加载耗时 28.595 秒，627 个表情均导入，5 个静态状态写入/复位检查通过，无非有限网格范围。但截图检查发现：震惊眼可见，摸头组合没有显示预期闭眼，圆口组合没有显示预期张嘴。上述静态入口因此只能标记为“名称和写入兼容，视觉适配未通过”，不能按测试 passed 字段宣称表情已全部可用。后续需分别核对形态键实际变形、Unity 求值与渲染刷新及作者预览；不擅自改模型形态键掩盖问题。
