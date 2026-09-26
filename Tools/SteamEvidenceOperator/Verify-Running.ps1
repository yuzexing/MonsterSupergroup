[CmdletBinding()]
param([string]$CaseDirectory,[string]$PackageDirectory)
. (Join-Path $PSScriptRoot 'Case-Common.ps1')
Import-Module (Join-Path $PSScriptRoot 'Tools/SteamEvidenceIdentity.psm1') -Force
Write-Host "Verifier revision: network-verify-3; PowerShell $($PSVersionTable.PSVersion)"
$reference = Assert-CaseRelease
$paths = Resolve-CaseArguments $CaseDirectory $PackageDirectory
$context = Read-CaseContext $paths.CaseDirectory $paths.PackageDirectory
Write-Host "Verifying case: $($context.Case)"
$output = Join-Path $context.Case ('runtime-check-' + (New-CaseSuffix))
& (Join-Path $PSScriptRoot 'Tools/Scenarios/Export-SteamEvidence.ps1') -PackageDirectory $context.Package -ArtifactDirectory $output -Role $context.Metadata.role -Mode $context.Metadata.mode -ExpectedProof $reference -CaptureManifest $context.Manifest -GameProcessId ([int]$context.Capture.processId)
$proof = Read-CaseJson (Join-Path $output 'machine-proof.json')
if ($proof.packageVerified -ne $true -or $proof.packageMatchesExpected -ne $true -or $proof.runtime.attributionVerified -ne $true -or -not $proof.runtime.liveProcess) { throw 'Live package/process identity remains unverified.' }
if ($context.Metadata.mode -ne 'off') {
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
if ($context.Metadata.stage -eq 'network') {
    $configuration = [ordered]@{verified=$false;error=$null}
    try {
        $configuration = Get-SteamNetworkConfiguration -Capture $context.Capture -ExpectedBuildGuid $proof.identity.buildGuid
        Assert-SteamNetworkCapabilities -Actual $configuration.applied -Expected $context.Metadata.requestedNetworkCapabilities
    } catch {
        $configuration = [ordered]@{verified=$false;error=$_.Exception.Message}
        $diagnosticsPath = Join-Path $output 'network-startup-diagnostics.json'
        try {
            $diagnostics = Get-SteamNetworkStartupDiagnostics -Capture $context.Capture
            Write-CaseJson $diagnostics $diagnosticsPath
            Write-Host "Network startup diagnosis: $diagnosticsPath"
            Write-Host "Network output: $($diagnostics.directory); exists=$($diagnostics.directoryExists); JSONL files=$(@($diagnostics.files).Count)"
            foreach ($file in $diagnostics.files) { Write-Host "  $($file.path): bytes=$($file.bytes), firstKind=$($file.firstKind), readError=$($file.readError)" }
        } catch { Write-Host "Could not save startup diagnosis: $($_.Exception.Message)" }
        throw
    } finally { Write-CaseJson $configuration (Join-Path $output 'network-configuration.json') }
    Write-Host 'Lightweight network capture verified. Full combat evidence and fault injection are disabled.'
}
Write-Host "Live identity and requested configuration verified: $output . This does not prove actual game role, evidence completeness or gameplay correctness."
