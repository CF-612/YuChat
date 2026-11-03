param([Parameter(Mandatory=$true)][string]$SourcePackage,[Parameter(Mandatory=$true)][string]$Folder,[string]$ProjectPath='LingChat-XR')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if($Folder-notmatch '^local-[A-Za-z0-9._-]+$'){throw 'Use a local- prefixed package folder name.'}
$source=(Resolve-Path -LiteralPath $SourcePackage).Path
$manifestPath=Join-Path $source 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if($manifest.schemaVersion-ne 1 -or $manifest.model.sha256-notmatch '^[0-9a-fA-F]{64}$'){throw 'Invalid package manifest.'}
$project=[IO.Path]::GetFullPath((Join-Path $root $ProjectPath))
if(-not $project.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Target project must be inside this workspace.'}
$dest=Join-Path $project "Assets/StreamingAssets/Characters/$Folder"
$assets=@($manifest.assets)
if(-not($assets | Where-Object path -EQ $manifest.model.path)){$assets+=@{path=$manifest.model.path;sha256=$manifest.model.sha256}}
foreach($asset in $assets){
 $relative=[string]$asset.path
 if([IO.Path]::IsPathRooted($relative)-or $relative.Contains(':')-or $relative.Contains('\')-or ($relative.Split('/') | Where-Object {$_-in @('','..','.')})){throw 'Unsafe asset path.'}
 $file=Join-Path $source $relative
 if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash-ne $asset.sha256){throw "Asset hash mismatch: $relative"}
}
if(Test-Path -LiteralPath $dest){
 if((Get-FileHash -LiteralPath (Join-Path $dest 'manifest.json') -Algorithm SHA256).Hash-ne (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash){throw 'Existing package differs; choose another folder.'}
 foreach($asset in $assets){if((Get-FileHash -LiteralPath (Join-Path $dest $asset.path) -Algorithm SHA256).Hash-ne $asset.sha256){throw 'Existing asset differs; no overwrite.'}}
 Write-Output "Reused verified package: $dest";return
}
New-Item -ItemType Directory -Path $dest | Out-Null
foreach($asset in $assets){$file=Join-Path $dest $asset.path;New-Item -ItemType Directory -Force (Split-Path -Parent $file) | Out-Null;Copy-Item -LiteralPath (Join-Path $source $asset.path) -Destination $file}
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $dest 'manifest.json')
Write-Output "Installed verified local test package: $dest"
