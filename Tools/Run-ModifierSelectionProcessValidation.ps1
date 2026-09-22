param(
    [string]$Executable = '',
    [switch]$Keyboard
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.modifier-selection'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.modifier-selection' -Parameters $PSBoundParameters
