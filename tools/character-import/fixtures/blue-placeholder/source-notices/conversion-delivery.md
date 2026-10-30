# 蓝色大肥鱼 PMX → VRM 转换交付报告

日期：2026-10-19　状态：**基线转换、检查与预览验收完成（聊天用途基线上可用）**

## 输入

- 源包：`D:\BaiduNetdiskDownload\蓝色大肥鱼MMD模型.zip`
- 解压副本：`blue-fat-fish\input\`（`蓝色大肥鱼1.12.pmx` 4.2 MB + `textures\` 42 张 + `toon2.png` + `toonskin.png` + 使用规约.txt）
- 来源/许可：模型制作 水原镜；MMD 转换绑定 耶栗Yaki。**使用规约禁止商业使用、禁止用于非 MMD 视频制作的用途**；转换不改变原许可。

## 输出

- 交付 VRM：`D:\方的世界\vrm-conversion-lab\blue-fat-fish\output\BlueBigFatFish.vrm`（13.41 MB）
- 预览：VRMify `http://127.0.0.1:57328/`（后台运行，关闭页面一分钟后自动退出）
- 工具：VRMify `cfce6bb3`（PMX→VRM 纯 Python）；Python 3.11.14；Node 22.23.2

## 转换概况（`evidence/convert.log`）

- 源：顶点 44618 · 三角面 65083 · 骨骼 576 · 材质 43 · 表情 145
- T-pose 手臂姿态修正；缩放 0.07257；手性 Z 取反、绕序反转
- SpringBone：70 链（头发/鳍耳 54、`dress_0_*` 裙摆 16）· 碰撞体 77
- 输出：节点 475 · morph target 152 · 表情 155；材质透明模式 不透明 13 / MASK 23 / BLEND 7
- 表情预设：happy/angry/sad/relaxed/surprised/blink/blinkLeft/blinkRight/aa/ih/ou/ee/oh；另补 lookLeft/Right/Up/Down、hideHighlight、tongue

## 检查结果

| 检查 | 结果 | 说明 |
| --- | --- | --- |
| `check_vrm.py --no-schema` | ✅ 通过 | 0 错误 |
| `check-runtime.mjs`（CPU） | ⚠️ 13/15 | `blinkLeft`/`blinkRight` 判失败：源形态键在鼻梁中线附近有 18 个顶点随动（x∈[-0.026, 0]），视觉上对侧眼睛不动；未篡改源数据 |
| `check-springs.mjs`（原版） | ❌ pass=false | 脚本按链名 `/skirt/i` 分组，本模型裙摆链名为 `dress_0_*`，导致 skirt 分组 0 |
| `check-springs-local.mjs`（本地变体，分组 `/dress|skirt/i`） | ✅ 通过 | hair 54 / skirt 16；动态响应 hair 0.160 m、skirt 0.011 m；静止收敛 4.6e-16 / 1.3e-16 m；无重复关节、无 humanoid 冲突 |

## 视觉证据

- `evidence/shots/`：24 张（face-front / face-3q / chat-front / chat-3q × neutral、blink、blinkLeft、blinkRight、aa、happy），1280×1024，固定相机/光照，`manifest.json` 记录哈希与相机参数
- `evidence/shots-pose/`：6 张（body-front / body-3q × tpose、idle、walk）
- `evidence/shots-run/`：2 张（body-front / body-3q × run）

目视确认：

- 中性嘴部有原生唇线（无需补线），近景与聊天距离均清晰
- `blinkLeft` / `blinkRight` 各自正确闭合单侧眼，对侧眼不动
- `aa` 张嘴口腔与舌、齿正常；`happy` 眯眼笑自然
- T-pose / idle 全身比例、服装、鳍耳、长发完整；run 大动作下裙摆无可见穿模异常

## 代码与脚本位置

- 复跑转换：`evidence\run-convert.py`
- 复跑检查：`evidence\run-checks.py`
- 截图：`render\capture.mjs`、`render\render-server.mjs`、`render\harness\render.html`
- 规格：`render\shots.json`（表情）、`render\shots-pose.json`（姿态）、`render\shots-run.json`（跑动）

## 复跑命令

```powershell
$python = 'D:\SoftWare\VoiceSpeechMaker-core-fork\env\python.exe'
$work   = 'D:\方的世界\vrm-conversion-lab\blue-fat-fish'
& $python -B -X utf8 "$work\evidence\run-convert.py"     # PMX -> VRM
& $python -B -X utf8 "$work\evidence\run-checks.py"      # 结构 + CPU 检查
cd "$work\render"; node capture.mjs --spec render/shots.json --out evidence/shots
```

## 脸部阴影修复（2026-10-19）

预览中脸颊出现横向暗带，诊断结论：**不是模型几何/贴图问题，而是 three.js 阴影的 shadow acne**。

- 证据：关闭阴影后暗带消失（`evidence/shots-noshadow/`）；把 `shadow.normalBias` 从 0.003 提到 0.02 后暗带大幅减轻且保留立体感（`evidence/shots-bias/`、`evidence/shots-fixed/`）。
- 修复：修改共享预览工具 `VRMify/ui/viewer/viewer-core.js` 的 `normalBias`（构造处 0.003→0.02，`_fitShadow` 处 `0.003*height/1.6`→`0.02*height/1.6`），备份为 `viewer-core.js.bak-20261019`。该改动是阴影质量参数，对所有模型预览生效。
- 预览生效方式：刷新 VRMify 预览页（Ctrl+F5 强刷）。
- 说明：交付的 VRM 文件未因此改动；其他引擎若出现同类横带，调其阴影 bias/normalBias 或关闭阴影即可。眼下两点白斑与鼻尖小点为源贴图自带（眼周高光/卧蚕），非转换缺陷，需要去除可另行处理。

## 限制与说明

- 材质为近似：源 PMX 的 MMD toon 效果（Ray 渲等）无法无损迁移到 MToon；透明材质白边（规约提及）在部分角度可能仍存在。
- 物理为骨链近似：70 条 SpringBone 来自源物理骨，不是原 MMD 参数无损继承；未做全部极端动作的碰撞验收。
- `blinkLeft` 中线 18 顶点随动属源形态键设计，默认不改；如需处理另行评估。
- 本转换不授予模型新的使用权；使用前请按源规约注明作者。
