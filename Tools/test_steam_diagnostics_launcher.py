"""Steam launcher contracts using synthetic packages and process stubs only."""
import base64
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).parent / "Scenarios/Start-SteamDiagnostics.ps1"
POWERSHELL = os.environ.get("PWSH_EXE") or shutil.which("powershell") or shutil.which("pwsh")


@unittest.skipUnless(POWERSHELL, "PowerShell is required")
class LauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="steam launcher ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.package = self.root / "synthetic package"
        (self.package / "Player_Data/Managed").mkdir(parents=True)
        (self.package / "D3D12").mkdir()
        (self.package / "Player.exe").write_bytes(b"never executable")
        (self.package / "UnityPlayer.dll").write_bytes(b"synthetic runtime")
        (self.package / "Player_Data/Managed/MonsterSupergroup.dll").write_bytes(b"synthetic code")

    def run_launcher(self, output, mode=None, attach=False, prepare=True, observe=False, profile=None):
        values = dict(Executable=str(self.package / "Player.exe"), ArtifactDirectory=str(output),
                      Graphics="Default", PrepareOnly=prepare)
        if mode is not None:
            values["EvidenceMode"] = mode
        if profile is not None:
            values["EvidenceProfile"] = profile
        if attach:
            values["AttachProcessId"] = 321
        if observe:
            values["ObserveEvidenceQueue"] = True
        payload = base64.b64encode(json.dumps(values).encode("utf-8")).decode("ascii")
        script = str(SCRIPT.resolve()).replace("'", "''")
        # Stub every process-start path. The file on disk is intentionally not executable.
        code = f"""
$ErrorActionPreference = 'Stop'
$fixture = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{payload}')) | ConvertFrom-Json
$parameters = @{{}}; $fixture.PSObject.Properties | ForEach-Object {{ $parameters[$_.Name] = $_.Value }}
$global:launcherTestLaunches = 0
function Get-Process {{ param($Id) [pscustomobject]@{{Path=$fixture.Executable;Id=$Id}} }}
function Get-CimInstance {{ param($ClassName,$Filter)
    if ($ClassName -eq 'Win32_Process') {{ [pscustomobject]@{{CommandLine='synthetic process only'}} }}
}}
function Start-Process {{
    param($FilePath,$WorkingDirectory,$ArgumentList,$WindowStyle,[switch]$PassThru)
    $global:launcherTestLaunches++
    if ($fixture.PrepareOnly) {{ throw 'PrepareOnly attempted to start a process' }}
    $fake = [pscustomobject]@{{Id=321;Handle=0;StartTime=(Get-Date);ExitTime=(Get-Date);ExitCode=0}}
    $fake | Add-Member ScriptMethod WaitForExit {{ param($milliseconds) return $true }}
    return $fake
}}
function Start-Sleep {{ param($Seconds) }}
function Get-WinEvent {{ param($FilterHashtable) }}
try {{ & '{script}' @parameters }}
catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}
Write-Output ('STUB_LAUNCHES=' + $global:launcherTestLaunches)
"""
        encoded = base64.b64encode(code.encode("utf-16-le")).decode("ascii")
        # A Python child bypasses PowerShell's cross-version PSModulePath normalization.
        environment = {key: value for key, value in os.environ.items() if key.lower() not in ("psmodulepath", "pythonpath", "pythonhome")}
        result = subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, errors="replace", timeout=30,
                                env=environment)
        capture = output / "capture.json"
        return result, json.loads(capture.read_text(encoding="utf-8-sig")) if capture.exists() else None

    def assert_prepared(self, result, capture):
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("STUB_LAUNCHES=0", result.stdout)
        self.assertFalse(capture["launcherArgumentsApplied"])
        self.assertIsNone(capture["processId"])
        self.assertFalse(capture["complete"])

    def test_off_explicitly_disables_evidence_and_keeps_network_trends(self):
        output = self.root / "off"
        result, capture = self.run_launcher(output, "off")
        self.assert_prepared(result, capture)
        self.assertIn("--no-combat-evidence", capture["arguments"])
        self.assertNotIn("--combat-evidence", capture["arguments"])
        self.assertEqual("off", capture["requestedEvidenceMode"])
        self.assertIsNone(capture["requestedEvidenceOutput"])
        self.assertIn('"--network-diagnostics-output=' + str(output / "metrics") + '"', capture["arguments"])

    def test_local_explicitly_disables_replication_and_isolates_evidence(self):
        output = self.root / "local"
        result, capture = self.run_launcher(output, "local")
        self.assert_prepared(result, capture)
        self.assertIn("--combat-evidence-local-only", capture["arguments"])
        self.assertNotIn("--combat-evidence", capture["arguments"])
        self.assertEqual("local", capture["requestedEvidenceMode"])
        self.assertEqual(str(output / "CombatDiagnostics"), capture["requestedEvidenceOutput"])

    def test_replicated_explicitly_enables_evidence(self):
        output = self.root / "replicated"
        result, capture = self.run_launcher(output, "replicated")
        self.assert_prepared(result, capture)
        self.assertIn("--combat-evidence", capture["arguments"])
        self.assertNotIn("--combat-evidence-local-only", capture["arguments"])
        self.assertEqual("replicated", capture["requestedEvidenceMode"])

    def test_default_keeps_legacy_arguments_and_does_not_claim_a_mode(self):
        for mode in (None, "Default"):
            with self.subTest(mode=mode):
                output = self.root / ("omitted" if mode is None else "explicit default")
                result, capture = self.run_launcher(output, mode)
                self.assert_prepared(result, capture)
                self.assertEqual(["-timestamps", "-logFile", '"' + str(output / "Player.log") + '"',
                                  "--network-diagnostics", '"--network-diagnostics-output=' + str(output / "metrics") + '"'],
                                 capture["arguments"])
                self.assertEqual("Default", capture["requestedEvidenceMode"])
                self.assertIsNone(capture["requestedEvidenceOutput"])

    def test_paths_with_spaces_are_single_quoted_argument_tokens(self):
        output = self.root / "session with spaces"
        result, capture = self.run_launcher(output, "local")
        self.assert_prepared(result, capture)
        self.assertIn('"' + str(output / "Player.log") + '"', capture["arguments"])
        self.assertIn('"--combat-evidence-output=' + str(output / "CombatDiagnostics") + '"', capture["arguments"])
        self.assertEqual(str(output / "Player.log"), capture["requestedPlayerLog"])
        self.assertEqual(str(output / "metrics"), capture["requestedNetworkOutput"])

    def test_existing_output_is_rejected_and_left_intact(self):
        output = self.root / "existing"
        output.mkdir()
        (output / "keep.txt").write_text("existing evidence")
        result, capture = self.run_launcher(output, "local")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("new artifact directory", result.stderr)
        self.assertIsNone(capture)
        self.assertEqual(["keep.txt"], [item.name for item in output.iterdir()])
        self.assertEqual("existing evidence", (output / "keep.txt").read_text())

    def test_attach_rejects_each_explicit_mode_before_creating_output(self):
        for mode in ("off", "local", "replicated"):
            with self.subTest(mode=mode):
                output = self.root / ("attach " + mode)
                result, capture = self.run_launcher(output, mode, attach=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("AttachProcessId cannot apply EvidenceMode", result.stderr)
                self.assertFalse(output.exists())
                self.assertIsNone(capture)

    def test_default_attach_remains_available_without_claiming_applied_arguments(self):
        result, capture = self.run_launcher(self.root / "attach default", attach=True)
        self.assert_prepared(result, capture)
        self.assertTrue(capture["attached"])
        self.assertTrue(any("not applied" in note for note in capture["notes"]))

    def test_default_profile_zero_adds_no_high_frequency_observation(self):
        for mode in ("off", "local", "replicated"):
            with self.subTest(mode=mode):
                result, capture = self.run_launcher(self.root / ("no profile " + mode), mode)
                self.assert_prepared(result, capture)
                arguments = " ".join(capture["arguments"]).lower()
                self.assertNotIn("profiler", arguments)
                self.assertNotIn("observe", arguments)

    def test_recording_without_standalone_metrics_requires_embedded_trend_audit(self):
        result, capture = self.run_launcher(self.root / "fake local run", "local", prepare=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("STUB_LAUNCHES=1", result.stdout)
        self.assertTrue(capture["launcherArgumentsApplied"])
        self.assertTrue(capture["complete"])
        self.assertFalse(any("No diagnostic samples" in error for error in capture["errors"]))
        self.assertTrue(any("CombatDiagnostics" in note and "unknown" in note for note in capture["notes"]))

    def test_off_without_standalone_metrics_still_reports_missing_samples(self):
        result, capture = self.run_launcher(self.root / "fake off run", "off", prepare=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(capture["complete"])
        self.assertTrue(any("No diagnostic samples" in error for error in capture["errors"]))

    def test_explicit_writer_observation_is_prepared_without_starting_a_game(self):
        for mode in ("local", "replicated"):
            with self.subTest(mode=mode):
                output = self.root / ("writer trace " + mode)
                result, capture = self.run_launcher(output, mode, observe=True)
                self.assert_prepared(result, capture)
                self.assertIn("--combat-evidence-observe-queue", capture["arguments"])
                self.assertTrue(capture["requestedQueueObservation"])
                self.assertFalse(capture["queueObservationArgumentsApplied"])
                self.assertEqual(str(output / "CombatDiagnostics"), capture["requestedQueueObservationDirectory"])
                self.assertNotIn("profiler", " ".join(capture["arguments"]))

    def test_writer_observation_rejects_off_or_ambiguous_default_before_output(self):
        for mode in ("off", "Default"):
            with self.subTest(mode=mode):
                output = self.root / ("reject writer " + mode)
                result, capture = self.run_launcher(output, mode, observe=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("ObserveEvidenceQueue requires", result.stderr)
                self.assertFalse(output.exists())
                self.assertIsNone(capture)

    def test_writer_observation_cannot_be_applied_to_an_attached_process(self):
        output = self.root / "attached writer"
        result, capture = self.run_launcher(output, attach=True, observe=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("AttachProcessId cannot apply ObserveEvidenceQueue", result.stderr)
        self.assertFalse(output.exists())
        self.assertIsNone(capture)

    def test_writer_arguments_applied_does_not_claim_an_observation_was_exported(self):
        result, capture = self.run_launcher(self.root / "stub writer run", "local", prepare=False, observe=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(capture["queueObservationArgumentsApplied"])
        self.assertTrue(capture["complete"])
        self.assertTrue(any("writer" in note.lower() and "unknown" in note.lower() for note in capture["notes"]))

    def test_existing_writer_observation_output_is_preserved(self):
        output = self.root / "previous trace"
        output.mkdir()
        (output / "writer-observation.json").write_text("preserve original")
        result, capture = self.run_launcher(output, "local", observe=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("new artifact directory", result.stderr)
        self.assertEqual("preserve original", (output / "writer-observation.json").read_text())

    def test_diagnostic_profile_requests_exact_configuration_without_claiming_application(self):
        result, capture = self.run_launcher(self.root / "diagnostic", "local", profile="Diagnostic")
        self.assert_prepared(result, capture)
        self.assertIn("--combat-evidence-profile=diagnostic", capture["arguments"])
        self.assertEqual(dict(profile="diagnostic", queueBytes=512 << 20, reservedBytes=4 << 20,
                              memoryBudgetBytes=768 << 20, windowMilliseconds=1000, drainPolicy="WaitForCompletion"),
                         capture["requestedEvidenceConfiguration"])
        self.assertFalse(capture["evidenceProfileArgumentsApplied"])
        self.assertFalse(capture["evidenceConfigurationVerified"])
        self.assertIsNone(capture["appliedEvidenceConfiguration"])

    def test_standard_is_default_for_explicit_evidence_modes(self):
        result, capture = self.run_launcher(self.root / "standard", "local")
        self.assert_prepared(result, capture)
        self.assertIn("--combat-evidence-profile=standard", capture["arguments"])
        self.assertEqual(32 << 20, capture["requestedEvidenceConfiguration"]["queueBytes"])
        self.assertEqual("Bounded30Seconds", capture["requestedEvidenceConfiguration"]["drainPolicy"])

    def test_diagnostic_rejects_disabled_ambiguous_or_attached_capture(self):
        for mode, attach in (("off", False), ("Default", False), (None, True)):
            with self.subTest(mode=mode, attach=attach):
                output = self.root / (str(mode) + str(attach))
                result, capture = self.run_launcher(output, mode, attach=attach, profile="Diagnostic")
                self.assertNotEqual(0, result.returncode)
                self.assertIsNone(capture)
                self.assertFalse(output.exists())

    def test_applied_arguments_without_startup_record_remain_unverified(self):
        result, capture = self.run_launcher(self.root / "no actual configuration", "local", prepare=False, profile="Diagnostic")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(capture["evidenceProfileArgumentsApplied"])
        self.assertFalse(capture["evidenceConfigurationVerified"])
        self.assertIsNone(capture["appliedEvidenceConfiguration"])
        self.assertTrue(capture["evidenceConfigurationError"])


if __name__ == "__main__":
    unittest.main()
