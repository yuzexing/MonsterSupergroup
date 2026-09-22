[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory, [int]$Port=7996)
$ErrorActionPreference='Stop'
$executablePath=[IO.Path]::GetFullPath($Executable)
$outputPath=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $outputPath){throw 'Use a new evidence directory.'}
New-Item -ItemType Directory -Path $outputPath | Out-Null
foreach($profile in @('audio-wisp','audio-beam')) {
    $processes=@()
    try {
        foreach($role in @('host','client')) {
            $directory=Join-Path $outputPath "$profile/$role"
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            $arguments=@('-batchmode','-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
                '-logFile',('"'+(Join-Path $directory 'player.log')+'"'),"--limbo-profile=$profile","--limbo-role=$role",
                '--limbo-audio-case=edge-smoke','--limbo-wait-for=2',"--limbo-port=$Port",'--limbo-log-detail=light',
                '--limbo-autowalk=false','--limbo-windowed=true','--limbo-performance-preset=720p60',
                ('"--limbo-output='+$directory+'"'))
            $p=Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path -Parent $executablePath) -ArgumentList $arguments -WindowStyle Hidden -PassThru
            $null=$p.Handle; $processes+=$p
            if($role -eq 'host') { Start-Sleep -Seconds 3 }
        }
        $deadline=(Get-Date).AddSeconds(180)
        while(@($processes | Where-Object {-not $_.HasExited}).Count -gt 0) {
            if((Get-Date) -gt $deadline){throw "$profile timed out"}
            Start-Sleep -Milliseconds 250
        }
        foreach($p in $processes){$p.WaitForExit(); if($p.ExitCode -ne 0){throw "$profile exited $($p.ExitCode)"}}
        foreach($role in @('host','client')) {
            $path=Join-Path $outputPath "$profile/$role/weapon-audio.jsonl"
            $rows=@(Get-Content -LiteralPath $path | ForEach-Object {ConvertFrom-Json -InputObject $_})
            if(-not ($rows | Where-Object kind -eq 'smoke-passed')) {throw "Missing completion: $profile/$role"}
            $edges=@($rows | Where-Object kind -eq 'edge-check')
            if($edges.Count -ne 9 -or ($edges | Where-Object detail -NotMatch 'passed=True')) {throw "Edge check failed: $profile/$role"}
            Write-Output "PASS $profile/$role : nine positions, framing, release; $path"
        }
    }
    finally {foreach($p in $processes){if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}}}
}
