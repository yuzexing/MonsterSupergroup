param(
    [string[]]$Cases = @('Skeleton0','Skeleton2','Elite0','Elite1','Brotchi0','Brotchi1','Slime0','Slime1','Rusher2','Rusher1'),
    [string]$Prefix = 'stage2-matrix',
    [int]$Port = 8130
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$binary = Join-Path $project 'Builds/LimboReference/MonsterSupergroupLimbo.exe'
$processFiles = @()
function Stop-RecordedPlayers {
    foreach ($file in $processFiles) {
        $playerProcess = Get-Process -Id ([int](Get-Content -LiteralPath $file)) -ErrorAction SilentlyContinue
        if ($playerProcess -and $playerProcess.Path -eq $binary) { Stop-Process -Id $playerProcess.Id }
    }
}
try {
    foreach ($case in $Cases) {
        $solo = "$Prefix-$case-solo"; $pair = "$Prefix-$case-pair"
        & "$PSScriptRoot/Run-LimboReference.ps1" -Profile stage2-fixture -FixtureEnemy $case -Windowed -Role host -Port $Port -RunName $solo
        & "$PSScriptRoot/Run-LimboReference.ps1" -Profile stage2-fixture -FixtureEnemy $case -Windowed -Role host -FixtureTarget client -WaitFor 2 -Port ($Port+1) -RunName $pair
        $processFiles = @("$project/Logs/LimboReference/$solo/host/process.pid", "$project/Logs/LimboReference/$pair/host/process.pid")
        # Let the ordinary Host establish its listening socket before starting the Client.
        Start-Sleep -Seconds 8
        & "$PSScriptRoot/Run-LimboReference.ps1" -Profile stage2-fixture -FixtureEnemy $case -Windowed -Role client -FixtureTarget client -Port ($Port+1) -RunName $pair
        $processFiles += "$project/Logs/LimboReference/$pair/client/process.pid"
        $audits = @("$project/Logs/LimboReference/$solo/host/host-audit.jsonl", "$project/Logs/LimboReference/$pair/host/host-audit.jsonl", "$project/Logs/LimboReference/$pair/client/client-audit.jsonl")
        $deadline = [DateTime]::UtcNow.AddSeconds(170)
        do {
            Start-Sleep -Seconds 2
            $complete = 0
            foreach ($audit in $audits) {
                if (Test-Path -LiteralPath $audit) {
                    $frame = Get-Content -LiteralPath $audit -Tail 30 | Where-Object { $_ -match '^\{"kind":"frame"' } | Select-Object -Last 1 | ConvertFrom-Json
                    if ($frame -and $frame.snapshot.Phase -eq 5 -and $frame.snapshot.Elapsed -eq $frame.snapshot.StageEndTime) { $complete++ }
                }
            }
            if ([DateTime]::UtcNow -gt $deadline) { throw "Fixture did not complete: $case. Evidence preserved." }
        } while ($complete -lt 3)
        Write-Output "Recorded $case in solo Host and Host/Client; rendered artifacts require review."
        Stop-RecordedPlayers
        $processFiles = @()
        Start-Sleep -Seconds 2
    }
} finally { Stop-RecordedPlayers }
