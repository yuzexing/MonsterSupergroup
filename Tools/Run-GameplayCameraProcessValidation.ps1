param(
    [string]$Executable = 'Builds/GameplayCameraValidation/GameplayCameraValidation.exe',
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [switch]$ForceD3D11
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.camera'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.camera' -Parameters $PSBoundParameters
