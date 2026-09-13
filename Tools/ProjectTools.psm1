$ErrorActionPreference = 'Stop'
$script:OwnedProcesses = [System.Collections.Generic.List[object]]::new()

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
        if ($key -in @('Apply','ScriptsOnly')) {
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
