param([Parameter(Mandatory=$true)][string]$Apk,[switch]$RequireLocomotion)
$ErrorActionPreference='Stop'
$apkPath=(Resolve-Path -LiteralPath $Apk).Path
$root=Split-Path -Parent $PSScriptRoot
$archive=[IO.Compression.ZipFile]::OpenRead($apkPath)
try {
    $names=@($archive.Entries|ForEach-Object FullName)
    $models=@()
    foreach($folder in @('local-smallbluefish-alpha2','local-physics-reference')){
        $entry=@($archive.Entries|Where-Object {$_.FullName.EndsWith("/Characters/$folder/model.vrm")})
        if($entry.Count-ne1){throw "APK 缺少或重复模型：$folder"}
        $stream=$entry[0].Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
        $expected=(Get-FileHash (Join-Path $root "LingChat-XR/Assets/StreamingAssets/Characters/$folder/model.vrm")).Hash
        if($hash-ne$expected){throw "APK 模型与原件不同：$folder"}
        $models+=[pscustomobject]@{Folder=$folder;SHA256=$hash;MatchesSource=$true}
    }
    if(-not($names-match '^lib/arm64-v8a/libil2cpp.so$')){throw '缺少 ARM64 IL2CPP 库'}
    if(-not($names-match '/Characters/index.json$')){throw '缺少 Android 角色索引'}
    if($names-match 'xr-host-connection|xr-service-settings|xr-dialogue-preferences|xr-paired-host|xr-pairing'){throw 'APK 不应包含私有连接、配对凭据或用户配置'}
    $locomotion=@()
    if($RequireLocomotion){
        $catalog=Get-Content (Join-Path $root 'LingChat-XR/Assets/StreamingAssets/Motions/local-locomotion/catalog.json') -Raw | ConvertFrom-Json
        foreach($id in @($catalog.actions | ForEach-Object id)){
            $matches=@($archive.Entries|Where-Object {$_.FullName.EndsWith("/Motions/local-locomotion/$id.vrma")})
            if($matches.Count-ne1){throw "APK 缺少或重复行走动作：$id"}
            $stream=$matches[0].Open();$sha=[Security.Cryptography.SHA256]::Create()
            try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
            $expected=(Get-FileHash (Join-Path $root "LingChat-XR/Assets/StreamingAssets/Motions/local-locomotion/$id.vrma")).Hash
            if($hash-ne$expected){throw "APK 行走动作与已验版本不同：$id"}
            $locomotion+=[pscustomobject]@{Id=$id;SHA256=$hash;MatchesSource=$true}
        }
        if(-not($names-match '/Motions/local-locomotion/catalog.json$')){throw 'APK 缺少行走目录'}
        if($names-match 'LocalMotionAudit|\.unitypackage$|\.fbx$'){throw 'APK 不应夹带动作审计原件'}
    }
    $result=[pscustomobject]@{ApkSHA256=(Get-FileHash $apkPath).Hash;Bytes=(Get-Item $apkPath).Length;Models=$models;Locomotion=$locomotion;Arm64Il2cpp=$true;PrivateConfigNamesAbsent=$true;DeviceTested=$false}
    $result|ConvertTo-Json -Depth 5|Set-Content ([IO.Path]::ChangeExtension($apkPath,'audit.json')) -Encoding utf8
    Write-Output 'APK 文件结构与双模型原件校验通过；尚未安装或验证设备画面。'
}finally{$archive.Dispose()}
