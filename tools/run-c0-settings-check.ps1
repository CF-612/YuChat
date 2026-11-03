param([ValidateSet('Initial','Restore','Voice','Resize','UiRecovery')][string]$Mode='Initial',[string]$EvidenceDirectory='evidence/q2-c0-2026-10-31/run-01')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root '.r1-unity-validation'
$evidence=Join-Path $root $EvidenceDirectory
$private=Join-Path $root '.cache/c0-settings-check'
New-Item -ItemType Directory -Force $private,$evidence | Out-Null
Copy-Item "$root/LingChat-XR/Assets/LingChatXR" "$project/Assets" -Recurse -Force
Copy-Item "$root/LingChat-XR/Assets/Scenes" "$project/Assets" -Recurse -Force
Copy-Item "$root/LingChat-XR/Packages" $project -Recurse -Force
Copy-Item "$root/LingChat-XR/ProjectSettings" $project -Recurse -Force
$env:XR_PREFERENCES_DIRECTORY=Join-Path $private 'preferences'
if($Mode-eq'Initial'){
    if(Test-Path $env:XR_PREFERENCES_DIRECTORY){$env:XR_PREFERENCES_DIRECTORY=Join-Path $private ('preferences-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
    $env:XR_PREFERENCES_DIRECTORY | Set-Content "$private/latest-preferences.txt"
}else{$env:XR_PREFERENCES_DIRECTORY=(Get-Content "$private/latest-preferences.txt" -Raw).Trim()}
$env:XR_SERVICE_SETTINGS=Join-Path $private 'service-settings.json'
if($Mode-eq'Initial'){'{"llm.providers":[{"id":"fixture","label":"本地回归样本","provider":"openai","model":"fixture","api_key":"fixture-not-real","base_url":""}],"llm.chat_provider_id":"fixture","llm.translate_provider_id":"fixture"}' | Set-Content $env:XR_SERVICE_SETTINGS -Encoding utf8}
$env:XR_HOST_CONNECTION=Join-Path $private 'invalid-connection.json'
'{"endpoint":"invalid","token":""}'|Set-Content $env:XR_HOST_CONNECTION -Encoding utf8
if($Mode-eq'Voice'){
    $env:XR_HOST_CONNECTION=Join-Path $root '.cache/c0-host-check/xr-host-connection.json'
    $env:XR_SERVICE_SETTINGS=Join-Path $root '.cache/c0-host-check/service-settings.json'
}
if($Mode-eq'UiRecovery'){$env:XR_HOST_CONNECTION=$null}
$env:XR_CAPTURE_UI='1';$env:XR_C0_MODE=$Mode;$env:XR_C0_EVIDENCE=$evidence
$log=Join-Path $evidence ('C0-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$p=Start-Process 'D:/GameCreator/Unity/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList "-batchmode -projectPath `"$project`" -executeMethod LingChatXR.Editor.C0SettingsProbe.Run -logFile `"$log`""
$deadline=(Get-Date).AddMinutes(12)
while(-not $p.WaitForExit(1000)){if((Get-Date)-gt $deadline){Stop-Process -Id $p.Id;throw "C0 timeout: $log"}}
if($p.ExitCode-ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'XR_C0_SETTINGS_PASSED' -Quiet)){throw "C0 failed: $log"}
Write-Output "C0 settings $Mode passed: $log"
