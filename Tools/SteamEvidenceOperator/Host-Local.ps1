[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$OutputRoot,
    [ValidateSet('Standard','Diagnostic')][string]$EvidenceProfile = 'Diagnostic',
    [switch]$ObserveEvidenceQueue = $true
)
$ErrorActionPreference = 'Stop'
$delivery = Split-Path -Parent $PSScriptRoot
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $delivery 'product' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $delivery 'logs' }
& (Join-Path $PSScriptRoot 'Start-Case.ps1') -PackageDirectory $PackageDirectory -OutputRoot $OutputRoot -Role Host -Mode local -Stage chain -EvidenceProfile $EvidenceProfile -ObserveEvidenceQueue:$ObserveEvidenceQueue
