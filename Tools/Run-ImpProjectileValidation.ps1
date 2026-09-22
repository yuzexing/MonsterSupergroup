param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [switch]$Impaired,
    [switch]$Graphics,
    [int]$Port = 7987
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.imp'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.imp' -Parameters $PSBoundParameters
