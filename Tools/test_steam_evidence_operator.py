"""Synthetic wrapper composition only: no real Player, identity query or Unity."""
import base64
import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

TOOLS = Path(__file__).parent
KIT = TOOLS / 'SteamEvidenceOperator'
PWSH = Path(os.environ.get('PWSH_EXE', r'C:\Users\ADMIN\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.EXE'))

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding='utf-8')

def quote(text):
    return "'" + str(text).replace("'", "''") + "'"

MOCK_EXPORT = r'''
param($PackageDirectory,$ArtifactDirectory,$Role,$Mode,$ExpectedProof,$CaptureManifest,[int]$GameProcessId,[string[]]$LogPath)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
$valid = -not (Test-Path -LiteralPath (Join-Path $PackageDirectory 'reject-package'))
$proof=@{packageVerified=$valid;packageMatchesExpected=$valid;identity=@{executable=(Join-Path $PackageDirectory 'Fake Game.exe');buildGuid=('1' * 32)};runtime=@{attributionVerified=([bool]$CaptureManifest);liveProcess=$null};logArchiveComplete=($LogPath.Count -gt 0)}
if($GameProcessId){$proof.runtime.liveProcess=@{processId=$GameProcessId}}
$proof | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'machine-proof.json')
@{package=$PackageDirectory;role=$Role;mode=$Mode;reference=$ExpectedProof;capture=$CaptureManifest;pid=$GameProcessId;logs=$LogPath} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'mock-call.json')
'''
MOCK_LAUNCH = r'''
param($Executable,$ArtifactDirectory,$ExpectedRole,$EvidenceMode,$EvidenceProfile,$Graphics,[int]$ProfileSeconds,$Scenario,[switch]$ObserveEvidenceQueue)
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
@{executable=$Executable;graphics=$Graphics;profileSeconds=$ProfileSeconds;mode=$EvidenceMode;evidenceProfile=$EvidenceProfile;role=$ExpectedRole;scenario=$Scenario;observeEvidenceQueue=[bool]$ObserveEvidenceQueue} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'mock-launch.json')
$configuration=Get-SteamEvidenceProfileConfiguration $EvidenceProfile
$evidence=Join-Path $ArtifactDirectory 'CombatDiagnostics'
@{processId=987654;startedUtc='2026-09-24T10:00:00Z';executable=$Executable;launcherArgumentsApplied=$true;attached=$false;requestedEvidenceMode=$EvidenceMode;expectedRole=$ExpectedRole;complete=$true;normalExit=$false;actualCommandLine='synthetic only';requestedEvidenceProfile=$EvidenceProfile.ToLowerInvariant();requestedEvidenceConfiguration=$configuration;evidenceProfileArgumentsApplied=$true;requestedEvidenceOutput=$evidence} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'capture.json')
$source=Join-Path $evidence 'boot/0/sources/test-capture'
New-Item -ItemType Directory -Path $source -Force | Out-Null
@{schemaVersion=2;stage='process.start';recordSequence='1';captureId='test-capture';utc='2026-09-24T10:00:01Z';input=@{captureId='test-capture';processId=987654;executablePath=$Executable;buildGuid=('1' * 32);evidenceConfiguration=$configuration}} | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath (Join-Path $source 'events-00000000000000000001.jsonl')
'''

class OperatorKitTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='steam kit mock ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.kit = self.root / 'operator kit'
        shutil.copytree(KIT, self.kit)
        (self.kit / 'Tools/Scenarios').mkdir(parents=True)
        shutil.copy2(TOOLS / 'SteamEvidenceIdentity.psm1', self.kit / 'Tools/SteamEvidenceIdentity.psm1')
        for role in ('Host', 'Client'):
            for mode in ('off', 'local', 'replicated'):
                write(self.kit / f'launch-configs/{role}-{mode}.json', {'readyToRun': False, 'parameters': {'Role': role, 'Mode': mode}})
        self.package = self.root / 'fake package with spaces'
        self.package.mkdir()
        (self.package / 'Fake Game.exe').write_bytes(b'never executed')
        self.output = self.root / 'case outputs with spaces'
        (self.kit / 'Tools/Scenarios/Export-SteamEvidence.ps1').write_text(MOCK_EXPORT, encoding='utf-8')
        (self.kit / 'Tools/Scenarios/Start-SteamDiagnostics.ps1').write_text(MOCK_LAUNCH, encoding='utf-8')
        write(self.kit / 'package-reference.json', {'packageVerified': True, 'identity': {'synthetic': True}})
        self.release()

    def release(self):
        sha = hashlib.sha256((self.kit / 'package-reference.json').read_bytes()).hexdigest()
        write(self.kit / 'release.json', {'readyToRun': True, 'packageReferenceSha256': sha})
        for path in (self.kit / 'launch-configs').glob('*.json'):
            data = json.loads(path.read_text(encoding='utf-8-sig'))
            data['readyToRun'] = True
            write(path, data)

    def run_ps(self, command, success=True):
        environment = {key: value for key, value in os.environ.items() if key.lower() not in ('psmodulepath', 'pythonpath', 'pythonhome')}
        result = subprocess.run([str(PWSH), '-NoLogo', '-NoProfile', '-Command', "$ErrorActionPreference='Stop'; " + command], capture_output=True, text=True, encoding='utf-8', errors='replace', env=environment)
        if success:
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        else:
            self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        return result

    def start(self, mode='local', role='Host', stage='chain', output=None, success=True, observe=False):
        command = '& ' + quote(self.kit / 'Start-Case.ps1') + ' -PackageDirectory ' + quote(self.package) + ' -OutputRoot ' + quote(output or self.output) + f' -Role {role} -Mode {mode} -Stage {stage}'
        if observe:
            command += ' -ObserveEvidenceQueue'
        return self.run_ps(command, success)

    def case(self):
        self.start()
        return next(self.output.iterdir())

    def context_command(self, script, case):
        return '& ' + quote(self.kit / script) + ' -PackageDirectory ' + quote(self.package) + ' -CaseDirectory ' + quote(case)

    def test_all_scripts_parse(self):
        command = "$fail=@(); Get-ChildItem -LiteralPath " + quote(KIT) + " -Recurse -Filter '*.ps1' | ForEach-Object {$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$t,[ref]$e);$fail+=@($e)};if($fail.Count){throw ($fail | Out-String)}"
        self.run_ps(command)

    def test_disabled_release_prevents_launch(self):
        write(self.kit / 'release.json', {'readyToRun': False})
        self.start(success=False)
        self.assertFalse(self.output.exists())

    def test_reference_change_prevents_launch(self):
        write(self.kit / 'package-reference.json', {'packageVerified': True, 'changed': True})
        self.start(success=False)
        self.assertFalse(self.output.exists())

    def test_config_not_released_prevents_launch(self):
        path = self.kit / 'launch-configs/Host-local.json'
        config = json.loads(path.read_text())
        config['readyToRun'] = False
        write(path, config)
        self.start(success=False)
        self.assertFalse(self.output.exists())

    def test_output_inside_package_or_kit_rejected(self):
        for root in (self.package, self.kit):
            with self.subTest(root=root):
                self.start(output=root / 'new case', success=False)
                self.assertFalse((root / 'new case').exists())

    def test_six_mode_role_compositions_spaces_and_fresh_cases(self):
        for role in ('Host', 'Client'):
            for mode in ('off', 'local', 'replicated'):
                self.start(mode=mode, role=role, stage='comparison')
        cases = list(self.output.iterdir())
        self.assertEqual(6, len(cases))
        for case in cases:
            metadata = json.loads((case / 'case.json').read_text(encoding='utf-8-sig'))
            launch = json.loads((case / 'capture/mock-launch.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(metadata['mode'], launch['mode'])
            self.assertEqual(metadata['role'].lower(), launch['role'])
            self.assertEqual('Default', launch['graphics'])
            self.assertEqual(0, launch['profileSeconds'])
            self.assertEqual(str(self.package / 'Fake Game.exe'), launch['executable'])

    def test_chain_off_is_rejected(self):
        self.start(mode='off', success=False)
        self.assertFalse(self.output.exists())

    def test_package_preflight_failure_does_not_launch(self):
        (self.package / 'reject-package').touch()
        self.start(success=False)
        self.assertEqual([], list(self.output.glob('*/capture')))

    def test_verify_composes_capture_pid_and_expected_reference(self):
        case = self.case()
        self.run_ps(self.context_command('Verify-Running.ps1', case))
        call = json.loads(next(case.glob('runtime-check-*/mock-call.json')).read_text(encoding='utf-8-sig'))
        self.assertEqual(987654, call['pid'])
        self.assertEqual(str(case / 'capture/capture.json'), str(Path(call['capture'])))
        self.assertEqual(str(self.kit / 'package-reference.json'), str(Path(call['reference'])))

    def test_verify_rejects_inconsistent_capture_mode(self):
        case = self.case()
        capture = case / 'capture/capture.json'
        value = json.loads(capture.read_text(encoding='utf-8-sig'))
        value['requestedEvidenceMode'] = 'replicated'
        write(capture, value)
        self.run_ps(self.context_command('Verify-Running.ps1', case), False)

    def test_export_rejects_unfinalized_capture(self):
        case = self.case()
        capture = case / 'capture/capture.json'
        value = json.loads(capture.read_text(encoding='utf-8-sig'))
        value['complete'] = False
        write(capture, value)
        self.run_ps(self.context_command('Export-Case.ps1', case), False)

    def test_export_rejects_running_process(self):
        case = self.case()
        mock = 'function Get-CimInstance { [pscustomobject]@{ProcessId=987654} }; '
        self.run_ps(mock + self.context_command('Export-Case.ps1', case), False)

    def test_export_rejects_unknown_process_query(self):
        case = self.case()
        self.run_ps("function Get-CimInstance { throw 'synthetic access denied' }; " + self.context_command('Export-Case.ps1', case), False)

    def test_export_preserves_abnormal_finalized_case_and_selects_whole_folder(self):
        case = self.case()
        self.run_ps('function Get-CimInstance { }; ' + self.context_command('Export-Case.ps1', case))
        exports = list(self.output.glob('*-export-*'))
        self.assertEqual(1, len(exports))
        call = json.loads((exports[0] / 'mock-call.json').read_text(encoding='utf-8-sig'))
        self.assertEqual([str(case)], call['logs'])
        self.assertEqual(0, call['pid'])
        self.assertFalse(json.loads((case / 'capture/capture.json').read_text(encoding='utf-8-sig'))['normalExit'])

    def test_queue_observation_is_disabled_by_default(self):
        self.start(role='Client')
        case = next(self.output.iterdir())
        metadata = json.loads((case / 'case.json').read_text(encoding='utf-8-sig'))
        launch = json.loads((case / 'capture/mock-launch.json').read_text(encoding='utf-8-sig'))
        self.assertIs(False, metadata['requestedQueueObservation'])
        self.assertIs(False, launch['observeEvidenceQueue'])
        self.assertEqual(0, launch['profileSeconds'])

    def test_explicit_queue_observation_requires_local_chain(self):
        for role, mode, stage in [('Host', 'replicated', 'chain'), ('Client', 'replicated', 'chain'), ('Client', 'off', 'comparison'), ('Client', 'local', 'comparison')]:
            with self.subTest(role=role, mode=mode, stage=stage):
                self.start(role=role, mode=mode, stage=stage, observe=True, success=False)
        self.assertFalse(self.output.exists())
        for role in ('Host', 'Client'):
            self.start(role=role, observe=True)
        for case in self.output.iterdir():
            metadata = json.loads((case / 'case.json').read_text(encoding='utf-8-sig'))
            launch = json.loads((case / 'capture/mock-launch.json').read_text(encoding='utf-8-sig'))
            self.assertIs(True, metadata['requestedQueueObservation'])
            self.assertIs(True, launch['observeEvidenceQueue'])
            self.assertEqual(0, launch['profileSeconds'])

    def test_simplified_client_start_uses_adjacent_paths_and_explicit_observation(self):
        package = self.root / 'product'
        package.mkdir()
        (package / 'Fake Game.exe').write_bytes(b'never executed')
        self.run_ps('& ' + quote(self.kit / 'Client-Local.ps1'))
        case = next((self.root / 'logs').iterdir())
        launch = json.loads((case / 'capture/mock-launch.json').read_text(encoding='utf-8-sig'))
        self.assertEqual(str(package / 'Fake Game.exe'), launch['executable'])
        self.assertEqual('client', launch['role'])
        self.assertEqual('local', launch['mode'])
        self.assertIs(True, launch['observeEvidenceQueue'])
        self.assertEqual(0, launch['profileSeconds'])

    def test_simplified_client_start_preserves_release_gate(self):
        write(self.kit / 'release.json', {'readyToRun': False})
        result = self.run_ps('& ' + quote(self.kit / 'Client-Local.ps1') + ' -ObserveEvidenceQueue', success=False)
        self.assertIn('This kit has not been released', result.stderr)
        self.assertFalse((self.root / 'logs').exists())

    def test_both_shortcuts_default_diagnostic_and_publish_current_case(self):
        for role in ('Host', 'Client'):
            self.run_ps('& ' + quote(self.kit / f'{role}-Local.ps1') + ' -PackageDirectory ' + quote(self.package))
            current = json.loads((self.root / 'current-case.json').read_text(encoding='utf-8-sig'))
            case = Path(current['caseDirectory'])
            capture = json.loads((case / 'capture/capture.json').read_text(encoding='utf-8-sig'))
            self.assertEqual('diagnostic', capture['requestedEvidenceProfile'])
            self.assertEqual(512 << 20, capture['requestedEvidenceConfiguration']['queueBytes'])
            self.run_ps('& ' + quote(self.kit / 'Verify-Running.ps1'))
            configuration = json.loads(next(case.glob('runtime-check-*/evidence-configuration.json')).read_text(encoding='utf-8-sig'))
            self.assertTrue(configuration['verified'])
            self.assertEqual('diagnostic', configuration['applied']['profile'])
            self.run_ps('function Get-CimInstance { }; & ' + quote(self.kit / 'Export-Case.ps1'))

    def test_shortcut_standard_override_preserves_requested_configuration(self):
        self.run_ps('& ' + quote(self.kit / 'Host-Local.ps1') + ' -PackageDirectory ' + quote(self.package) + ' -EvidenceProfile Standard')
        current = json.loads((self.root / 'current-case.json').read_text(encoding='utf-8-sig'))
        case = Path(current['caseDirectory'])
        self.run_ps('& ' + quote(self.kit / 'Verify-Running.ps1'))
        configuration = json.loads(next(case.glob('runtime-check-*/evidence-configuration.json')).read_text(encoding='utf-8-sig'))
        self.assertEqual('standard', configuration['applied']['profile'])
        self.assertEqual(32 << 20, configuration['applied']['queueBytes'])

    def test_verify_requires_actual_configuration_even_if_capture_claims_verified(self):
        case = self.case()
        record_path = next(case.glob('capture/CombatDiagnostics/boot/0/sources/*/events-*.jsonl'))
        record = json.loads(record_path.read_text(encoding='utf-8-sig'))
        record['input']['evidenceConfiguration']['queueBytes'] = 1
        write(record_path, record)
        capture_path = case / 'capture/capture.json'
        capture = json.loads(capture_path.read_text(encoding='utf-8-sig'))
        capture['evidenceConfigurationVerified'] = True
        write(capture_path, capture)
        self.run_ps(self.context_command('Verify-Running.ps1', case), False)
        configuration = json.loads(next(case.glob('runtime-check-*/evidence-configuration.json')).read_text(encoding='utf-8-sig'))
        self.assertFalse(configuration['verified'])

    def test_failed_new_launch_invalidates_old_current_case(self):
        self.case()
        (self.package / 'reject-package').touch()
        self.start(success=False)
        current = json.loads((self.root / 'current-case.json').read_text(encoding='utf-8-sig'))
        self.assertIsNone(current['caseDirectory'])
        self.run_ps('& ' + quote(self.kit / 'Verify-Running.ps1'), False)

    def test_verify_rejects_case_profile_mismatch(self):
        case = self.case()
        path = case / 'case.json'
        metadata = json.loads(path.read_text(encoding='utf-8-sig'))
        metadata['requestedEvidenceProfile'] = 'diagnostic'
        write(path, metadata)
        self.run_ps(self.context_command('Verify-Running.ps1', case), False)

    def test_verify_reads_hashed_gzip_block_and_external_startup_metadata(self):
        case = self.case()
        path = next(case.glob('capture/CombatDiagnostics/boot/0/sources/*/events-*.jsonl'))
        record = json.loads(path.read_text(encoding='utf-8-sig'))
        raw = json.dumps(record.pop('input')).encode('utf-8')
        name = hashlib.sha256(raw).hexdigest() + '.json.gz'
        payload = path.parent / 'inputs' / name
        payload.parent.mkdir()
        payload.write_bytes(gzip.compress(raw))
        record['inputRef'] = 'inputs/' + name
        encoded = (json.dumps(record) + '\n').encode('utf-8')
        write(path, dict(schemaVersion=2, count=1, encoding='gzip-jsonl-v1', hash=hashlib.sha256(encoded).hexdigest(),
                         data=base64.b64encode(gzip.compress(encoded)).decode('ascii')))
        self.run_ps(self.context_command('Verify-Running.ps1', case))
        configuration = json.loads(next(case.glob('runtime-check-*/evidence-configuration.json')).read_text(encoding='utf-8-sig'))
        self.assertTrue(configuration['verified'])
        self.assertEqual(payload, Path(configuration['startupPayloadFile']))
        payload.write_bytes(gzip.compress(raw + b' '))
        result = self.run_ps(self.context_command('Verify-Running.ps1', case), False)
        self.assertIn('checksum mismatch', result.stderr)

    def test_verify_rejects_runtime_process_or_build_mismatch(self):
        for field, value in (('processId', 1), ('buildGuid', '2' * 32), ('captureId', 'other-capture'),
                             ('executablePath', 'C:/old/Player.exe')):
            with self.subTest(field=field):
                self.start()
                current = json.loads((self.root / 'current-case.json').read_text(encoding='utf-8-sig'))
                case = Path(current['caseDirectory'])
                path = next(case.glob('capture/CombatDiagnostics/boot/0/sources/*/events-*.jsonl'))
                record = json.loads(path.read_text(encoding='utf-8-sig'))
                record['input'][field] = value
                write(path, record)
                self.run_ps(self.context_command('Verify-Running.ps1', case), False)

    def test_verify_rejects_missing_and_stale_startup_record(self):
        case = self.case()
        path = next(case.glob('capture/CombatDiagnostics/boot/0/sources/*/events-*.jsonl'))
        record = json.loads(path.read_text(encoding='utf-8-sig'))
        record['utc'] = '2020-01-01T00:00:00Z'
        write(path, record)
        self.run_ps(self.context_command('Verify-Running.ps1', case), False)
        path.unlink()
        self.run_ps(self.context_command('Verify-Running.ps1', case), False)

if __name__ == '__main__':
    unittest.main(verbosity=2)
