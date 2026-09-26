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
function Get-SteamEvidenceProfileConfiguration {
    param([ValidateSet('Standard','Diagnostic')][string]$Profile = 'Standard')
    $diagnostic = $Profile -ieq 'Diagnostic'
    return [ordered]@{profile=$Profile.ToLowerInvariant();queueBytes=$(if($diagnostic){536870912}else{33554432});
        reservedBytes=4194304;memoryBudgetBytes=$(if($diagnostic){805306368}else{134217728});
        windowMilliseconds=$(if($diagnostic){1000}else{100});drainPolicy=$(if($diagnostic){'WaitForCompletion'}else{'Bounded30Seconds'})}
}

function Expand-SteamEvidenceGzip([byte[]]$Bytes, [int]$MaximumBytes = 1048576) {
    $inputStream = [IO.MemoryStream]::new($Bytes,$false)
    $gzip = [IO.Compression.GZipStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress)
    $outputStream = [IO.MemoryStream]::new()
    try {
        $buffer = New-Object byte[] 8192
        while (($count = $gzip.Read($buffer,0,$buffer.Length)) -gt 0) {
            if ($outputStream.Length + $count -gt $MaximumBytes) { throw 'Startup evidence exceeds the verification read limit.' }
            $outputStream.Write($buffer,0,$count)
        }
        return ,$outputStream.ToArray()
    } finally { $outputStream.Dispose(); $gzip.Dispose(); $inputStream.Dispose() }
}

