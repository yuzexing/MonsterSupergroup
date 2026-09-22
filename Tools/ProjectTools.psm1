$ErrorActionPreference = 'Stop'
$script:OwnedProcesses = [System.Collections.Generic.List[object]]::new()

function Resolve-ProjectBuildExecutable {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Recipe,
        [string]$Executable, [string]$BuildDirectory,
        [switch]$RequireDevelopmentTools,
        [ValidateSet('Steam','Kcp')][string]$Network,
        [ValidateSet('Normal','Evidence')][string]$Diagnostics)

    if ($Executable -and $BuildDirectory) { throw 'Choose either Executable or BuildDirectory.' }
    if ($BuildDirectory) {
        if (-not [IO.Path]::IsPathRooted($BuildDirectory)) { $BuildDirectory = Join-Path $ProjectRoot $BuildDirectory }
        $players = @(Get-ChildItem -LiteralPath $BuildDirectory -Directory -Filter '*_Data' | ForEach-Object {
            $candidate = Join-Path $BuildDirectory ($_.Name.Substring(0, $_.Name.Length - 5) + '.exe')
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $candidate }
        })
        if ($players.Count -ne 1) { throw "Expected one Unity Player in explicit build directory: $BuildDirectory" }
        $Executable = $players[0]
    }
    # Explicit paths support frozen historical packages. Automatic selection never searches old output folders.
    if ($Executable) {
        if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $ProjectRoot $Executable }
        if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Missing explicit Player: $Executable" }
        return (Resolve-Path -LiteralPath $Executable).Path
    }

    $resultPath = Join-Path $ProjectRoot "Library/ProjectTools/BuildResults/$Recipe.json"
    $instruction = "Build '$Recipe' using MonsterSupergroup > 构建与验收 > 构建配置 or Invoke-ProjectTool.ps1 -ToolId build.player -Profile $Recipe. Use -Executable only to select a specific frozen package."
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw "No successful current build result. $instruction" }
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($result.success -isnot [bool] -or -not $result.success -or $result.recipe -cne $Recipe -or -not $result.buildId) {
        throw "Invalid or failed build result: $resultPath. $instruction"
    }
    $executablePath = [IO.Path]::GetFullPath([string]$result.executable)
    $buildRoot = Split-Path -Parent $executablePath
    $embedded = Join-Path $buildRoot (([IO.Path]::GetFileNameWithoutExtension($executablePath)) + '_Data/StreamingAssets/BuildInfo.json')
    $marker = Join-Path $buildRoot 'build-complete.json'
    foreach ($required in @($executablePath,$embedded,$marker)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Incomplete build: $required. $instruction" }
    }
    if ([IO.Path]::GetFullPath([string]$result.buildInfoPath) -ne [IO.Path]::GetFullPath($embedded) -or
        (Get-FileHash -LiteralPath $embedded -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash) {
        throw "BuildInfo does not match the successful package: $resultPath"
    }
    $info = Get-Content -LiteralPath $embedded -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($info.buildId -cne $result.buildId -or $info.profile -cne $Recipe) { throw 'Build identity or recipe differs from the selected result.' }
    if ($RequireDevelopmentTools -and ($info.developmentTools -isnot [bool] -or -not $info.developmentTools)) {
        throw "The selected package has no mechanism/development capability. $instruction"
    }
    if ($Network -and $info.network -cne $Network) { throw "The selected $Recipe package uses '$($info.network)'; this scenario requires $Network. $instruction" }
    if ($Diagnostics -and $info.diagnostics -cne $Diagnostics) { throw "This scenario requires $Diagnostics diagnostics. $instruction" }
    Write-Host "Using $Recipe $($info.buildId): $executablePath"
    return $executablePath
}

