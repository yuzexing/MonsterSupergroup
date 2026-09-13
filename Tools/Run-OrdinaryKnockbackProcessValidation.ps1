param(
    [string]$Executable = 'Builds/M3Knockback/M3Knockback.exe',
    [string]$EnemyPrefab,
    [switch]$Dedicated,
    [switch]$ImpairedNetwork,
    [switch]$CaptureFrames,
    [switch]$ValidateHitFlash,
    [switch]$ValidateDamageNumbers,
    [switch]$VisibleWindows,
    [switch]$ForceD3D11,
    [switch]$ForceGfxDirect,
    [switch]$IsolateTemporaryCache,
    [string]$PsoCacheSeed,
    [int]$Port = 7962
)
Write-Warning '旧脚本兼容一版；新入口: Invoke-ProjectTool.ps1 -ToolId test.knockback'
& (Join-Path $PSScriptRoot 'Invoke-ProjectTool.ps1') -ToolId 'test.knockback' -Parameters $PSBoundParameters
