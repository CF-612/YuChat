param([int]$Port=17880)
$ErrorActionPreference='Stop'
if($Port -lt 1 -or $Port -gt 65535){throw '无效端口'}
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cache=Join-Path $root '.cache/my-neuro-prototype'
$model=Join-Path $cache 'T2M'
$worker=Join-Path $root '.cache/video-motion/worker311/Scripts/python.exe'
if(!(Test-Path $worker) -or !(Test-Path "$model/web.py")){throw '请先准备本地 Python 3.11 CUDA 环境和已校验、已解压的 T2M 包'}
$deps=Join-Path $cache 'text-deps'
New-Item -ItemType Directory -Force $deps | Out-Null
foreach($name in 'ftfy','wcwidth'){
    if(!(Test-Path (Join-Path $deps $name))){Copy-Item -LiteralPath "$model/.runtime/python/$name" -Destination $deps -Recurse}
}
$env:PYTHONPATH=$deps
$env:PYTHONDONTWRITEBYTECODE='1'
$env:TEMP=Join-Path $cache 'tmp'
$env:TMP=$env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
Push-Location $model
try { & $worker web.py --device cuda --port $Port --no-open; exit $LASTEXITCODE }
finally {Pop-Location}
