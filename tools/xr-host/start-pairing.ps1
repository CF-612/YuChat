param([Parameter(Mandatory=$true)][string]$BindAddress,[int]$Port=0,[switch]$Build)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$private=Join-Path $taskRoot '.cache/xr-pairing'
$connectionPath=Join-Path $taskRoot '.cache/xr-host-connection.json'
$address=[Net.IPAddress]::Parse($BindAddress);$a=$address.GetAddressBytes()
$isPrivate=$a.Length-eq4-and($a[0]-eq10-or($a[0]-eq172-and$a[1]-ge16-and$a[1]-le31)-or($a[0]-eq192-and$a[1]-eq168))
if(-not$isPrivate){throw '必须显式选择本机私网 IPv4，不能监听所有网卡。'}
if(-not(Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $BindAddress)){throw '所选地址不属于本机网卡。'}
$connection=Get-Content -LiteralPath $connectionPath -Raw -Encoding utf8 | ConvertFrom-Json
$upstream=[Uri]$connection.endpoint
if($upstream.Host-ne'127.0.0.1'-or$upstream.Scheme-ne'http'){throw '请先启动原本机 XR 宿主。'}
$health=Invoke-RestMethod ($connection.endpoint+'/health') -Headers @{Authorization=('Bearer '+$connection.token)} -TimeoutSec 3
if(-not$health.ready){throw '原宿主尚未就绪。'}
if($Port-eq0){$Port=$upstream.Port+1}
if($Port-lt1-or$Port-gt65535){throw '请输入有效的配对端口。'}
$binary=Join-Path $PSScriptRoot 'target/c0/debug/lingchat-xr-host.exe'
if($Build){& 'C:/Users/fangcheng612/.cargo/bin/cargo.exe' build --locked --target-dir (Join-Path $PSScriptRoot 'target/c0') --manifest-path (Join-Path $PSScriptRoot 'Cargo.toml');if($LASTEXITCODE-ne0){throw '网关构建失败。'}}
New-Item -ItemType Directory -Force $private | Out-Null
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw '配对端口已占用；未停止任何现有服务。'}
$statusPath=Join-Path $private 'status.json'
$env:XR_PAIRING_BIND=$BindAddress;$env:XR_PAIRING_PORT=[string]$Port;$env:XR_PAIRING_CONNECTION=$connectionPath;$env:XR_PAIRING_STATUS=$statusPath
$process=Start-Process -FilePath $binary -ArgumentList '--pairing-gateway' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $private 'gateway.log') -RedirectStandardError (Join-Path $private 'gateway-error.log')
$until=(Get-Date).AddSeconds(10)
do {
    if($process.HasExited){throw '配对网关退出，请检查 .cache/xr-pairing/gateway-error.log。'}
    if(Test-Path $statusPath){$state=Get-Content $statusPath -Raw | ConvertFrom-Json;if($state.pid-eq$process.Id){Write-Output ('PC 地址：'+$state.endpoint);Write-Output ('一次性配对码：'+$state.code+'（5 分钟有效）');Write-Output '在应用「连接 PC」输入以上信息。不会自动开放防火墙，也不停止原宿主。';return}}
    Start-Sleep -Milliseconds 200
}while((Get-Date)-lt$until)
throw '网关启动超时。'
