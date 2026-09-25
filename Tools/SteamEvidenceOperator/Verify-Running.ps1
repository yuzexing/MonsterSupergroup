[CmdletBinding()]
param([string]$CaseDirectory,[string]$PackageDirectory)
. (Join-Path $PSScriptRoot 'Case-Common.ps1')
$reference = Assert-CaseRelease
$paths = Resolve-CaseArguments $CaseDirectory $PackageDirectory
$context = Read-CaseContext $paths.CaseDirectory $paths.PackageDirectory
Write-Host "Verifying case: $($context.Case)"
$output = Join-Path $context.Case ('runtime-check-' + (New-CaseSuffix))
& (Join-Path $PSScriptRoot 'Tools/Scenarios/Export-SteamEvidence.ps1') -PackageDirectory $context.Package -ArtifactDirectory $output -Role $context.Metadata.role -Mode $context.Metadata.mode -ExpectedProof $reference -CaptureManifest $context.Manifest -GameProcessId ([int]$context.Capture.processId)
$proof = Read-CaseJson (Join-Path $output 'machine-proof.json')
if ($proof.packageVerified -ne $true -or $proof.packageMatchesExpected -ne $true -or $proof.runtime.attributionVerified -ne $true -or -not $proof.runtime.liveProcess) { throw 'Live package/process identity remains unverified.' }
if ($context.Metadata.mode -ne 'off') {
    Import-Module (Join-Path $PSScriptRoot 'Tools/SteamEvidenceIdentity.psm1')
    $configuration = [ordered]@{verified=$false;error=$null}
    try {
        if (-not $context.Metadata.requestedEvidenceProfile) { throw 'This case has no requested evidence profile. Use its original operator kit for identity-only verification.' }
        $configuration = Get-SteamEvidenceConfiguration -Capture $context.Capture -ExpectedProfile $context.Metadata.requestedEvidenceProfile -ExpectedBuildGuid $proof.identity.buildGuid
        foreach ($name in $configuration.requested.Keys) {
            if ([string]$context.Metadata.requestedEvidenceConfiguration.$name -cne [string]$configuration.requested[$name]) { throw "Case evidence configuration differs: $name" }
        }
    } catch {
        $configuration = [ordered]@{verified=$false;error=$_.Exception.Message}
        throw
    } finally { Write-CaseJson $configuration (Join-Path $output 'evidence-configuration.json') }
    Write-Host "Applied evidence profile verified: $($configuration.applied.profile), queue $($configuration.applied.queueBytes) bytes, memory budget $($configuration.applied.memoryBudgetBytes) bytes."
}
Write-Host "Live identity and requested configuration verified: $output . This does not prove actual game role, evidence completeness or gameplay correctness."
