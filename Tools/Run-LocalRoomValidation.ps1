param(
    [string]$Executable = '',
    [ValidateSet('party','solo','errors','admission','release')][string]$Scenario = 'party',
    [int]$Width = 1280, [int]$Height = 720, [int]$Port = 7777,
    [switch]$Visible
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.local-room'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.local-room' -Parameters $PSBoundParameters
