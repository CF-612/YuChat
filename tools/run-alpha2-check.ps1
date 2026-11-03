param([ValidateSet('Load','Physics','FallCloth','Interaction','Experience','Capture')][string]$Mode='Load', [string]$Unity='D:/GameCreator/Unity/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root '.cache/f2-unity-validation'
$source=Join-Path $root 'LingChat-XR'
New-Item -ItemType Directory -Force "$project/Assets/StreamingAssets/Characters","$project/UserSettings",(Join-Path $root 'evidence/f1-alpha2') | Out-Null
Copy-Item "$source/Assets/LingChatXR" "$project/Assets" -Recurse -Force
Copy-Item "$source/Assets/Scenes" "$project/Assets" -Recurse -Force
Copy-Item "$source/Assets/StreamingAssets/Characters/local-smallbluefish-alpha2" "$project/Assets/StreamingAssets/Characters" -Recurse -Force
Copy-Item "$source/Packages" $project -Recurse -Force
Copy-Item "$source/ProjectSettings" $project -Recurse -Force
'{"indexOnEditorStartup":false}' | Set-Content "$project/UserSettings/Search.settings"
$env:XR_HOST_CONNECTION=Join-Path $root '.cache/xr-host-connection.json'
$env:XR_EVIDENCE_DIR=Join-Path $root 'evidence/f1-alpha2'
$env:XR_CAPTURE_UI=if($Mode -in @('Experience','Capture')){'1'}else{'0'}
$method=switch($Mode){'Load'{'Alpha2Probe'} 'Physics'{'Alpha2PhysicsCheck'} 'FallCloth'{'FallingClothCheck'} 'Interaction'{'Alpha2InteractionCheck'} 'Experience'{'ExperienceRegression'} 'Capture'{'ExperienceCapture'}}
$marker=switch($Mode){'Load'{'XR_ALPHA2_PROBE_PASSED'} 'Physics'{'XR_ALPHA2_PHYSICS_PASSED'} 'FallCloth'{'XR_FALL_CLOTH_PASSED'} 'Interaction'{'XR_ALPHA2_INTERACTION_PASSED'} 'Experience'{'XR_EXPERIENCE_PASSED'} 'Capture'{'XR_EXPERIENCE_PASSED'}}
$log=Join-Path $env:XR_EVIDENCE_DIR ($Mode+'-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.log')
$p=Start-Process $Unity -WindowStyle Hidden -PassThru -ArgumentList "-batchmode -projectPath `"$project`" -executeMethod LingChatXR.Editor.$method.Run -logFile `"$log`""
$deadline=(Get-Date).AddMinutes(10)
while(-not $p.WaitForExit(1000)){if((Get-Date)-gt $deadline){Stop-Process -Id $p.Id;throw "Alpha2 isolated check timed out: $log"}}
if($p.ExitCode-ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)){throw "Alpha2 check failed: $log"}
Write-Output "Alpha2 $Mode passed. Log: $log"
