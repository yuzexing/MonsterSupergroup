[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project ('Logs/BuildProfilesResume/offline-input-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Use a new output directory: $OutputDirectory" }
$null = Get-Command dotnet -ErrorAction Stop
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$production = Join-Path $project 'Assets/_Project/EditorTools/Editor/ProjectBuildInputEvidence.cs'
$harness = Join-Path $PSScriptRoot 'BuildInputEvidenceTests.cs'
$inputs = @($production, $harness, $PSCommandPath)
$before = @($inputs | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} })
# Compile frozen copies of the actual production component. No Unity or NuGet packages.
Copy-Item -LiteralPath $production -Destination (Join-Path $OutputDirectory 'ProjectBuildInputEvidence.cs')
Copy-Item -LiteralPath $harness -Destination (Join-Path $OutputDirectory 'BuildInputEvidenceTests.cs')
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $OutputDirectory 'runner-source.ps1')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <UseAppHost>false</UseAppHost>
    <UseSharedCompilation>false</UseSharedCompilation>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $OutputDirectory 'OfflineInputEvidence.csproj') -Encoding UTF8
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $OutputDirectory 'NuGet.Config') -Encoding UTF8
$arguments = @('run','--project',(Join-Path $OutputDirectory 'OfflineInputEvidence.csproj'),'--configuration','Release','--',(Join-Path $OutputDirectory 'Fixtures With Spaces'))
$started = [DateTime]::UtcNow.ToString('o')
& dotnet @arguments *> (Join-Path $OutputDirectory 'execution.log')
$testExit = $LASTEXITCODE
$after = @($inputs | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} })
$unchanged = @($before | Where-Object { $entry=$_; -not ($after | Where-Object { $_.path -eq $entry.path -and $_.sha256 -eq $entry.sha256 }) }).Count -eq 0
[ordered]@{
    startedUtc=$started;finishedUtc=[DateTime]::UtcNow.ToString('o');cwd=(Get-Location).Path
    executable=(Get-Command dotnet).Source;arguments=$arguments;dotnetVersion=(& dotnet --version)
    exitCode=$testExit;sourceUnchanged=$unchanged;before=$before;after=$after
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'execution.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $OutputDirectory 'execution.log')
if ($testExit -ne 0 -or -not $unchanged) { throw "Offline evidence regression failed or inputs changed. $OutputDirectory" }
Write-Output "Evidence: $OutputDirectory"
