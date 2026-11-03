param([string]$Unity = 'D:/GameCreator/Unity/Editor/Unity.exe', [switch]$Conversation)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root '.cache/f2-unity-validation'
$source=Join-Path $root 'LingChat-XR'
if (-not (Test-Path (Join-Path $root '.cache/xr-host-connection.json'))) { throw 'Start the XR host first.' }
New-Item -ItemType Directory -Force -Path "$project/Assets","$project/UserSettings" | Out-Null
Copy-Item "$source/Assets/LingChatXR" "$project/Assets" -Recurse -Force
Copy-Item "$source/Assets/Scenes" "$project/Assets" -Recurse -Force
Copy-Item "$source/Packages" $project -Recurse -Force
Copy-Item "$source/ProjectSettings" $project -Recurse -Force
'{"indexOnEditorStartup":false}' | Set-Content "$project/UserSettings/Search.settings"
$env:XR_HOST_CONNECTION=Join-Path $root '.cache/xr-host-connection.json'
$env:XR_EVIDENCE_DIR=Join-Path $root 'evidence'
$env:XR_CAPTURE_UI='1'
$log=Join-Path $root ('evidence/f2-unity-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.log')
$method=if($Conversation){'RunConversation'}else{'Run'}
$p=Start-Process $Unity -WindowStyle Hidden -PassThru -ArgumentList "-batchmode -projectPath `"$project`" -executeMethod LingChatXR.Editor.F2Smoke.$method -logFile `"$log`""
$deadline=(Get-Date).AddMinutes(10)
while (-not $p.WaitForExit(1000)) {
    if ((Get-Date) -gt $deadline) { Stop-Process -Id $p.Id; throw "Isolated Unity check timed out; see $log" }
}
if ($p.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'XR_F2_SMOKE_PASSED' -Quiet)) { throw "F2 validation failed; see $log" }
Write-Output "F2 isolated PlayMode passed. Log: $log"
