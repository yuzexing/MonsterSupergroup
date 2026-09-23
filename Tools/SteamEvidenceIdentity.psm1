function Get-SteamEvidenceRuntimeIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExpectedExecutable,
        [Parameter(Mandatory)][ValidateRange(1,2147483647)][int]$GameProcessId,
        # Injection is confined to this read/validate helper for contract tests.
        # Export-SteamEvidence.ps1 always uses the real filtered CIM query.
        [scriptblock]$ProcessReader = {
            param([int]$requestedId)
            Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $requestedId" -Property ProcessId,ExecutablePath,CreationDate -ErrorAction Stop
        }
    )
    $records = @(& $ProcessReader $GameProcessId)
    if ($records.Count -ne 1 -or -not $records[0]) { throw 'The specified game process does not exist or cannot be read.' }
    $record = $records[0]
    if ($record.ProcessId -ne $GameProcessId -or [string]::IsNullOrWhiteSpace([string]$record.ExecutablePath) -or -not $record.CreationDate) {
        throw 'The process identity is incomplete or belongs to a different PID.'
    }
    $actual = [IO.Path]::GetFullPath([string]$record.ExecutablePath)
    if (-not $actual.Equals([IO.Path]::GetFullPath($ExpectedExecutable),[StringComparison]::OrdinalIgnoreCase)) {
        throw "The specified process runs a different executable: $actual"
    }
    $created = [DateTime]$record.CreationDate
    if ($created -eq [DateTime]::MinValue) { throw 'The process creation time is missing.' }
    return [ordered]@{kind='ReadOnlyCimProcessIdentity';attributionVerified=$true;processId=$GameProcessId;
        executablePath=$actual;startedUtc=$created.ToUniversalTime().ToString('o');observedUtc=[DateTime]::UtcNow.ToString('o');
        reason='Explicit PID executable path and creation time matched the selected package; no command line was requested'}
}
Export-ModuleMember -Function Get-SteamEvidenceRuntimeIdentity
