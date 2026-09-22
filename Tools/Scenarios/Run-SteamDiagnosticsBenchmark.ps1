param([string]$Executable='Builds/SteamDiagnosticsValidation/MonsterSupergroup.exe',[string]$Artifacts)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
if(-not [IO.Path]::IsPathRooted($Executable)){$Executable=Join-Path $root $Executable}
if(-not $Artifacts){$Artifacts=Join-Path $root ('Logs/SteamLagBenchmark/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null
$results=@()
try {
    for($pair=1;$pair -le 3;$pair++) {
        foreach($mode in @('off','on')) {
            $folder=Join-Path $Artifacts "$pair-$mode"
            New-Item -ItemType Directory -Path $folder | Out-Null
            $arguments=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+$folder+'/Player.log"'),"--lag-benchmark=$mode",'--lag-uncapped',('"--lag-output='+$folder+'"'),('"--network-diagnostics-output='+$folder+'/metrics"'))
            $p=Start-ProjectProcess -FilePath $Executable -WindowStyle Hidden -ArgumentList $arguments -PassThru
            Write-Output "Pair $pair $mode PID $($p.Id)"
            Wait-ProjectProcess $p 150
            $p.ExitCode | Set-Content -LiteralPath (Join-Path $folder 'exit-code.txt')
            if($p.ExitCode -ne 0){throw "Benchmark failed: $folder"}
            $result=Get-Content -LiteralPath (Join-Path $folder 'benchmark.json') -Raw | ConvertFrom-Json
            $results+=$result
            Write-Output "mean=$($result.meanMs) ms; main=$($result.mainMeanMs) ms; enemies=$($result.enemies)"
        }
    }
    $off=($results|Where-Object {-not $_.diagnostics}|Measure-Object meanMs -Average).Average
    $on=($results|Where-Object {$_.diagnostics}|Measure-Object meanMs -Average).Average
    $summary=[ordered]@{result='measured';method='Three alternating pairs, 20 seconds per sample after warmup, identical two-enemy KCP graphical fixture; uncapped FPS, VSync off. Not Steam performance.';runs=$results;offMeanMs=$off;onMeanMs=$on;overheadPercent=100*($on/$off-1);withinFivePercent=($on -le $off*1.05)}
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Artifacts 'summary.json') -Encoding UTF8
    $summary | ConvertTo-Json -Depth 6
} finally {Clear-ProjectProcesses}
