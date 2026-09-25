[CmdletBinding()]
param([string]$CaseDirectory,[string]$PackageDirectory)
. (Join-Path $PSScriptRoot 'Case-Common.ps1')
$reference = Assert-CaseRelease
$paths = Resolve-CaseArguments $CaseDirectory $PackageDirectory
$context = Read-CaseContext $paths.CaseDirectory $paths.PackageDirectory
Write-Host "Exporting case: $($context.Case)"
if ($context.Capture.complete -isnot [bool] -or -not $context.Capture.complete) { throw 'Collector has not finalized. Preserve the case and wait; do not edit capture.complete.' }
$name = [IO.Path]::GetFileName([string]$context.Capture.executable).Replace("'","''")
$filter = "Name = '$name' OR ProcessId = $([int]$context.Capture.processId)"
$running = @(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,ExecutablePath -ErrorAction Stop)
if ($running.Count) { throw 'Cannot prove the game is stopped. Close it normally and wait for collection to finish.' }
$output = $context.Case + '-export-' + (New-CaseSuffix)
& (Join-Path $PSScriptRoot 'Tools/Scenarios/Export-SteamEvidence.ps1') -PackageDirectory $context.Package -ArtifactDirectory $output -Role $context.Metadata.role -Mode $context.Metadata.mode -ExpectedProof $reference -CaptureManifest $context.Manifest -LogPath @($context.Case)
$proof = Read-CaseJson (Join-Path $output 'machine-proof.json')
if ($proof.logArchiveComplete -ne $true -or $proof.packageVerified -ne $true -or $proof.packageMatchesExpected -ne $true) { throw 'Case export did not complete; preserve both original and failed export.' }
Write-Host "Send this complete export directory: $output . Normal or abnormal exit is preserved; completeness and replay remain unverified."
