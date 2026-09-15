[CmdletBinding()]
param(
    [ValidateSet('pair','dedicated')][string]$Mode = 'pair',
    [Parameter(Mandatory=$true)][string]$RunName,
    [int]$Port = 8071,
    [string]$BuildDirectory = 'Builds/RegressionClosure20260916'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must be a simple folder name.' }
$runDir = Join-Path $projectRoot ('Logs/RegressionClosure20260916/' + $RunName)
if (Test-Path -LiteralPath $runDir) { throw "Run already exists: $runDir" }
$exePath = Join-Path $projectRoot ($BuildDirectory + '/RegressionClosure.exe')
if (!(Test-Path -LiteralPath $exePath)) { throw "Build missing: $exePath" }
New-Item -ItemType Directory -Path $runDir | Out-Null
$serverRole = if ($Mode -eq 'dedicated') { 'server' } else { 'host' }
$common = @('-force-d3d11', "--regression-port=$Port", "--regression-artifacts=$runDir")
$serverArgs = $common + @("--regression-role=$serverRole", '-logFile', "$runDir/$serverRole.log")
if ($Mode -eq 'dedicated') {
    $serverProcess = Start-Process -FilePath $exePath -ArgumentList ($serverArgs + @('-batchmode','-nographics','--dedicated-server')) -WindowStyle Hidden -PassThru
} else {
    $serverProcess = Start-Process -FilePath $exePath -ArgumentList ($serverArgs + @('-screen-fullscreen','0','-screen-width','960','-screen-height','640')) -PassThru
}
$serverProcess.Id | Set-Content -LiteralPath (Join-Path $runDir ($serverRole + '.pid'))
$waitUntil = (Get-Date).AddSeconds(20)
while (!(Test-Path -LiteralPath (Join-Path $runDir 'listening')) -and (Get-Date) -lt $waitUntil) { Start-Sleep -Milliseconds 200 }
if (!(Test-Path -LiteralPath (Join-Path $runDir 'listening'))) { throw "Server did not listen; inspect $runDir" }
$clientProcess = Start-Process -FilePath $exePath -ArgumentList ($common + @('--regression-role=client','-logFile',"$runDir/client.log",'-screen-fullscreen','0','-screen-width','960','-screen-height','640')) -PassThru
$clientProcess.Id | Set-Content -LiteralPath (Join-Path $runDir 'client.pid')
$managed = Join-Path (Split-Path -Parent $exePath) 'RegressionClosure_Data/Managed'
Get-FileHash -Algorithm SHA256 $exePath,(Join-Path $managed 'MonsterSupergroup.Gameplay.Tests.PlayMode.dll'),(Join-Path $managed 'MonsterSupergroup.NetworkCombat.dll') |
    ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath (Join-Path $runDir 'build-hashes.json')
$serverProcess,$clientProcess | Select-Object Id,ProcessName
Write-Output $runDir
