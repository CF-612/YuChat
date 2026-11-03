# 官方控制器与输入资源

复用 Unity XR Interaction Toolkit 3.4.1 的 Starter Assets：UniversalController、左右控制器 prefab、ControllerAnimator、XRI Default Input Actions。来源为当前官方包 `Samples~/Starter Assets`；LICENSE.txt 保留官方许可证。

输入文件重命名为 QuestInput.inputactions，保留原 GUID 以保留控制器按钮动画引用。ControllerAnimator 位于 XR/ThirdParty，未改算法。控制器材质只在准备场景时转换为本项目已有 Built-in Standard，保留示例几何及按钮绑定；Pointer 是本项目射线/标定点材质。没有导入示例的移动、传送或整套场景。
