[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$SteamAppId
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Delivery must use a new directory; existing packages are never overwritten.' }
New-Item -ItemType Directory -Path $output | Out-Null
$product = Join-Path $output 'product'
Copy-Item -LiteralPath $package -Destination $product -Recurse
# This local Steam test copy launches the inspected binary rather than the installed store copy.
[IO.File]::WriteAllText((Join-Path $product 'steam_appid.txt'), $SteamAppId + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$kit = Join-Path $output 'operator-kit'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SteamEvidenceOperator') -Destination $kit -Recurse
$kitTools = Join-Path $kit 'Tools'
New-Item -ItemType Directory -Path (Join-Path $kitTools 'Scenarios'),(Join-Path $kit 'launch-configs'),(Join-Path $output 'logs') -Force | Out-Null
foreach ($name in @('ProjectTools.psm1','SteamEvidenceIdentity.psm1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $kitTools $name)
}
foreach ($name in @('Start-SteamDiagnostics.ps1','Export-SteamEvidence.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('Scenarios/' + $name)) -Destination (Join-Path $kitTools ('Scenarios/' + $name))
}
$proofDirectory = Join-Path $output 'package-proof'
& (Join-Path $kitTools 'Scenarios/Export-SteamEvidence.ps1') -PackageDirectory $product -ArtifactDirectory $proofDirectory -Role Host -Mode off
$reference = Join-Path $kit 'package-reference.json'
Copy-Item -LiteralPath (Join-Path $proofDirectory 'machine-proof.json') -Destination $reference
$proof = Get-Content -LiteralPath $reference -Raw -Encoding UTF8 | ConvertFrom-Json
if ($proof.packageVerified -ne $true) { throw 'Package proof failed; this delivery is not released.' }
foreach ($role in @('Host','Client')) {
    foreach ($mode in @('off','local','replicated')) {
        [ordered]@{readyToRun=$true;parameters=[ordered]@{Role=$role;Mode=$mode}} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $kit "launch-configs/$role-$mode.json") -Encoding UTF8
    }
}
$analysis = Join-Path $output 'analysis-tools'
New-Item -ItemType Directory -Path $analysis | Out-Null
foreach ($name in @('CombatEvidence.py','CombatInvestigation.py','CombatInvestigationIndex.py','CombatInvestigationExport.py','CombatNetworkEvidence.py','Analyze-NetworkDiagnostics.py')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $analysis $name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CombatInvestigationWeb') -Destination (Join-Path $analysis 'CombatInvestigationWeb') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../docs/steam-network-light.md') -Destination (Join-Path $output 'README.md')
[ordered]@{schemaVersion=1;readyToRun=$true;purpose='Steam lightweight network diagnostics; real reproduction pending';
    packageReferenceSha256=(Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant();
    identity=$proof.identity;createdUtc=[DateTime]::UtcNow.ToString('o');
    networkCapabilities=[ordered]@{version=1;lightweightNetworkEnabled=$true;fullCombatEvidenceEnabled=$false;injectionEnabled=$false;samplingIntervalSeconds=1}
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $kit 'release.json') -Encoding UTF8
Write-Output "Released lightweight kit: $output"
