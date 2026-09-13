param(
    [string]$Executable = 'Builds/MenuDevelopment/MonsterSupergroup.exe',
    [ValidateSet('party','solo')][string]$Scenario = 'party',
    [int]$Width = 1280,
    [int]$Height = 720,
    [int]$Port = 7998,
    [switch]$Visible
)
$ErrorActionPreference = 'Stop'
$profile = if ($Scenario -eq 'party') { 'combat-menu' } else { 'combat-menu-solo' }
& (Join-Path $PSScriptRoot 'Run-PreparationMenuValidation.ps1') -Executable $Executable -Profile $profile -Width $Width -Height $Height -Port $Port -Visible:$Visible
