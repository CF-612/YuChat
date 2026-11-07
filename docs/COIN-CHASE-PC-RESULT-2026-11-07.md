# Token 硬币 · PC 试玩与验收（2026-11-07）

## 试玩

Unity 6 编辑模式菜单选 **YuChat → 玩具箱 → PC 硬币追逐**。进入 FeiyuPreview 后，在 Game 画面点桌面放好小鱼，打开右上角的 **玩具箱 → 取出硬币**。需要更多画面空间时按空格收起对话框。

- 在硬币或桌面空白处按住左键，沿桌面拖动，松开即从按下处向拖动方向滚出。拖动长度控制初速。
- 按住 H，鼠标在桌面上的位置就是模拟手的位置。把手放在小鱼和币之间可以挡路。盖住币心后继续移动鼠标即可带币暗移，松开 H 揭开。
- 右键提起小鱼，滚轮调整提起高度，松开右键放回。取回硬币会把同一枚币放回身前，收回会结束追逐。
- 入口支持本机已准备行走素材的角色。本次录像使用 SmallBlueFish V21 AllExpressions，模型 SHA256 为 cf331c0f5c62f818b05bb7f132bd34424bdd99784bb908d5f4651d901f855514。

## PC 验收

隔离 Unity 6 Play Mode 通过正式玩具箱按钮和桌面鼠标输入处理函数进行回放。50 秒连续录制，20 fps，场景时间 1 倍速，字幕注明输入操作。模拟手、遮挡、贴纸与角色运动均由运行时代码产生。行为演示使用脚本输入，鼠标手感及逗宠物的自然度待玩家体验。

| 场景 | 结果 | 实际核对 |
|---|---|---|
| 取出、重复取出、滚动 | 通过 | 正式按钮在 Game 面板可命中；再次取出复用实体；左键按下和松开经过物理投放入口 |
| 发现、追逐、近身停稳 | 通过 | 鱼从头部视点发现目标，转向、移动并留出足迹和币半径；目标更新复用同一动作所有者 |
| 挡路、绕行 | 通过 | 手挡在脚前时等待；留出空间后沿手掌一侧绕行；绕行线段校验桌面边界 |
| 盖住暗移、异侧露出 | 通过 | 跟踪日志记录币位置与最后可见位置；隐藏期间记忆固定；揭开后重新定向；一次稳定遮挡触发一次疑问贴纸 |
| 持续遮盖直到收回 | 通过 | 单独保持 12 秒并带币平移，记忆坐标固定，寻找只触发一次，盖住直接收回后目标和移动均已清除 |
| 抓取与放回 | 通过 | 追逐中右键提起，同帧移动停止；滚轮提起、右键释放、物理下落，落稳且重新看见后继续 |
| 出界与收回 | 通过 | 币滚过活动区边缘，角色每个空闲采样帧的足迹都合法；取回后可继续，收回停止追逐 |
| 重置、换模型 | 通过 | 重置当帧清除玩具；切换到 local-physics-reference 后复查硬币和移动均已清理 |
| 生成动作对照 | 本批选用固定动作主线 | 正式走跑、转向与减速通过，未用 ARDY 服务生成本次录像 |
| 玩家体验、Quest | 待体验 / 超出本批范围 | 此次交付 PC 试玩 |

## 复用核对

- YuChat 基线 b3178ec。PetInteraction 裁定空间位置及足迹，PetLocomotion 负责走跑、转向、减速和撤销。本地目录 local-locomotion 的 skip、run、left、right 和 idle 继续由既有播放器加载。玩具更新有独立入口，不登记玩家重复敲桌的加速计数。
- LingChat vrm-mvp 参考工作树 HEAD 为 0478d5dd010aaad3c33f1ff89ff81b0593928b58。重新读取 2026-10-30 连续互动交接，核对 motion-transition.ts 内容 SHA256 前八位 971CDCE6，许可检查脚本前八位 74D47FFB。本机运行 check-motion-library-preferences.mjs 通过 5/5。动作许可与过渡来自既有动画系统，物理硬币和视线遮挡在 Unity 桌面坐标下实现。
- 复用 FeiyuAvatar 的眼头平滑与摸头优先级，MoodSticker 的“疑惑 → 疑问”映射和原图集。PC 的模拟手承担遮挡和路障；稳定遮挡后只把最后可见位置交给角色。
- CoreChecks 官方脚本通过 2698 项断言。模型、已购动作及贴纸原文件未随本次源码发布。

## 复现

在仓库根目录运行以下命令。PC 验证依赖已准备好的本地模型、动作和贴纸资源。

```powershell
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/my-coin-chase -Method LingChatXR.Editor.CoinChaseProbe.Run
./tools/CoreChecks/run-checks.ps1
ffmpeg -framerate 20 -i evidence/my-coin-chase/frames/%04d.jpg -c:v libx264 -crf 18 -pix_fmt yuv420p evidence/my-coin-chase/coin.mp4
```

本机交付目录为 evidence/coin-chase-pc-2026-11-07。YuChat-Coin-Chase-PC.mp4 为正常速度演示，toybox.png 为实际 Game 操作入口，trace.csv 为逐帧状态，result.txt 为场景断言结果。
