param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [switch]$Impaired,
    [int]$Port = 7990
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.timeline-waves'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.timeline-waves' -Parameters $PSBoundParameters
