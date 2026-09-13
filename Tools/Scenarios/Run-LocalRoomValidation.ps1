param(
    [string]$Executable = 'Builds/MenuDevelopment/MonsterSupergroup.exe',
    [ValidateSet('party','solo','errors','admission','release')][string]$Scenario = 'party',
    [int]$Width = 1280, [int]$Height = 720, [int]$Port = 7777,
    [switch]$Visible, [switch]$Headless
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

& (Join-Path $PSScriptRoot 'Run-PreparationMenuValidation.ps1') -Executable $Executable -Profile ('local-' + $Scenario) -Width $Width -Height $Height -Port $Port -Visible:$Visible -Headless:$Headless
