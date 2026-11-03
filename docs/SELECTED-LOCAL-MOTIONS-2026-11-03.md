# 本地候选动作 · 2026-11-03

从 KayKit Character Animations 1.1 的 Rig_Medium 和 Universal Animation Library 2 Standard 的原地版本挑选九段。前者提供短跳、大跳、拾取、蹲伏、潜行以及 Idle_A、Idle_B；后者提供抱物行走和抱臂待机。完整跳跃保留局部髋部起伏，沿用原空间根控制。抱物行走是演出片段，后续硬币握点和碰撞仍由玩具逻辑负责。

先准备本机既有动作目录，再执行 `python tools/motion-import/prepare-selected.py <下载目录>`。脚本拆出单段旋转轨道与髋部位移，复用 UniVRM Humanoid 重定向，按稳定 ID 更新本地动作目录；不导入源模型网格。素材归档与转换后的动作保存在本地。若重建原动作库，请再执行本步骤。新增三段循环待机可在默认待机设置选择，另外六段进入场景动作候选。

验证入口：`./tools/run-m1-check.ps1 -EvidenceDirectory evidence/selected-motion-recheck -Method LingChatXR.Editor.PetTouchProbe.RunSelectedMotions`。当前 Mouth24 模型九段均实际载入并采样，两个跳跃检测到髋部垂直位移。此检查不评价观感，也未将潜行或负重走变成自动导航状态。
