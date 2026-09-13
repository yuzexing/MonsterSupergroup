param(
    [string]$Executable = 'Builds/Phase02/CirclingValidation.exe',
    [switch]$Dedicated,
    [int]$Port = 7948,
    [string]$LogDirectory = 'Logs/Phase02'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.circling'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.circling' -Parameters $PSBoundParameters
