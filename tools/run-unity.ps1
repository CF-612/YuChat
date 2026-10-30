param(
    [ValidateSet('Import','Prepare','Smoke')][string]$Mode = 'Prepare',
    [string]$Unity = 'D:\GameCreator\Unity\Editor\Unity.exe',
    [string]$LogName = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $taskRoot 'LingChat-XR'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity 6 executable missing: $Unity" }
$version = (Get-Item -LiteralPath $Unity).VersionInfo.ProductVersion
if ($version -notmatch '^6000\.') { throw "Unity 6 required; found $version" }
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'evidence') -Force | Out-Null
if (-not $LogName) { $LogName = 'unity6-' + $Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log' }
if ([IO.Path]::GetFileName($LogName) -ne $LogName) { throw 'LogName must be a filename.' }
$logPath = Join-Path (Join-Path $taskRoot 'evidence') $LogName
# Unity 6000.4.0f1's empty-project Search startup can throw before PlayMode starts.
# This project-local setting only skips eager indexing. Preserve any existing user settings.
$searchSettings = Join-Path $projectRoot 'UserSettings\Search.settings'
if (-not (Test-Path -LiteralPath $searchSettings)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $searchSettings) -Force | Out-Null
    [IO.File]::WriteAllText($searchSettings, '{"indexOnEditorStartup":false}')
}
$arguments = @('-batchmode','-projectPath',('"' + $projectRoot + '"'),'-logFile',('"' + $logPath + '"'))
if ($Mode -eq 'Smoke') {
    $arguments += @('-force-d3d11','-executeMethod','LingChatXR.Editor.PreviewSmoke.Run')
} else {
    $arguments += @('-nographics','-quit')
    if ($Mode -eq 'Prepare') { $arguments += @('-executeMethod','LingChatXR.Editor.PreviewProject.ValidateProject') }
}
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
Write-Output "Unity exit=$($process.ExitCode); log=$logPath"
if ($process.ExitCode -ne 0) { throw "Unity $Mode failed; inspect $logPath" }
if ($Mode -eq 'Smoke' -and -not (Select-String -LiteralPath $logPath -SimpleMatch 'XR_SMOKE_PASSED' -Quiet)) { throw 'Smoke process exited without a success marker.' }
