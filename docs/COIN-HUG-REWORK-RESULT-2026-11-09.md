# PC 双手抱币返工结果 · 2026-11-09

**默认币径 8 厘米，角色身高 24 厘米。PC 可试玩，外观待玩家确认。** 本机连续回放里，小鱼走近硬币、低身伸手、从两侧夹住币缘后抬到胸前，保持 23.1 秒，随后玩家模拟掌拿走硬币。9 厘米在同一套角色、动作和判定下够不到两侧，没有进入抱持。

## 试玩与观察

Unity 6 编辑器选 **YuChat → 玩具箱 → PC 硬币追逐**，在 Game 视图点击桌面放下小鱼，再点右上 **玩具箱 → 取出硬币**。让慢币停在她面前，留出走近的空间。想让币滚动，在桌面按左键拖动后松开。想拿走抱着的币，把指针移到币面，按住 H 再移动；右键提起小鱼也会打断抱持。播放许可里需允许拾取和搬运动作。

- [36 秒正常速度连续录像](../evidence/coin-hug-final/YuChat-Coin-Hug.mp4) · [同机位旧／新正面、45°、90°](../evidence/coin-hug-final/comparison.jpg) · [握持局部](../evidence/coin-hug-final/grip-detail.jpg)
- [新版正面标注](../evidence/coin-hug-final/eight/grip-0-debug.png) · [45° 标注](../evidence/coin-hug-final/eight/grip-45-debug.png) · [正侧标注](../evidence/coin-hug-final/eight/grip-90-debug.png) · [旧版调试正侧](../evidence/coin-hug-rework/baseline/grip-90-debug.png)
- [逐帧双手与蒙皮距离](../evidence/coin-hug-final/eight/grip.csv) · [9 厘米逐帧记录](../evidence/coin-hug-final/nine/grip.csv)。影像、调试图和 CSV 存在施工机本地。
- 固定图使用同一模型、站位、身高和正交镜头，观察距离 0.65 米。三张新版图取自抱稳满三秒的同一帧，只改变观察方位。旧姿态腰弯得更低，同镜头下脸部与手的高度会不同。录像为隔离 Unity Play Mode 脚本输入，832 × 624、20 fps、720 帧，一倍场景时间。

## 手在哪里

旧版半径 3.1 厘米，抱稳末帧左右指根中心离实心币表面分别还有 7.93、9.96 毫米。旧报告的 1.1 毫米取自拾取瞬间。改为在币局部空间指定左右握区，复用 kay_pickup 和 ual2_walk_carry_loop，按 Humanoid 臂长解双肘和手腕，让指尖从外侧搭上币面。拾取阶段由物理币提供目标，双手同时就位后，抱持轨迹接管币、双手跟随各自握区。玩家取币、抓鱼、收回玩具都会撤销原持有者。

| 8 厘米稳定阶段，连续 462 帧 | 左 | 右 |
|---|---:|---:|
| 指根中点到本侧握区，中位数 | 0.86 mm | 0.66 mm |
| 指根中点到本侧握区，最大值 | 1.47 mm | 1.49 mm |
| 手部蒙皮顶点到握区，最近点距离中位数 | 1.10 mm | 1.71 mm |
| 皮肤进入实心币的最大深度 | 1.04 mm | 0.80 mm |
| 袖口及手饰采样进入实心币的最大深度 | 3.00 mm | 3.00 mm |

正面与 45° 可以看到双手搭在币左右边缘，脸和币面记号可见。正侧能看到近侧手与币缘，长发遮住了部分手臂，远手与近手重叠。**币缘仍有细小袖饰穿入**，数值包含被前层遮住的顶点；表中穿深是到圆柱边界的有符号距离绝对值。这个版本是否像自然抱住，由玩家看正常材质实机画面确认。

## 回归和复现

- 已通过正式入口投放、滚动追逐、绕行与无路等待、桌边足迹、遮住币心但露出边缘时仍可跟踪、整掌盖住暗移与露出、持续盖住 12 秒、收回、抓起再放下、重置放置及换角。[玩法记录](../evidence/coin-hug-final/cover-check/result.txt)
- 单独从抱币中抓鱼、关闭动作许可、伸手期间收回，三项通过。另一次正常拾取角色移动了 13.62 厘米，币心被抬到桌面上方 9.70 厘米。[中断记录](../evidence/coin-hug-final/interrupt/hug.txt)
- 桌面核心回归 2698 个断言通过。LingChat 过渡源码 SHA256 前缀 971CDCE6，许可检查脚本 74D47FFB，本轮 5/5。已核对 DOT 任务单，本机未找到已交付的双侧标定坐标。运行时使用玩具配置及 Humanoid 骨骼，沿用 V21 模型和既有动作资源。
- 在仓库根目录运行以下 PowerShell。验收工程使用项目固定的 Unity 6 和本机已有的试玩资产。测 9 厘米时将直径改为 0.090；本轮它在双手接触阶段退出，预期会留下 CSV 并报未取得三秒抱持画面。

```powershell
$env:XR_COIN_GRIP_REVIEW='1'
$env:XR_COIN_MOVIE='1'
$env:XR_COIN_DIAMETER='0.080'
& tools/run-m1-check.ps1 -EvidenceDirectory evidence/local-coin-review -Method LingChatXR.Editor.CoinChaseProbe.Run
Remove-Item Env:XR_COIN_GRIP_REVIEW,Env:XR_COIN_MOVIE,Env:XR_COIN_DIAMETER
& tools/run-m1-check.ps1 -EvidenceDirectory evidence/local-coin-play -Method LingChatXR.Editor.CoinChaseProbe.Run
```

本批验收范围是 PC。提币带鱼排在玩家认可本次抱姿之后。
