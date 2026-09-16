[CmdletBinding()]
param([ValidateSet('host','client')][string]$Role='host', [ValidateRange(1,2)][int]$WaitFor=1,
    [ValidateSet('light','detailed')][string]$LogDetail='detailed',
    [ValidateSet('idle','busy')][string]$Case='idle', [int]$Port=7993,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Session=(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$ErrorActionPreference='Stop'
$package=$PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$outputDirectory=Join-Path $package "TechnicalRuns/$Session/$Role"
if(Test-Path -LiteralPath $outputDirectory){throw 'Use a new technical session; preserve the old logs.'}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
@{version=$manifest.version;role=$Role;profile='full-validation';manual=$false;assisted=$true;logDetail=$LogDetail;
    helpers='Normal auto-walk; first legal card; heal below 300. Busy case queues one upgrade at transition. Not pressure evidence.';
    startedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'launch.json') -Encoding UTF8
$arguments=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    '-logFile',('"'+(Join-Path $outputDirectory 'player.log')+'"'), '--limbo-profile=full-validation',"--limbo-role=$Role",
    "--limbo-log-detail=$LogDetail","--limbo-full-case=$Case","--limbo-port=$Port","--limbo-wait-for=$WaitFor",'--limbo-autowalk=true','--limbo-windowed=true',
    "--limbo-version=$($manifest.version)","--limbo-session=$Session",('"--limbo-output='+$outputDirectory+'"'))
$process=Start-Process -FilePath (Join-Path $package 'MonsterSupergroupLimbo.exe') -WorkingDirectory $package -ArgumentList $arguments -WindowStyle Normal -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $outputDirectory 'process.pid')
Write-Output "TECHNICAL ONLY: $Session/$Role PID=$($process.Id) Logs=$outputDirectory"
