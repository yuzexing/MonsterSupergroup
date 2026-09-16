[CmdletBinding()]
param([ValidateSet('Solo','Host','Client')][string]$Mode = 'Solo')
$ErrorActionPreference = 'Stop'
$package = $PSScriptRoot
$executable = Join-Path $package 'MonsterSupergroupLimbo.exe'
$manifestPath = Join-Path $package 'build-manifest.json'
if (-not (Test-Path -LiteralPath $executable) -or -not (Test-Path -LiteralPath $manifestPath)) { throw 'Extract the entire Limbo package before launching.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$runs = Join-Path $package 'Runs'
try { New-Item -ItemType Directory -Path $runs -Force | Out-Null } catch { throw "Package is not writable. Extract to a writable folder: $runs" }
$pointer = Join-Path $runs 'local-host.json'
if ($Mode -eq 'Client') {
    if (-not (Test-Path -LiteralPath $pointer)) { throw 'Start the two-player Host first, then Join Local Host.' }
    $hostSession = Get-Content -LiteralPath $pointer -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($hostSession.session -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid local Host session.' }
    $hostProcess = Get-Process -Id $hostSession.pid -ErrorAction SilentlyContinue
    if (-not $hostProcess -or $hostProcess.Path -ne $executable) { throw 'The package Host is no longer running. Start a new two-player Host.' }
    $session = $hostSession.session
} else { $session = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $Mode.ToLowerInvariant() }
$role = if ($Mode -eq 'Client') { 'client' } else { 'host' }
$participants = if ($Mode -eq 'Solo') { 1 } else { 2 }
$outputDirectory = Join-Path $runs "$session/$role"
if (Test-Path -LiteralPath $outputDirectory) { throw "This role already has logs. Start a new session; old evidence is preserved: $outputDirectory" }
try {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $launch = [ordered]@{ version=$manifest.version; session=$session; mode=$Mode; role=$role; profile='full'; logDetail='light'; manual=$true;
        assisted=$false; endpoint='127.0.0.1:7993'; expectedParticipants=$participants; startedUtc=[DateTime]::UtcNow.ToString('o');
        graphics='D3D11'; width=1280; height=720; targetFps=60; manifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash }
    $launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'launch.json') -Encoding UTF8
} catch { throw "Cannot write run logs in $outputDirectory. Extract the package to a writable folder. $($_.Exception.Message)" }
$launchArgs = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    '-logFile',('"' + (Join-Path $outputDirectory 'player.log') + '"'),
    '--limbo-manual=true','--limbo-profile=full','--limbo-log-detail=light','--limbo-windowed=true',
    "--limbo-role=$role",'--limbo-port=7993',"--limbo-wait-for=$participants", "--limbo-version=$($manifest.version)","--limbo-session=$session",
    ('"--limbo-output=' + $outputDirectory + '"'))
# The three manual entries intentionally have no extra-argument/auxiliary escape hatch.
$game = Start-Process -FilePath $executable -WorkingDirectory $package -ArgumentList $launchArgs -WindowStyle Normal -PassThru
$game.Id | Set-Content -LiteralPath (Join-Path $outputDirectory 'process.pid')
if ($Mode -eq 'Host') { @{session=$session;pid=$game.Id} | ConvertTo-Json | Set-Content -LiteralPath $pointer -Encoding UTF8 }
Write-Output "Limbo $($manifest.version), $session / $role. Logs: $outputDirectory"
