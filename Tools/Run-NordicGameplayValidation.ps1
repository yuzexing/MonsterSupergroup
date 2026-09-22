param(
    [string]$Executable = '',
    [string]$OutputDirectory = 'Logs/NordicGameplay/Acceptance',
    [switch]$FullSuite,
    [switch]$FullPerformance
)
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.nordic-gameplay' -Parameters $PSBoundParameters
