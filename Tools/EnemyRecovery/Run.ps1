param([Parameter(Mandatory=$true)][string]$Name,[ValidateSet('capture','observe')][string]$Mode='capture',[string]$Enemy='Imp',[int]$Variant=0)
$ErrorActionPreference='Stop'
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a simple unique lowercase run name.' }
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$work=Join-Path $root 'Logs/EnemyRecovery'
$output=Join-Path $work $Name
if (Test-Path -LiteralPath $output) { throw 'Run output already exists; choose a new name.' }
New-Item -ItemType Directory -Path $output | Out-Null
$exe=Join-Path $work 'runtime/Hell Maiden.exe'
$probe=Join-Path $work 'runtime/Hell Maiden_Data/Managed/RecoveryProbe.dll'
if (!(Test-Path -LiteralPath $probe) -or !(Test-Path -LiteralPath (Join-Path $work 'input-fingerprints.json'))) { throw 'Build the isolated probe and record input fingerprints first.' }
$manifest=@{mode=$Mode;enemy=$Enemy;variant=$Variant;startedAt=(Get-Date).ToUniversalTime().ToString('o');files=@{}}
foreach($file in @($probe,(Join-Path $work 'runtime/Hell Maiden_Data/Managed/Assembly-CSharp.dll'),(Join-Path $PSScriptRoot 'RecoveryProbe.cs'),(Join-Path $PSScriptRoot 'PatchRuntime.cs'))) {
    $manifest.files[$file]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'run-manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RecoveryProbe.cs') -Destination (Join-Path $output 'RecoveryProbe.source.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PatchRuntime.cs') -Destination (Join-Path $output 'PatchRuntime.source.cs')
$arguments=@('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+$output+'\Player.log"'),('"--recovery-output='+$output+'"'),('--recovery-mode='+$Mode),('--recovery-enemy='+$Enemy),('--recovery-variant='+$Variant))
$process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -ArgumentList $arguments -WindowStyle Normal -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $output 'pid.txt')
Write-Output "Recovery runtime PID $($process.Id), output $output"
