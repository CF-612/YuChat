# 在当前 PowerShell 进程内设置 PC 实验缓存。
$experimentRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.cache/ardy-horizon8'
foreach ($entry in @{
    HF_HOME='hf'; HF_HUB_CACHE='hf/hub'; PIP_CACHE_DIR='pip'; TORCH_HOME='torch'
    TORCHINDUCTOR_CACHE_DIR='inductor'; TEMP='tmp'; TMP='tmp'; TMPDIR='tmp'
}.GetEnumerator()) {
    $directory = Join-Path $experimentRoot $entry.Value
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    [Environment]::SetEnvironmentVariable($entry.Key,$directory,'Process')
}
