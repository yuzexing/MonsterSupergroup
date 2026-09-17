param([string]$Original = 'F:\BaiduNetdiskDownload\d-地狱公主\Hell Maiden-v0.2.31', [string]$Unity = 'D:\RealSoftware\6000.3.21f1\Editor')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$build = Join-Path $root 'Logs/EnemyRecovery/build'
$managed = Join-Path $Original 'Hell Maiden_Data/Managed'
$target = Join-Path $root 'Logs/EnemyRecovery/runtime/Hell Maiden_Data/Managed'
if (!(Test-Path -LiteralPath $target)) { throw 'Create the isolated runtime copy before building.' }
$runtimeExe=Join-Path $root 'Logs/EnemyRecovery/runtime/Hell Maiden.exe'
if (Get-Process | Where-Object {$_.Path -eq $runtimeExe}) { throw 'Stop the isolated runtime before rebuilding its assemblies.' }
New-Item -ItemType Directory -Force -Path $build | Out-Null
$lines = @('/nologo','/target:library','/nostdlib',('/out:"' + $build + '\RecoveryProbe.dll"'))
$lines += Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$lines += ('"' + $PSScriptRoot + '\RecoveryProbe.cs"')
$lines += ('"' + $PSScriptRoot + '\ArtRecoveryProbe.cs"')
$lines += ('"' + $PSScriptRoot + '\AudioRecoveryProbe.cs"')
$lines | Set-Content -LiteralPath (Join-Path $build 'probe.rsp') -Encoding utf8BOM
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /noconfig ('@' + $build + '\probe.rsp')
if ($LASTEXITCODE) { throw 'Probe compilation failed.' }
Copy-Item -LiteralPath 'F:\C_Downloads\Mono.Cecil.dll' -Destination $build -Force
& $compiler /nologo /target:exe ('/reference:' + $build + '\Mono.Cecil.dll') ('/reference:' + $Unity + '\Data\MonoBleedingEdge\lib\mono\4.8-api\Facades\netstandard.dll') ('/out:' + $build + '\PatchRuntime.exe') (Join-Path $PSScriptRoot 'PatchRuntime.cs')
if ($LASTEXITCODE) { throw 'Patcher compilation failed.' }
& (Join-Path $Unity 'Data/MonoBleedingEdge/bin/mono.exe') (Join-Path $build 'PatchRuntime.exe') $managed $target (Join-Path $build 'RecoveryProbe.dll')
if ($LASTEXITCODE) { throw 'Runtime patching failed.' }
& $compiler /nologo ('/reference:' + $build + '\Mono.Cecil.dll') ('/reference:' + $Unity + '\Data\MonoBleedingEdge\lib\mono\4.8-api\Facades\netstandard.dll') ('/out:' + $build + '\DumpOriginalIL.exe') (Join-Path $PSScriptRoot 'DumpOriginalIL.cs')
if ($LASTEXITCODE) { throw 'Original IL reader compilation failed.' }
& (Join-Path $Unity 'Data/MonoBleedingEdge/bin/mono.exe') (Join-Path $build 'DumpOriginalIL.exe') (Join-Path $managed 'Assembly-CSharp.dll') (Join-Path $build 'original-attack-il.txt')
if ($LASTEXITCODE) { throw 'Original IL read failed.' }
