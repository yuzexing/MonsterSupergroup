param(
    [string]$BuildDirectory = 'Builds/LimboSpatial20260915',
    [string]$ClientDirectory = 'Builds/LimboSpatialClient20260915',
    [string]$Prefix = 'spatial-matrix-20260915',
    [int]$Port = 8180,
    [switch]$CancelOnly,
    [string[]]$OnlyCases = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cases = @(
    @('spatial-b','pause-all'), @('spatial-b','cancel-Delay'), @('spatial-b','cancel-Building'),
    @('spatial-barrier','cancel-Framing'), @('spatial-barrier','cancel-Building'),
    @('spatial-barrier','cancel-Shrinking'), @('spatial-barrier','cancel-Stopping'),
    @('spatial-overlap','observe'), @('spatial-overlap','occupancy')
)
foreach ($case in $cases) {
    if ($OnlyCases.Count -gt 0 -and "$($case[0])/$($case[1])" -notin $OnlyCases) { continue }
    if ($CancelOnly -and !$case[1].StartsWith('cancel-')) { continue }
    $name = "$Prefix-$($case[0])-$($case[1])"
    & "$PSScriptRoot/Run-LimboReference.ps1" -Role host -Profile $case[0] -SpatialCase $case[1] -WaitFor 2 -Port $Port -Windowed -AutoWalk -BuildDirectory $BuildDirectory -RunName $name
    $folder = Join-Path $root "Logs/LimboReference/$name"
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(90)
    while (!(Test-Path -LiteralPath "$folder/host/player.log") -or
           !(Select-String -LiteralPath "$folder/host/player.log" -SimpleMatch '[Preparation] Open' -Quiet)) {
        if ([DateTime]::UtcNow -ge $readyDeadline) { throw "Host did not open preparation: $name" }
        Start-Sleep -Milliseconds 500
    }
    & "$PSScriptRoot/Run-LimboReference.ps1" -Role client -Profile $case[0] -SpatialCase $case[1] -WaitFor 2 -Port $Port -Windowed -AutoWalk -BuildDirectory $ClientDirectory -RunName $name
    $folder = Join-Path $root "Logs/LimboReference/$name"
    $pids = @((Get-Content -LiteralPath "$folder/host/process.pid"), (Get-Content -LiteralPath "$folder/client/process.pid"))
    $deadline = [DateTime]::UtcNow.AddSeconds(160)
    $done = $false
    try {
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 1
            $hostFile = "$folder/host/spatial-observation.jsonl"
            $clientFile = "$folder/client/spatial-observation.jsonl"
            if (!(Test-Path -LiteralPath $hostFile) -or !(Test-Path -LiteralPath $clientFile)) { continue }
            $both = (Select-String -LiteralPath $hostFile -SimpleMatch 'completed-cleanup' -Quiet) -and
                (Select-String -LiteralPath $clientFile -SimpleMatch 'completed-cleanup' -Quiet)
            if ($both) {
                # Cancellation remains paused briefly to validate cleanup before resuming.
                if ($case[1].StartsWith('cancel-') -and !(Select-String -LiteralPath "$folder/host/spatial-actions.jsonl" -SimpleMatch '"kind":"resume"' -Quiet)) { continue }
                $done = $true; break
            }
        }
        [pscustomobject]@{ run=$name; completedRecords=$done; classification='Technical fixture only; graphical and semantic review required' } |
            ConvertTo-Json | Set-Content -LiteralPath "$folder/matrix-run.json"
        if (!$done) { throw "Spatial fixture timed out: $name" }
        Start-Sleep -Seconds 2
    } finally {
        foreach ($processId in $pids) {
            $process = Get-Process -Id ([int]$processId) -ErrorAction SilentlyContinue
            if ($process -and $process.ProcessName -eq 'MonsterSupergroupLimbo') { Stop-Process -Id $process.Id }
        }
    }
    $Port++
    Write-Output "Recorded $name"
}
