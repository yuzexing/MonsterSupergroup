@echo off
setlocal
set "STEAM_STARTUP_DIAG_SELF=%~f0"
echo Collecting startup diagnosis. No game control or log changes.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$text=[IO.File]::ReadAllText($env:STEAM_STARTUP_DIAG_SELF); & ([scriptblock]::Create($text.Substring($text.LastIndexOf('# BEGIN_POWERSHELL'))))"
set "diag_exit=%errorlevel%"
if /i "%~1"=="--no-pause" exit /b %diag_exit%
echo.
echo Finished. The report path is printed above. Press a key to close this window.
pause >nul
exit /b %diag_exit%
# BEGIN_POWERSHELL
$ErrorActionPreference='Stop'
$kit=Split-Path -Parent $env:STEAM_STARTUP_DIAG_SELF
$output=Join-Path $kit ('network-startup-diagnostics-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)+'.json')
$report=[ordered]@{collectorRevision='standalone-1';utc=[DateTime]::UtcNow.ToString('o');kit=$kit;powershellVersion=$PSVersionTable.PSVersion.ToString();error=$null;files=@()}
function Read-Fragment([string]$Path,[int]$Limit=262144,[switch]$Tail) {
    if (-not $Path -or -not [IO.File]::Exists($Path)) { return @{path=$Path;exists=$false} }
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $length=$stream.Length
        $count=[int][Math]::Min($length,$Limit)
        if($Tail){[void]$stream.Seek([Math]::Max(0,$length-$count),[IO.SeekOrigin]::Begin)}
        $bytes=New-Object byte[] $count
        $read=0
        while($read -lt $count){$n=$stream.Read($bytes,$read,$count-$read);if($n -eq 0){break};$read+=$n}
        return @{path=$Path;exists=$true;bytes=$length;readBytes=$read;truncated=($length -gt $read);text=[Text.Encoding]::UTF8.GetString($bytes,0,$read).TrimStart([char]0xfeff)}
    } finally {$stream.Dispose()}
}
try {
    Write-Host '[1/4] Reading case and launch metadata'
    $current=Read-Fragment (Join-Path (Split-Path -Parent $kit) 'current-case.json')
    if(-not $current.exists){throw 'Place this CMD file inside the operator-kit used to start the game. Parent current-case.json was not found.'}
    $case=$current.text | ConvertFrom-Json
    $report.currentCase=$case
    $captureText=Read-Fragment (Join-Path $case.caseDirectory 'capture/capture.json')
    if(-not $captureText.exists){throw 'The selected case has no capture/capture.json.'}
    $capture=$captureText.text | ConvertFrom-Json
    $report.capture=$capture
    Write-Host '[2/4] Reading bounded startup headers'
    $report.directory=$capture.requestedNetworkOutput
    $report.directoryExists=[IO.Directory]::Exists([string]$capture.requestedNetworkOutput)
    if($report.directoryExists){
        $files=@(Get-ChildItem -LiteralPath $capture.requestedNetworkOutput -Filter '*.jsonl' -File)
        $report.jsonlCount=$files.Count
        $report.fileListingTruncated=($files.Count -gt 32)
        foreach($file in ($files | Select-Object -First 32)){
            $entry=[ordered]@{path=$file.FullName;bytes=$file.Length;header=$null;readError=$null;status=$null}
            try {
                $fragment=Read-Fragment $file.FullName
                if($fragment.text){$first=($fragment.text -split "`n",2)[0];$entry.header=$first | ConvertFrom-Json}
                $entry.status=Read-Fragment ($file.FullName+'.status.json')
            } catch {$entry.readError=$_.Exception.Message}
            $report.files+= $entry
        }
    }
    Write-Host '[3/4] Reading recent Player.log and installed verifier revision'
    $report.playerLogTail=Read-Fragment $capture.requestedPlayerLog -Limit 65536 -Tail
    $report.verifier=Read-Fragment (Join-Path $kit 'Verify-Running.ps1') -Limit 65536
    $modulePath=Join-Path $kit 'Tools/SteamEvidenceIdentity.psm1'
    $report.identityModule=Read-Fragment $modulePath -Limit 65536
} catch {$report.error=$_.Exception.Message;Write-Host ('Collection error: '+$report.error)}
Write-Host '[4/4] Saving diagnosis'
try {
    [IO.File]::WriteAllText($output,($report | ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
    Write-Host ('REPORT: '+$output)
} catch {Write-Host ('Could not save report: '+$_.Exception.Message);exit 1}
if($report.error){exit 1}
