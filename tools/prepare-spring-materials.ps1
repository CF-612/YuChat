param(
    [string]$PackageDirectory = "$PSScriptRoot/../LingChat-XR/Assets/StreamingAssets/Characters/local-smallbluefish-alpha2",
    [string]$Profile = "$PSScriptRoot/physics-profiles/smallbluefish-spring-materials.json",
    [switch]$RestoreDefaults
)
$ErrorActionPreference='Stop'
$path=Join-Path $PackageDirectory 'manifest.json'
$package=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if ($RestoreDefaults) {
    $package.PSObject.Properties.Remove('springPhysics')
} else {
    $config=Get-Content -LiteralPath $Profile -Raw | ConvertFrom-Json
    if (-not $config.materials -or -not $config.bindings) {throw '缺少物理材质或链映射'}
    $package | Add-Member -Force NoteProperty springPhysics $config
}
$package | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $path -Encoding utf8
Write-Output '已更新角色包物理配置。重新载入角色后生效；VRM 文件未修改。'
