param(
    [string]$Executable = '',
    [string]$OutputDirectory = 'Logs/NordicStaticSample/Acceptance'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.nordic'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.nordic' -Parameters $PSBoundParameters
