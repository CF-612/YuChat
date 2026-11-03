param([string]$EvidenceDirectory='evidence/default-flow-r1',[ValidateSet('All','VoiceRetry','Directed')][string]$Mode='All')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root '.r1-unity-validation'
$evidence=Join-Path $root $EvidenceDirectory
New-Item -ItemType Directory -Force $project,$evidence | Out-Null
# 与主工程同一级，正常产品路径 ../../.cache 与 ../../tools 无需重定向或桩。
if(-not(Test-Path "$project/Library")){
    & robocopy "$root/.cache/f2-unity-validation" $project /E /R:1 /W:1 /NFL /NDL /NJH /NJS /XD Temp Logs .vs | Out-Null
    if($LASTEXITCODE-ge 8){throw 'Could not prepare isolated project.'}
}
Copy-Item "$root/LingChat-XR/Assets/LingChatXR" "$project/Assets" -Recurse -Force
Copy-Item "$root/LingChat-XR/Assets/Scenes" "$project/Assets" -Recurse -Force
Copy-Item "$root/LingChat-XR/Packages" $project -Recurse -Force
Copy-Item "$root/LingChat-XR/ProjectSettings" $project -Recurse -Force
& "$PSScriptRoot/install-physics-sample.ps1" -SourcePackage "$root/.cache/retired-models/blue-placeholder" -Folder local-physics-reference -ProjectPath '.r1-unity-validation'
& "$PSScriptRoot/install-physics-sample.ps1" -SourcePackage "$root/LingChat-XR/Assets/StreamingAssets/Characters/local-smallbluefish-alpha2" -Folder local-smallbluefish-alpha2 -ProjectPath '.r1-unity-validation'
$listeners=@(Get-NetTCPConnection -LocalPort 17861 -State Listen -ErrorAction SilentlyContinue)
$hosts=@(Get-Process -Name lingchat-xr-host -ErrorAction SilentlyContinue)
$before=[pscustomobject]@{timeUtc=[DateTime]::UtcNow.ToString('o');hostCount=$hosts.Count;listenerCount=$listeners.Count;coldStart=($hosts.Count-eq 0 -and $listeners.Count-eq 0);physicsSha=(Get-FileHash "$root/LingChat-XR/Assets/LingChatXR/Runtime/PortableSpringPhysics.cs" -Algorithm SHA256).Hash.ToLowerInvariant()}
$before|ConvertTo-Json|Set-Content -Encoding utf8 "$evidence/preflight.json"
# 有正在使用的宿主时复用，明确不能算冷启动；绝不为测试结束它。
$env:XR_R1_COLD=if($before.coldStart){'1'}else{'0'}
$env:XR_R1_EVIDENCE=$evidence
$env:XR_R1_MODE=$Mode
Remove-Item Env:XR_HOST_CONNECTION -ErrorAction SilentlyContinue
$env:XR_CAPTURE_UI='1'
$log=Join-Path $evidence ('R1-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.log')
$p=Start-Process 'D:/GameCreator/Unity/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList "-batchmode -projectPath `"$project`" -executeMethod LingChatXR.Editor.R1DefaultFlowProbe.Run -logFile `"$log`""
$deadline=(Get-Date).AddMinutes(15)
while(-not $p.WaitForExit(1000)){if((Get-Date)-gt $deadline){Stop-Process -Id $p.Id;throw "R1 isolated validation timeout: $log"}}
if($p.ExitCode-ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'XR_R1_TECHNICAL_CHECKS_PASSED' -Quiet)){throw "R1 validation failed: $log"}
if(Select-String -LiteralPath $log -Pattern 'Leak Detected : Persistent|Found \d+ leak\(s\)' -Quiet){throw "Native allocation leak: $log"}
Write-Output "R1 technical checks passed; visual review remains separate: $log"
