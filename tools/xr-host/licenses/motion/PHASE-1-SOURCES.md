# 第一阶段开源复用记录

固定版本取自 2026-10-22 施工方案；未新增 npm、模型或服务依赖。

- [Rexclaw idle_motion.js](https://github.com/Codemarchant/rexclaw/blob/9e475cc97c6e78404e7aae282e77ca2ca8bc263d/web/src/services/idle_motion.js)，MIT，Copyright (c) 2026 Codemarchant。`conversation-signals.ts` 抽取 smootherstep、低频 value noise/drift、非对称呼吸曲线。改为 TypeScript，导出少量纯函数；删除全身摆动、头眼、嘴部、眨眼、腿与随机叹气控制。呼吸周期和状态幅度在本地控制器校准。完整许可如下。
- [Hanami fades.ts](https://github.com/Undi95/Hanami/blob/5cf4afaac1ad8499d0890101a7c93762bf1dc8b0/client/src/scene/fades.ts)，AGPL-3.0：本轮读取固定源码，只参考区分状态过渡、从当前权重继续混合的设计；没有复制其文件、曲线或时长表，也未采用 overteMath。实现延用 LingChat 现有指数包络，加上上述 Rexclaw smootherstep。
- [TalkingHead](https://github.com/met4citizen/TalkingHead/blob/91796631b569ec70aeba1e43f6fa1987e5e4115c/modules/talkinghead.mjs)、[MotionEngine](https://github.com/lhupyn/motion-engine/blob/d3abb9e450747c7ede994c704416daf63b322064/src/director.js)：按既有研究记录借鉴状态/稳定风格分离、允许无动作、通道忙碌、冷却和去重。未复制源码或引入其音频/渲染/逐句分类系统。
- HAST 保留既有固定接入；新的持续身体控制和节奏候选不调用 HAST plan。

## Rexclaw 原始许可

MIT License

Copyright (c) 2026 Codemarchant

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
