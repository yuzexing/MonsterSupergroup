param(
    [Parameter(Mandatory)][string]$BuildDirectory,
    [ValidateSet('lostsoul','ghoul')][string]$Enemy = 'lostsoul',
    [string[]]$Cases = @('0-host','0-client','1-host','1-client'),
    [string]$Prefix = 'timed-attack',
    [string]$Mechanism = 'main',
    [ValidateSet('normal','warning-only')][string]$AttackEdges = 'warning-only',
    [int]$Port = 8380
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$binary = [IO.Path]::GetFullPath((Join-Path $project "$BuildDirectory/MonsterSupergroupLimbo.exe"))
foreach ($case in $Cases) {
    $parts = $case.Split('-')
    if ($parts.Count -ne 2 -or $parts[0] -notin @('0','1') -or $parts[1] -notin @('host','client')) { throw "Invalid case: $case" }
    if ($Enemy -eq 'ghoul' -and $parts[0] -ne '0') { throw 'Ghoul has only variant 0.' }
    $run = "$Prefix-$Enemy-$Mechanism-$case"
    $folder = Join-Path $project "Logs/LimboReference/$run"
    $owned = @()
    $options = @{Profile="$Enemy-fixture"; BuildDirectory=$BuildDirectory; FixtureTarget=$parts[1]; Port=$Port; RunName=$run; Windowed=$true; AttackEdges=$AttackEdges}
    if ($Enemy -eq 'lostsoul') { $options.LostSoulVariant=[int]$parts[0]; $options.LostSoulCase=$Mechanism }
    else { $options.GhoulCase=$Mechanism }
    try {
        & "$PSScriptRoot/Run-LimboReference.ps1" @options -Role host -WaitFor 2
        $owned += [int](Get-Content "$folder/host/process.pid")
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        while (!(Test-Path -LiteralPath "$folder/host/player.log") -or !(Select-String -LiteralPath "$folder/host/player.log" -SimpleMatch '[Preparation] Open' -Quiet)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Host preparation timeout; evidence retained.' }
            Start-Sleep -Milliseconds 500
        }
        & "$PSScriptRoot/Run-LimboReference.ps1" @options -Role client
        $owned += [int](Get-Content "$folder/client/process.pid")
        $deadline = [DateTime]::UtcNow.AddSeconds(420)
        $complete = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 1
            $files = @('host','client') | ForEach-Object { "$folder/$_/stage2-observation.jsonl" }
            if (@($files | Where-Object { (Test-Path -LiteralPath $_) -and (Select-String -LiteralPath $_ -SimpleMatch 'completed-cleanup' -Quiet) }).Count -eq 2) { $complete=$true; break }
        }
        if (!$complete) { throw "Fixture timeout: $run. Inspect evidence before retrying." }
        Write-Output "Recorded $run. Timing, actual health, images and cleanup still require review."
    } finally {
        foreach ($processId in $owned) {
            $game = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($game -and $game.Path -eq $binary) { Stop-Process -Id $game.Id }
        }
    }
    $Port++
}
