param([Parameter(Mandatory=$true)][string]$Zip)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=(Resolve-Path -LiteralPath $Zip).Path
$sha=(Get-FileHash -LiteralPath $source).Hash
$cache=Join-Path $root '.cache/motion-source'
$destination=Join-Path $cache ('deformed-'+$sha.Substring(0,16).ToLowerInvariant())
if(-not [IO.Path]::GetFullPath($destination).StartsWith([IO.Path]::GetFullPath($cache)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid destination'}
New-Item -ItemType Directory -Force $destination | Out-Null
$archive=[IO.Compression.ZipFile]::OpenRead($source)
try{
    $entries=@($archive.Entries | Where-Object {$_.FullName -eq 'Deformed_Character_Animation_01.unitypackage'})
    if($entries.Count -ne 1){throw 'Expected purchased Unity package'}
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0],(Join-Path $destination 'source.unitypackage'),$true)
}finally{$archive.Dispose()}
$package=Join-Path $destination 'source.unitypackage'
$paths=@(& tar -tzf $package)
if($LASTEXITCODE -ne 0 -or @($paths|Where-Object{$_ -match '(^[/\\]|^[A-Za-z]:|(^|[/\\])\.\.([/\\]|$))'}).Count){throw 'Unsafe package entries'}
$details=@(& tar -tvzf $package)
if($LASTEXITCODE -ne 0 -or @($details|Where-Object{$_ -match '^[lh]'}).Count){throw 'Links are not allowed'}
& tar -xzf $package -C $destination
if($LASTEXITCODE -ne 0){throw 'Extraction failed'}
$catalog=@(Get-ChildItem -LiteralPath $destination -Filter pathname -Recurse | ForEach-Object {
    $path=([IO.File]::ReadAllText($_.FullName)).Trim()
    if($path -notin @('Assets/Animation/walkcycle.anim','Assets/Animation/skip.anim','Assets/Animation/run.anim')){throw "Unexpected asset: $path"}
    [ordered]@{Path=$path;Folder=$_.Directory.FullName}
})
if($catalog.Count -ne 3){throw 'Expected three purchased clips'}
[IO.File]::WriteAllText((Join-Path $cache 'deformed-catalog.json'),($catalog|ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
[ordered]@{zipSHA256=$sha;clips=3;localOnly=$true;licenseIncluded=$false}|ConvertTo-Json
