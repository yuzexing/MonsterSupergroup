param(
    [string]$Executable = '',
    [ValidateSet('normal', 'impaired')][string]$Profile = 'normal',
    [ValidateRange(2, 600)][int]$Duration = 120,
    [ValidateSet('NetworkEnemySkeleton', 'NetworkEnemySkeletonExample', 'NetworkEnemyLustSinner', 'NetworkEnemyImp')][string]$SkeletonPrefab = 'NetworkEnemySkeleton',
    [int]$Port = 7993
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.enemy-handoff'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.enemy-handoff' -Parameters $PSBoundParameters
