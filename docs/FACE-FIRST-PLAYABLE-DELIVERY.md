# 第一批交付 · 说话时摸头、提起和放下

打开 FeiyuPreview 场景并进入 Play Mode，选择「YuChat → 动作实验 → 自然语言导演试玩」，放置角色后展开「表情测试 · 离线语音」。播放含停顿样句，左键摸头，Game 视图右键按住身体提起、松开落地；保持提起时可回到窗口更换手动心情。页面显示眼和嘴的控制者。自然语言页仍使用真实导演。此批表情选择为手动测试与现有聊天情绪，不含新的 LLM 情绪决策工具。

本机离线样句来自现有角色问候语，增加 0.55 秒停顿后重复，用同一 AudioSource 和 RMS 口型驱动。新机器可用 tools/prepare-face-sample.ps1 -VoiceFile <本地已获准角色语音> 准备，不随公开源码分发原音。

[能力表](FACE-CAPABILITIES-FIRST-PLAYABLE.md)按两份实际 VRM 提取。Mouth24 的局部特殊眼阻挡眨眼而允许发音；圆嘴阻挡发音。保持语音时提起圆嘴占用 0.38 秒，随后释放给 Aa，眼型继续独立保持。没有整组局部键时回退标准 VRM 眼嘴。害羞与心动共用 blush，手动害羞加偏移视线，心动使用开心眉与嘴角。默认轻抚仍关闭三斜杠。源文件 SHA 仅供本批脸部能力表核对，既有材质配置尚有模型身份判断。

效果信号按变化边缘触发一次。旧鱼使用 vee_Question22、vee_Anger Mark、vee_Exclamation；无对应键的模型显示短暂文字标记。已核对桌面项目分通道及效果文件指纹 1C6470A5、FA7EB11D、B4287190、22E99DBE。未确认可单独再分发的 LingChat 疑问、生气和惊讶原动画，当前文字标记是占位。DOT 后续请优先确认特殊眼与 Aa 并发的实际外观、心动眉嘴的安全混合量。

程序断言：evidence/face-first-playable-final/checks.json。验证了语音启动、提起后的 Aa、停顿、落地恢复新心情、替换样句及切到旧模型清除旧预览。真实导演与缓存重播/抓取回归在 evidence/face-director-final。完整画面 evidence/face-first-playable-final/talking-held.mp4，按实际音频播放时钟复用同一 WAV 配音（6.43 秒画面、5.70 秒音轨），不是设备扬声器收音。目视为说话、摸头、斜提、下降和落稳。细微嘴形、符号大小和害羞/心动是否容易区分，由玩家判断。无 Quest 验收或 APK。

## 心情贴纸更新

已用 LingChat 的 public/pictures/animation 动画 WEBP 替换本批早期的文字标记。运行 tools/prepare-mood-stickers.py 并传入 LingChat 源码目录，可从 src/controllers/emotion/config.ts 复用心情映射并生成 Unity 本地图集。生成目录位于被 Git 忽略的 LocalLingChatEffects/Stickers。本机准备了 14 个动画和 26 个心情名称。窗口中选择疑问、生气、惊讶、害羞或心动并点预览即可比较。停留同一心情时仅播放一遍，清除后再选择可重播。隔离 Unity 实际验证五种贴纸换帧、结束隐藏和持续状态不重启，截图位于 evidence/mood-sticker-verified。
