# V21 实际试玩入口与裙摆检查

## 当前 PC 操作

1. 在 Unity 6 打开 Assets/Scenes/FeiyuPreview.unity，按 Play，切到 Game 页。
2. 模型就绪后，点 Game 右上角蓝色 **测试 ARDY 行走**。它会自动放置小鱼。按钮旁显示 Core8 冷启动进度，采集普通走路的 16 帧后，显示 **已播放 N 帧 ARDY**。
3. 同一个按钮随后变成 **改为侧方目标**。点它可以观察真实生成动作转向，点 **停下** 结束。在 Game 空白处按住鼠标中键拖动，可以从侧面看裙摆。直接点桌面让角色走过去，观察的是日常行走素材。
4. 要在别的本地角色包副本复现同一 V21 配置，先运行 tools/prepare-spring-materials.ps1 再重新进入 Play。实验仅在 PC 编辑器显示。

## 到达待机与生成等待复验

PC 实验以开头 16 帧普通步行测得的上身倾角和髋部高度作为参考，限制后续极端前倾和高度漂移。转向与步态仍由 Core8 生成。到达后、手动停止时，通过已有行走退场过渡回到待机。若连续 50 个生成帧未继续接近目标，则停止并提示原因；抓取立即撤权。

实时 worker 使用 6 个去噪步骤；离线对照仍可用 10 步。在同一次 Play 内停止后可复用已加载的 worker。主动用过实验后，同一个编辑器会话再次进入 FeiyuPreview 会在读取角色时并行预加载。退出 Play 释放进程。

隔离 Unity 6 V21 实测（evidence/ardy-stance-final）：点击到首个已播生成帧，冷启动 22.44 秒，同次 Play 第二次 1.11 秒。改侧向目标到首个新帧约 0.50 秒。第二次从生成开始到到达共 25 帧，髋至头连线相对竖直最大 8.2°，有 6 个播放时刻等待补帧。到达后及手动停止后，均验证身体已交还、姿态过渡已结束；另测请求期间提起当帧清空缓冲。截图 arrival-idle.png 显示该次实际到达后的角色。生成动作的全部姿态、裙层穿插和 Quest 不在此验收范围内。

复验时在下方侧面对照命令前另设 `$env:XR_ARDY_STANCE='1'`，换一个新的 EvidenceDirectory。要回到侧面对照，移除这个环境变量。

## 本次侧面复查

用 Game 按钮从未放置状态启动，检查按钮中心实际可命中，送入 UI 事件，等到生成帧播放，再通过同一个按钮改目标。隔离 Unity 输出 Game-start.png、Game-generated.png 和 ardy.txt。

随项目分发的 UniVRM 分支在检测外部胶囊时，用裙骨根部选择最近的胶囊轴点，却用裙骨末端检测距离。两者处于不同高度时可能漏碰。现在根据待检测的末端选轴点。回归用例检查骨根在胶囊底部、预测端点位于中段的情况。V21 裙摆配置的端点接触半径从模型默认约 0.0135 调到 0.035（模型单位）。回放同一组实际生成姿态时，0.09 和 0.16 会明显撑起裙摆，未采用。弹簧骨求解的是骨链与碰撞体，全裙三角面间没有自碰撞保证。大幅抬腿、转向俯身和初次进入时的外观仍须分开验收。

复现本次侧面对照：

```powershell
$env:XR_V21_TEST_ARDY='1'
$env:XR_SKIRT_SIDE='1'
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/v21-game-final -Method LingChatXR.Editor.V21WalkDiagnostic.Run
```

replay-0、replay-35、replay-55 图片依次为作者半径、0.035、0.055，同一编号为同一生成姿态。evidence 为本机验证文件。

## 上次导演窗口与测量记录

PC 菜单 **YuChat → 动作实验 → 直接试玩 ARDY 接步** 打开同一导演窗口、进入 PC 场景，模型就绪后开始。Game 右上角显示实际控制者与已播放的 ARDY 帧数。冷启动时保持待机；服务就绪后采集 16 帧固定走路，再提交真实生成结果。进程错误保存在 D 盘项目的 .cache/ardy-horizon8/last-worker-error.txt。切 Game 与编辑器面板时保留虚拟桌面，释放鼠标触摸或持有。真实 XR 输入仍走 Quest 生命周期。

复现使用隔离 Unity 的正常桌面输入，从 ExecuteMenuItem 打开上述菜单，V21 SHA 为 cf331c0f5c62f818b05bb7f132bd34424bdd99784bb908d5f4651d901f855514。A 播出 20 帧再改 B，总计播出 60 帧。该次录像运行改向到新目标首帧 1.12 秒。录像中 B 阶段实际左大腿控制骨 X 角约 −81° 至 +25°。结论限于本机隔离 Play Mode；上一批旧鱼时间不能套到 V21。

放置时先就位和定向，再启用角色；重置时清空根位移历史，等当前实例的弹簧尾端进入 UniVRM 缓冲后才完成播种，并对复位时的异常长帧暂缓写回。V21 本地校准沿用原生 SpringBone，使用绑定到上下腿关节的原生大腿胶囊。原球位于大腿关节中心、半径 0.0585；本机只读绑定姿态测得左大腿主要影响的皮肤及内裤顶点到大腿轴的 98 百分位距离分别约 0.0974 和 0.0999，因此胶囊半径取 0.10（模型单位）。VRM 文件本身保留，原 manifest 在 .cache/model-backups/pre-leg-capsule。

验收时 44 个关节应用物理材质。原配置下实际行走时采样到裙骨约 6–11° 的变化。一次相同蹦跳周期分别用原球和新胶囊测试，按裙骨主要加权的蒙皮顶点计算，120120 个点次在两种配置下都没有进入测量用大腿胶囊超过 0.5 mm。此项仅测大腿近似体，不含脚、裙层自碰撞或全部姿态。截图中的穿插和初次进入的异常姿态未稳定复现，无法据此认定全部消失。

可复现命令（仓库根）：

```powershell
$env:XR_V21_TEST_ARDY='1'
./tools/run-m1-check.ps1 -EvidenceDirectory evidence/v21-user-recheck -Method LingChatXR.Editor.V21WalkDiagnostic.Run
```

本地验证证据：evidence/v21-gameplay-verified 中的 Unity 日志、spring.json、coverage.json、ardy.txt、demo-timeline.json，以及 V21-ARDY-menu.mp4。仅 PC 源码与本地模型配置更新，本批未创建安装包。
