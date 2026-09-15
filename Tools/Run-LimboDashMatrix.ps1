param(
 [string]$BuildDirectory='Builds/LimboDash20260915',
 [string]$ClientDirectory='Builds/LimboDashClient20260915',
 [string]$Prefix='dash-matrix-20260915',
 [string[]]$Cases=@('0-host','0-client','1-host','1-client'),
 [ValidateSet("main","boundary","reuse")][string]$DashCase="main",
 [int]$Port=8240
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
foreach($case in $Cases){
 $parts=$case.Split('-');$variant=[int]$parts[0];$target=$parts[1]
 if($variant -notin @(0,1) -or $target -notin @('host','client')){throw 'Cases must be 0-host, 0-client, 1-host, 1-client'}
 $name="$Prefix-$case";$folder=Join-Path $root "Logs/LimboReference/$name";$owned=@()
 try{
  & "$PSScriptRoot/Run-LimboReference.ps1" -Role host -Profile dash-fixture -DashVariant $variant -DashCase $DashCase -FixtureTarget $target -WaitFor 2 -Port $Port -Windowed -BuildDirectory $BuildDirectory -RunName $name
  $owned += [int](Get-Content -LiteralPath "$folder/host/process.pid")
  $deadline=[DateTime]::UtcNow.AddSeconds(90)
  while(!(Test-Path -LiteralPath "$folder/host/player.log") -or !(Select-String -LiteralPath "$folder/host/player.log" -SimpleMatch '[Preparation] Open' -Quiet)){
   if([DateTime]::UtcNow -gt $deadline){throw 'Host preparation timeout'}
   Start-Sleep -Milliseconds 500
  }
  & "$PSScriptRoot/Run-LimboReference.ps1" -Role client -Profile dash-fixture -DashVariant $variant -DashCase $DashCase -FixtureTarget $target -Port $Port -Windowed -BuildDirectory $ClientDirectory -RunName $name
  $owned += [int](Get-Content -LiteralPath "$folder/client/process.pid")
  $deadline=[DateTime]::UtcNow.AddSeconds(250);$complete=$false
  while([DateTime]::UtcNow -lt $deadline){
   Start-Sleep -Seconds 1
   $files=@('host','client') | ForEach-Object {"$folder/$_/stage2-observation.jsonl"}
   if(@($files | Where-Object {(Test-Path -LiteralPath $_) -and (Select-String -LiteralPath $_ -SimpleMatch 'completed-cleanup' -Quiet)}).Count -eq 2){$complete=$true;break}
  }
  if(!$complete){throw "Dash fixture timeout: $case. Preserve records and inspect before retrying."}
  Start-Sleep -Seconds 2
  Write-Output "Recorded $name; analysis and rendered review required."
 }finally{
  foreach($processId in $owned){$game=Get-Process -Id $processId -ErrorAction SilentlyContinue;if($game -and $game.ProcessName -eq 'MonsterSupergroupLimbo'){Stop-Process -Id $game.Id}}
 }
 $Port++
}