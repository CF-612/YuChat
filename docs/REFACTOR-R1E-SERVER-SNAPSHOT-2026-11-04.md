# R1e 服务端恢复数据（2026-11-04）

本批仅交付新播放方案可复用的服务端恢复契约，不改客户端播放、动作或语音引擎。

- GET /snapshot?sessionId=...&expectedGeneration=...：鉴权后返回 sessionId、generation、busy、latestCursor、snapshot。身份不符返回 409；无当前轮（停止或服务重启）明确返回 null。
- snapshot 含 turnId、generation、ended、sentenceStart、sentenceEndExclusive、sentences 和轮次 error；句段含 index、中文正文、emotion、motion、audioState（pending/ready/silent/failed）、audioFile、error。不返回日语配音正文。
- 快照在句段开始时登记，完成/失败更新；不依赖最多 1024 条的事件环。非零句序重试保留实际起点。结束时未完成句转为可重试失败，不永久 pending。
- /events 增加 latestCursor、sessionId、generation、busy；游标失效返回同轮快照与旧版历史兜底。历史读盘后复核代次和游标，若期间发生变化返回 409 要求重试，避免混合两个时刻的数据。
- 停止清空当前快照，旧代次事件不能写回；新轮新建快照。局域网网关允许鉴权后的 /snapshot。

## 验证

36 项宿主回归通过。淘汰首句/中间句至仅剩末尾事件后，仍能找回三句及完整表现信息；测试非零起点 7、结束范围 10、停止后过期查询 409、无权限 401、旧音频事件拒绝、已完成音频保留和断线后 pending 转失败。

命令同 R1d：`C:/Users/fangcheng612/.cargo/bin/cargo.exe test --offline --locked --target-dir tools/xr-host/target/c0 --manifest-path tools/xr-host/Cargo.toml`。

## 明确未验收

未接入 Unity 待播队列重建、已消费去重、正在播放保留；这些属于用户允许跳过的新播放方案范围。客户端仍使用原历史兜底，不能宣称真机断线无重复音频或自动续播通过。快照仅为当前进程当前轮；进程重启后不会重新播放旧轮。没有 APK、没有真实服务重启或私有数据迁移。
