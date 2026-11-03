param([Parameter(Mandatory=$true)][string]$VoiceFile)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$target=Join-Path $root 'LingChat-XR/Assets/LingChatXR/Resources/LocalLingChatEffects/faceSample.wav'
New-Item -Type Directory -Force (Split-Path -Parent $target)|Out-Null
& ffmpeg -v error -y -i $VoiceFile -filter_complex 'asplit[a][b];[a]apad=pad_dur=0.55[x];[x][b]concat=n=2:v=0:a=1' -c:a pcm_s16le $target
if($LASTEXITCODE){throw '准备离线样句失败'}
