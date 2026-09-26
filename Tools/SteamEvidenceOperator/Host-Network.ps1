[CmdletBinding()]
param([string]$PackageDirectory,[string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$delivery = Split-Path -Parent $PSScriptRoot
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $delivery 'product' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $delivery 'logs' }
& (Join-Path $PSScriptRoot 'Start-Case.ps1') -PackageDirectory $PackageDirectory -OutputRoot $OutputRoot -Role Host -Mode off -Stage network -EvidenceProfile Standard
