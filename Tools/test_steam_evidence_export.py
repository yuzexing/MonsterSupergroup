"""PowerShell collector contract tests, using synthetic files, never a Player."""
import json
import base64
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).parent / "Scenarios/Export-SteamEvidence.ps1"
POWERSHELL = shutil.which("powershell") or shutil.which("pwsh")


@unittest.skipUnless(POWERSHELL, "PowerShell is required")
class ExportTests(unittest.TestCase):
    def package(self, root):
        package = root / "package"
        data = package / "Player_Data/StreamingAssets"
        data.mkdir(parents=True)
        (package / "Player.exe").write_bytes(b"synthetic identity fixture, never executable")
        (package / "UnityPlayer.dll").write_bytes(b"synthetic runtime")
        (package / "steam_appid.txt").write_text("4886160")
        info = dict(buildId="test-id", profile="product", kind="test", network="Steam", distribution="Direct",
                    diagnostics="Evidence", development=False, evidence=True, testAssemblies=False, developmentTools=False)
        encoded = json.dumps(info)
        (data / "BuildInfo.json").write_text(encoded)
        (package / "build-complete.json").write_text(encoded)
        manifest = dict(buildId=info["buildId"], protocol="6", sourceConfigurationHash="a" * 64,
                        logFormat=2, replicationProtocol=2, replayFormat=2)
        (package / "combat-build.json").write_text(json.dumps(dict(buildId=info["buildId"], buildGuid="1" * 32,
                                                                     manifest=json.dumps(manifest), result="Unknown")))
        return package

    def run_export(self, package, output, *extra, shell=POWERSHELL):
        result = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT),
                                 "-PackageDirectory", str(package), "-ArtifactDirectory", str(output), "-Role", "Host", "-Mode", "local", *map(str, extra)],
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
        proof = output / "machine-proof.json"
        return result, json.loads(proof.read_text(encoding="utf-8-sig")) if proof.exists() else None

    def test_identity_and_selected_logs_are_verified_without_claiming_runtime(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); package = self.package(root)
            logs = root / "selected logs"; logs.mkdir(); (logs / "Player.log").write_text("user-selected evidence")
            result, proof = self.run_export(package, root / "artifacts", "-LogPath", logs)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertTrue(proof["packageVerified"])
            self.assertTrue(proof["logArchiveComplete"])
            self.assertFalse(proof["runtime"]["attributionVerified"])
            self.assertEqual("user-selected evidence", (root / "artifacts/logs/000-selected logs/Player.log").read_text())
            self.assertEqual("6", proof["identity"]["protocol"])

    def test_reference_matches_copied_package_and_rejects_changed_binary(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); package = self.package(root)
            first, proof = self.run_export(package, root / "host")
            self.assertEqual(0, first.returncode, first.stdout)
            copied = root / "copied package"; shutil.copytree(package, copied)
            result, proof = self.run_export(copied, root / "client", "-ExpectedProof", root / "host/machine-proof.json")
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertTrue(proof["packageMatchesExpected"])
            (copied / "UnityPlayer.dll").write_bytes(b"different runtime")
            result, proof = self.run_export(copied, root / "mismatch", "-ExpectedProof", root / "host/machine-proof.json")
            self.assertNotEqual(0, result.returncode)
            self.assertFalse(proof["packageMatchesExpected"])

    def process_identity(self, record):
        module = (SCRIPT.parent.parent / "SteamEvidenceIdentity.psm1").resolve()
        # JSON travels as UTF-8 base64, never as executable PowerShell text.
        payload = base64.b64encode(json.dumps(record).encode("utf-8")).decode("ascii")
        code = f"""
$ErrorActionPreference = 'Stop'
Import-Module '{str(module).replace("'", "''")}'
$mock = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{payload}')) | ConvertFrom-Json
$reader = {{ param($requestedId) if ($requestedId -ne 321) {{ throw 'Unexpected query PID' }}; $mock }}.GetNewClosure()
try {{ Get-SteamEvidenceRuntimeIdentity -ExpectedExecutable 'C:/fixture/Player.exe' -GameProcessId 321 -ProcessReader $reader | ConvertTo-Json -Compress }}
catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}
"""
        encoded = base64.b64encode(code.encode("utf-16-le")).decode("ascii")
        return subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, errors="replace")

    def test_explicit_process_identity_accepts_only_matching_executable_and_creation(self):
        result = self.process_identity(dict(ProcessId=321, ExecutablePath="C:/fixture/Player.exe", CreationDate="2026-09-23T05:00:00Z"))
        self.assertEqual(0, result.returncode, result.stderr)
        proof = json.loads(result.stdout)
        self.assertTrue(proof["attributionVerified"])
        self.assertEqual(321, proof["processId"])
        self.assertNotIn("commandLine", proof)

    def test_missing_pid_wrong_executable_and_incomplete_process_identity_are_rejected(self):
        for record in (None, dict(ProcessId=321, ExecutablePath="C:/old/Player.exe", CreationDate="2026-09-23T05:00:00Z"),
                       dict(ProcessId=321, ExecutablePath=None, CreationDate="2026-09-23T05:00:00Z"),
                       dict(ProcessId=322, ExecutablePath="C:/fixture/Player.exe", CreationDate="2026-09-23T05:00:00Z"),
                       dict(ProcessId=321, ExecutablePath="C:/fixture/Player.exe", CreationDate=None)):
            with self.subTest(record=record):
                result = self.process_identity(record)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", result.stdout.strip())

    def test_invalid_manifest_and_empty_selected_logs_fail(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); package = self.package(root)
            (package / "build-complete.json").write_text("{}")
            result, proof = self.run_export(package, root / "bad-package")
            self.assertNotEqual(0, result.returncode)
            self.assertFalse(proof["packageVerified"])
            (package / "build-complete.json").write_bytes((package / "Player_Data/StreamingAssets/BuildInfo.json").read_bytes())
            empty = root / "empty"; empty.mkdir()
            result, proof = self.run_export(package, root / "empty-logs", "-LogPath", empty)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse(proof["logArchiveComplete"])

    def test_output_inside_package_is_rejected_without_mutation(self):
        with tempfile.TemporaryDirectory() as folder:
            package = self.package(Path(folder))
            result, proof = self.run_export(package, package / "output")
            self.assertNotEqual(0, result.returncode)
            self.assertIsNone(proof)
            self.assertFalse((package / "output").exists())

    def test_existing_capture_manifest_attribution_remains_supported(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); package = self.package(root)
            capture = root / "capture.json"
            record = dict(processId=321, startedUtc="2026-09-23T05:00:00Z", actualCommandLine='"Player.exe" --combat-evidence',
                          executable=str(package / "Player.exe"), normalExit=True)
            capture.write_text(json.dumps(record))
            result, proof = self.run_export(package, root / "valid-capture", "-CaptureManifest", capture)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertTrue(proof["runtime"]["attributionVerified"])
            record["executable"] = str(root / "old/Player.exe")
            capture.write_text(json.dumps(record))
            result, proof = self.run_export(package, root / "wrong-capture", "-CaptureManifest", capture)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse(proof["runtime"]["attributionVerified"])

    @unittest.skipUnless(shutil.which("powershell") and shutil.which("pwsh"), "Both PowerShell versions are required")
    def test_machine_identity_matches_between_windows_powershell_and_pwsh(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); package = self.package(root)
            (package / "中文资源.txt").write_text("same package", encoding="utf-8")
            first, _ = self.run_export(package, root / "host", shell=shutil.which("powershell"))
            self.assertEqual(0, first.returncode, first.stdout)
            result, proof = self.run_export(package, root / "client", "-ExpectedProof", root / "host/machine-proof.json", shell=shutil.which("pwsh"))
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertTrue(proof["packageMatchesExpected"])


if __name__ == "__main__": unittest.main()
