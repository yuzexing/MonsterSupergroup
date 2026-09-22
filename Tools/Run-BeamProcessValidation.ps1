param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [int]$Port = 7928,
    [string]$LogDirectory = 'Logs/Phase02'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.beam'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.beam' -Parameters $PSBoundParameters
