param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$private=Join-Path $root '.cache/c0-host-check'
$evidence=Join-Path $root 'evidence/q2-c0-2026-10-31/voice'
New-Item -ItemType Directory -Force $private,$evidence | Out-Null
if(Get-NetTCPConnection -LocalPort 17862 -State Listen -ErrorAction SilentlyContinue){throw '独立测试端口 17862 已占用；未停止任何服务。'}
$config='{"llm.providers":[{"id":"fixture","label":"本机语音验收","provider":"openai","model":"fixture-no-cloud-request","api_key":"fixture-not-real"}],"llm.chat_provider_id":"fixture","llm.translate_provider_id":"fixture"}'
$env:XR_LINGCHAT_SETTINGS=Join-Path $private 'service-settings.json'
[IO.File]::WriteAllText($env:XR_LINGCHAT_SETTINGS,$config,[Text.UTF8Encoding]::new($false))
$env:XR_TTS_MODELS='D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp/data/models/tts-local'
$env:ORT_DYLIB_PATH='D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp/target/debug/onnxruntime.dll'
$env:XR_HOST_DATA=Join-Path $private 'data';$env:XR_HOST_PORT='17862'
$process=Start-Process -FilePath "$root/tools/xr-host/target/c0/debug/lingchat-xr-host.exe" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$private/host-out.log" -RedirectStandardError "$private/host-error.log"
try{
    $ready=$false;$until=(Get-Date).AddSeconds(30)
    while((Get-Date)-lt$until){
        try{$connection=Get-Content "$private/xr-host-connection.json" -Raw|ConvertFrom-Json;$health=Invoke-RestMethod ($connection.endpoint+'/health') -Headers @{Authorization=('Bearer '+$connection.token)} -TimeoutSec 2;if($health.ready){$ready=$true;break}}catch{}
        if($process.HasExited){throw '独立宿主提前退出，详见私有日志。'}
        Start-Sleep -Milliseconds 250
    }
    if(-not$ready){throw '独立宿主未就绪。'}
    & "$PSScriptRoot/run-c0-settings-check.ps1" -Mode Voice -EvidenceDirectory 'evidence/q2-c0-2026-10-31/voice'
    [pscustomobject]@{privateFixtureConfiguration=$true;port=17862;realLocalTts=$true;cloudRequestSent=$false;userHostStopped=$false} |ConvertTo-Json|Set-Content "$evidence/host-boundary.json"
}finally{if(-not$process.HasExited){Stop-Process -Id $process.Id};Remove-Item Env:XR_HOST_PORT -ErrorAction SilentlyContinue}
