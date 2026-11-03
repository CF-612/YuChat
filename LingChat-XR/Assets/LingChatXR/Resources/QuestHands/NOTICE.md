# XR Hands 官方资源复用

来源：Unity XR Hands 1.7.3 `Samples~/HandVisualizer`，安装包 SHA1 `3e241be2064283bf42e309918d6882bac2a52ba1`。许可证见本目录 LICENSE.txt。

左右手骨骼模型和 Tracking 预制体保留原 GUID 与关节映射；运行时使用官方 XRHandTrackingEvents / XRHandSkeletonDriver / XRHandMeshController。
QuestProject.Prepare 仅将示例材质适配现有 Built-in Standard，设置淡蓝色、低高光；没有重新实现关节跟踪或捏合识别。
输入复用原 XRI Default Input Actions 的 Pinch/Poke/Aim 与 MetaAimHand 按钮，触碰和选择继续交给 XRI。
