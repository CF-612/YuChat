param([Parameter(Mandatory)][string]$Report,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$walk=Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
$results=foreach($path in $walk.trajectories){
    $near=0
    $below=0
    $above=0
    $drift=[Collections.Generic.List[double]]::new()
    $band=.01*$path.height
    for($i=1;$i -lt $path.samples.Count;$i++){
        $previous=$path.samples[$i-1]
        $current=$path.samples[$i]
        if($previous.phase -ne 'Walking' -or $current.phase -ne 'Walking' -or $previous.speed -lt .9*$path.nominalSpeed -or $current.speed -lt .9*$path.nominalSpeed){continue}
        foreach($foot in 'leftSole','rightSole'){
            $point=$current.$foot
            $prior=$previous.$foot
            if($point.Count -ne 3 -or $prior.Count -ne 3){throw '轨迹缺少脚底参考点，请重新运行 LocomotionProbe'}
            $gap=$point[1]-$current.root[1]
            if($gap -gt $band){$above++;continue}
            if($gap -lt -$band){$below++;continue}
            $near++
            if([Math]::Abs($prior[1]-$previous.root[1]) -gt $band){continue}
            $dx=$point[0]-$prior[0]
            $dz=$point[2]-$prior[2]
            $drift.Add(60*[Math]::Sqrt($dx*$dx+$dz*$dz)/$current.speed)
        }
    }
    $drift.Sort()
    $median=$null
    if($drift.Count -gt 0){
        $middle=[int][Math]::Floor($drift.Count/2)
        $median=if($drift.Count%2){$drift[$middle]}else{($drift[$middle-1]+$drift[$middle])/2}
    }
    [ordered]@{model=$path.model;height=$path.height;bandMeters=$band;above=$above;below=$below;near=$near;consecutiveNear=$drift.Count;medianSpeedRatio=$median}
}
[ordered]@{method='踝下参考点随足骨变换；连续两帧处于桌面上下身高1%范围，60Hz。不是实际鞋底蒙皮或受力。';results=@($results)}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Output
