[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory, [string]$DestinationRoot)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($BuildDirectory)
$marker = Join-Path $source 'build-complete.json'
if (-not (Test-Path -LiteralPath $marker)) { throw 'Missing successful build marker. Build through the project tool first.' }
$info = Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
if ($info.gameVersion -notmatch '^\d+\.\d+\.\d+$' -or $info.kind -notin @('dev','test','shipping') -or $info.buildId -notmatch '^\d{8}T\d{9}Z-[a-f0-9]{8}$') { throw 'Invalid BuildInfo.' }
$embedded = @(Get-ChildItem -LiteralPath $source -Directory -Filter '*_Data' | ForEach-Object {
    $path = Join-Path $_.FullName 'StreamingAssets/BuildInfo.json'
    if (Test-Path -LiteralPath $path) { $path }
})
if ($embedded.Count -ne 1 -or (Get-FileHash -LiteralPath $embedded[0]).Hash -ne (Get-FileHash -LiteralPath $marker).Hash) { throw 'BuildInfo does not match the successful build marker.' }
if (-not $DestinationRoot) { $DestinationRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds/Packages' }
$name = 'MonsterSupergroup-v' + $info.gameVersion + '-' + $info.kind + '-' + $info.buildId
$destination = Join-Path ([IO.Path]::GetFullPath($DestinationRoot)) $name
$zip = $destination + '.zip'
if ($destination.StartsWith($source.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive destination must be outside the source build directory.' }
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $zip)) { throw 'Archive already exists. Frozen deliveries cannot be overwritten.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $source) {
    if ($entry.Name -like '*BackUpThisFolder_ButDontShipItWithYourGame*' -or $entry.Name -like '*BurstDebugInformation_DoNotShip*') { continue }
    Copy-Item -LiteralPath $entry.FullName -Destination $destination -Recurse
}
if ($info.kind -eq 'shipping' -and (Get-ChildItem -LiteralPath $destination -Recurse -File -Filter '*.Tests*.dll')) { throw 'Shipping archive contains test assemblies.' }
$files = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($destination.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered]@{ buildInfo=$info;packagedUtc=[DateTime]::UtcNow.ToString('o');files=$files } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'package-manifest.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($destination,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
[pscustomobject]@{ BuildId=$info.buildId;Version=$info.gameVersion;Kind=$info.kind;Package=$destination;Archive=$zip;Sha256=$hash }
