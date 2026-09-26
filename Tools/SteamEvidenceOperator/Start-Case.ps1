[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][ValidateSet('Host','Client')][string]$Role,
    [Parameter(Mandatory)][ValidateSet('off','local','replicated')][string]$Mode,
    [Parameter(Mandatory)][ValidateSet('chain','comparison','network')][string]$Stage,
    [ValidateSet('Standard','Diagnostic')][string]$EvidenceProfile = 'Standard',
    [switch]$ObserveEvidenceQueue
)
. (Join-Path $PSScriptRoot 'Case-Common.ps1')
Import-Module (Join-Path $PSScriptRoot 'Tools/SteamEvidenceIdentity.psm1') -Force
# Invalidate the shortcut immediately so a failed new launch cannot select the previous case.
Write-CurrentCase $null $null
if ($ObserveEvidenceQueue -and ($Mode -ne 'local' -or $Stage -ne 'chain')) { throw 'This bounded observation case requires local/chain on either Host or Client; other captures keep observation disabled.' }
if ($EvidenceProfile -eq 'Diagnostic' -and $Mode -eq 'off') { throw 'Diagnostic EvidenceProfile requires evidence enabled.' }
if ($Stage -eq 'network' -and ($Mode -ne 'off' -or $ObserveEvidenceQueue)) { throw 'The network case requires full evidence off and no full-evidence queue observation.' }
$reference = Assert-CaseRelease
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$root = [IO.Path]::GetFullPath($OutputRoot)
Assert-CaseOutside $root $package
Assert-CaseOutside $root $PSScriptRoot
if ($Stage -eq 'chain' -and $Mode -eq 'off') { throw 'The chain check requires local or replicated evidence.' }
$config = Read-CaseJson (Join-Path $PSScriptRoot "launch-configs/$Role-$Mode.json")
if ($config.readyToRun -ne $true -or $config.parameters.Role -ine $Role -or $config.parameters.Mode -cne $Mode) { throw 'Launch configuration has not been released or does not match.' }
$case = Join-Path $root ("$Stage-$Role-$Mode-" + (New-CaseSuffix))
if ($Stage -eq 'network') { Assert-SteamNetworkOutputPath -Directory (Join-Path $case 'capture/metrics') }
$proofOutput = Join-Path $case 'package-check'
& (Join-Path $PSScriptRoot 'Tools/Scenarios/Export-SteamEvidence.ps1') -PackageDirectory $package -ArtifactDirectory $proofOutput -Role $Role -Mode $Mode -ExpectedProof $reference
$proof = Read-CaseJson (Join-Path $proofOutput 'machine-proof.json')
if ($proof.packageVerified -ne $true -or $proof.packageMatchesExpected -ne $true) { throw 'Package preflight did not verify the released package.' }
$metadata = [ordered]@{schemaVersion=2;packageDirectory=$package;role=$Role;mode=$Mode;stage=$Stage;createdUtc=[DateTime]::UtcNow.ToString('o');referenceSha256=(Get-FileHash -LiteralPath $reference).Hash.ToLowerInvariant();scenario='diagnostic-buffer-10-minute';scene=$null;equipment=$null;actualEnemies=$null;targetFps=$null;graphics='Default';profileSeconds=0;completeEvidence=$null;replayPassed=$null}
$metadata.requestedQueueObservation = [bool]$ObserveEvidenceQueue
$metadata.requestedEvidenceProfile = $EvidenceProfile.ToLowerInvariant()
$metadata.requestedEvidenceConfiguration = Get-SteamEvidenceProfileConfiguration $EvidenceProfile
if ($Stage -eq 'network') {
    $metadata.scenario = 'steam-network-light-8-minute'
    $metadata.requestedNetworkCapabilities = [ordered]@{version=1;lightweightNetworkEnabled=$true;fullCombatEvidenceEnabled=$false;injectionEnabled=$false;samplingIntervalSeconds=1}
}
Write-CaseJson $metadata (Join-Path $case 'case.json')
Write-CurrentCase $case $package
Write-Host "CaseDirectory: $case"
Write-Host 'At the menu, run .\Verify-Running.ps1 in a second terminal in operator-kit; no path arguments are needed.'
Write-Host 'After the game exits and collection finishes, run .\Export-Case.ps1 in operator-kit. Stop after this case and submit exported logs.'
if ($Stage -eq 'network') {
    Write-Host 'Lightweight network case: both machines use this same package. Play about 8 minutes, focusing on 280-330 seconds and high enemy counts. If a problem occurs, keep playing about 30 seconds when possible, then exit normally. No fault injection or full combat evidence is enabled.'
} elseif ($EvidenceProfile -eq 'Diagnostic') {
    Write-Host 'Diagnostic profile: play one continuous 10-minute round, then close normally and wait for saving and collection. Saving has no automatic timeout. Export the entire case even if incomplete or explicitly abandoned.'
} else { Write-Host 'Standard profile keeps the bounded 30-second save policy. Export the entire case even if incomplete.' }
$scenario = if ($Stage -eq 'network') { 'steam-network-light-8-minute' } else { "$Stage-diagnostic-buffer-10-minute" }
& (Join-Path $PSScriptRoot 'Tools/Scenarios/Start-SteamDiagnostics.ps1') -Executable $proof.identity.executable -ArtifactDirectory (Join-Path $case 'capture') -ExpectedRole $Role.ToLowerInvariant() -EvidenceMode $Mode -EvidenceProfile $EvidenceProfile -Graphics Default -ProfileSeconds 0 -Scenario $scenario -ObserveEvidenceQueue:$ObserveEvidenceQueue
