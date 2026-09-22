[CmdletBinding()]
param(
    [ValidateSet('Prepare','Enable','Restore')][string]$Mode='Prepare',
    [string]$BackupFile
)
$ErrorActionPreference='Stop'
# Exact image name only: no system-wide WER settings or debugger registration.
$key='HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Monster Supergroup.exe'
$names=@('DumpFolder','DumpType','DumpCount')
$desired=@{DumpFolder='%LOCALAPPDATA%\CrashDumps';DumpType=2;DumpCount=5}
if($Mode -eq 'Prepare') {
    [pscustomobject]@{key=$key;values=$desired;requiresAdministrator=$true;attachesDebugger=$false} | ConvertTo-Json -Depth 4
    return
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Windows requires administrator rights for per-application WER settings. Run this script in an administrator PowerShell. No settings changed.'
}
if($Mode -eq 'Enable') {
    if(-not $BackupFile){$BackupFile=Join-Path $env:LOCALAPPDATA ('MonsterSupergroupDiagnostics/WER-before-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.clixml')}
    if(Test-Path -LiteralPath $BackupFile){throw 'Choose a new backup file; existing backups are never replaced.'}
    $before=@(); $existing=Get-Item -LiteralPath $key -ErrorAction SilentlyContinue
    foreach($name in $names) {
        $present=$existing -and $existing.GetValueNames() -contains $name
        $before += [pscustomobject]@{name=$name;present=[bool]$present;value=$(if($present){$existing.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null});kind=$(if($present){$existing.GetValueKind($name).ToString()}else{$null})}
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($BackupFile))) -Force | Out-Null
    [pscustomobject]@{key=$key;values=$before} | Export-Clixml -LiteralPath $BackupFile
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name DumpFolder -Value $desired.DumpFolder -PropertyType ExpandString -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name DumpType -Value 2 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $key -Name DumpCount -Value 5 -PropertyType DWord -Force | Out-Null
    Write-Output "Full WER dumps configured for Monster Supergroup.exe only. Backup: $BackupFile"
} else {
    if(-not $BackupFile){throw 'Restore requires the backup path printed by Enable.'}
    $before=Import-Clixml -LiteralPath $BackupFile
    if($before.key -ne $key -or @($before.values | Where-Object {$_.name -notin $names}).Count){throw 'Backup does not match the exact game WER key.'}
    foreach($value in $before.values) {
        if($value.present) {
            New-Item -Path $key -Force | Out-Null
            New-ItemProperty -LiteralPath $key -Name $value.name -Value $value.value -PropertyType $value.kind -Force | Out-Null
        } elseif(Test-Path -LiteralPath $key) {Remove-ItemProperty -LiteralPath $key -Name $value.name -ErrorAction SilentlyContinue}
    }
    Write-Output 'Original per-game WER values restored.'
}
