$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$root = Join-Path $project ('Logs/BuildArchiveTests/' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'Build With Spaces'
$embedded = Join-Path $source 'Game_Data/StreamingAssets'
New-Item -ItemType Directory -Path $embedded -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $source 'Game.exe'), [byte[]]@(1,2,3))
$info = '{"gameVersion":"0.0.0","kind":"test","buildId":"20260922T123456789Z-abcdef01"}'
[IO.File]::WriteAllText((Join-Path $source 'build-complete.json'), $info)
[IO.File]::WriteAllText((Join-Path $embedded 'BuildInfo.json'), $info)
$export = Join-Path $project 'Tools/Export-ProjectBuild.ps1'
function MustReject([scriptblock]$action) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected archive rejection.' }
}
MustReject { & $export -BuildDirectory $source -DestinationRoot (Join-Path $source 'Nested') }
$result = & $export -BuildDirectory $source -DestinationRoot (Join-Path $root 'Deliveries')
if ($result.Version -ne '0.0.0' -or $result.BuildId -ne '20260922T123456789Z-abcdef01') { throw 'Snapshot identity changed.' }
if ((Get-FileHash -LiteralPath $result.Archive).Hash -ne $result.Sha256) { throw 'Archive hash mismatch.' }
$manifest = Get-Content -LiteralPath (Join-Path $result.Package 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.files.Count -ne 3 -or $manifest.buildInfo.buildId -ne $result.BuildId) { throw 'Incomplete package manifest.' }
MustReject { & $export -BuildDirectory $source -DestinationRoot (Join-Path $root 'Deliveries') }
[IO.File]::WriteAllText((Join-Path $embedded 'BuildInfo.json'), $info.Replace('0.0.0', '0.0.1'))
MustReject { & $export -BuildDirectory $source -DestinationRoot (Join-Path $root 'Tampered') }
MustReject { & $export -BuildDirectory $root -DestinationRoot (Join-Path $root 'Missing') }
Write-Output "PASS: immutable archive, hashes, matching snapshot, missing metadata, nested destination and paths with spaces. Evidence: $root"
