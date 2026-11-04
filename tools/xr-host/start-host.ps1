param(
    [string]$LingChatRoot = 'D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp',
    [string]$SettingsPath = (Join-Path $env:APPDATA 'com.noiq.ling-chat.vrm-mvp/settings.json'),
    [string]$ServiceSettingsPath = '',
    [string]$AsrSettingsPath = '',
    [switch]$Build,
    [switch]$VerifyTranslation,
    [switch]$ExportReactions
)
$ErrorActionPreference = 'Stop'
$startupStage = 'settings'
trap {
    # 仅回传阶段，不把含凭据的配置或异常正文送进 Unity 日志。
    Write-Output ('XR_HOST_FAILED:' + $startupStage)
    exit 1
}
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$data = Join-Path $taskRoot '.cache/xr-host-data'
New-Item -ItemType Directory -Force -Path $data | Out-Null
if(-not $ServiceSettingsPath){$ServiceSettingsPath=Join-Path $taskRoot '.cache/xr-service-settings.json'}
if(-not(Test-Path -LiteralPath $ServiceSettingsPath)){
    # 首次只读迁移服务字段；后续永远使用 XR 私有副本，不把编辑写回 LingChat。
    $sourceSettings=Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if($null -eq $sourceSettings -or $null -eq $sourceSettings.'llm.providers'){throw 'Source service settings unavailable.'}
    $xrSettings=@{}
    foreach($key in @('llm.providers','llm.chat_provider_id','llm.translate_provider_id')){
        if($null-ne $sourceSettings.PSObject.Properties[$key]){$xrSettings[$key]=$sourceSettings.$key}
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $ServiceSettingsPath) | Out-Null
    [IO.File]::WriteAllText($ServiceSettingsPath,($xrSettings | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
}
$env:XR_LINGCHAT_SETTINGS = $ServiceSettingsPath
if(-not $AsrSettingsPath){$AsrSettingsPath=$SettingsPath}
$env:XR_ASR_SETTINGS = $AsrSettingsPath
$env:XR_TTS_MODELS = Join-Path $LingChatRoot 'data/models/tts-local'
$env:XR_HOST_DATA = $data
$env:ORT_DYLIB_PATH = Join-Path $LingChatRoot 'target/debug/onnxruntime.dll'
$binary = Join-Path $PSScriptRoot 'target/c0/debug/lingchat-xr-host.exe'
if ($Build -or -not (Test-Path $binary)) {
    $startupStage = 'build'
    & (Join-Path $env:USERPROFILE '.cargo/bin/cargo.exe') build --locked --target-dir (Join-Path $PSScriptRoot 'target/c0') --manifest-path (Join-Path $PSScriptRoot 'Cargo.toml')
    if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }
}
if ($ExportReactions) { & $binary --export-reactions; if($LASTEXITCODE-ne0){throw "Reaction export failed."}; exit }
if ($VerifyTranslation) {
    & $binary --verify-translation
    if ($LASTEXITCODE -ne 0) { throw 'Real translation validation failed.' }
    Copy-Item (Join-Path $data 'translation-verification.json') (Join-Path $taskRoot 'evidence/f2-real-translation.json') -Force
    exit
}
$connectionPath=Join-Path $taskRoot '.cache/xr-host-connection.json'
function Test-HostReady {
    try {
        $c=Get-Content -LiteralPath $connectionPath -Raw | ConvertFrom-Json
        if($c.endpoint -ne 'http://127.0.0.1:17861'){return $false}
        $health=Invoke-RestMethod ($c.endpoint+'/health') -Headers @{Authorization=('Bearer '+$c.token)} -TimeoutSec 2
        return ($health.ready -eq $true)
    } catch { return $false }
}
if(Test-HostReady){Write-Output 'XR host already ready; reused existing local service.';return}
$startupStage = 'port'
if (Get-NetTCPConnection -LocalPort 17861 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 17861 is in use but XR authentication failed; no duplicate host was started.' }
$startupStage = 'start'
$process = Start-Process -FilePath $binary -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $data 'host-out.log') -RedirectStandardError (Join-Path $data 'host-error.log')
$process.Id | Set-Content (Join-Path $data 'host.pid')
$deadline=(Get-Date).AddSeconds(30)
do {
    if(Test-HostReady){Write-Output "XR host ready: PID $($process.Id). Private state stays in .cache/xr-host-data.";return}
    if($process.HasExited){throw "XR host exited before ready (code $($process.ExitCode)); inspect .cache/xr-host-data/host-error.log."}
    Start-Sleep -Milliseconds 250
} while((Get-Date)-lt $deadline)
throw 'XR host did not become ready within 30 seconds; inspect .cache/xr-host-data/host-error.log.'
