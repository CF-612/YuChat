param([string]$EvidenceDirectory='evidence/m1-2026-10-31/run-01',[string]$Method='LingChatXR.Editor.M1MotionProbe.Run',[switch]$AndroidBuild,[string]$HostConnectionPath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root '.r1-unity-validation'
$evidence=Join-Path $root $EvidenceDirectory
New-Item -ItemType Directory -Force $evidence,"$project/Assets" | Out-Null
Copy-Item "$root/LingChat-XR/Assets/LingChatXR" "$project/Assets" -Recurse -Force
Copy-Item "$root/LingChat-XR/Assets/Scenes" "$project/Assets" -Recurse -Force
if(Test-Path "$root/LingChat-XR/Assets/XR"){Copy-Item "$root/LingChat-XR/Assets/XR" "$project/Assets" -Recurse -Force}
Copy-Item "$root/LingChat-XR/Packages" $project -Recurse -Force
Copy-Item "$root/LingChat-XR/ProjectSettings" $project -Recurse -Force
Copy-Item "$root/LingChat-XR/Assets/StreamingAssets" "$project/Assets" -Recurse -Force
$env:XR_PREFERENCES_DIRECTORY=Join-Path $root ('.cache/m1-check/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $env:XR_PREFERENCES_DIRECTORY|Out-Null
$env:XR_HOST_CONNECTION=if($HostConnectionPath){[IO.Path]::GetFullPath($HostConnectionPath)}else{Join-Path $root '.cache/xr-host-connection.json'}
$env:XR_CAPTURE_UI='1';$env:XR_M1_EVIDENCE=$evidence
$log=Join-Path $evidence ('M1-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$targetArgument='-buildTarget Win64'
if($AndroidBuild){
    # 只修改隔离副本。Android 使用新输入系统；主项目保留 Both 的桌面兼容性。
    $settings=Join-Path $project 'ProjectSettings/ProjectSettings.asset'
    $content=[IO.File]::ReadAllText($settings)
    [IO.File]::WriteAllText($settings,($content -replace 'activeInputHandler: [012]','activeInputHandler: 1'))
    $targetArgument='-buildTarget Android'
}
$previousJavaOptions=$env:JAVA_TOOL_OPTIONS
try {
    if($AndroidBuild){
        # Windows 应用宿主的默认临时路径会使 Java NIO 本机管道失败；显式使用工作区目录。
        $javaTemp=Join-Path $root '.cache/quest-java'
        New-Item -ItemType Directory -Force $javaTemp|Out-Null
        $env:JAVA_TOOL_OPTIONS=($previousJavaOptions+' -Djdk.net.unixdomain.tmpdir="'+$javaTemp+'"').Trim()
    }
    $p=Start-Process 'D:/GameCreator/Unity/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList "-batchmode $targetArgument -projectPath `"$project`" -executeMethod $Method -logFile `"$log`""
} finally {$env:JAVA_TOOL_OPTIONS=$previousJavaOptions}
$deadline=(Get-Date).AddMinutes($(if($AndroidBuild){45}else{15}))
while(-not $p.WaitForExit(1000)){if((Get-Date)-gt $deadline){Stop-Process -Id $p.Id;throw "M1 timeout: $log"}}
if($p.ExitCode-ne0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'XR_M1_PASSED' -Quiet)){throw "M1 failed: $log"}
if(Select-String -LiteralPath $log -Pattern '^(MissingReferenceException|NullReferenceException|ArgumentException|InvalidOperationException|error CS)' -Quiet){throw "M1 runtime or shutdown error: $log"}
Write-Output "M1 passed: $log"
