param(
    [string]$PackageDirectory = "$PSScriptRoot/../LingChat-XR/Assets/StreamingAssets/Characters/local-smallbluefish-alpha2",
    [string]$TailGripPath = 'WhaleHumanoid/hips/tail01/tail01_mid/tail02/tail02_mid/tail03'
)
$ErrorActionPreference = 'Stop'
# 骨骼路径属于角色包配置。更换角色时由制作者提供，不在运行时代码中猜骨名。
$manifest = Join-Path $PackageDirectory 'manifest.json'
$config = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$config | Add-Member -Force -NotePropertyName interactionBindings -NotePropertyValue ([pscustomobject]@{tailGripPath=$TailGripPath})
$config | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $manifest -Encoding utf8
Write-Output "已配置尾巴抓点：$TailGripPath"
