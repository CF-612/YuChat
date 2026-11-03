# UniGLTF 0.131.4 本地生命周期修正

源为 UniVRM tag v0.131.4，提交 efd20f62f1bf0ac3fcc34de1e497324c5197d146。保留 LICENSE.md 与全包原件结构，无升级。

唯一上游代码差异：Runtime/SpringBoneJobs/InputPorts/FastSpringBoneBuffer.cs 增加已释放标记；BackupCurrentTails 跳过已释放对象，Dispose 幂等。原实现中队列延迟删除/替换，在 Dispose 后再次分配备份数组，导致每次相关事件遗留原生内存。证据见仓库 evidence/native-physics-s2/leak-before。

求解算法、作者数据、渲染及模型无变化。尺度与 center 重置坐标适配在 LingChatXR/Runtime/PortableSpringPhysics.cs；本包没有加入角色专用条件。
