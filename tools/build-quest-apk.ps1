param([string]$EvidenceDirectory=('evidence/quest-apk-'+(Get-Date -Format 'yyyyMMdd-HHmmss')),[ValidateRange(0,65535)][int]$LocalProxyPort=0)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $root $EvidenceDirectory
& "$PSScriptRoot/run-m1-check.ps1" -AndroidBuild -EvidenceDirectory $EvidenceDirectory -Method LingChatXR.Editor.QuestApkBuild.Run
$java='D:/GameCreator/Unity/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe'
$gradle='D:/GameCreator/Unity/Editor/Data/PlaybackEngines/AndroidPlayer/Tools/gradle/lib/gradle-launcher-8.13.jar'
$javaTemp=Join-Path $root '.cache/quest-java'
$javaProperty='-Djdk.net.unixdomain.tmpdir="'+$javaTemp+'"'
$networkOptions=@('-Dorg.gradle.internal.http.connectionTimeout=30000','-Dorg.gradle.internal.http.socketTimeout=30000')
if($LocalProxyPort){$networkOptions+=@('-Dhttp.proxyHost=127.0.0.1',"-Dhttp.proxyPort=$LocalProxyPort",'-Dhttps.proxyHost=127.0.0.1',"-Dhttps.proxyPort=$LocalProxyPort")}
$previous=$env:JAVA_TOOL_OPTIONS
Push-Location (Join-Path $root '.cache/quest-gradle-export')
try {
    $env:JAVA_TOOL_OPTIONS=($previous+' '+$javaProperty).Trim()
    & $java ("-Djdk.net.unixdomain.tmpdir=$javaTemp") -classpath $gradle org.gradle.launcher.GradleMain --no-daemon ("-Dorg.gradle.jvmargs=-Xmx4096m $javaProperty") @networkOptions --stacktrace assembleDebug *> (Join-Path $evidence 'gradle.log')
    if($LASTEXITCODE-ne0){throw 'Gradle 打包失败，见本批 gradle.log。'}
    $apk=Join-Path $evidence 'Desktop-SmallBlueFish-Quest.apk'
    if(Test-Path $apk){throw '输出 APK 已存在，请使用新的证据目录。'}
    Copy-Item -LiteralPath 'launcher/build/outputs/apk/debug/launcher-debug.apk' -Destination $apk
} finally {$env:JAVA_TOOL_OPTIONS=$previous;Pop-Location}
& "$PSScriptRoot/check-quest-apk.ps1" -Apk $apk -RequireLocomotion
Write-Output "APK 已构建并审计：$apk；未安装或实机验收。"
