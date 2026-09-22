param(
    [string]$Executable = '',
    [int]$Width = 1280,
    [int]$Height = 720,
    [int]$Port = 7988,
    [switch]$Headless
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.player-debug'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.player-debug' -Parameters $PSBoundParameters
