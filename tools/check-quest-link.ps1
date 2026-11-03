param([string]$OutputDirectory='evidence/xr-tonight-2026-10-31/pc-readiness')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$output=Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Force $output|Out-Null
# 只读检查；不启动编辑器、不更改全局运行时、不输出连接令牌。
$runtime=@()
foreach($key in @('HKLM:\SOFTWARE\Khronos\OpenXR\1','HKLM:\SOFTWARE\WOW6432Node\Khronos\OpenXR\1')){
    if(Test-Path $key){$value=(Get-ItemProperty $key -ErrorAction SilentlyContinue).ActiveRuntime;if($value){$runtime+=[pscustomobject]@{Registry=$key;Manifest=$value;Exists=(Test-Path -LiteralPath $value)}}}
}
$apps=@()
foreach($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall')){
    $apps+=@(Get-ItemProperty "$key\*" -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -match 'Meta.*(Link|Quest)|Oculus'}|Select-Object DisplayName,DisplayVersion,InstallLocation)
}
$gpu=@(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Video\*\0000' -ErrorAction SilentlyContinue|Where-Object DriverDesc|Select-Object -ExpandProperty DriverDesc)
$installer=Join-Path $root '.cache/quest-link/MetaLinkSetup.exe'
$signature=$null
if(Test-Path $installer){$sig=Get-AuthenticodeSignature $installer;$signature=[pscustomobject]@{Status=$sig.Status.ToString();Signer=$sig.SignerCertificate.Subject;SHA256=(Get-FileHash $installer).Hash}}
$result=[pscustomobject]@{CheckedAt=(Get-Date -Format o);Runtime=$runtime;InstalledApps=$apps;DisplayDrivers=$gpu;Installer=$signature;SkipLoaderSet=([bool]$env:XR_SKIP_LOADER -or [bool][Environment]::GetEnvironmentVariable('XR_SKIP_LOADER','User') -or [bool][Environment]::GetEnvironmentVariable('XR_SKIP_LOADER','Machine'));DeviceTested=$false}
$result|ConvertTo-Json -Depth 5|Set-Content (Join-Path $output 'pc-readiness.json') -Encoding utf8
Write-Output ('运行时记录：{0}；Link 安装记录：{1}；安装程序签名：{2}' -f $runtime.Count,$apps.Count,$signature.Status)
Write-Output "检查结果：$output；没有安装记录不等于整机绝对没有安装。"
