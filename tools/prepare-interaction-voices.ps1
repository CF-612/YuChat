param([switch]$Generate, [switch]$Build)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Generate) {
    & "$PSScriptRoot/xr-host/start-host.ps1" -ExportReactions -Build:$Build
    if ($LASTEXITCODE -ne 0) { throw '短声导出失败' }
}
$source = Join-Path $root '.cache/reaction-clips'
$target = Join-Path $root 'LingChat-XR/Assets/LingChatXR/Resources/LocalInteractionVoice'
New-Item -ItemType Directory -Force $target | Out-Null
foreach ($group in @('happy','curious','surprised','protest')) {
    foreach ($variant in 1..2) {
        $file = Join-Path $source "$($group)_$variant.wav"
        if (-not (Test-Path -LiteralPath $file)) { throw "缺少 $file；首次请加 -Generate" }
        Copy-Item -LiteralPath $file -Destination $target -Force
    }
}
Write-Output '八条临时短声已复制。在 Unity 执行 YuChat/互动短声/更新本地素材配置。'
