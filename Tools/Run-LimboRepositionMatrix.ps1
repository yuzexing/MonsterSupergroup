param(
    [string]$BuildDirectory = 'Builds/LimboSpatialBoundary20260915',
    [string]$ClientDirectory = 'Builds/LimboSpatialBoundaryClient20260915',
    [string]$Prefix = 'spatial-boundary-20260915',
    [int]$Port = 8220,
    [ValidateSet('distance','placement-failure','expiry','framing')][string[]]$Cases = @('distance','placement-failure','expiry','framing')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($case in $Cases) {
    $name = "$Prefix-$case"
    $folder = Join-Path $root "Logs/LimboReference/$name"
    $owned = @()
    try {
        & "$PSScriptRoot/Run-LimboReference.ps1" -Role host -Profile spatial-reposition -RepositionCase $case -FixtureTarget client -WaitFor 2 -Port $Port -Windowed -BuildDirectory $BuildDirectory -RunName $name
        $owned += [int](Get-Content -LiteralPath "$folder/host/process.pid")
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        while (!(Test-Path -LiteralPath "$folder/host/player.log") -or !(Select-String -LiteralPath "$folder/host/player.log" -SimpleMatch '[Preparation] Open' -Quiet)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Host preparation timeout' }
            Start-Sleep -Milliseconds 500
        }
        & "$PSScriptRoot/Run-LimboReference.ps1" -Role client -Profile spatial-reposition -RepositionCase $case -FixtureTarget client -WaitFor 2 -Port $Port -Windowed -BuildDirectory $ClientDirectory -RunName $name
        $owned += [int](Get-Content -LiteralPath "$folder/client/process.pid")
        $deadline = [DateTime]::UtcNow.AddSeconds(190)
        $complete = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 1
            $files = @('host','client') | ForEach-Object { "$folder/$_/spatial-observation.jsonl" }
            if (@($files | Where-Object { (Test-Path -LiteralPath $_) -and (Select-String -LiteralPath $_ -SimpleMatch 'completed-cleanup' -Quiet) }).Count -eq 2) { $complete = $true; break }
        }
        if (!$complete) { throw "Boundary fixture timeout: $case" }
        Start-Sleep -Seconds 2
        Write-Output "Recorded $name"
    } finally {
        foreach ($processId in $owned) {
            $game = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($game -and $game.ProcessName -eq 'MonsterSupergroupLimbo') { Stop-Process -Id $game.Id }
        }
    }
    $Port++
}
