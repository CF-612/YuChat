param([Parameter(Mandatory=$true)][string]$Package)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$archive=(Resolve-Path -LiteralPath $Package).Path
$sha=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$cache=Join-Path $root '.cache/motion-source'
$destination=Join-Path $cache ('human-basic-'+$sha.Substring(0,16).ToLowerInvariant())
$resolved=[IO.Path]::GetFullPath($destination)
if(-not $resolved.StartsWith([IO.Path]::GetFullPath($cache)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid cache destination'}
$entries=@(& tar -tzf $archive)
if($LASTEXITCODE-ne0){throw 'Expected an official Unity package (gzip tar)'}
foreach($entry in $entries){if($entry-match '(^[/\\]|^[A-Za-z]:|(^|[/\\])\.\.([/\\]|$))'){throw 'Unsafe archive entry'}}
$details=@(& tar -tvzf $archive)
if($LASTEXITCODE-ne0 -or @($details|Where-Object {$_ -match '^[lh]'}).Count){throw 'Links are not accepted in this source package'}
if(-not(Test-Path -LiteralPath $destination)){
    New-Item -ItemType Directory -Force $destination|Out-Null
    & tar -xzf $archive -C $destination
    if($LASTEXITCODE-ne0){throw 'Extraction failed; retained incomplete cache for inspection'}
}
$catalog=@(Get-ChildItem -LiteralPath $destination -Filter pathname -Recurse|ForEach-Object {
    $path=([IO.File]::ReadAllText($_.FullName)).Trim()
    if(-not $path.StartsWith('Assets/') -or $path.Contains('..')){throw 'Invalid Unity asset pathname'}
    [ordered]@{Path=$path;Folder=$_.Directory.FullName}
})
foreach($name in @('HumanF@Idle01.fbx','HumanF@Walk01_Forward.fbx','HumanF@Turn01_Left.fbx','HumanF@Turn01_Right.fbx')){
    if(@($catalog|Where-Object {[IO.Path]::GetFileName($_.Path)-eq$name}).Count-ne1){throw "Required clip missing or ambiguous: $name"}
}
[IO.File]::WriteAllText((Join-Path $cache 'kevin-catalog.json'),($catalog|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
[ordered]@{packageSHA256=$sha;assetEntries=$catalog.Count;cache=$destination;publicRedistribution=$false}|ConvertTo-Json
# 原素材仅保存在被忽略的缓存中；不会写主工程、用户模型或公开动作目录。
