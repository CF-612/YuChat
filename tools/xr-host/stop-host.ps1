$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pidFile=Join-Path $root '.cache/xr-host-data/host.pid'
if (-not (Test-Path $pidFile)) { Write-Output 'No host PID record.'; exit }
$hostPid=[int](Get-Content $pidFile -Raw)
$p=Get-CimInstance Win32_Process -Filter "ProcessId=$hostPid"
$expected=(Join-Path $PSScriptRoot 'target/debug/lingchat-xr-host.exe').Replace('/','\')
if ($p -and $p.ExecutablePath -eq $expected) { $process=Get-Process -Id $hostPid; Stop-Process -Id $hostPid; $process.WaitForExit(); Write-Output 'XR host stopped.' }
elseif ($p) { throw 'PID no longer belongs to this XR host; no process was stopped.' }
else { Write-Output 'XR host already exited.' }
