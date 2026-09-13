param(
    [string]$Executable = 'Builds/HealthHUDValidation/HealthHUDValidation.exe'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.health-hud'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.health-hud' -Parameters $PSBoundParameters
