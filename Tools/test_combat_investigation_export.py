import gzip
import hashlib
import json
import os
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from unittest.mock import patch

import CombatEvidence as decoder
import CombatInvestigationExport as export
import CombatInvestigationIndex as investigation


class InvestigationExportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.archive = self.root / "archive" / "CombatDiagnostics"
        self.source = self.archive / "r" / "1" / "sources" / "c"
        self.source.mkdir(parents=True)
        self.analysis = self.root / "analysis.sqlite"
        self.selection = {"run": "r", "round": 1, "after": 4, "before": 4, "entity": "3"}
        self.seed = [{"capture": "c", "sequence": "5"}]

    def tearDown(self):
        self.temp.cleanup()

    def blob(self, value):
        raw = decoder.compact(value).encode()
        name = hashlib.sha256(raw).hexdigest()
        path = self.source / "inputs" / (name + ".json.gz")
        path.parent.mkdir(exist_ok=True)
        path.write_bytes(gzip.compress(raw, mtime=0))
        return {"$evidenceRef": "inputs/" + path.name}

    def fixture(self, gaps=None):
        # Real decoder files, separate physical containers: record 3 can be intentionally
        # omitted, while the batch's actor requires baseline 2 and revision 4 outside time scope.
        shared = self.blob({"weaponId": "18446744073709551615", "damage": 0.12345678901234568})
        state = self.blob({"definition": shared, "health": 99})
        baseline = {"entity": 3, "domain": "build", "perspective": "Owner", "identity": {"birth": "life"},
                    "baseline": True, "continuous": True, "stateRevision": "1", "state": state}
        rows = [dict(stage="source.start", input={"version": "test"}),
                dict(stage="investigation.state", target=3, input=baseline),
                dict(stage="other.unrelated", target=9),
                dict(stage="investigation.state", target=3, input=dict(baseline, baseline=False, stateRevision="2")),
                dict(stage="network.submit", input={"Results": [{"SourceEntityId": 2, "TargetEntityId": 3,
                     "EventId": "18446744073709551614"}], "value": 0.12345678901234568})]
        self.write_rows(rows, gaps)

    def write_rows(self, rows, gaps=None):
        for seq, values in enumerate(rows, 1):
            record = dict(schemaVersion=2, captureId="c", recordSequence=str(seq), runId="r", round=1,
                          monotonicTime=float(seq), utc="2026-09-25T00:00:00.0000000Z", role="Owner",
                          source=0, target=0, outcome="Applied")
            record.update(values)
            (self.source / f"events-{seq:05d}.jsonl").write_bytes((decoder.compact(record) + "\n").encode())
        coverage = dict(schemaVersion=1, captureId="c", runId="r", round=1, produced=str(len(rows)), written=str(len(rows)), flushed=str(len(rows)),
                        complete=not gaps, tailUnknown=False, gaps=gaps or [], failure=None)
        (self.source / "coverage.json").write_text(json.dumps(coverage), encoding="utf-8")
        with closing(decoder.connect(self.analysis)) as db:
            decoder.import_roots(db, [self.archive])
        investigation.build_index(self.analysis)

    def package(self):
        with investigation.Investigation(self.analysis) as inv:
            exporter = export.IssueExporter(inv, self.root / "exports")
            preview = exporter.preview(self.selection, self.seed, "test <script>escaped</script>")
            result = exporter.export(self.selection, self.seed, "test <script>escaped</script>", preview["previewToken"])
        return Path(result["directory"]), preview

    @staticmethod
    def snapshot(root):
        return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}

    @staticmethod
    def read_manifest(package):
        return json.loads((package / "investigation-package.json").read_text(encoding="utf-8"))

    @staticmethod
    def write_manifest(package, manifest):
        (package / "investigation-package.json").write_text(json.dumps(manifest), encoding="utf-8")

    def test_subset_retains_batch_actor_baseline_nested_blobs_precision_and_source_bytes(self):
        self.fixture()
        before = self.snapshot(self.archive)
        package, preview = self.package()
        self.assertTrue(preview["complete"], preview["warnings"])
        manifest = self.read_manifest(package)
        self.assertEqual({"1", "2", "4", "5"}, {p["sequence"] for p in manifest["required"]})
        self.assertFalse(any("events-00003" in f["path"] for f in manifest["files"]))
        self.assertEqual(2, sum(f["path"].endswith(".json.gz") for f in manifest["files"]))
        offline = (package / "report.html").read_text(encoding="utf-8")
        self.assertIn("范围外上下文", offline)
        self.assertIn("为恢复状态保留的更早基线", offline)
        self.assertIn("c / #2 · 本端 1.0 秒", offline)
        self.assertIn("等级与经验 · 未知", offline)
        self.assertIn("选卡 · 未知", offline)
        self.assertNotIn("<script>escaped</script>", offline)
        package_before = self.snapshot(package)
        output = self.root / "reimport.sqlite"
        imported = export.import_package(package, output)
        self.assertTrue(imported["complete"], imported)
        self.assertEqual([], imported["verificationErrors"])
        with investigation.Investigation(output) as inv:
            self.assertTrue(inv.coverage(self.selection)["complete"])
            self.assertFalse(inv.coverage({"run": "r", "round": 1})["complete"])
            state = inv.state(self.selection, "c", "3", 4)
            self.assertEqual("reliable", state["status"], state)
            self.assertEqual(["2", "4"], [p["sequence"] for p in state["domains"][0]["dependencies"]])
            definition = state["domains"][0]["state"]["definition"]
            self.assertEqual("18446744073709551615", definition["weaponId"])
            self.assertEqual((0.12345678901234568).hex(), definition["damage"].hex())
            self.assertEqual(4, inv.detail(self.selection, "c", "5")["record"]["elapsed"])
        self.assertEqual(before, self.snapshot(self.archive))
        self.assertEqual(package_before, self.snapshot(package))
        self.assertFalse(Path(str(output) + "-wal").exists())
        output.unlink()  # Windows fails here if the importer retains its database handle.

    def test_historical_capture_gap_is_preserved_not_confused_with_omitted_range(self):
        self.fixture(gaps=[dict(first="3", last="3", reason="QueueLimit")])
        package, preview = self.package()
        self.assertFalse(preview["complete"])
        output = self.root / "gap.sqlite"
        imported = export.import_package(package, output)
        self.assertFalse(imported["complete"])
        with investigation.Investigation(output) as inv:
            result = inv.coverage(self.selection)
            self.assertFalse(result["complete"])
            self.assertIn("CaptureGap:QueueLimit", [g["reason"] for i in result["intervals"] for g in i["gaps"]])
            self.assertNotEqual("reliable", inv.state(self.selection, "c", "3", 4)["status"])

    def test_removed_nested_dependency_is_incomplete_even_when_not_declared_in_manifest(self):
        self.fixture()
        package, _ = self.package()
        manifest = self.read_manifest(package)
        blob = next(f for f in manifest["files"] if f["path"].endswith(".json.gz"))
        (package / blob["path"]).unlink()
        manifest["files"].remove(blob)
        self.write_manifest(package, manifest)
        result = export.import_package(package, self.root / "missing.sqlite")
        self.assertFalse(result["complete"])
        self.assertTrue(any("InvalidDependency:" in error and "MissingBlob" in error for error in result["verificationErrors"]), result)

    def test_blob_content_hash_failure_is_not_hidden_by_valid_package_file_checksum(self):
        self.fixture()
        package, _ = self.package()
        manifest = self.read_manifest(package)
        blob = next(f for f in manifest["files"] if f["path"].endswith(".json.gz"))
        path = package / blob["path"]
        path.write_bytes(gzip.compress(b'{"changed":true}', mtime=0))
        blob.update(bytes=path.stat().st_size, sha256=export.digest(path))
        self.write_manifest(package, manifest)
        result = export.import_package(package, self.root / "corrupt.sqlite")
        self.assertFalse(result["complete"])
        self.assertTrue(any("InvalidBlob" in error for error in result["verificationErrors"]), result)
        self.assertFalse(any("PackageHashMismatch" in error for error in result["verificationErrors"]))

    def test_unlisted_file_and_traversal_are_rejected_before_decoder_or_destination(self):
        self.fixture()
        package, _ = self.package()
        outsider = self.root / "private.json"
        outsider.write_bytes(b'{"private":true}')
        manifest = self.read_manifest(package)
        old = manifest["files"][0]["path"]
        for path in ("../private.json", "original/../../private.json", "original\\..\\private.json", "C:/private.json"):
            with self.subTest(path=path):
                manifest["files"][0]["path"] = path
                self.write_manifest(package, manifest)
                destination = self.root / "unsafe.sqlite"
                with patch.object(decoder, "import_roots") as importer, self.assertRaises(ValueError):
                    export.import_package(package, destination)
                importer.assert_not_called()
                self.assertFalse(destination.exists())
        manifest["files"][0]["path"] = old
        self.write_manifest(package, manifest)
        (package / "original" / "events-rogue.jsonl").write_bytes(b'{}\n')
        with patch.object(decoder, "import_roots") as importer, self.assertRaisesRegex(ValueError, "未列入"):
            export.import_package(package, self.root / "unlisted.sqlite")
        importer.assert_not_called()
        self.assertEqual(b'{"private":true}', outsider.read_bytes())

    def test_source_archive_cannot_be_export_destination(self):
        self.fixture()
        before = self.snapshot(self.archive)
        with investigation.Investigation(self.analysis) as inv:
            with self.assertRaisesRegex(ValueError, "原始日志"):
                export.IssueExporter(inv, self.archive / "new-export").preview(self.selection, self.seed)
        self.assertEqual(before, self.snapshot(self.archive))

    def test_preview_rejects_same_size_changed_source_with_restored_mtime(self):
        self.fixture()
        with investigation.Investigation(self.analysis) as inv:
            exporter = export.IssueExporter(inv, self.root / "exports")
            preview = exporter.preview(self.selection, self.seed)
            source = self.source / "events-00005.jsonl"
            metadata = source.stat()
            source.write_bytes(source.read_bytes().replace(b'"Applied"', b'"Changed"'))
            os.utime(source, ns=(metadata.st_atime_ns, metadata.st_mtime_ns))
            with self.assertRaisesRegex(ValueError, "原文件发生变化"):
                exporter.export(self.selection, self.seed, preview_token=preview["previewToken"])
        self.assertFalse((self.root / "exports").exists())

    def test_state_operation_closure_includes_new_actor_baseline(self):
        def baseline(entity):
            return {"entity": entity, "domain": "build", "perspective": "Owner", "identity": {"birth": "life" + str(entity)},
                    "baseline": True, "continuous": True, "stateRevision": "1", "state": {"health": 99}}
        self.write_rows([dict(stage="source.start"),
                         dict(stage="investigation.state", target=7, input=baseline(7)),
                         dict(stage="investigation.state", target=3, input=baseline(3)),
                         dict(stage="investigation.build.begin", source=7, target=3,
                              input=dict(baseline(3), operationId="42")),
                         dict(stage="investigation.state", target=3,
                              input=dict(baseline(3), baseline=False, stateRevision="2", operationId="42")),
                         dict(stage="network.submit", input={"Results": [{"TargetEntityId": 3}]})])
        self.selection.update(after=5, before=5)
        self.seed = [{"capture": "c", "sequence": "6"}]
        package, preview = self.package()
        self.assertTrue(preview["complete"], preview)
        self.assertEqual(set(map(str, range(1, 7))), {p["sequence"] for p in self.read_manifest(package)["required"]})
        imported = self.root / "operation.sqlite"
        self.assertTrue(export.import_package(package, imported)["complete"])
        with investigation.Investigation(imported) as inv:
            linked = inv.state(dict(self.selection, entity="7"), "c", "7", 3)
            self.assertEqual("2", linked["domains"][0]["baseline"]["sequence"])
            self.assertEqual(99, linked["domains"][0]["state"]["health"])
            self.assertEqual("unknown", linked["status"])  # Data retained; outside-package scope is still unknown.

    def test_required_snapshot_keeps_incidental_actors_without_expanding_unrelated_state_targets(self):
        self.write_rows([dict(stage="source.start"),
                         dict(stage="observation.snapshot", input={"actors": [{"entity": 3, "health": 10}, {"entity": 7, "health": 20}]}),
                         dict(stage="owner.hit", source=2, target=3, before={"health": 10}, after={"health": 9})])
        self.selection.update(after=2, before=2)
        self.seed = [{"capture": "c", "sequence": "3"}]
        package, _ = self.package()
        report = json.loads((package / "investigation-report.json").read_text(encoding="utf-8"))
        self.assertNotIn("7", {s["entity"] for s in report["states"]})
        output = self.root / "snapshot.sqlite"
        self.assertTrue(export.import_package(package, output)["complete"])
        with investigation.Investigation(output) as inv:
            snapshot = json.loads(inv.db.execute("SELECT body FROM records WHERE capture='c' AND seq='2'").fetchone()[0])
            self.assertEqual([3, 7], [a["entity"] for a in snapshot["input"]["actors"]])
        self.selection.update(after=1, before=1)
        self.seed = [{"capture": "c", "sequence": "2"}]
        selected_package, _ = self.package()
        selected_report = json.loads((selected_package / "investigation-report.json").read_text(encoding="utf-8"))
        self.assertIn("7", {s["entity"] for s in selected_report["states"]})

    def test_latest_attack_observations_do_not_seed_recursive_earlier_attack_history(self):
        self.write_rows([dict(stage="source.start"),
                         dict(stage="owner.attack_stats", source=3, input={"weaponId": 1}),
                         dict(stage="owner.attack_gate", source=3, reason="Cooldown"),
                         dict(stage="owner.attack_stats", source=3, input={"weaponId": 2}),
                         dict(stage="owner.attack_gate", source=3, reason="Ready"),
                         dict(stage="owner.hit", source=3, target=4, before={"health": 10}, after={"health": 9})])
        self.selection.update(after=5, before=5)
        self.seed = [{"capture": "c", "sequence": "6"}]
        package, preview = self.package()
        # This focused fixture has no replay engine/checkpoint. That source limitation
        # stays incomplete; it must not affect which latest observations are retained.
        self.assertFalse(preview["complete"], preview)
        self.assertEqual({"1", "4", "5", "6"}, {p["sequence"] for p in self.read_manifest(package)["required"]})
        imported = self.root / "attack-observations.sqlite"
        self.assertFalse(export.import_package(package, imported)["complete"])
        with investigation.Investigation(imported) as inv:
            domains = {d["domain"]: d for d in inv.state(self.selection, "c", "3", 5)["domains"]}
            self.assertEqual(2, domains["attackAttributes"]["state"]["input"]["weaponId"])
            self.assertEqual("Ready", domains["attackGate"]["state"]["reason"])

    def test_player_epoch_identity_required_by_snapshot_survives_reimport(self):
        self.write_rows([dict(stage="source.start"),
                         dict(stage="network.identity", target=3, connectionEpoch=17),
                         dict(stage="observation.snapshot", input={"actors": [{"entity": 3, "health": 10}]}),
                         dict(stage="owner.hit", source=2, target=3, before={"health": 10}, after={"health": 9})])
        self.selection.update(after=3, before=3)
        self.seed = [{"capture": "c", "sequence": "4"}]
        package, preview = self.package()
        self.assertTrue(preview["complete"], preview)
        self.assertIn("2", {p["sequence"] for p in self.read_manifest(package)["required"]})
        imported = self.root / "epoch.sqlite"
        self.assertTrue(export.import_package(package, imported)["complete"])
        with investigation.Investigation(imported) as inv:
            state = inv.state(dict(self.selection, generation="avatar:3:epoch:17"), "c", "3", 3)
            self.assertEqual("avatar:3:epoch:17", state["generation"])
            self.assertEqual(10, state["domains"][0]["state"]["health"])
            self.assertNotIn("NoStateAtOrBeforeRequestedTime", state["unknowns"])

    def test_track_observation_boundaries_and_invalid_values_survive_scoped_import(self):
        def sample(window, frame):
            return dict(stage="performance.snapshot", input={"windowSeconds": window, "frameMaxMs": frame,
                "evidenceQueuedBytes": 512, "pendingDeaths": 0, "oldestPendingDeathSeconds": 999,
                "role": "Client", "playerCount": 2, "connections": [{"readSucceeded": False, "queueValid": False,
                "pendingValid": False, "queueValidity": "ReadFailed", "queueMilliseconds": -1, "pendingReliableBytes": 0}]})
        self.write_rows([dict(stage="source.start"), sample(.8, 12), dict(stage="other.unrelated"),
                         dict(stage="owner.hit", source=2, target=3), sample(2, 180), sample(1, 900)])
        self.selection.update(after=3, before=3)
        self.seed = [{"capture": "c", "sequence": "4"}]
        package, preview = self.package()
        manifest = self.read_manifest(package)
        self.assertEqual({"1", "2", "4", "5"}, {p["sequence"] for p in manifest["required"]})
        scope = manifest["trackScopes"][0]
        self.assertEqual((1, 4), (scope["selection"]["after"], scope["selection"]["before"]))
        self.assertEqual((3, 3), (scope["businessAfter"], scope["businessBefore"]))
        self.assertEqual(preview["evidenceBytes"], sum(f["bytes"] for f in manifest["files"]))
        self.assertEqual(preview["estimatedPackageBytes"], sum(p.stat().st_size for p in package.rglob("*") if p.is_file()))
        report = json.loads((package / "investigation-report.json").read_text(encoding="utf-8"))
        self.assertNotIn("originalCoverage", report)
        self.assertTrue(all("gaps" not in t and t["gapsReference"] for t in report["tracks"]["tracks"]))
        output = self.root / "tracks.sqlite"
        self.assertTrue(export.import_package(package, output)["complete"])
        def compare_shape(value):
            if isinstance(value, dict):
                return {k: compare_shape(v) for k, v in value.items() if k != "references"}
            if isinstance(value, list):
                return [compare_shape(v) for v in value]
            return value
        with investigation.Investigation(self.analysis) as before, investigation.Investigation(output) as after:
            original = before.tracks(scope["selection"])
            restored = after.tracks(dict(scope["selection"], packageTrackScope=scope["id"]))
            self.assertEqual([compare_shape(t) for t in original["tracks"]], [compare_shape(t) for t in restored["tracks"]])
            tracks = {t["id"]: t for t in restored["tracks"]}
            self.assertEqual("observed", tracks["frames"]["status"])
            self.assertEqual("invalid", tracks["steam"]["status"])
            self.assertFalse(after.coverage(scope["selection"])["complete"])  # Only declared track queries authorize this wider window.

    def test_writer_point_snapshot_boundaries_are_preserved_without_actor_expansion(self):
        self.write_rows([dict(stage="source.start"),
                         dict(stage="observation.snapshot", input={"pendingBytes": 100, "dropped": 0, "actors": [{"entity": 7, "health": 10}]}),
                         dict(stage="performance.snapshot", input={"windowSeconds": .5, "frameMaxMs": 20}),
                         dict(stage="owner.hit", source=2, target=3),
                         dict(stage="observation.snapshot", input={"pendingBytes": 0, "dropped": 0, "actors": [{"entity": 7, "health": 9}]}),
                         dict(stage="performance.snapshot", input={"windowSeconds": 1.5, "frameMaxMs": 22}),
                         dict(stage="observation.snapshot", input={"pendingBytes": 99999})])
        self.selection.update(after=3, before=3)
        self.seed = [{"capture": "c", "sequence": "4"}]
        package, _ = self.package()
        manifest = self.read_manifest(package)
        self.assertEqual(set(map(str, range(1, 7))), {p["sequence"] for p in manifest["required"]})
        report = json.loads((package / "investigation-report.json").read_text(encoding="utf-8"))
        self.assertNotIn("7", {state["entity"] for state in report["states"]})
        output = self.root / "writer-points.sqlite"
        self.assertTrue(export.import_package(package, output)["complete"])
        scope = manifest["trackScopes"][0]
        with investigation.Investigation(output) as inv:
            tracks = inv.tracks(dict(scope["selection"], packageTrackScope=scope["id"]))
            writer = next(t for t in tracks["tracks"] if t["id"] == "writer")
            points = [s for s in writer["samples"] if s.get("clock", {}).get("observationKind") == "point"]
            self.assertEqual([100, 0], [s["values"]["pendingBytes"] for s in points])
            self.assertEqual(["2", "5"], [s["sequence"] for s in points])

    def test_directory_link_to_external_evidence_is_not_followed(self):
        self.fixture()
        package, _ = self.package()
        outside = self.root / "outside"
        outside.mkdir()
        (outside / "events-00001.jsonl").write_bytes(b'{}\n')
        link = package / "original" / "external"
        try:
            link.symlink_to(outside, target_is_directory=True)
        except OSError as error:
            if os.name != "nt":
                self.skipTest("Directory symlinks unavailable: " + str(error))
            # Windows junctions exercise the real traversal hazard without requiring
            # developer mode or the CreateSymbolicLink privilege.
            import _winapi
            _winapi.CreateJunction(str(outside), str(link))
        with patch.object(decoder, "import_roots") as importer, self.assertRaisesRegex(ValueError, "文件链接"):
            export.import_package(package, self.root / "symlink.sqlite")
        importer.assert_not_called()


if __name__ == "__main__":
    unittest.main()
