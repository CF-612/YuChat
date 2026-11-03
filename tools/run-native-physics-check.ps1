param([Parameter(Mandatory=$true)][string[]]$CharacterPackages,[ValidateSet('Baseline','S2','Portable','Switch','Motion','Center')][string]$Stage='Baseline',[string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root '.cache/f2-unity-validation'
foreach($package in $CharacterPackages){if(-not(Test-Path -LiteralPath (Join-Path $package 'manifest.json'))){throw "Missing manifest: $package"}}
Copy-Item "$root/LingChat-XR/Assets/LingChatXR" "$project/Assets" -Recurse -Force
Copy-Item "$root/LingChat-XR/Packages" $project -Recurse -Force
Copy-Item "$root/LingChat-XR/ProjectSettings" $project -Recurse -Force
$env:XR_NATIVE_PACKAGES=($CharacterPackages | ForEach-Object{(Resolve-Path -LiteralPath $_).Path}) -join '|'
$env:XR_NATIVE_EVIDENCE=Join-Path $root $(if($Stage-eq 'Switch'){'evidence/model-switch-s4'}elseif($Stage-eq 'S2'){'evidence/native-physics-s2'}elseif($Stage-eq 'Portable'){'evidence/portable-physics'}else{'evidence/native-physics'})
if($EvidenceDirectory){$env:XR_NATIVE_EVIDENCE=[IO.Path]::GetFullPath((Join-Path $root $EvidenceDirectory))}
$env:XR_PORTABLE_PHYSICS=if($Stage-eq 'Portable'){'1'}else{'0'}
New-Item -ItemType Directory -Force $env:XR_NATIVE_EVIDENCE | Out-Null
$log=Join-Path $env:XR_NATIVE_EVIDENCE ('Native-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.log')
$env:XR_CENTER_MOTION_TEST=if($Stage-eq 'Center'){'1'}else{'0'}
$method=if($Stage-in @('Motion','Center')){'MotionAirProbe'}elseif($Stage-eq 'Switch'){'ModelSwitchProbe'}elseif($Stage-eq 'S2'){'ScaleRecoveryProbe'}else{'NativePhysicsProbe'}
if($Stage-in @('Switch','Motion','Center')){
 $names=@();for($i=0;$i-lt $CharacterPackages.Count;$i++){$name="local-s4-$i";& "$PSScriptRoot/install-physics-sample.ps1" -SourcePackage $CharacterPackages[$i] -Folder $name -ProjectPath '.cache/f2-unity-validation';$names+=$name}
 $env:XR_SWITCH_FOLDERS=$names -join '|'
}
if($Stage-eq 'Motion'){$env:XR_HOST_CONNECTION=Join-Path $root '.cache/xr-host-connection.json'}
$p=Start-Process 'D:/GameCreator/Unity/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList "-batchmode -projectPath `"$project`" -executeMethod LingChatXR.Editor.$method.Run -logFile `"$log`""
$deadline=(Get-Date).AddMinutes(10)
while(-not $p.WaitForExit(1000)){if((Get-Date)-gt $deadline){Stop-Process -Id $p.Id;throw "Native physics check timed out: $log"}}
if($p.ExitCode-ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'XR_NATIVE_PHYSICS_PASSED' -Quiet)){throw "Native physics check failed: $log"}
if(Select-String -LiteralPath $log -Pattern 'Leak Detected : Persistent|Found \d+ leak\(s\)' -Quiet){throw "Native physics allocation leak: $log"}
Write-Output "Native physics passed: $log"
