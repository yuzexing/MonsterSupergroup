import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import CombatEvidenceValidation as validation


class SnapshotTests(unittest.TestCase):
    def fixture(self, root):
        source = root / "source"
        for name in validation.PROJECT_ROOTS: (source / name).mkdir(parents=True)
        (source / "Assets/Test.cs").write_text("new source", encoding="utf-8")
        (source / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.21f1", encoding="utf-8")
        unity = root / "Unity.exe"; unity.write_bytes(b"test executable fingerprint only")
        def git(*args): subprocess.run(["git", "-C", str(source), *args], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        git("init"); git("add", ".")
        git("-c", "user.name=Snapshot Test", "-c", "user.email=test@example.invalid", "commit", "-m", "fixture")
        return source, root / "project", root / "evidence", unity

    def test_physical_snapshot_overrides_and_stale_files_are_preserved_with_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); source, project, output, unity = self.fixture(root)
            (project / "Assets").mkdir(parents=True)
            (project / "Assets/Stale.cs").write_text("old unused source")
            overlay = root / "old"; overlay.mkdir(); (overlay / "Test.cs").write_text("old tested source")
            validation.prepare(source, project, output, unity, overlay)
            self.assertEqual("new source", (source / "Assets/Test.cs").read_text())
            self.assertEqual("old tested source", (project / "Assets/Test.cs").read_text())
            self.assertTrue((output / "stale-validation-inputs/Assets/Stale.cs").exists())
            sync = json.loads((output / "sync.json").read_text())
            self.assertNotEqual(sync["sourceOverrides"][0]["sourceSha256"], sync["sourceOverrides"][0]["overrideSha256"])
            result = validation.finish(output)
            self.assertTrue(result["sourceUnchanged"])
            self.assertTrue(result["validationInputsUnchanged"])
            self.assertTrue((output / "artifact-manifest.json").exists())
            self.assertTrue((output / "tested-code-and-config.zip").exists())

    def test_changes_in_workspace_and_frozen_snapshot_are_distinguished(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            validation.prepare(source, project, output, unity)
            (source / "Assets/Test.cs").write_text("parallel edit")
            result = validation.finish(output)
            self.assertFalse(result["sourceUnchanged"])
            self.assertTrue(result["validationInputsUnchanged"])
            (project / "Assets/Test.cs").write_text("unexpected test mutation")
            self.assertFalse(validation.finish(output)["validationInputsUnchanged"])

    def test_snapshot_refuses_source_subdirectory_and_shared_hardlink(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            with self.assertRaises(ValueError): validation.prepare(source, source / "Assets/clone", output, unity)
            (project / "Assets").mkdir(parents=True)
            os.link(source / "Assets/Test.cs", project / "Assets/Test.cs")
            with self.assertRaisesRegex(ValueError, "Hardlinked"):
                validation.prepare(source, project, output, unity)
            self.assertEqual("new source", (source / "Assets/Test.cs").read_text())

    def test_real_input_audit_requires_original_record_identity_and_never_claims_fault_replay(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            original = root / "events.jsonl"
            original.write_text('{"captureId":"capture","recordSequence":"10"}\n{"captureId":"capture","recordSequence":"20"}\n')
            fixture = root / "fixture.json"
            fixture.write_text(json.dumps(dict(source="capture", engine="gateway", version=2, complete=True, steps=[{}])))
            entry = dict(file=str(fixture), sha256=validation.digest(fixture), capture="capture", engine="gateway", steps=1,
                         firstCheckpoint="10", lastSequence="20", originalReferences=[dict(path=str(original), line=1), dict(path=str(original), line=2)])
            provenance = root / "provenance.json"; provenance.write_text(json.dumps([entry]))
            result = validation.audit_fixtures(provenance, root / "audit.json")
            self.assertTrue(result["allProvenanceVerified"])
            self.assertFalse(result["fixtures"][0]["businessFaultReproduced"])
            self.assertFalse(result["fixtures"][0]["businessFaultFixed"])
            original.write_text('{"captureId":"different","recordSequence":"10"}\n{"captureId":"capture","recordSequence":"20"}\n')
            self.assertFalse(validation.audit_fixtures(provenance, root / "audit.json")["allProvenanceVerified"])


if __name__ == "__main__": unittest.main()
