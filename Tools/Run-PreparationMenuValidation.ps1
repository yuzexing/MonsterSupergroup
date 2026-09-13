param(
    [string]$Executable = 'Builds/MenuDevelopment/MonsterSupergroup.exe',
    [ValidateSet('party','offline','invites','combat-menu','combat-menu-solo','run-end','run-end-solo','local-party','local-solo','local-errors','local-admission','local-release')][string]$Profile = 'party',
    [int]$Width = 1280, [int]$Height = 720, [int]$Port = 7998,
    [switch]$Headless,
    [switch]$MixedLanguages,
    [string]$Language = '',
    [switch]$Visible
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.preparation-menu'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.preparation-menu' -Parameters $PSBoundParameters
