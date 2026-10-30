$ErrorActionPreference = 'Stop'
$android = 'D:\GameCreator\Unity\Editor\Data\PlaybackEngines\AndroidPlayer'
$workspace = 'D:\GameCreator\Document\DesktopSmallBlueFish'
function Invoke-CheckedNative {
    param([string]$Executable, [string[]]$Arguments)
    Write-Output "VERIFY: $Executable $($Arguments -join ' ')"
    & $Executable @Arguments 2>&1 | ForEach-Object { Write-Output $_ }
    if ($LASTEXITCODE -ne 0) { throw "Native verification failed: $Executable ($LASTEXITCODE)" }
}
Invoke-CheckedNative 'D:\GameCreator\Unity\Editor\Unity.exe' @('-version','-batchmode','-nographics')
Invoke-CheckedNative "$android\SDK\platform-tools\adb.exe" @('version')
Invoke-CheckedNative "$android\OpenJDK\bin\java.exe" @('-version')
Invoke-CheckedNative "$android\SDK\build-tools\36.0.0\aapt2.exe" @('version')
Invoke-CheckedNative "$android\SDK\cmake\3.22.1\bin\cmake.exe" @('--version')
Get-Content -LiteralPath "$android\NDK\source.properties"
Invoke-CheckedNative "$android\NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\clang.exe" @('--version')
foreach ($api in @(34,35,36)) {
    $jar = "$android\SDK\platforms\android-$api\android.jar"
    if (-not (Test-Path -LiteralPath $jar -PathType Leaf)) { throw "Missing Android API $api" }
    Write-Output "Android API $api android.jar present"
}
$probe = "$workspace\evidence\liblingchat_xr_ndk_probe.so"
Invoke-CheckedNative "$android\NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\clang.exe" @('--target=aarch64-linux-android32','-shared','-fPIC','-Wl,--no-undefined','-Wl,-soname,liblingchat_xr_ndk_probe.so','-o',$probe,"$workspace\evidence\unity6-ndk-probe.c")
Invoke-CheckedNative "$android\NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe" @('-h',$probe)
Write-Output 'PASS: Unity version, adb, JDK, aapt2, CMake, NDK, SDK platform files and Android ARM64 shared-library compile/link.'
Write-Output 'NOT TESTED: APK build/install, Quest connection, hand tracking, passthrough or device performance.'
