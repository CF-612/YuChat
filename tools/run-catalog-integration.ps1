param([int]$Port=17864)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$private=Join-Path $root ".cache/catalog-integration-$stamp"
$evidenceRelative="evidence/catalog-integration-$stamp"
$evidence=Join-Path $root $evidenceRelative
$build=Join-Path $root '.cache/catalog-validation-build'
New-Item -ItemType Directory -Force $private,$evidence,$build,"$private/data"|Out-Null
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw '测试端口被占用，未停止已有服务。'}
# 独立可执行名称共用已缓存依赖，避免覆盖用户正在运行的宿主。
$manifest=[IO.File]::ReadAllText("$root/tools/xr-host/Cargo.toml").Replace('name = "lingchat-xr-host"','name = "lingchat-xr-host-validation"').Replace('path = "vendor/esaxx-rs"',('path = "'+($root.Replace('\','/'))+'/tools/xr-host/vendor/esaxx-rs"'))
$manifest+="`n[[bin]]`nname = `"lingchat-xr-host-validation`"`npath = `"$($root.Replace('\','/'))/tools/xr-host/src/main.rs`"`n"
[IO.File]::WriteAllText("$build/Cargo.toml",$manifest)
Copy-Item "$root/tools/xr-host/Cargo.lock" "$build/Cargo.lock" -Force
& "$env:USERPROFILE/.cargo/bin/cargo.exe" build --offline --target-dir "$root/tools/xr-host/target/c0" --manifest-path "$build/Cargo.toml" > "$evidence/build.log" 2>&1
if($LASTEXITCODE-ne0){throw "独立宿主构建失败：$evidence/build.log"}
$settings=@{'llm.providers'=@(@{id='fixture';provider='openai';model='fixture-no-cloud';api_key='fixture-only';base_url='http://127.0.0.1:9/v1'});'llm.chat_provider_id'='fixture';'llm.translate_provider_id'='fixture'}
[IO.File]::WriteAllText("$private/settings.json",($settings|ConvertTo-Json -Depth 10))
$roles=Get-Content "$root/tools/xr-host/roles.default.json" -Raw|ConvertFrom-Json
$roles.roles+= [pscustomobject]@{id='fixture-third';label='独立测试角色';persona='private-fixture-persona';voiceProfile='ling-v2';spokenLanguage='ja'}
[IO.File]::WriteAllText("$private/data/roles.json",($roles|ConvertTo-Json -Depth 10))
$history=@(0..154|ForEach-Object {@{speaker='user';text="fixture-history-$_"}})
$sentences=@(0..104|ForEach-Object {@{turnId="old-$_";index=2;message="fixture-sentence-$_";ttsText='';audioFile='';emotion='开心';motion=''}})
$legacy=@{'fixture-session'=@{id='fixture-session';roleId='feiyu';generation=10;history=$history;sentences=$sentences}}
[IO.File]::WriteAllText("$private/data/sessions.json",($legacy|ConvertTo-Json -Depth 20))
$oldHash=(Get-FileHash "$private/data/sessions.json").Hash
$env:XR_LINGCHAT_SETTINGS="$private/settings.json";$env:XR_TTS_MODELS="$private/unused-models";$env:XR_HOST_DATA="$private/data";$env:XR_HOST_PORT="$Port"
$process=Start-Process "$root/tools/xr-host/target/c0/debug/lingchat-xr-host-validation.exe" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$private/stdout.log" -RedirectStandardError "$private/stderr.log"
$checks=[Collections.Generic.List[string]]::new()
function Check($condition,[string]$name){if(-not $condition){throw $name};$checks.Add($name)}
try {
    $ready=$false;$until=(Get-Date).AddSeconds(30)
    while((Get-Date)-lt$until){
        if($process.HasExited){throw "独立宿主退出：$private/stderr.log"}
        if(Test-Path "$private/xr-host-connection.json"){
            $connection=Get-Content "$private/xr-host-connection.json" -Raw|ConvertFrom-Json
            $headers=@{Authorization=('Bearer '+$connection.token)}
            try{$health=Invoke-RestMethod ($connection.endpoint+'/health') -Headers $headers -TimeoutSec 2;if($health.ready){$ready=$true;break}}catch{}
        }
        Start-Sleep -Milliseconds 200
    }
    Check $ready '独立真实宿主就绪'
    $inventory=Invoke-RestMethod ($connection.endpoint+'/storage-summary') -Headers $headers
    Check ($inventory.readOnly-and$inventory.storage.committedSessions-eq1-and$inventory.storage.committedHistoryRows-eq155-and$inventory.storage.committedSentences-eq105) '在线只读盘点返回已提交数量'
    Check (-not(($inventory|ConvertTo-Json -Depth 10).Contains('fixture-history'))) '容量盘点不返回历史正文'
    $catalog=Invoke-RestMethod ($connection.endpoint+'/catalog') -Headers $headers
    Check ($catalog.roles.Count-eq3) '真实路由返回三个配置角色'
    Check (-not(($catalog|ConvertTo-Json -Depth 10).Contains('private-fixture-persona'))) '公开清单不泄漏人设'
    $session=Invoke-RestMethod ($connection.endpoint+'/session') -Headers $headers -Method Post -ContentType 'application/json' -Body '{"roleId":"feiyu"}'
    Check ($session.history.Count-eq50-and$session.historyBefore-eq105) '旧历史迁移后的首次分页'
    $page=Invoke-RestMethod ($connection.endpoint+'/history?sessionId=fixture-session&before=105') -Headers $headers
    Check ($page.history.Count-eq50-and$page.historyBefore-eq55) '早期历史真实 HTTP 分页'
    $gap=Invoke-RestMethod ($connection.endpoint+'/events?sessionId=fixture-session&after=1') -Headers $headers
    Check ($gap.cursorExpired-and$gap.history.Count-eq155-and$null-eq$gap.snapshot-and-not$gap.busy) '重启后过期游标恢复历史且不重播旧轮'
    $snapshot=Invoke-RestMethod ($connection.endpoint+"/snapshot?sessionId=fixture-session&expectedGeneration=$($session.generation)") -Headers $headers
    Check ($null-eq$snapshot.snapshot-and$snapshot.generation-eq$session.generation) '真实快照路由保持当前身份'
    foreach($case in @(@{path='/catalog';headers=@{};status=401},@{path='/storage-summary';headers=@{};status=401},@{path='/snapshot?sessionId=fixture-session&expectedGeneration=10';headers=$headers;status=409})){
        $code=200;try{$null=Invoke-WebRequest ($connection.endpoint+$case.path) -Headers $case.headers}catch{$code=[int]$_.Exception.Response.StatusCode}
        Check ($code-eq$case.status) ("HTTP 拒绝状态 "+$case.status)
    }
    Check ((Get-FileHash "$private/data/sessions.json").Hash-eq$oldHash-and(Get-FileHash "$private/data/sessions.pre-journal.json").Hash-eq$oldHash) '迁移保留原文件与备份字节'
    $env:XR_SERVICE_SETTINGS="$private/settings.json"
    & "$PSScriptRoot/run-m1-check.ps1" -EvidenceDirectory $evidenceRelative -Method LingChatXR.Editor.CatalogConnectionProbe.Run -HostConnectionPath "$private/xr-host-connection.json"
    @{checks=$checks;realHost=$true;realUnityHttp=$true;realInference=$false;userServiceStopped=$false;apkBuilt=$false}|ConvertTo-Json -Depth 10|Set-Content "$evidence/host-checks.json" -Encoding utf8
    Write-Output "CATALOG_INTEGRATION_PASSED $evidenceRelative"
} finally {
    if(-not$process.HasExited){Stop-Process -Id $process.Id}
}
