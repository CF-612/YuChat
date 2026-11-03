param([switch]$Conversation)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$connection=Get-Content (Join-Path $root '.cache/xr-host-connection.json') -Raw|ConvertFrom-Json
$headers=@{Authorization=('Bearer '+$connection.token)}
function Post($route,$body){Invoke-RestMethod ($connection.endpoint+$route) -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body|ConvertTo-Json -Compress)))}
$session=Post '/session' @{roleId='feiyu'}
$cursor=$session.cursor
$results=@()
$prompts=if($Conversation){@('你好，我今天想给小样取个昵称叫泡泡。请简短回复。','你还记得我刚才取的昵称吗？','那我们下次再聊，给我一句简短的告别吧。')}else{@('')}
foreach($prompt in $prompts){
    $turn=Post '/turn' @{sessionId=$session.sessionId;text=$prompt;testVoice=(-not $Conversation)}
    $all=@();$deadline=(Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 1
        $batch=Invoke-RestMethod ($connection.endpoint+'/events?sessionId='+$session.sessionId+'&after='+$cursor) -Headers $headers
        foreach($event in $batch.events){$cursor=[Math]::Max($cursor,$event.cursor);if($event.turnId -eq $turn.turnId){$all+=$event}}
        if((Get-Date) -gt $deadline){throw 'Live service timeout'}
    }until($all.kind -contains 'streamEnd')
    $errors=@($all|Where-Object {$_.kind -in @('error','sentenceError')})
    $audio=@($all|Where-Object kind -eq 'audio')
    $texts=@($all|Where-Object kind -eq 'text')
    $all|ConvertTo-Json -Depth 6|Set-Content (Join-Path $root ('.cache/f2-live-'+$turn.turnId+'.json'))
    if($errors.Count -gt 0 -or $audio.Count -ne $texts.Count -or $audio.Count -eq 0){throw 'Live round did not produce complete sentence audio; private diagnostic is in .cache.'}
    foreach($a in $audio){$f=Join-Path $root ('.cache/xr-host-data/audio/'+$a.audioFile);if((Get-Item $f).Length -lt 1000){throw 'Audio missing or too short'}}
    $results+=@{round=$results.Count+1;sentences=$texts.Count;audio=$audio.Count;generation=$turn.generation;result='passed'}
    Write-Output "Real round $($results.Count): $($audio.Count) sentence audio files ready."
}
$report=@{result='passed';realLlm=[bool]$Conversation;realLocalTts=$true;voice='ling-v2';rounds=$results;unityPlaybackVerified=$false;userListeningAccepted=$false;questTested=$false}
$report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $root ('evidence/f2-live-'+$(if($Conversation){'conversation'}else{'tts'})+'.json'))
