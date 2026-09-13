param(
    [string]$Executable = 'Builds/WispValidation/MonsterSupergroup.exe',
    [switch]$Dedicated,
    [int]$Port = 7928,
    [string]$LogDirectory = 'Logs/Wisp/Network'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.wisp'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.wisp' -Parameters $PSBoundParameters
