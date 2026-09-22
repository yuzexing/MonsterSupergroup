param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [int]$Port = 7956,
    [string]$LogDirectory = 'Logs/Phase02'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.summon'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.summon' -Parameters $PSBoundParameters
