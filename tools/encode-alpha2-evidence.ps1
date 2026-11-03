param([string]$FFmpeg='D:/SoftWare/ffmpeg/bin/ffmpeg.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $jobs=@(
        @{Frames='motion-0';Caption='physics-a.ass';Output='physics-original.mp4'},
        @{Frames='motion-1';Caption='physics-b.ass';Output='physics-world-centers.mp4'},
        @{Frames='motion-rear';Caption='physics-b.ass';Output='physics-rear.mp4'},
        @{Frames='landing';Caption='landing.ass';Output='landing-and-regrab.mp4'}
    )
    foreach($job in $jobs){
        & $FFmpeg -hide_banner -loglevel error -y -framerate 30 -i ('evidence/f1-alpha2/'+$job.Frames+'/%04d.png') -vf ('subtitles=evidence/f1-alpha2/'+$job.Caption) -c:v libx264 -crf 19 -pix_fmt yuv420p -movflags +faststart ('evidence/f1-alpha2/'+$job.Output)
        if($LASTEXITCODE-ne 0){throw ('Video encode failed: '+$job.Output)}
    }
} finally { Pop-Location }
