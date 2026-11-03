param([string]$SourceRoot, [string]$Node='D:/SoftWare/Nodejs/node.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if (-not $SourceRoot) { $SourceRoot=Join-Path $root '.cache/model-intake/smallbluefish-alpha2/SmallBlueFish-v0.1.0-alpha.2' }
$expected='9c490a224fe7502f6be2cba929ff57445e6627bbd59986eb4ef240e79538a291'
$model=Join-Path $SourceRoot 'model/SmallBlueFish-v0.1.0-alpha.2.vrm'
if ((Get-FileHash -LiteralPath $model).Hash.ToLowerInvariant() -ne $expected) { throw 'Unexpected alpha.2 model fingerprint.' }
$source=Join-Path $root '.cache/f1-import-source'
New-Item -ItemType Directory -Force $source | Out-Null
$copy=Join-Path $source 'model.vrm'
if ((Test-Path $copy) -and (Get-FileHash $copy).Hash.ToLowerInvariant() -ne $expected) { throw 'Staging model differs; retained without replacement.' }
if (-not (Test-Path $copy)) { Copy-Item -LiteralPath $model -Destination $copy }
@'
ai_name: 小肥鱼
ai_subtitle: alpha.2 原型
info: 独立 XR 模型包；角色人设由独立会话宿主提供。
tts_type: localsbv2api
voice_lang: ja
vrm:
  model: model.vrm
'@ | Set-Content (Join-Path $source 'settings.yml') -Encoding utf8
& $Node (Join-Path $PSScriptRoot 'character-import/import.mjs') --source $source --output (Join-Path $root 'LingChat-XR/Assets/StreamingAssets/Characters') --records (Join-Path $root '.cache/character-records') --character-id local-smallbluefish-alpha2 --expected-model-sha256 $expected --source-rules (Join-Path $SourceRoot 'README.md')
if ($LASTEXITCODE -ne 0) { throw 'Alpha.2 import failed.' }
