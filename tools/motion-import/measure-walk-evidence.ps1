param([string]$Report='evidence/locomotion-2026-11-02/walk-final/walk.json',[string]$Output='evidence/locomotion-2026-11-02/foot-drift.json')
$ErrorActionPreference='Stop'
$reportData=Get-Content -LiteralPath $Report -Raw|ConvertFrom-Json
$results=@()
foreach($trajectory in $reportData.trajectories){
    $speeds=@()
    foreach($foot in @('leftFoot','rightFoot')){
        $heights=@($trajectory.samples|ForEach-Object {$_.$foot[1]})
        $low=($heights|Measure-Object -Minimum).Minimum;$high=($heights|Measure-Object -Maximum).Maximum
        for($i=1;$i -lt $trajectory.samples.Count;$i++){
            $a=$trajectory.samples[$i-1];$b=$trajectory.samples[$i]
            if($b.phase-eq'Walking' -and $b.speed-gt .9*$trajectory.nominalSpeed -and $b.$foot[1]-lt $low+.3*($high-$low)){
                $dx=$b.$foot[0]-$a.$foot[0];$dz=$b.$foot[2]-$a.$foot[2];$speeds+=[Math]::Sqrt($dx*$dx+$dz*$dz)*60
            }
        }
    }
    if(-not $speeds.Count){throw 'No supported stance samples'}
    $sorted=@($speeds|Sort-Object);$median=$sorted[[int]($sorted.Count/2)]
    $results+=[ordered]@{model=$trajectory.model;height=$trajectory.height;nominalSpeed=$trajectory.nominalSpeed;stanceMedianSpeed=$median;relativeMedianDrift=$median/$trajectory.nominalSpeed;samples=$sorted.Count}
}
[ordered]@{method='60 Hz; horizontal foot movement at >90% nominal speed and bottom 30% of foot-height range; ankle proxy, not physical sole contact';results=$results;notFootLock=$true;questTested=$false}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Output -Encoding utf8
Write-Output "Saved: $Output"
