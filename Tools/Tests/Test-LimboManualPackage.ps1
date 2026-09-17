$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = [IO.Path]::GetFullPath((Join-Path $project ('Logs/LimboManualPackageTests/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '/Package With Spaces')))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -Path (Join-Path $project 'Tools/LimboManual/*') -Destination $fixture
[IO.File]::WriteAllBytes((Join-Path $fixture 'MonsterSupergroupLimbo.exe'),[byte[]]@())
'{"version":"test-version"}' | Set-Content -LiteralPath (Join-Path $fixture 'build-manifest.json')
# Mock only process creation. The real portable script must resolve paths and create its evidence.
function Start-Process { param($FilePath,$WorkingDirectory,$ArgumentList,$WindowStyle,[switch]$PassThru)
    $global:limboManualTestLaunch = @{exe=$FilePath;cwd=$WorkingDirectory;arguments=$ArgumentList}; [pscustomobject]@{Id=999999} }
$rejected=$false
try { & (Join-Path $fixture 'Start-Limbo.ps1') -Mode Solo -AutoWalk } catch { $rejected=$true }
if(-not $rejected){throw 'Manual wrapper accepted an auxiliary parameter.'}
& (Join-Path $fixture 'Start-Limbo.ps1') -Mode Solo
$captured = $global:limboManualTestLaunch
Remove-Variable -Name limboManualTestLaunch -Scope Global
if($captured.cwd -ne $fixture -or $captured.exe -ne (Join-Path $fixture 'MonsterSupergroupLimbo.exe')){throw 'Launch depends on the project path.'}
foreach($expected in @('--limbo-manual=true','--limbo-log-detail=light','--limbo-profile=full','--limbo-wait-for=1','-force-d3d11')) {
    if($captured.arguments -notcontains $expected){throw "Missing $expected"}
}
if(-not ($captured.arguments | Where-Object {$_ -like '"--limbo-output=*Package With Spaces*"'})){throw 'Output path with spaces is not quoted.'}
$run=Get-ChildItem -LiteralPath (Join-Path $fixture 'Runs') -Directory | Select-Object -First 1
$audit=Join-Path $run.FullName 'host/host-audit.jsonl'
@('{"kind":"run-ended","run":"r1","round":1,"detail":"failed"}', '{"kind":"process-closed"}') | Set-Content -LiteralPath $audit
& (Join-Path $fixture 'Archive-Logs.ps1') -Session $run.Name
$status=Get-Content -LiteralPath (Join-Path $run.FullName 'archive-status.json') -Raw | ConvertFrom-Json
if($status.roles[0].integrity -ne 'closed' -or $status.roles[0].rounds[0].result -ne 'failed'){throw 'A failed round was mislabeled.'}
'{"kind":"run-ended","run":"r1","detail":"completed"}' | Set-Content -LiteralPath $audit
& (Join-Path $fixture 'Archive-Logs.ps1') -Session $run.Name
$status=Get-Content -LiteralPath (Join-Path $run.FullName 'archive-status.json') -Raw | ConvertFrom-Json
if($status.roles[0].integrity -ne 'incomplete-or-still-running'){throw 'Missing normal close was accepted.'}
foreach ($profile in @('spatial-b','spatial-barrier','audio-wisp','audio-beam')) {
    & (Join-Path $fixture 'Start-Technical.ps1') -Profile $profile -LogDetail light -Session $profile
    $captured = $global:limboManualTestLaunch
    foreach ($expected in @("--limbo-profile=$profile",'--limbo-log-detail=light','--limbo-spatial-case=observe','--limbo-autowalk=false')) {
        if ($captured.arguments -notcontains $expected) { throw "Fire observation is missing $expected" }
    }
    if ($captured.exe -ne (Join-Path $fixture 'MonsterSupergroupLimbo.exe')) { throw 'Fire observation escaped the package.' }
}
Remove-Variable -Name limboManualTestLaunch -Scope Global
Write-Output "PASS: manual isolation, portable quoted paths, fire observation profiles, failed/incomplete archive classifications. Evidence: $fixture"
