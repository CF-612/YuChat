param([Parameter(Mandatory = $true)][string]$SourcePath)
$ErrorActionPreference = 'Stop'
$expectedHash = '733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6'
$source = Get-Item -LiteralPath $SourcePath
if ($source.PSIsContainer -or $source.Length -ne 14061476) { throw 'Expected the original BlueBigFatFish.vrm (14061476 bytes).' }
if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'Model fingerprint mismatch; use the character importer for a different model.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $taskRoot 'LingChat-XR/Assets/StreamingAssets/Characters/blue-placeholder/model.vrm'
if (Test-Path -LiteralPath $destination) {
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'Destination contains another model; refusing to overwrite it.' }
    Write-Output 'The matching local placeholder is already present.'
    exit 0
}
Copy-Item -LiteralPath $source.FullName -Destination $destination
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'Copied model failed verification.' }
Write-Output 'Local placeholder restored. This model remains excluded from Git.'
