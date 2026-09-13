param(
    [string]$Executable = 'Builds/MenuDevelopment/MonsterSupergroup.exe',
    [ValidateSet('party','solo')][string]$Scenario = 'party',
    [int]$Width = 1280, [int]$Height = 720, [int]$Port = 7998,
    [switch]$Visible
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

$profile = if ($Scenario -eq 'party') { 'run-end' } else { 'run-end-solo' }
& (Join-Path $PSScriptRoot 'Run-PreparationMenuValidation.ps1') -Executable $Executable -Profile $profile -Width $Width -Height $Height -Port $Port -Visible:$Visible
