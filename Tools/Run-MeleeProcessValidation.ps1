param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [int]$Port = 7908,
    [string]$LogDirectory = 'Logs/Phase02'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.melee'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.melee' -Parameters $PSBoundParameters
