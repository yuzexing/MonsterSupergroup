param(
    [string]$Executable = '',
    [ValidateRange(1, 65535)]
    [int]$Port = 7798,
    [ValidateRange(10, 300)]
    [int]$ProcessTimeoutSeconds = 45
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.enemy-simulation'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.enemy-simulation' -Parameters $PSBoundParameters