function Resolve-ProjectUnity {
    param([string]$ProjectRoot, [string]$Unity)
    $version = (Select-String -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches.Groups[1].Value.Trim()
    if (-not $Unity) { $Unity = $env:UNITY_EDITOR_PATH }
    if (-not $Unity) {
        $candidate = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe"
        if (Test-Path -LiteralPath $candidate) { $Unity = $candidate }
    }
    if (-not $Unity -or -not (Test-Path -LiteralPath $Unity)) { throw "Specify -Unity or UNITY_EDITOR_PATH for Unity $version." }
    $Unity = (Resolve-Path -LiteralPath $Unity).Path
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($Unity).ProductVersion
    if (-not $actual.StartsWith($version)) { throw "Unity version mismatch: expected $version, found $actual ($Unity)." }
    return $Unity
}

function Test-ProjectUnityCommandLine {
    param([string]$CommandLine, [string]$ProjectRoot)
    if ([string]::IsNullOrWhiteSpace($CommandLine) -or [string]::IsNullOrWhiteSpace($ProjectRoot)) { return $false }
    # Inspect argument tokens, not substrings: the project may occur in -logFile or
    # be the parent of an isolated validation project. Quotes keep spaces in one token.
    $tokens = @([regex]::Matches($CommandLine, '(?:[^\s"]+|"[^"]*")+') | ForEach-Object { $_.Value.Replace('"','') })
    $projectArguments = @()
    for ($index = 0; $index -lt $tokens.Count; $index++) {
        if ($tokens[$index] -ieq '-projectPath') {
            if ($index + 1 -ge $tokens.Count) { return $false }
            $projectArguments += $tokens[$index + 1]
        }
    }
    if ($projectArguments.Count -ne 1 -or -not [IO.Path]::IsPathRooted($projectArguments[0])) { return $false }
    try {
        $actual = [IO.Path]::GetFullPath($projectArguments[0].Replace('/', '\')).TrimEnd('\')
        $expected = [IO.Path]::GetFullPath($ProjectRoot.Replace('/', '\')).TrimEnd('\')
        return [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}

function Start-ProjectProcess {
    [CmdletBinding()]
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory,
        [string]$WindowStyle = 'Hidden', [switch]$PassThru, [hashtable]$Environment)
    if (-not (Test-Path -LiteralPath $FilePath)) { throw "Missing executable: $FilePath" }
    $options = @{ FilePath=$FilePath; ArgumentList=$ArgumentList; WindowStyle=$WindowStyle; PassThru=$true }
    if ($WorkingDirectory) { $options.WorkingDirectory = $WorkingDirectory }
    if ($Environment) {
        if (-not (Get-Command Microsoft.PowerShell.Management\Start-Process).Parameters.ContainsKey('Environment')) {
            throw 'Per-process environment requires PowerShell 7.4 or later.'
        }
        $options.Environment = $Environment
    }
    $process = Microsoft.PowerShell.Management\Start-Process @options
    $null = $process.Handle
    $record = [pscustomobject]@{ Process=$process; Id=$process.Id; File=$FilePath; Arguments=$ArgumentList; StartUtc=[DateTime]::UtcNow.ToString('o'); CleanupRequested=$false }
    $script:OwnedProcesses.Add($record)
    if ($env:PROJECT_TOOL_PROCESS_LOG) { $record | Select-Object Id,File,Arguments,StartUtc | ConvertTo-Json -Compress -Depth 5 | Add-Content -LiteralPath $env:PROJECT_TOOL_PROCESS_LOG -Encoding UTF8 }
    if ($PassThru) { return $process }
}

function Wait-ProjectProcess {
    param([Diagnostics.Process]$Process, [int]$TimeoutSeconds = 1800)
    if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) { throw "Process $($Process.Id) timed out after $TimeoutSeconds seconds." }
    $Process.WaitForExit()
}

function Stop-ProjectProcess {
    [CmdletBinding()]
    param([int]$Id, [switch]$Force)
    $record = $script:OwnedProcesses | Where-Object Id -eq $Id | Select-Object -Last 1
    if (-not $record) { throw "Refusing to stop an unowned process: $Id" }
    if (-not $record.Process.HasExited) {
        $record.CleanupRequested = $true
        $record.Process.Kill()
        $null = $record.Process.WaitForExit(10000)
    }
}

function Assert-ProjectProcessExits {
    foreach ($record in $script:OwnedProcesses) {
        if ($record.CleanupRequested) { continue }
        if (-not $record.Process.HasExited) { throw "Process $($record.Id) is still running after the scenario returned." }
        $record.Process.WaitForExit()
        if ($record.Process.ExitCode -ne 0) { throw "Process $($record.Id) exited with $($record.Process.ExitCode). A PASS log does not override a native crash." }
    }
}

function Clear-ProjectProcesses {
    foreach ($record in $script:OwnedProcesses) {
        if (-not $record.Process.HasExited) { Stop-ProjectProcess -Id $record.Id -Force }
        if ($env:PROJECT_TOOL_PROCESS_LOG) {
            [pscustomobject]@{ Id=$record.Id; ExitCode=$record.Process.ExitCode; CleanupRequested=$record.CleanupRequested; EndUtc=[DateTime]::UtcNow.ToString('o') } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $env:PROJECT_TOOL_PROCESS_LOG -Encoding UTF8
        }
    }
    $script:OwnedProcesses.Clear()
}

function Convert-ProjectToolParameters {
    param($Tool, [hashtable]$Values)
    $allowed = @($Tool.parameters) + @('Unity', 'Apply')
    $converted = @{}
    foreach ($key in $Values.Keys) {
        if ($allowed -notcontains $key) { throw "Unsupported tool parameter: $key. Query -Help for this tool." }
        $value = $Values[$key]
        if ($key -in @('Apply','ScriptsOnly','UniqueOutput')) {
            if ($value -is [string]) { $value = [bool]::Parse($value) }
            $converted[$key] = [bool]$value
        } else { $converted[$key] = [string]$value }
    }
    return $converted
}

function Convert-ProjectParameters {
    param([string]$ScriptPath, [hashtable]$Values)
    $definitions = (Get-Command $ScriptPath).Parameters
    $converted = @{}
    foreach ($key in $Values.Keys) {
        $name = $key
        if ($key -eq 'Profile' -and -not $definitions.ContainsKey($key) -and $definitions.ContainsKey('Scenario')) { $name = 'Scenario' }
        if (-not $definitions.ContainsKey($name)) { throw "Unsupported scenario parameter: $key. Query -Help for this tool." }
        $type = $definitions[$name].ParameterType
        if ($type -eq [System.Management.Automation.SwitchParameter] -or $type -eq [bool]) {
            $value = $Values[$key]
            if ($value -is [string]) { $value = [bool]::Parse($value) }
            $converted[$name] = [bool]$value
        } else { $converted[$name] = [System.Management.Automation.LanguagePrimitives]::ConvertTo($Values[$key], $type) }
    }
    return $converted
}
Export-ModuleMember -Function *-Project*
