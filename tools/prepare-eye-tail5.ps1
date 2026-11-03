param([Parameter(Mandatory)][string]$Vrm)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$expected='8d399eae8f55c65a5c6f37c18864db1d441d7e2184ff83d08e97dc7a7b94e613'
if((Get-FileHash -LiteralPath $Vrm).Hash -ne $expected){throw '文件不是此次 Eye-Tail5 测试模型'}
$cache=Join-Path $root '.cache/eye-tail5-intake'
$source=Join-Path $cache 'source'
New-Item -ItemType Directory -Force $source|Out-Null
Copy-Item -LiteralPath $Vrm -Destination (Join-Path $source 'model.vrm')
@"
ai_name: 小肥鱼
ai_subtitle: Eye-Tail5 测试模型
info: 独立 XR 模型包；角色人设由独立会话宿主提供。
tts_type: localsbv2api
voice_lang: ja
vrm:
  model: model.vrm
"@ | Set-Content (Join-Path $source 'settings.yml') -Encoding utf8
$rules=Join-Path $cache 'source-rules.txt'
'用户提供的 SmallBlueFish-2026-11-02-Eye-Tail5-TEST.vrm。本地 XR 测试使用。' | Set-Content $rules -Encoding utf8
$id='local-smallbluefish-alpha2'
$output=Join-Path $cache 'packages'
& node (Join-Path $PSScriptRoot 'character-import/import.mjs') --source $source --output $output --records (Join-Path $cache 'records') --character-id $id --expected-model-sha256 $expected --source-rules $rules
if($LASTEXITCODE){throw '角色包导入失败'}
$package=Join-Path $output $id
$target=Join-Path $root "LingChat-XR/Assets/StreamingAssets/Characters/$id"
foreach($name in 'model.vrm','source-rules.txt','manifest.json'){Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $target $name)}
