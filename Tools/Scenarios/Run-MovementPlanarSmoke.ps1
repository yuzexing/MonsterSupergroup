[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$Output,
    [ValidateSet('movement-observe','effects-observe')][string]$Profile='movement-observe',
    [switch]$TwoPlayers,[int]$Port=7987)
$ErrorActionPreference='Stop'
$exe=[IO.Path]::GetFullPath($Executable)
$root=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $root){throw 'Preserve old evidence: use a new output directory.'}
New-Item -ItemType Directory -Path $root -Force | Out-Null
$processes=@()
try {
    foreach($role in $(if($TwoPlayers){@('host','client')}else{@('host')})) {
        $directory=Join-Path $root $role
        New-Item -ItemType Directory -Path $directory | Out-Null
        $arguments=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
            '-logFile',('"'+(Join-Path $directory 'player.log')+'"'),"--limbo-profile=$Profile","--limbo-role=$role",
            '--limbo-log-detail=light','--limbo-motion-case=smoke','--limbo-effects-case=smoke',
            "--limbo-port=$Port",("--limbo-wait-for="+$(if($TwoPlayers){2}else{1})),
            '--limbo-autowalk=false','--limbo-windowed=true',('"--limbo-output='+$directory+'"'))
        if($TwoPlayers){$arguments+='--limbo-motion-target=client'}
        $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $processes+= $process
        @{role=$role;pid=$process.Id;executable=$exe;arguments=$arguments;assisted=$true;pressureEvidence=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'launch.json') -Encoding utf8
        Start-Sleep -Milliseconds 500
    }
    $deadline=[DateTime]::UtcNow.AddSeconds(240)
    while(@($processes|Where-Object {-not $_.HasExited}).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Seconds 1}
    $results=@($processes|ForEach-Object {@{pid=$_.Id;exited=$_.HasExited;exitCode=$(if($_.HasExited){$_.ExitCode}else{-1})}})
    $results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'process-results.json') -Encoding utf8
    if(@($results|Where-Object {-not $_.exited -or $_.exitCode -ne 0}).Count){throw 'Smoke did not exit successfully; inspect preserved logs.'}
    Write-Output "Process smoke exited successfully: $root (assisted, no visual/pressure acceptance)."
} finally {
    foreach($process in $processes){if(-not $process.HasExited){Stop-Process -Id $process.Id}}
}
