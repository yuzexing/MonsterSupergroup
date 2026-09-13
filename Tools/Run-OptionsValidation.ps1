param(
    [string]$Executable = 'Builds/OptionsValidation/MonsterSupergroup.exe',
    [string]$Artifacts = 'Logs/OptionsStandalone'
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.options'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.options' -Parameters $PSBoundParameters
