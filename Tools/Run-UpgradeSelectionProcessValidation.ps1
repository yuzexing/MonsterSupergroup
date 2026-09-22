param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [switch]$ForceD3D11,
    [switch]$IsolateTemporaryCache,
    [string]$PsoCacheSeed,
    [int]$Port = 7972
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.upgrade-selection'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.upgrade-selection' -Parameters $PSBoundParameters
