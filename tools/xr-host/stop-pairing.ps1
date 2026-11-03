$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$statusPath=Join-Path $taskRoot '.cache/xr-pairing/status.json'
$state=Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
$process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$state.pid)
$expected=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'target/c0/debug/lingchat-xr-host.exe'))
if($null-eq$process){Write-Output '配对网关已停止。';return}
if($process.ExecutablePath-ne$expected-or$process.CommandLine-notmatch'--pairing-gateway'){throw 'PID 已不属于配对网关，未停止进程。'}
Stop-Process -Id $state.pid
Write-Output '已停止配对网关，旧配对令牌失效；原本机聊天宿主保持运行。'
