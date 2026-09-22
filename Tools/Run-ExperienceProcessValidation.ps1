param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [switch]$Simulation,
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [int]$Port = 7988
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.experience'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.experience' -Parameters $PSBoundParameters
