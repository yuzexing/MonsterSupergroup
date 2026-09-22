param(
    [string]$Executable = '',
    [ValidateRange(1, 65535)]
    [int]$Port = 7801,
    [ValidateRange(20, 300)]
    [int]$ProcessTimeoutSeconds = 70
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.boot-process'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.boot-process' -Parameters $PSBoundParameters
