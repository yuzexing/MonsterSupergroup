[CmdletBinding()]
param([string]$Session)
$ErrorActionPreference = 'Stop'
$package = $PSScriptRoot
$runs = Join-Path $package 'Runs'
if (-not $Session) { $Session = (Get-ChildItem -LiteralPath $runs -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name }
if (-not $Session -or $Session -notmatch '^[a-zA-Z0-9_-]+$') { throw 'No run found, or invalid session name.' }
$run = Join-Path $runs $Session
if (-not (Test-Path -LiteralPath $run -PathType Container)) { throw "Run not found: $Session" }
$archives = Join-Path $package 'Archives'
New-Item -ItemType Directory -Path $archives -Force | Out-Null
$rows = foreach ($role in Get-ChildItem -LiteralPath $run -Directory) {
    $audit = Join-Path $role.FullName ($role.Name + '-audit.jsonl')
    $closed = $false; $rounds = @(); $parseErrors = 0
    if (Test-Path -LiteralPath $audit) {
        foreach ($line in [IO.File]::ReadLines($audit)) {
            try { $record = $line | ConvertFrom-Json -ErrorAction Stop } catch { $parseErrors++; continue }
            if ($record.kind -eq 'process-closed') { $closed = $true }
            if ($record.kind -eq 'run-ended') { $rounds += @{run=$record.run;round=$record.round;result=$record.detail} }
        }
    }
    $writerIssues = @()
    if (Test-Path -LiteralPath (Join-Path $role.FullName 'performance-detail.jsonl')) {
        foreach ($file in Get-ChildItem -LiteralPath $role.FullName -File -Filter '*.jsonl') {
            $statusPath = $file.FullName + '.status.json'
            if (!(Test-Path -LiteralPath $statusPath)) { $writerIssues += "Missing writer status: $($file.Name)"; continue }
            try { $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json } catch { $writerIssues += "Unreadable writer status: $($file.Name)"; continue }
            if (!$status.complete) { $writerIssues += "$($file.Name): incomplete; $($status.failure)" }
        }
    }
    [pscustomobject]@{role=$role.Name; orderlyClose=$closed; parseErrors=$parseErrors; writerIssues=$writerIssues; rounds=$rounds; integrity=if($closed -and $parseErrors -eq 0 -and $writerIssues.Count -eq 0){'closed'}else{'incomplete-or-still-running'} }
}
$index = [ordered]@{ session=$Session; archivedUtc=[DateTime]::UtcNow.ToString('o'); roles=@($rows);
    note='A missing close record, parse error, or incomplete writer status means incomplete evidence. Interrupted/failed runs are never passes. Buffered records may be lost after a crash.' }
$index | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'archive-status.json') -Encoding UTF8
$zip = Join-Path $archives ($Session + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
Compress-Archive -LiteralPath @($run,(Join-Path $package 'build-manifest.json')) -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Output "Archive: $zip"
$rows | Format-Table role,orderlyClose,parseErrors,integrity
