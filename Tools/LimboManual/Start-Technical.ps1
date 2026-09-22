[CmdletBinding()]
param([ValidateSet('host','client')][string]$Role='host', [ValidateRange(1,2)][int]$WaitFor=1,
    [ValidateSet('full-validation','ghoul-motion','spatial-b','spatial-barrier','audio-wisp','audio-beam','pickup-observe','pickup-drops','movement-observe','effects-observe')][string]$Profile='full-validation',
    [ValidateSet('light','detailed')][string]$LogDetail='detailed',
    [ValidateSet('observe','edge')][string]$AudioCase='observe',
    [ValidateSet('idle','busy')][string]$Case='idle', [int]$Port=7993,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Session=(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$ErrorActionPreference='Stop'
$package=$PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$outputDirectory=Join-Path $package "TechnicalRuns/$Session/$Role"
if(Test-Path -LiteralPath $outputDirectory){throw 'Use a new technical session; preserve the old logs.'}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$helpers=if($Profile.StartsWith('pickup-')){'Explicit drop, health and pause controls; drop mode spawns normal enemies for weapon kills; no pressure evidence or automatic screenshots.'}elseif($Profile.StartsWith('audio-')){'Local sound presentation fixture; normal weapons disabled; explicit manual element/count/contact/cancel buttons; no damage or pressure evidence; no automatic screenshots.'}elseif($Profile.StartsWith('spatial-')){'Isolated source trap fixture; manual movement; no auto-walk, auto-heal, forced pause or cancellation. Light logs disable automatic screenshots. Not pressure evidence.'}elseif($Profile -eq 'ghoul-motion'){'Manual movement; weapons suppressed; heal below 300; first legal card if offered; no screenshots. 65-second observation, not pressure evidence.'}else{'Normal auto-walk; first legal card; heal below 300. Busy case queues one upgrade at transition. Not pressure evidence.'}
if($Profile -eq 'effects-observe'){$helpers='Presentation-only: explicit weapon, element, direction, scale and phase controls; normal weapon execution suppressed. No gameplay damage or screenshots.'}
if($AudioCase -eq 'edge'){$helpers+=' Explicit center/edge/corner teleport and test camera-framing controls; low-frequency spatial/DSP diagnostics.'}
if($Profile -eq 'movement-observe'){$helpers='Explicit enemy spawn; fixed birth speed multiplier 1; weapons suppressed; heal below 300; manual movement and 60/144 FPS comparison; bounded render log, no screenshots. Not pressure evidence.'}
@{version=$manifest.version;role=$Role;profile=$Profile;manual=$false;assisted=$true;logDetail=$LogDetail;audioCase=$AudioCase;
    helpers=$helpers;
    startedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'launch.json') -Encoding UTF8
$arguments=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    '-logFile',('"'+(Join-Path $outputDirectory 'player.log')+'"'), "--limbo-profile=$Profile","--limbo-role=$Role",
    "--limbo-log-detail=$LogDetail","--limbo-audio-case=$AudioCase","--limbo-full-case=$Case",'--limbo-spatial-case=observe',"--limbo-port=$Port","--limbo-wait-for=$WaitFor",("--limbo-autowalk="+($Profile -eq 'full-validation').ToString().ToLowerInvariant()),'--limbo-windowed=true',
    "--limbo-version=$($manifest.version)","--limbo-session=$Session",('"--limbo-output='+$outputDirectory+'"'))
$process=Start-Process -FilePath (Join-Path $package 'MonsterSupergroupLimbo.exe') -WorkingDirectory $package -ArgumentList $arguments -WindowStyle Normal -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $outputDirectory 'process.pid')
Write-Output "TECHNICAL ONLY: $Session/$Role PID=$($process.Id) Logs=$outputDirectory"
