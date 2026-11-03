# 第二阶段动作质量复用记录

2026-10-23 更新；以用户“不手动 K 帧、不逐关节硬调，优先复用”为当前约束。

- **挥手成品**：Microsoft Rocketbox 的 f_wave_01，采用 Hanami 已转换好的 VRMA 文件，固定版本、SHA256、处理沿革和 MIT 许可见 [素材来源](../../src/assets/vrm/README.md)。本项目不改动作关键帧，原速一次播放；这段较高的单手招呼仍待用户认可，不标为自然度通过。
- **标准加载与播放**：新增官方 [@pixiv/three-vrm-animation](https://github.com/pixiv/three-vrm/tree/v3.5.5/packages/three-vrm-animation) 3.5.5，与现有 three-vrm 同版本；复用 VRMAnimationLoaderPlugin、createVRMAnimationClip、Three.js AnimationMixer。适配层只负责已有舞台的所有权与进出/取消，不生成姿势。
- **已认可的点头/摇头**：保留 [MotionEngine gestures.json](https://github.com/lhupyn/motion-engine/blob/d3abb9e450747c7ede994c704416daf63b322064/src/gestures.json) 中既有配方与 smootherstep 采样，未重调头部动作。其 MIT 许可见下方。原 sampleLocalWave 和手写肩肘腕姿势已移除。
- **Rexclaw**：继续复用一期的 smootherstep 思路，固定来源与完整许可见 [一期来源记录](PHASE-1-SOURCES.md)。未自行重写动作库。
- **HAST 历史路线**：固定 checkout f4d5c9aa32f9d04e249585e0b2e6d4c56572ca51。接手已存在的候选优选实际返回 111_37、区间 0–54、SHA256 523756759fc6422635f1e00added94698d2ff72c82774883fc5cfeca3bb2c6ff；原文的 14_24/160–221 只是旧轮次记录。原有 20Hz 采样、slerp 及取消仍保留，手工重分配旋转与胸肩跟随已移除。明确挥手当前走完整 VRMA，不把它称为 HAST 生成。
- **HAST 全身求解试验**：复用上游 controllerV9 的离线诊断留在 .local；未加入正式桥，未复制外部受限代码进产品构建，未用数值可行性冒充动作自然。

完整证据、用户否决记录与剩余问题见 [本轮报告](PHASE-2-SEMANTIC-ACTION-QUALITY-REPORT.md)。

## MotionEngine 原始许可

MIT License

Copyright (c) 2026 lhupyn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
