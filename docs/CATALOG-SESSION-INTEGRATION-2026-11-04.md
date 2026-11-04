# 角色清单与会话真实接线验收（2026-11-04）

承接占位模型批次，继续原重构计划的配置与会话测试，不改新动作/播放方案。

新增 tools/run-catalog-integration.ps1：使用同一宿主源码、独立 Cargo 入口和可执行名称，复用 D 盘依赖缓存。用户实际运行的 target/c0/debug/lingchat-xr-host.exe 不覆盖、不停止。测试宿主仅监听独立回环端口，使用合成的 155 条历史、105 条句段和三个角色；provider 指向回环关闭端口，不调用 LLM/TTS 或云端，也不读取真实会话。

真实宿主 HTTP 10 项通过：配置角色、私有人设不回传、旧 JSON 迁移、近期/早期分页、进程启动后的失效游标历史恢复、空当前轮快照、401/409、原文件及备份字节保持。

Unity 6 隔离运行 10 项通过：占位模型实际加载、HTTP 拉取第三角色、首次历史与分页、设置下拉框切换第三角色并保存后关闭、身份/历史隔离、角色选择持久化、重连保留、切回旧角色恢复历史、不自动播放历史音频。

复现：`./tools/run-catalog-integration.ps1`（默认独立端口 17864，占用则拒绝，不停止其他服务；可用 -Port 指定其他空闲端口）。

证据 evidence/catalog-integration-20261104-204444/：host-checks.json、unity-checks.json、构建及 Unity 日志。测试进程在 finally 退出，仅处理自身 PID。

工具 run-m1-check.ps1 新增可选 HostConnectionPath；未提供时保持原行为。没有更换生产宿主、修改私有设置、APK、真实推理、头显或界面观感验收。
