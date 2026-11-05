# V21 实际试玩入口与裙摆检查

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