function Get-SteamEvidenceBytesHash([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-SteamEvidenceConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Capture,
        [Parameter(Mandatory)][ValidateSet('Standard','Diagnostic')][string]$ExpectedProfile,
        [string]$ExpectedBuildGuid
    )
    if (-not $Capture.launcherArgumentsApplied -or $Capture.attached -or -not $Capture.processId -or
        -not $Capture.startedUtc -or -not $Capture.executable -or -not $Capture.requestedEvidenceOutput) {
        throw 'Capture lacks the launched process and isolated evidence output required for configuration verification.'
    }
    $expected = Get-SteamEvidenceProfileConfiguration $ExpectedProfile
    if ($Capture.requestedEvidenceProfile -cne $expected.profile -or -not $Capture.evidenceProfileArgumentsApplied) {
        throw 'Capture did not apply the requested evidence profile.'
    }
    $boot = Join-Path ([IO.Path]::GetFullPath([string]$Capture.requestedEvidenceOutput)) 'boot/0/sources'
    $startupMatches = @()
    # process.start is the first logical record in the isolated boot/0 source. Do
    # not scan an active match or infer the applied configuration from arguments.
    foreach ($source in @(Get-ChildItem -LiteralPath $boot -Directory -ErrorAction SilentlyContinue)) {
        $file = Get-ChildItem -LiteralPath $source.FullName -Filter 'events-*.jsonl' -File | Sort-Object Name | Select-Object -First 1
        if (-not $file) { continue }
        $line = Get-Content -LiteralPath $file.FullName -TotalCount 1 -Encoding UTF8
        if (-not $line) { continue }
        $record = $line | ConvertFrom-Json
        if ($record.encoding) {
            if ($record.schemaVersion -ne 2 -or $record.encoding -cne 'gzip-jsonl-v1' -or
                $record.count -lt 1 -or $record.count -gt 65536) { throw 'Unsupported startup evidence block.' }
            $raw = Expand-SteamEvidenceGzip ([Convert]::FromBase64String([string]$record.data))
            if ((Get-SteamEvidenceBytesHash $raw) -cne $record.hash) { throw 'Startup evidence block checksum mismatch.' }
            $record = ([Text.UTF8Encoding]::new($false,$true).GetString($raw).Split([char]10)[0]) | ConvertFrom-Json
        }
        if ($record.stage -cne 'process.start') { continue }
        $metadata = $record.input
        $payloadPath = $null
        if ($record.inputRef) {
            $payloadPath = [IO.Path]::GetFullPath((Join-Path $source.FullName ([string]$record.inputRef)))
            if (-not $payloadPath.StartsWith($source.FullName + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Startup payload reference escapes its source.' }
            $payload = Get-Item -LiteralPath $payloadPath
            if ($payload.Length -gt 1048576 -or $payload.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Startup payload is linked or exceeds the verification read limit.' }
            $raw = Expand-SteamEvidenceGzip ([IO.File]::ReadAllBytes($payloadPath))
            if ($payload.Name -cnotmatch '^([0-9a-f]{64})\.json\.gz$') { throw 'Startup payload name is not content addressed.' }
            $expectedHash = $Matches[1]
            if ((Get-SteamEvidenceBytesHash $raw) -cne $expectedHash) { throw 'Startup payload checksum mismatch.' }
            $metadata = [Text.UTF8Encoding]::new($false,$true).GetString($raw) | ConvertFrom-Json
        }
        if (-not $metadata -or $metadata.processId -ne $Capture.processId -or
            -not $metadata.executablePath -or -not [IO.Path]::GetFullPath([string]$metadata.executablePath).Equals([IO.Path]::GetFullPath([string]$Capture.executable),[StringComparison]::OrdinalIgnoreCase) -or
            $record.captureId -cne $metadata.captureId -or $source.Name -cne $metadata.captureId) { throw 'Startup evidence does not match the launched process or source.' }
        if (-not $record.utc -or ([DateTime]$record.utc).ToUniversalTime() -lt ([DateTime]$Capture.startedUtc).ToUniversalTime().AddSeconds(-1)) { throw 'Startup evidence predates the launched process.' }
        if ($ExpectedBuildGuid -and ([Guid]$metadata.buildGuid) -ne ([Guid]$ExpectedBuildGuid)) { throw 'Startup evidence Build GUID differs from the released package.' }
        $actual = $metadata.evidenceConfiguration
        foreach ($name in $expected.Keys) {
            if (-not $actual -or [string]$actual.$name -cne [string]$expected[$name]) { throw "Applied evidence configuration differs: $name" }
            if ([string]$Capture.requestedEvidenceConfiguration.$name -cne [string]$expected[$name]) { throw "Requested evidence configuration differs: $name" }
        }
        $startupMatches += [ordered]@{verified=$true;requested=$expected;applied=$actual;captureId=$metadata.captureId;
            processId=$metadata.processId;buildGuid=$metadata.buildGuid;recordSequence=$record.recordSequence;
            startupRecordFile=$file.FullName;startupPayloadFile=$payloadPath;observedUtc=[DateTime]::UtcNow.ToString('o');
            limitation='Verifies startup configuration and process binding, not evidence completeness or gameplay correctness.'}
    }
    if ($startupMatches.Count -ne 1) { throw 'Exactly one matching process.start record is required; wait at the menu for its initial flush and retry.' }
    return $startupMatches[0]
}
function Assert-SteamNetworkCapabilities {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Actual,[Parameter(Mandatory)]$Expected)
    foreach ($name in @('version','lightweightNetworkEnabled','fullCombatEvidenceEnabled','injectionEnabled','samplingIntervalSeconds')) {
        $actualValue = $Actual.$name
        $expectedValue = $Expected.$name
        if ($name -in @('version','samplingIntervalSeconds')) {
            # Windows PowerShell reads JSON 1.0 as Decimal; PowerShell 7 reads Double.
            # Compare numeric values while rejecting strings, booleans and missing fields.
            $actualNumber = $actualValue -is [int] -or $actualValue -is [long] -or $actualValue -is [double] -or $actualValue -is [decimal]
            $expectedNumber = $expectedValue -is [int] -or $expectedValue -is [long] -or $expectedValue -is [double] -or $expectedValue -is [decimal]
            $matches = $actualNumber -and $expectedNumber -and ($actualValue -eq $expectedValue)
        } else {
            $matches = $actualValue -is [bool] -and $expectedValue -is [bool] -and ($actualValue -eq $expectedValue)
        }
        if (-not $matches) { throw "Applied network configuration differs: $name" }
    }
}
function Assert-SteamNetworkOutputPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory)
    # Include the ten-digit PID, capture ID and status suffix, not just the JSONL.
    # The shipped Mono writer can create a 256-character JSONL but fail its 268-character status file.
    $longestStatus = Join-Path ([IO.Path]::GetFullPath($Directory)) ('99991231-235959-2147483647-' + ('f' * 32) + '.jsonl.status.json')
    if ($longestStatus.Length -ge 260) {
        throw "Network log path is too long: status path may reach $($longestStatus.Length) characters (must be below 260). Copy product and operator-kit into a short folder such as C:\SteamNet, then start a new case. No game was launched. Directory: $Directory"
    }
}
function Get-SteamNetworkConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Capture,[string]$ExpectedBuildGuid)
    if (-not $Capture.launcherArgumentsApplied -or $Capture.attached -or -not $Capture.processId -or
        -not $Capture.startedUtc -or -not $Capture.executable -or -not $Capture.requestedNetworkOutput -or
        $Capture.requestedEvidenceMode -cne 'off') {
        throw 'Lightweight verification requires a launched process, isolated network output and full evidence off.'
    }
    $expected = [ordered]@{version=1;lightweightNetworkEnabled=$true;fullCombatEvidenceEnabled=$false;injectionEnabled=$false;samplingIntervalSeconds=1}
    $matchesFound = @()
    if (-not (Test-Path -LiteralPath $Capture.requestedNetworkOutput -PathType Container)) {
        throw "Network output directory is missing: $($Capture.requestedNetworkOutput). The requested lightweight capture is not verified."
    }
    $files = @(Get-ChildItem -LiteralPath $Capture.requestedNetworkOutput -Filter '*.jsonl' -File -ErrorAction Stop)
    $emptyFiles = 0; $nonHeaderFiles = 0
    foreach ($file in $files) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked network startup files are not accepted.' }
        $line = Get-Content -LiteralPath $file.FullName -TotalCount 1 -Encoding UTF8
        if (-not $line) { $emptyFiles++; continue }
        $header = $line | ConvertFrom-Json
        if ($header.kind -cne 'header') { $nonHeaderFiles++; continue }
        if ($header.schemaVersion -lt 3 -or -not $header.captureId -or $header.processId -ne $Capture.processId -or
            -not $header.executablePath -or -not [IO.Path]::GetFullPath([string]$header.executablePath).Equals([IO.Path]::GetFullPath([string]$Capture.executable),[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Network header does not prove the requested process, executable and lightweight capability version.'
        }
        if (-not $header.utcStart -or ([DateTime]$header.utcStart).ToUniversalTime() -lt ([DateTime]$Capture.startedUtc).ToUniversalTime().AddSeconds(-1)) { throw 'Network header predates the launched process.' }
        if ($ExpectedBuildGuid -and ([Guid]$header.buildGuid) -ne ([Guid]$ExpectedBuildGuid)) { throw 'Network header Build GUID differs from the released package.' }
        Assert-SteamNetworkCapabilities -Actual $header.networkCapabilities -Expected $expected
        $matchesFound += [ordered]@{verified=$true;verifierRevision='network-verify-3';applied=$header.networkCapabilities;captureId=$header.captureId;
            processId=$header.processId;buildGuid=$header.buildGuid;startupRecordFile=$file.FullName;
            observedUtc=[DateTime]::UtcNow.ToString('o');limitation='Startup identity and capability verification only; not completeness, actual game role or Steam congestion acceptance.'}
    }
    if ($matchesFound.Count -ne 1) {
        throw "Expected one lightweight network header; found $($matchesFound.Count). JSONL files=$($files.Count), empty=$emptyFiles, nonHeader=$nonHeaderFiles. Directory: $($Capture.requestedNetworkOutput). Inspect network-startup-diagnostics.json; capture is not verified."
    }
    return $matchesFound[0]
}
function Get-SteamNetworkStartupDiagnostics {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Capture)
    $report = [ordered]@{verifierRevision='network-verify-3';powershellVersion=$PSVersionTable.PSVersion.ToString();
        modulePath=$PSCommandPath;moduleSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;
        processId=$Capture.processId;executable=$Capture.executable;startedUtc=$Capture.startedUtc;
        requestedEvidenceMode=$Capture.requestedEvidenceMode;actualCommandLine=$Capture.actualCommandLine;
        directory=$Capture.requestedNetworkOutput;directoryCharacters=([string]$Capture.requestedNetworkOutput).Length;directoryExists=$false;files=@();playerLog=$Capture.requestedPlayerLog;playerLogHints=@()}
    $report.directoryExists = Test-Path -LiteralPath $Capture.requestedNetworkOutput -PathType Container
    if ($report.directoryExists) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Capture.requestedNetworkOutput -Filter '*.jsonl' -File -ErrorAction Stop)) {
            $entry = [ordered]@{path=$file.FullName;bytes=$file.Length;pathCharacters=$file.FullName.Length;statusPathCharacters=($file.FullName.Length+12);firstKind=$null;processId=$null;captureId=$null;networkCapabilities=$null;readError=$null;writerStatus=$null}
            try {
                if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked startup file' }
                $line = Get-Content -LiteralPath $file.FullName -TotalCount 1 -Encoding UTF8
                if ($line) {
                    $header = $line | ConvertFrom-Json
                    $entry.firstKind=$header.kind; $entry.processId=$header.processId; $entry.captureId=$header.captureId
                    $entry.networkCapabilities=$header.networkCapabilities
                }
                $status = $file.FullName + '.status.json'
                if (Test-Path -LiteralPath $status -PathType Leaf) { $entry.writerStatus=Get-Content -LiteralPath $status -Raw -Encoding UTF8 | ConvertFrom-Json }
            } catch { $entry.readError=$_.Exception.Message }
            $report.files += $entry
        }
    }
    if ($Capture.requestedPlayerLog -and (Test-Path -LiteralPath $Capture.requestedPlayerLog -PathType Leaf)) {
        $report.playerLogHints = @(Get-Content -LiteralPath $Capture.requestedPlayerLog -Tail 300 -Encoding UTF8 |
            Select-String -Pattern 'NetworkDiagnostics|Exception|Error|CombatEvidence|Unauthorized|denied' | Select-Object -Last 30 | ForEach-Object { $_.Line })
    }
    return $report
}
Export-ModuleMember -Function Get-SteamEvidenceRuntimeIdentity,Get-SteamEvidenceProfileConfiguration,Get-SteamEvidenceConfiguration,Get-SteamNetworkConfiguration,Assert-SteamNetworkCapabilities,Get-SteamNetworkStartupDiagnostics,Assert-SteamNetworkOutputPath
