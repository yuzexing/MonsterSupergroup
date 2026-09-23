[CmdletBinding()]
param(
    [switch]$List, [switch]$Help, [string]$ToolId,
    [string]$BuildProfile, [switch]$CleanBuildCache, [switch]$RunAfterBuild,
    [string]$Profile, [ValidateSet("Dev","Test","Shipping")][string]$BuildKind, [ValidateSet("true","false")][string]$Development, [switch]$UniqueOutput, [int]$Port, [string]$AssetPath, [string]$Source, [string]$Output,
    [ValidateSet('Steam','Kcp')][string]$Network, [ValidateSet('Steam','Direct')][string]$Distribution, [ValidateSet('Normal','Evidence')][string]$Diagnostics,
    [string]$Unity, [string]$Executable, [switch]$Apply, [switch]$ScriptsOnly,
    [string]$ResultPath, [string]$ParametersFile, [hashtable]$Parameters = @{},
    [ValidateRange(1,14400)][int]$TimeoutSeconds = 1800,
    [Parameter(ValueFromRemainingArguments=$true)][object[]]$ExtraArguments
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ProjectTools.psm1')
$catalog = Get-Content -LiteralPath (Join-Path $projectRoot 'docs/editor-tools/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($List) { $catalog.tools | Select-Object id,name,category,parameters,writesAssets,graphics,interactive | Format-Table -AutoSize; return }
$tool = $catalog.tools | Where-Object id -eq $ToolId
if (-not $tool) { throw "Unknown ToolId '$ToolId'. Use -List." }
if ($Help) {
    $tool | ConvertTo-Json -Depth 10
    if ($ToolId -eq 'build.player') {
        Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Settings/Build Profiles') -Filter '*.asset' | Select-Object Name
        Write-Output 'Specify -BuildProfile Assets/Settings/Build Profiles/<name>.asset. Legacy recipes and business overrides are disabled.'
    }
    if ($tool.script) {
        $definition = Get-Command (Join-Path $PSScriptRoot ('Scenarios/' + [IO.Path]::GetFileName($tool.script)))
        $definition.ScriptBlock.Ast.ParamBlock.Parameters | ForEach-Object {
            $name = $_.Name.VariablePath.UserPath
            $parameter = $definition.Parameters[$name]
            [pscustomobject]@{
                Parameter = $name
                Type = $parameter.ParameterType.Name
                Default = $_.DefaultValue.Extent.Text
                Choices = ($parameter.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues -join ', '
            }
        } | Format-Table -Wrap -AutoSize
    }
    return
}
if ($ParametersFile) {
    $inputParameters = Get-Content -LiteralPath $ParametersFile -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $inputParameters.PSObject.Properties) { $Parameters[$property.Name] = $property.Value }
}
foreach ($key in @('Profile','Port','Executable')) { if ($PSBoundParameters.ContainsKey($key)) { $Parameters[$key] = $PSBoundParameters[$key] } }
for ($i=0; $i -lt $ExtraArguments.Count; $i++) {
    $key = [string]$ExtraArguments[$i]
    if (-not $key.StartsWith('-')) { throw "Expected parameter name, got: $key" }
    $key = $key.TrimStart('-')
    if ($i + 1 -lt $ExtraArguments.Count -and -not ([string]$ExtraArguments[$i+1]).StartsWith('-')) { $i++; $Parameters[$key] = $ExtraArguments[$i] }
    else { $Parameters[$key] = $true }
}
$folder = Join-Path $projectRoot ('Logs/ProjectTools/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $ToolId)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
if (-not $ResultPath) { $ResultPath = Join-Path $folder 'result.json' }
$ResultPath = [IO.Path]::GetFullPath($ResultPath)
$log = Join-Path $folder 'command.log'
$result = [ordered]@{ id=$ToolId; startedUtc=[DateTime]::UtcNow.ToString('o'); success=$false; pending=$false; durationSeconds=0; error=''; report=$ResultPath; log=$log; artifacts=@() }
$watch = [Diagnostics.Stopwatch]::StartNew()
$previousProcessLog = $env:PROJECT_TOOL_PROCESS_LOG
$env:PROJECT_TOOL_PROCESS_LOG = Join-Path $folder 'processes.jsonl'
$transcript = $false
Push-Location $projectRoot
try {
    Start-Transcript -LiteralPath $log | Out-Null; $transcript = $true
    if (-not $tool.script) {
        $values = @{}
        foreach ($key in $Parameters.Keys) { $values[$key] = $Parameters[$key] }
        foreach ($key in @('Profile','Port','AssetPath','Source','Output','Unity','Executable','Apply','ScriptsOnly','BuildKind','Development','UniqueOutput','Network','Distribution','Diagnostics','BuildProfile','CleanBuildCache','RunAfterBuild')) {
            if ($PSBoundParameters.ContainsKey($key)) { $values[$key] = $PSBoundParameters[$key] }
        }
        $values = Convert-ProjectToolParameters -Tool $tool -Values $values
        $BuildProfile = $values.BuildProfile; $CleanBuildCache = [bool]$values.CleanBuildCache; $RunAfterBuild = [bool]$values.RunAfterBuild
        $Profile = $values.Profile; $AssetPath = $values.AssetPath; $Source = $values.Source
        $Output = $values.Output; $Unity = $values.Unity
        $Apply = [bool]$values.Apply; $ScriptsOnly = [bool]$values.ScriptsOnly
        # Keep optional values separate from parameter variables: assigning null back to a
        # ValidateSet string casts it to '' and rejects an otherwise valid default request.
        $toolBuildKind = $values.BuildKind; $toolDevelopment = $values.Development; $UniqueOutput = [bool]$values.UniqueOutput
        $toolNetwork = $values.Network; $toolDistribution = $values.Distribution; $toolDiagnostics = $values.Diagnostics
    }
    if ($tool.writesAssets -and -not $Apply) { throw "Asset writes require -Apply. Impact: $($tool.impact)" }
    if ($tool.script) {
        $script = Join-Path $PSScriptRoot ('Scenarios/' + [IO.Path]::GetFileName($tool.script))
        if ($tool.parameters -contains 'Unity') {
            if (-not $Unity -and $Parameters.ContainsKey('Unity')) { $Unity = $Parameters.Unity }
            $Parameters.Unity = Resolve-ProjectUnity -ProjectRoot $projectRoot -Unity $Unity
            $active = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { Test-ProjectUnityCommandLine -CommandLine $_.CommandLine -ProjectRoot $projectRoot }
            if ($active) { throw 'Close this project in Unity before running Editor tests.' }
        }
        $arguments = Convert-ProjectParameters -ScriptPath $script -Values $Parameters
        & $script @arguments
        Assert-ProjectProcessExits
    } else {
        if ($tool.interactive) { throw 'This tool requires an already open Editor. Use the Tool Center.' }
        $Unity = Resolve-ProjectUnity -ProjectRoot $projectRoot -Unity $Unity
        $active = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { Test-ProjectUnityCommandLine -CommandLine $_.CommandLine -ProjectRoot $projectRoot }
        if ($active) { throw 'The project is already open in Unity. Use the Tool Center or close that Editor before batch execution.' }
        $unityResult = Join-Path $folder 'unity-result.json'
        $unityLog = Join-Path $folder 'unity.log'
        $arguments = @('-batchmode','-projectPath',('"' + $projectRoot + '"'),'-logFile',('"' + $unityLog + '"'),
            '-executeMethod',$(if ($ToolId -eq 'build.player') { 'MonsterSupergroup.EditorTools.NativeBuildEntry.Batch' } else { 'MonsterSupergroup.EditorTools.ProjectToolRunner.Batch' }),'-toolId',$ToolId,'-toolResult',('"' + $unityResult + '"'))
        if (-not $tool.graphics) { $arguments += '-nographics' }
        if ($ToolId -eq 'build.player') {
            $null = Get-ProjectBuildProfileGuid -ProjectRoot $projectRoot -BuildProfile $BuildProfile
            if ($BuildProfile.Contains('"')) { throw 'Profile path cannot contain quotes.' }
            $arguments += @('-activeBuildProfile', ('"' + $BuildProfile.Replace('\','/') + '"'))
            if ($CleanBuildCache) { $arguments += '-toolCleanBuildCache' }
            if ($RunAfterBuild) { $arguments += '-toolRunAfterBuild' }
        }
        foreach ($pair in @(@('-toolProfile',$Profile),@('-toolAsset',$AssetPath),@('-toolSource',$Source),@('-toolOutput',$Output),@('-toolBuildKind',$toolBuildKind),@('-toolDevelopment',$toolDevelopment),@('-toolNetwork',$toolNetwork),@('-toolDistribution',$toolDistribution),@('-toolDiagnostics',$toolDiagnostics))) {
            if ($pair[1]) {
                if ($pair[1].Contains('"')) { throw 'A quoted path/value is not supported; pass the unquoted value as one PowerShell argument.' }
                $arguments += @($pair[0],('"' + $pair[1] + '"'))
            }
        }
        if ($Apply) { $arguments += '-toolApply' }
        if ($ScriptsOnly) { $arguments += '-toolScriptsOnly' }
        if ($UniqueOutput) { $arguments += '-toolUniqueOutput' }
        $process = Start-ProjectProcess -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru -WorkingDirectory $projectRoot
        Wait-ProjectProcess -Process $process -TimeoutSeconds $TimeoutSeconds
        if (Test-Path -LiteralPath $unityResult) {
            $unityStatus = Get-Content -LiteralPath $unityResult -Raw -Encoding UTF8 | ConvertFrom-Json
            $result.artifacts = @($unityStatus.artifacts) + @($unityLog,$unityResult)
            if (-not $unityStatus.success -or $unityStatus.pending) { throw "Unity tool failed: $($unityStatus.error). Log: $unityLog" }
        } else { throw "Unity exited without a completed result. Log: $unityLog" }
        Assert-ProjectProcessExits
    }
    $result.success = $true
} catch { $result.error = $_.Exception.ToString(); Write-Warning $result.error }
finally {
    Clear-ProjectProcesses
    if (Test-Path -LiteralPath $env:PROJECT_TOOL_PROCESS_LOG) {
        $artifacts = @($result.artifacts) + @($log,$env:PROJECT_TOOL_PROCESS_LOG)
        foreach ($line in (Get-Content -LiteralPath $env:PROJECT_TOOL_PROCESS_LOG -Encoding UTF8)) {
            $record = $line | ConvertFrom-Json
            if ($record.Arguments) {
                $at = [Array]::IndexOf([object[]]$record.Arguments, '-logFile')
                if ($at -ge 0 -and $at+1 -lt $record.Arguments.Count) {
                    $path = $record.Arguments[$at+1].Trim('"')
                    if (Test-Path -LiteralPath $path) { $artifacts += $path }
                }
            }
        }
        $result.artifacts = @($artifacts | Select-Object -Unique)
    }
    $result.durationSeconds = $watch.Elapsed.TotalSeconds
    if ($transcript) { Stop-Transcript | Out-Null }
    New-Item -ItemType Directory -Path (Split-Path -Parent $ResultPath) -Force | Out-Null
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $projectRoot "Logs/ProjectTools/$ToolId.json") -Encoding UTF8
    $env:PROJECT_TOOL_PROCESS_LOG = $previousProcessLog
    Pop-Location
}
Write-Output "Project tool $ToolId success=$($result.success): $ResultPath"
if (-not $result.success) { throw "Project tool failed: $ResultPath" }
