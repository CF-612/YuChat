# UniGLTF 0.131.4 本地生命周期修正

源为 UniVRM tag v0.131.4，提交 efd20f62f1bf0ac3fcc34de1e497324c5197d146。保留 LICENSE.md 与全包原件结构，无升级。

生命周期修正：Runtime/SpringBoneJobs/InputPorts/FastSpringBoneBuffer.cs 增加已释放标记；BackupCurrentTails 跳过已释放对象，Dispose 幂等。原实现中队列延迟删除/替换，在 Dispose 后再次分配备份数组，导致每次相关事件遗留原生内存。证据见仓库 evidence/native-physics-s2/leak-before。

碰撞修正在 `Runtime/SpringBoneJobs/SpringBoneCollision.cs`，胶囊最近点按待检测的预测端点求取。

姿势写回修正在 `Runtime/SpringBoneJobs/PushTransformJob.cs`。求解器同时保存世界与局部旋转；父子节点批量写回时直接使用已求出的局部旋转。验收用 `V21WalkDiagnostic` 在同一个物理步内比较求解缓冲与实际 Transform。尺度与中心重置由 YuChat 的 PortableSpringPhysics 负责。
