param([string]$LingChatRoot='D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$destination=Join-Path $root 'LingChat-XR/Assets/StreamingAssets/Motions/local-lingchat'
$catalog=Get-Content (Join-Path $LingChatRoot 'src/assets/vrm/action-library.json') -Encoding UTF8 -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $destination | Out-Null
foreach($item in $catalog.actions){
    $source=Join-Path $LingChatRoot $item.asset.TrimStart('/')
    if((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -ne $item.sha256){throw "Motion SHA mismatch: $($item.id)"}
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination ($item.id+'.vrma')) -Force
}
[IO.File]::WriteAllText((Join-Path $destination 'catalog.json'),($catalog|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
Write-Output "Prepared $($catalog.actions.Count) local-only motion assets. Original files unchanged."
