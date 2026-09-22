param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [int]$Port = 7896,
    [string]$LogDirectory = 'Logs/Phase01'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.runtime-boundary'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.runtime-boundary' -Parameters $PSBoundParameters
