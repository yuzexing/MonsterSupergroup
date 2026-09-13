param([string]$Unity)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Unity = Resolve-ProjectUnity -ProjectRoot $projectRoot -Unity $Unity
$folder = Join-Path $projectRoot ('Logs/ProjectTools/Tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
# Shared infrastructure must reject a native failure even if a scenario printed PASS.
$shell = (Get-Process -Id $PID).Path
$failedProcess = Start-ProjectProcess -FilePath $shell -ArgumentList @('-NoProfile','-Command','exit 7') -PassThru
Wait-ProjectProcess -Process $failedProcess
$rejected = $false
try { Assert-ProjectProcessExits } catch { $rejected = $true }
Clear-ProjectProcesses
if (-not $rejected) { throw 'Nonzero process exit was accepted.' }
$localScript = Join-Path $PSScriptRoot 'Run-LocalRoomValidation.ps1'
$mapped = Convert-ProjectParameters -ScriptPath $localScript -Values @{Profile='party';Port='7777';Headless='true'}
if ($mapped.Scenario -ne 'party' -or $mapped.Port -ne 7777 -or -not $mapped.Headless) { throw 'Scenario parameter conversion failed.' }
$rejected = $false
try { Convert-ProjectParameters -ScriptPath $localScript -Values @{UnknownParameter='value'} | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw 'Unknown scenario parameter was ignored.' }
$catalog = Get-Content -LiteralPath (Join-Path $projectRoot 'docs/editor-tools/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$buildTool = $catalog.tools | Where-Object id -eq 'build.player'
$mapped = Convert-ProjectToolParameters -Tool $buildTool -Values @{Profile='menu-release';Output='Builds/custom.exe';ScriptsOnly='false'}
if ($mapped.Profile -ne 'menu-release' -or $mapped.Output -ne 'Builds/custom.exe' -or $mapped.ScriptsOnly) { throw 'Unity tool parameter conversion failed.' }
$rejected = $false
try { Convert-ProjectToolParameters -Tool $buildTool -Values @{UnknownParameter='value'} | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw 'Unknown Unity tool parameter was ignored.' }
Write-Output 'PowerShell process and parameter contracts passed.'
$xml = Join-Path $folder 'editmode.xml'
$arguments = @('-batchmode','-nographics','-projectPath',('"'+$projectRoot+'"'),'-runTests','-testPlatform','EditMode',
    '-testFilter','MonsterSupergroup.EditorTools.Tests.ProjectToolTests','-testResults',('"'+$xml+'"'),'-logFile',('"'+$folder+'/unity.log"'))
$process = Start-ProjectProcess -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Wait-ProjectProcess -Process $process
if ($process.ExitCode -ne 0) { throw "EditorTool tests exited with $($process.ExitCode): $folder" }
if (-not (Test-Path -LiteralPath $xml)) { throw "Missing test results: $folder" }
[xml]$results = Get-Content -LiteralPath $xml -Raw
if ($results.'test-run'.result -ne 'Passed' -or [int]$results.'test-run'.passed -lt 7) { throw "EditorTool tests did not pass: $folder" }
Write-Output "EditorTool tests passed: $folder"
