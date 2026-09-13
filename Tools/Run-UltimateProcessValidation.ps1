param(
    [string]$Executable = 'Builds/Phase02/UltimateValidation.exe',
    [switch]$Dedicated,
    [int]$Port = 7958,
    [string]$LogDirectory = 'Logs/Phase02'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.ultimate'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.ultimate' -Parameters $PSBoundParameters
