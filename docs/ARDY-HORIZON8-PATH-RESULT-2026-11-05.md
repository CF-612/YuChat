# Horizon8 无文本接步与抓放，2026-11-05

在固定权重和旧鱼上完成隔离 Unity 6 PC 试玩。结论是继续即时实验。接步和改向有真实画面，抓取可同帧打断。转向仍有脚滑与等帧，玩家自然度待验收。

普通步行 locomotion_walk，SHA256 0d68383cffb93d9c1d1e98ab3cdd303562908bb4cabd24d9ae1eca438797eb92，循环 0.8 秒，角色高 0.24 m，腿长 0.05935 m，速度 0.14276 m/s。实际播放 48 帧，以 20 Hz 取最后 16 帧。复核 LingChat 0478d5dd 的过渡和许可指纹，原许可测试 5/5。依赖新增在独立 D 盘目录，总缓存约 858 MB。

官方 load_model(core8,text_encoder=False) 实载成功。沿用官方空文本分支语义，文本为全零 [1,1,4096]，bool 掩码全假。历史 [1,16,330]，Root2DConstraintSet 提供位置与朝向。autoregressive_step 在 20 FPS 下返回 8 帧。相同已消费历史和种子比较桌面 A (0,.31) 和 B (.44,.14)。Unity 世界位移由 Core 逐帧检查，空间根朝向固定。B 骨盆在约 1 秒达到约 89°。固定到生成的首步平面位移约 7.8 mm。较低脚的平面速度 P95 在 A/B 分别约 15.8、28.2 cm/s，此指标包括换支撑脚，不能当作纯滑移速度。

10 个去噪步，冷启动 12.06 秒，首步 411.7 ms，40 个暖步均值 185.1 ms、P95 202.9 ms（黄）。CUDA 张量峰值 927 MB、保留峰值 950 MB，进程工作集峰值 2.63 GB。初版 4 帧预取，现场断供 25 次；提前至 7 帧预取后的单次录像运行，断供 12 次。B 首帧 327 ms，骨盆相对改向前超过 12° 为 507 ms。未回与正在播两次提起均同帧移交控制，撤销缓冲。实际拒收四批晚到的八帧结果。松手就地恢复，停止 ARDY 后固定走和抓放通过。两次运行不能作为严格性能对照。

在主 PC 导演试玩窗口，先放置，再按“固定走路 → ARDY 接步”，点“切换前方 / 侧方目标”；桌面右键按住角色提起，松开落下。“停止路径试玩”退出。使用标准源项目及 D 盘 P0 worker。开发复现：使用 tools/run-m1-check.ps1 分别运行 LingChatXR.Editor.ArdyWalkProbe.Run 和 RunLive；tools/ardy/path_walk.py 输入 walk.json、输出目录；设置 XR_ARDY_SAMPLES 为 samples.json 可在 Run 中回放。render_trial.py 将 RunLive 的实际时间序列导出 MP4。隔离依赖可用 worker pip install --no-deps --target .cache/ardy-horizon8/deps 安装 peft==0.19.0、accelerate==1.15.0、vector-quantize-pytorch==1.25.2、einx==0.3.0、einops==0.8.2、frozendict==2.5.1。

本机证据在 evidence/ardy-table-ab、ardy-table-replay 和 ardy-live-final；连续视频 ARDY-Horizon8-PC-live.mp4 为 31 秒。录制走真实 PC Play Mode，通过公用抓取入口注入抓放。尚待玩家亲手评价。
