import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile
from unittest import mock

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

    def tool_fixture(self, source):
        inputs = {
            "Tools/CombatEvidenceValidation.py": Path(validation.__file__).read_text(encoding="utf-8"),
            "Tools/Invoke-CombatEvidenceValidation.ps1": "# validation launcher\n",
            "Tools/Invoke-CombatEvidenceBenchmark.ps1": "# benchmark launcher\n",
            "Tools/CompareCombatEvidence.py": "# comparison tool\n",
            "Tools/CombatEvidence.py": "# evidence tool\n",
            "Tools/test_combat_evidence_integrity.py": "MAGIC = 'archived helper'\n",
            "Tools/test_combat_evidence_added.py": (
                "import json\nfrom pathlib import Path\nimport unittest\n"
                "import test_combat_evidence_integrity as helper\n"
                "class PortableArchiveTest(unittest.TestCase):\n"
                "    def test_archived_dependencies(self):\n"
                "        self.assertEqual('archived helper', helper.MAGIC)\n"
                "        path = Path(__file__).parent / 'fixtures/combat-evidence-v2/nested/payload.json'\n"
                "        self.assertEqual({'golden': True}, json.loads(path.read_text()))\n"),
            "Tools/test_compare_combat_evidence_added.py": "# another matching test module\n",
            "Tools/fixtures/combat-evidence-v2/manifest.json": '{"nested": "nested/payload.json"}\n',
            "Tools/fixtures/combat-evidence-v2/nested/payload.json": '{"golden": true}\n',
            "Tools/fixtures/combat-evidence-v2/nested/payload.bytes": "opaque golden data\n",
        }
        for name, body in inputs.items():
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(body, encoding="utf-8")
        return inputs

    def finish_cli(self, output):
        return subprocess.run([sys.executable, "-B", str(Path(validation.__file__).resolve()),
                               "finish", "--output", str(output)],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def test_dynamic_tests_helpers_and_golden_files_are_frozen_and_runnable(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            inputs = self.tool_fixture(source)
            subprocess.run(["git", "-C", str(source), "add", "Tools"], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(source), "-c", "user.name=Snapshot Test", "-c",
                            "user.email=test@example.invalid", "commit", "-m", "tools"],
                           check=True, capture_output=True)
            changed = ("Tools/test_combat_evidence_added.py", "Tools/test_combat_evidence_integrity.py",
                       "Tools/fixtures/combat-evidence-v2/nested/payload.json")
            for name in changed:
                with (source / name).open("a", encoding="utf-8") as stream: stream.write("\n")
            validation.prepare(source, project, output, unity)
            before = json.loads((output / "tool-before.json").read_text(encoding="utf-8"))
            self.assertEqual(sorted(inputs), [row["path"] for row in before["files"]])
            source_before = json.loads((output / "source-before.json").read_text(encoding="utf-8"))
            source_names = {row["path"] for row in source_before["files"]}
            self.assertTrue(set(inputs).issubset(source_names))
            for row in before["files"]:
                archived = output / "tool-sources" / row["path"]
                self.assertEqual((source / row["path"]).read_bytes(), archived.read_bytes())
                self.assertEqual(archived.stat().st_size, row["bytes"])
                self.assertEqual(validation.digest(archived), row["sha256"])
            patch = (output / "dirty.patch").read_text(encoding="utf-8")
            for name in changed: self.assertIn("+++ b/" + name, patch)
            env = dict(os.environ)
            env.pop("PYTHONPATH", None)
            execution = subprocess.run([sys.executable, "-B", "-m", "unittest", "discover",
                                        "-s", "Tools", "-p", "test_*combat_evidence*.py", "-v"],
                                       cwd=output / "tool-sources", env=env, capture_output=True, text=True)
            self.assertEqual(0, execution.returncode, execution.stdout + execution.stderr)
            self.assertIn("Ran 1 test", execution.stderr)
            result = validation.finish(output)
            self.assertTrue(result["toolInputsUnchanged"])
            self.assertEqual([], result["toolInputChanges"])
            self.assertEqual(before, json.loads((output / "tool-after.json").read_text(encoding="utf-8")))

    def test_tool_input_discovery_excludes_caches_logs_and_unrelated_files(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            inputs = self.tool_fixture(source)
            excluded = ("Tools/__pycache__/test_combat_evidence_added.pyc", "Tools/Logs/run.json",
                        "Tools/test_unrelated.py", "Tools/fixtures/other/input.json")
            for name in excluded:
                path = source / name; path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("not a validation input", encoding="utf-8")
            validation.prepare(source, project, output, unity)
            archived = {p.relative_to(output / "tool-sources").as_posix()
                        for p in (output / "tool-sources").rglob("*") if p.is_file()}
            self.assertEqual(set(inputs), archived)
            for name in excluded: (source / name).write_text("irrelevant change", encoding="utf-8")
            result = validation.finish(output)
            self.assertTrue(result["sourceUnchanged"])
            self.assertTrue(result["toolInputsUnchanged"])

    def test_workspace_tool_add_delete_edit_are_separate_from_frozen_input_changes(self):
        mutations = (("add", "Tools/test_combat_evidence_new_after_prepare.py"),
                     ("delete", "Tools/fixtures/combat-evidence-v2/nested/payload.json"),
                     ("edit", "Tools/test_combat_evidence_integrity.py"))
        for action, relative in mutations:
            with self.subTest(action=action), tempfile.TemporaryDirectory() as folder:
                source, project, output, unity = self.fixture(Path(folder))
                self.tool_fixture(source)
                validation.prepare(source, project, output, unity)
                if action == "delete": (source / relative).unlink()
                else: (source / relative).write_text("# parallel source edit\n", encoding="utf-8")
                result = validation.finish(output)
                self.assertFalse(result["sourceUnchanged"])
                self.assertEqual([relative], [row["path"] for row in result["sourceChanges"]])
                self.assertTrue(result["validationInputsUnchanged"])
                self.assertTrue(result["toolInputsUnchanged"])
                self.assertEqual([], result["toolInputChanges"])
                execution = self.finish_cli(output)
                self.assertEqual(0, execution.returncode, execution.stdout + execution.stderr)

    def test_changed_archived_tool_helper_or_golden_invalidates_finish_and_cli(self):
        mutations = (("edit", "Tools/CombatEvidence.py"),
                     ("edit", "Tools/test_combat_evidence_integrity.py"),
                     ("delete", "Tools/fixtures/combat-evidence-v2/nested/payload.json"),
                     ("add", "Tools/test_combat_evidence_unexpected.py"))
        for action, relative in mutations:
            with self.subTest(action=action, path=relative), tempfile.TemporaryDirectory() as folder:
                source, project, output, unity = self.fixture(Path(folder))
                self.tool_fixture(source)
                validation.prepare(source, project, output, unity)
                archived = output / "tool-sources" / relative
                if action == "delete": archived.unlink(missing_ok=True)
                else: archived.write_text("# unexpected archive mutation\n", encoding="utf-8")
                result = validation.finish(output)
                self.assertTrue(result["sourceUnchanged"])
                self.assertTrue(result["validationInputsUnchanged"])
                self.assertFalse(result["toolInputsUnchanged"])
                self.assertEqual([relative], [row["path"] for row in result["toolInputChanges"]])
                execution = self.finish_cli(output)
                self.assertEqual(2, execution.returncode, execution.stdout + execution.stderr)
                self.assertFalse(json.loads(execution.stdout)["toolInputsUnchanged"])

    def test_prepare_rejects_tool_source_change_during_copy(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            affected = source / "Tools/test_combat_evidence_integrity.py"
            original_copy = validation.shutil.copy2
            def changing_copy(original, destination, *args, **kwargs):
                if Path(original) == affected: affected.write_text("MAGIC = 'changed during copy'\n", encoding="utf-8")
                return original_copy(original, destination, *args, **kwargs)
            with mock.patch.object(validation.shutil, "copy2", side_effect=changing_copy):
                with self.assertRaises(ValueError): validation.prepare(source, project, output, unity)

    def test_prepare_rejects_corrupted_tool_destination_before_tests_can_start(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            affected = source / "Tools/CombatEvidence.py"
            original_copy = validation.shutil.copy2
            def corrupting_copy(original, destination, *args, **kwargs):
                result = original_copy(original, destination, *args, **kwargs)
                if Path(original) == affected: Path(destination).write_text("# corrupted copied tool\n", encoding="utf-8")
                return result
            with mock.patch.object(validation.shutil, "copy2", side_effect=corrupting_copy):
                with self.assertRaises(ValueError): validation.prepare(source, project, output, unity)

    def test_legacy_archive_without_tool_before_manifest_remains_unconfirmed(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            validation.prepare(source, project, output, unity)
            (output / "tool-before.json").unlink(missing_ok=True)
            historical = {}
            for name in ("execution.json", "integrity.json", "artifact-manifest.json", "source-after.json",
                         "validation-after.json", "tool-after.json"):
                body = json.dumps({"historicalFile": name, "success": True}).encode("utf-8")
                (output / name).write_bytes(body)
                historical[name] = body
            result = validation.finish(output)
            self.assertTrue(result["sourceUnchanged"])
            self.assertTrue(result["validationInputsUnchanged"])
            self.assertIsNone(result["toolInputsUnchanged"])
            self.assertFalse((output / "tool-before.json").exists())
            execution = self.finish_cli(output)
            self.assertEqual(2, execution.returncode, execution.stdout + execution.stderr)
            self.assertIsNone(json.loads(execution.stdout)["toolInputsUnchanged"])
            for name, original in historical.items(): self.assertEqual(original, (output / name).read_bytes())
            recheck = json.loads((output / "integrity-recheck.json").read_text(encoding="utf-8"))
            self.assertIsNone(recheck["toolInputsUnchanged"])

    def test_deleted_tracked_test_and_golden_remain_in_the_archived_diff(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            subprocess.run(["git", "-C", str(source), "add", "Tools"], check=True, capture_output=True)
            subprocess.run(["git", "-C", str(source), "-c", "user.name=Snapshot Test", "-c",
                            "user.email=test@example.invalid", "commit", "-m", "tools"],
                           check=True, capture_output=True)
            removed = ("Tools/test_combat_evidence_added.py",
                       "Tools/fixtures/combat-evidence-v2/nested/payload.json")
            for relative in removed: (source / relative).unlink()
            validation.prepare(source, project, output, unity)
            patch = (output / "dirty.patch").read_text(encoding="utf-8")
            before = json.loads((output / "tool-before.json").read_text(encoding="utf-8"))
            archived_names = {row["path"] for row in before["files"]}
            for relative in removed:
                self.assertIn("--- a/" + relative + "\n+++ /dev/null", patch)
                self.assertNotIn(relative, archived_names)

    def test_missing_snapshot_directory_invalidates_execution_and_records_audit_failure(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            validation.prepare(source, project, output, unity)
            execution_path = output / "execution.json"
            validation.save(execution_path, {"success": True, "unityExitCode": 0})
            (project / "Packages").rmdir()
            result = validation.finish(output)
            self.assertIsNone(result["validationInputsUnchanged"])
            self.assertTrue(result["toolInputsUnchanged"])
            self.assertTrue(result["auditErrors"])
            execution = json.loads(execution_path.read_text(encoding="utf-8"))
            self.assertFalse(execution["success"])
            manifest = json.loads((output / "artifact-manifest.json").read_text(encoding="utf-8"))
            archived_execution = next(row for row in manifest["files"] if row["path"] == "execution.json")
            self.assertEqual(validation.digest(execution_path), archived_execution["sha256"])
            cli = self.finish_cli(output)
            self.assertEqual(2, cli.returncode, cli.stdout + cli.stderr)
            self.assertIsNone(json.loads(cli.stdout)["validationInputsUnchanged"])

    def test_finish_finalizes_execution_before_hashing_artifact_manifest(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            validation.prepare(source, project, output, unity)
            execution_path = output / "execution.json"
            validation.save(execution_path, {"success": True, "unityExitCode": 0})
            (output / "tool-sources/Tools/CombatEvidence.py").write_text("# tampered\n", encoding="utf-8")
            validation.finish(output)
            execution = json.loads(execution_path.read_text(encoding="utf-8"))
            self.assertFalse(execution["success"])
            self.assertEqual(0, execution["unityExitCode"])
            manifest = json.loads((output / "artifact-manifest.json").read_text(encoding="utf-8"))
            archived_execution = next(row for row in manifest["files"] if row["path"] == "execution.json")
            self.assertEqual(validation.digest(execution_path), archived_execution["sha256"])
            self.assertEqual(execution_path.stat().st_size, archived_execution["bytes"])

    def test_finish_preserves_existing_failed_execution(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source)
            validation.prepare(source, project, output, unity)
            execution_path = output / "execution.json"
            validation.save(execution_path, {"success": False, "unityExitCode": 1, "errors": ["original test failed"]})
            validation.finish(output)
            execution = json.loads(execution_path.read_text(encoding="utf-8"))
            self.assertFalse(execution["success"])
            self.assertEqual(1, execution["unityExitCode"])
            self.assertIn("original test failed", execution["errors"])

    def generated_files(self, root, content="generated"):
        paths = ("Assets/AddressableAssetsData/link.xml", "Assets/AddressableAssetsData/link.xml.meta")
        for name in paths:
            path = root / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content + name, encoding="utf-8")
        return paths

    def editor_prepare(self, source, project, output, unity):
        return validation.prepare(source, project, output, unity,
                                  input_policy="editor-generated-v1", mode="Tests")

    def test_editor_generated_add_delete_modify_keep_stable_but_not_full_identity(self):
        for action in ("add", "delete", "modify"):
            with self.subTest(action=action), tempfile.TemporaryDirectory() as folder:
                source, project, output, unity = self.fixture(Path(folder))
                self.tool_fixture(source)
                if action != "add": self.generated_files(source)
                self.editor_prepare(source, project, output, unity)
                for root in (source, project):
                    if action == "delete":
                        for name in self.generated_files(root): (root / name).unlink()
                    else: self.generated_files(root, "changed")
                validation.save(output / "execution.json", {"success": True, "mode": "Tests", "exitCode": 0})
                result = validation.finish(output)
                self.assertFalse(result["sourceUnchanged"])
                self.assertFalse(result["validationInputsUnchanged"])
                self.assertTrue(result["sourceStableInputsUnchanged"])
                self.assertTrue(result["validationStableInputsUnchanged"])
                self.assertFalse(result["inputAuditPassed"])
                self.assertTrue(result["acceptanceInputAuditPassed"])
                execution = json.loads((output / "execution.json").read_text())
                self.assertTrue(execution["success"])
                self.assertFalse(execution["inputAuditPassed"])
                self.assertTrue(execution["acceptanceInputAuditPassed"])
                self.assertEqual(0, self.finish_cli(output).returncode)

    def test_generated_contents_and_absence_are_archived_for_both_roots_and_phases(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            names = self.generated_files(source)
            self.editor_prepare(source, project, output, unity)
            with zipfile.ZipFile(output / "tested-code-and-config.zip") as archive:
                for name in names: self.assertEqual((source / name).read_bytes(), archive.read(name))
            for name in names: (project / name).unlink()
            self.generated_files(source, "new")
            validation.finish(output)
            before = json.loads((output / "generated-inputs-before.json").read_text())
            after = json.loads((output / "generated-inputs-after.json").read_text())
            for root in ("source", "project"):
                for row in before[root]:
                    self.assertTrue(row["exists"])
                    archived = output / row["contentPath"]
                    self.assertEqual(row["sha256"], validation.digest(archived))
                    self.assertEqual(row["bytes"], archived.stat().st_size)
            self.assertTrue(all(not row["exists"] and row["sha256"] is None for row in after["project"]))
            self.assertTrue(all(row["exists"] for row in after["source"]))

    def test_python_default_and_build_remain_full_snapshot_strict(self):
        for mode in (None, "Build"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as folder:
                source, project, output, unity = self.fixture(Path(folder))
                self.generated_files(source)
                validation.prepare(source, project, output, unity, mode=mode)
                self.generated_files(project, "changed")
                result = validation.finish(output)
                self.assertEqual("full-v1", result["acceptancePolicy"]["id"])
                self.assertFalse(result["acceptanceInputAuditPassed"])
                self.assertEqual(2, self.finish_cli(output).returncode)
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            with self.assertRaises(ValueError):
                validation.prepare(source, project, output, unity, input_policy="editor-generated-v1", mode="Build")

    def test_editor_policy_never_hides_other_project_tool_or_golden_changes(self):
        for name in ("Assets/Test.cs", "Assets/Other/link.xml", "Assets/Other/link.xml.meta",
                     "ProjectSettings/ProjectVersion.txt", "Tools/CombatEvidence.py",
                     "Tools/fixtures/combat-evidence-v2/nested/payload.json"):
            with self.subTest(path=name), tempfile.TemporaryDirectory() as folder:
                source, project, output, unity = self.fixture(Path(folder))
                self.tool_fixture(source); self.generated_files(source)
                self.editor_prepare(source, project, output, unity)
                self.generated_files(project, "changed generated")
                target = (output / "tool-sources" if name.startswith("Tools/") else project) / name
                target.parent.mkdir(parents=True, exist_ok=True); target.write_text("unexpected edit")
                result = validation.finish(output)
                self.assertFalse(result["acceptanceInputAuditPassed"])
                self.assertEqual(2, self.finish_cli(output).returncode)

    def test_parallel_source_changes_remain_separate_from_stable_frozen_result(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.tool_fixture(source); self.generated_files(source)
            self.editor_prepare(source, project, output, unity)
            (source / "Assets/Test.cs").write_text("parallel edit")
            self.generated_files(project, "changed generated")
            result = validation.finish(output)
            self.assertFalse(result["sourceStableInputsUnchanged"])
            self.assertEqual(["Assets/Test.cs"], [r["path"] for r in result["sourceStableInputChanges"]])
            self.assertTrue(result["validationStableInputsUnchanged"])
            self.assertTrue(result["acceptanceInputAuditPassed"])

    def test_policy_tampering_missing_stable_manifest_and_mode_mismatch_fail_closed(self):
        for mutation in ("unknown", "changed exclusions", "missing policy", "missing stable", "mode mismatch"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as folder:
                source, project, output, unity = self.fixture(Path(folder))
                self.editor_prepare(source, project, output, unity)
                path = output / "acceptance-policy.json"
                policy = json.loads(path.read_text())
                if mutation == "unknown": policy["id"] = "future-v9"; validation.save(path, policy)
                elif mutation == "changed exclusions": policy["excludedPaths"].append("Assets/Test.cs"); validation.save(path, policy)
                elif mutation == "missing policy": path.unlink()
                elif mutation == "missing stable": (output / "validation-stable-before.json").unlink()
                else: validation.save(output / "invocation.json", {"mode": "Build"})
                result = validation.finish(output)
                self.assertFalse(result["acceptanceInputAuditPassed"])
                self.assertTrue(result["auditErrors"])
                self.assertEqual(2, self.finish_cli(output).returncode)

    def test_policyless_archive_recheck_is_new_and_never_rewrites_history(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            validation.prepare(source, project, output, unity)
            identity_path = output / "identity.json"
            identity = json.loads(identity_path.read_text())
            identity.pop("acceptancePolicy", None); identity.pop("acceptancePolicySha256", None)
            validation.save(identity_path, identity)
            (output / "acceptance-policy.json").unlink(missing_ok=True)
            for name in ("execution.json", "integrity.json", "artifact-manifest.json"):
                validation.save(output / name, {"historical": True, "success": False})
            originals = {p.relative_to(output).as_posix(): p.read_bytes() for p in output.rglob("*") if p.is_file()}
            result = validation.finish(output)
            self.assertIsNone(result["sourceStableInputsUnchanged"])
            self.assertIsNone(result["validationStableInputsUnchanged"])
            self.assertFalse(result["acceptanceInputAuditPassed"])
            self.assertNotEqual(output.resolve(), Path(result["auditOutput"]).resolve())
            self.assertEqual(originals, {p.relative_to(output).as_posix(): p.read_bytes() for p in output.rglob("*") if p.is_file()})

    def test_editor_generated_copy_is_still_checked_before_start(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            names = self.generated_files(source)
            original_copy = validation.shutil.copy2
            def corrupting_copy(original, destination, *args, **kwargs):
                result = original_copy(original, destination, *args, **kwargs)
                if Path(destination) == project / names[0]: Path(destination).write_text("bad copy")
                return result
            with mock.patch.object(validation.shutil, "copy2", side_effect=corrupting_copy):
                with self.assertRaises(ValueError): self.editor_prepare(source, project, output, unity)

    def test_editor_acceptance_keeps_failed_execution_and_seals_final_status(self):
        with tempfile.TemporaryDirectory() as folder:
            source, project, output, unity = self.fixture(Path(folder))
            self.generated_files(source); self.editor_prepare(source, project, output, unity)
            self.generated_files(project, "changed")
            execution_path = output / "execution.json"
            validation.save(execution_path, {"success": False, "exitCode": 2, "error": "original failure"})
            result = validation.finish(output)
            self.assertTrue(result["acceptanceInputAuditPassed"])
            execution = json.loads(execution_path.read_text())
            self.assertFalse(execution["success"])
            self.assertEqual("original failure", execution["error"])
            archived = json.loads((output / "artifact-manifest.json").read_text())
            row = next(r for r in archived["files"] if r["path"] == "execution.json")
            self.assertEqual(validation.digest(execution_path), row["sha256"])

    def test_launcher_explicitly_routes_editor_and_build_policy(self):
        path = Path(validation.__file__).with_name("Invoke-CombatEvidenceValidation.ps1")
        text = path.read_text(encoding="utf-8")
        self.assertIn("if ($Mode -eq 'Build') { 'full-v1' } else { 'editor-generated-v1' }", text)
        self.assertIn("'--input-policy',$inputPolicy,'--mode',$Mode", text)


if __name__ == "__main__": unittest.main()
