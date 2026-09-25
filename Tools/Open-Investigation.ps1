[CmdletBinding()]
param(
    [string[]]$Source,
    [string]$SourceDatabase,
    [string]$Database,
    [string]$Output,
    [int]$Port = 0,
    [switch]$NoBrowser,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
if (@([bool]$Source, [bool]$SourceDatabase, [bool]$Database).Where({ $_ }).Count -gt 1) {
    throw 'Choose Source, SourceDatabase, or Database, not more than one.'
}
if (-not $Source -and -not $SourceDatabase -and -not $Database) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    $picker.Description = '选择原始证据导出目录或调查问题包；也可选择包含双端导出的上级目录。'
    $picker.ShowNewFolderButton = $false
    try {
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        $Source = @($picker.SelectedPath)
    } finally { $picker.Dispose() }
}
$runtime = Get-Command python -ErrorAction Stop
$arguments = @('-B', '-X', 'utf8', (Join-Path $PSScriptRoot 'CombatInvestigation.py'), '--port', [string]$Port)
if ($Source) { $arguments += '--roots'; $arguments += $Source }
elseif ($SourceDatabase) { $arguments += @('--source-db', $SourceDatabase) }
else { $arguments += @('--db', $Database) }
if ($Output) { $arguments += @('--output', $Output) }
if (-not $NoBrowser) { $arguments += '--open' }
if ($PrepareOnly) { $arguments += '--prepare-only' }
& $runtime.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Investigation exited with code $LASTEXITCODE" }
