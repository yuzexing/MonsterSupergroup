$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$errors = $null; $tokens = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Tools/Scenarios/Run-PlayerExitValidation.ps1'), [ref]$tokens, [ref]$errors)
if ($errors) { throw ($errors | Out-String) }
foreach ($name in @('Convert-ExitUtc','Read-QuitSignal','Read-CrashFields')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $false)
    . ([scriptblock]::Create($function.Extent.Text))
}
$time = [datetime]::UtcNow.AddSeconds(-1)
$parsed = Convert-ExitUtc $time.ToString('o')
if ($parsed.Ticks -ne $time.Ticks) { throw 'ISO timestamp did not retain UTC ticks.' }
if ((Convert-ExitUtc $time).Ticks -ne $time.Ticks) { throw 'Deserialized DateTime was converted twice.' }
$folder = Join-Path $root ('Logs/PlayerExitScriptTests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$path = Join-Path $folder 'quit.json'
@{runId='test-run';processId=42;requestedUtc=$time.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
if ((Read-QuitSignal $path 'test-run' 42).Ticks -ne $time.Ticks) { throw 'JSON signal timestamp changed timezone.' }
$rejected = $false
try { $null = Read-QuitSignal $path 'new-run' 42 } catch { $rejected = $true }
if (-not $rejected) { throw 'Old run signal was accepted.' }
$rejected = $false
try { $null = Read-QuitSignal $path 'test-run' 43 } catch { $rejected = $true }
if (-not $rejected) { throw 'Wrong process signal was accepted.' }
'{' | Set-Content -LiteralPath $path
if ($null -ne (Read-QuitSignal $path 'test-run' 42)) { throw 'Partially written signal was accepted.' }
$fields = Read-CrashFields ([xml]'<Event><EventData><Data>Player.exe</Data><Data>1</Data><Data>0</Data><Data>UnityPlayer.dll</Data><Data>1</Data><Data>0</Data><Data>c0000005</Data><Data>c2d6e9</Data><Data>10f40</Data></EventData></Event>')
if ($fields.ProcessId -ne '10f40' -or $fields.ModuleName -ne 'UnityPlayer.dll') { throw 'Positional Windows event was not decoded.' }
$fields = Read-CrashFields ([xml]'<Event><EventData><Data Name="ProcessId">0x10f40</Data><Data Name="ExceptionCode">c0000005</Data></EventData></Event>')
if ($fields.ProcessId -ne '0x10f40' -or $fields.ExceptionCode -ne 'c0000005') { throw 'Named Windows event was not decoded.' }
Write-Output 'Player exit recorder: 8 timestamp/signal/event checks passed.'
