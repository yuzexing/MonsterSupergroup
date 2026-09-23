"""Source retention must not certify a complete capture from a surviving prefix."""
import argparse
import json
import subprocess
import sys
import unittest
from pathlib import Path

import CombatEvidence as evidence
import test_combat_evidence_integrity as integrity_tests


class RetentionIntegrityTests(unittest.TestCase):
    setUp = integrity_tests.IntervalIntegrityTests.setUp
    tearDown = integrity_tests.IntervalIntegrityTests.tearDown
    source = integrity_tests.IntervalIntegrityTests.source
    ingest = integrity_tests.IntervalIntegrityTests.ingest

    @staticmethod
    def records():
        return [dict(stage="process.start", outcome="Started") for _ in range(3)] + [
            dict(stage="replay.engine_checkpoint", engine="status-1",
                 input=dict(engine="status-1", domain="status", state=dict(version=1))),
            dict(stage="replay.input", engine="status-1", operation="Advance", input=[.01]),
            dict(stage="replay.output", engine="status-1", operation="Advance", outcome="Completed"),
        ]

    def retained_source(self, kind="retention.json", before="4"):
        folder = self.source(records=self.records())
        path = folder / "events-00001.jsonl"
        rows = path.read_text(encoding="utf-8").splitlines()
        path.write_text("\n".join(rows[3:]) + "\n", encoding="utf-8")
        path.rename(folder / "events-00000000000000000004.jsonl")
        self.retention(folder, kind, before)
        self.ingest()
        return folder

    @staticmethod
    def retention(folder, kind, before):
        folder.mkdir(parents=True, exist_ok=True)
        (folder / kind).write_text(json.dumps(dict(reason="CapacityRetention", beforeSequence=before,
            firstRetainedFile="events-00000000000000000004.jsonl")), encoding="utf-8")

    def cli(self, *args):
        self.db.commit()
        return subprocess.run([sys.executable, "-B", str(Path(evidence.__file__)), "--db", str(self.db_path),
                               *args], capture_output=True, text=True)

    def assert_whole_capture_rejected(self, kind):
        self.retained_source(kind)
        self.assertFalse(evidence.coverage(self.db)["complete"], "A pruned capture is not a complete source capture")

    def assert_strict_whole_capture_rejected(self, kind):
        self.retained_source(kind)
        for scope in ([], ["--capture", "capture", "--run", "run", "--round", "1"]):
            with self.subTest(scope=scope):
                process = self.cli("coverage", "--strict", *scope)
                self.assertEqual(3, process.returncode, process.stderr + process.stdout)
                self.assertFalse(json.loads(process.stdout)["complete"])

    def test_source_retention_prevents_whole_capture_pass(self):
        self.assert_whole_capture_rejected("retention.json")

    def test_local_retention_prevents_whole_capture_pass(self):
        self.assert_whole_capture_rejected("retention.local.json")

    def test_source_retention_prevents_unbounded_strict_cli_pass(self):
        self.assert_strict_whole_capture_rejected("retention.json")

    def test_local_retention_prevents_unbounded_strict_cli_pass(self):
        self.assert_strict_whole_capture_rejected("retention.local.json")

    def assert_finite_interval_preserved(self, kind):
        self.retained_source(kind)
        result = evidence.assess_interval(self.db, "capture", 4, 6, "run", 1)
        self.assertTrue(result["complete"], result["gaps"])
        fixture = evidence.extract(self.db, "capture", "status-1", 4, 6)
        self.assertTrue(fixture["complete"], fixture["gaps"])
        self.assertEqual(1, len(fixture["steps"]))
        process = self.cli("coverage", "--capture", "capture", "--run", "run", "--round", "1",
                           "--first", "4", "--last", "6", "--strict")
        self.assertEqual(0, process.returncode, process.stderr + process.stdout)
        self.assertTrue(json.loads(process.stdout)["complete"])

    def test_source_retention_preserves_complete_finite_replay(self):
        self.assert_finite_interval_preserved("retention.json")

    def test_local_retention_preserves_complete_finite_replay(self):
        self.assert_finite_interval_preserved("retention.local.json")

    def test_another_copy_can_restore_an_explicit_interval_across_local_cleanup(self):
        folder = self.retained_source()
        self.retention(folder, "retention.local.json", "4")
        self.source(peer="client", records=self.records())
        self.ingest("host", "client")
        result = evidence.assess_interval(self.db, "capture", 1, 6, "run", 1)
        self.assertTrue(result["complete"], result["gaps"])

    def test_request_across_removed_prefix_is_incomplete(self):
        self.retained_source()
        result = evidence.assess_interval(self.db, "capture", 1, 6, "run", 1)
        self.assertFalse(result["complete"])
        self.assertFalse(evidence.extract(self.db, "capture", "status-1", 1, 6)["complete"])
        process = self.cli("coverage", "--capture", "capture", "--first", "1", "--last", "6", "--strict")
        self.assertEqual(3, process.returncode, process.stderr + process.stdout)

    def test_omitted_start_cannot_hide_removed_prefix_with_an_explicit_end(self):
        self.retained_source()
        result = evidence.assess_interval(self.db, "capture", last=6, run="run", round=1)
        self.assertFalse(result["complete"], "An end bound alone does not request only the surviving interval")

    def test_unrelated_capture_run_and_round_retention_does_not_taint_target(self):
        self.source(records=self.records())
        for run, round_number, capture in (("run", "1", "other-capture"), ("other-run", "1", "capture"), ("run", "2", "capture")):
            folder = self.root / "host" / run / round_number / "sources" / capture
            for kind in ("retention.json", "retention.local.json"):
                self.retention(folder, kind, "99")
        self.ingest()
        for first, last in ((None, None), (4, 6)):
            with self.subTest(first=first, last=last):
                result = evidence.assess_interval(self.db, "capture", first, last, "run", 1)
                self.assertTrue(result["complete"], result["gaps"])
        fixture = evidence.extract(self.db, "capture", "status-1", 4, 6)
        self.assertTrue(fixture["complete"], fixture["gaps"])
        process = self.cli("coverage", "--capture", "capture", "--run", "run", "--round", "1", "--strict")
        self.assertEqual(0, process.returncode, process.stderr + process.stdout)

    def assert_invalid_boundary_rejected(self, kind):
        folder = self.source(records=self.records())
        for before in (None, True, 0, -1, "unknown", "4.5", {"sequence": "4"}):
            with self.subTest(before=before):
                self.retention(folder, kind, before)
                self.ingest()
                result = evidence.assess_interval(self.db, "capture", 4, 6, "run", 1)
                self.assertFalse(result["complete"], "An unlocatable cleanup boundary must stay conservative")

    def test_invalid_source_retention_boundary_cannot_certify_a_finite_interval(self):
        self.assert_invalid_boundary_rejected("retention.json")

    def test_invalid_local_retention_boundary_cannot_certify_a_finite_interval(self):
        self.assert_invalid_boundary_rejected("retention.local.json")


class RunRetentionIntegrityTests(unittest.TestCase):
    """Root cleanup audits describe missing runs, not inferred source sequence gaps."""
    setUp = integrity_tests.IntervalIntegrityTests.setUp
    tearDown = integrity_tests.IntervalIntegrityTests.tearDown
    ingest = integrity_tests.IntervalIntegrityTests.ingest
    records = staticmethod(RetentionIntegrityTests.records)
    cli = RetentionIntegrityTests.cli

    def source(self, peer="host", run="run", round_number=1, capture="capture"):
        folder = self.root / peer / run / str(round_number) / "sources" / capture
        folder.mkdir(parents=True, exist_ok=True)
        records = [dict(schemaVersion=2, captureId=capture, recordSequence=str(i), runId=run,
                        round=round_number, source=9, **row) for i, row in enumerate(self.records(), 1)]
        (folder / "events-00001.jsonl").write_text("".join(json.dumps(row) + "\n" for row in records), encoding="utf-8")
        (folder / "coverage.json").write_text(json.dumps(dict(schemaVersion=2, captureId=capture,
            runId=run, round=round_number, produced="6", written="6", flushed="6", complete=True,
            tailUnknown=False, gaps=[], failure=None)), encoding="utf-8")
        return folder

    def audit(self, peer="host", run="deleted-run", raw=None):
        path = self.root / peer / "retention.jsonl"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(raw if raw is not None else json.dumps(dict(runId=run,
            reason="GlobalCapacityRetention", utc="2026-09-23T00:00:00Z")) + "\n", encoding="utf-8")
        return path

    def result(self, *args, expected=0):
        process = self.cli(*args)
        self.assertEqual(expected, process.returncode, process.stderr + process.stdout)
        return json.loads(process.stdout)

    def test_deleted_run_prevents_global_pass_but_allows_browsing(self):
        self.source(); self.audit(); self.ingest()
        report = evidence.coverage(self.db)
        self.assertFalse(report["complete"])
        self.assertTrue(report["scopeGaps"])
        self.assertFalse(self.result("coverage")["complete"])
        self.assertFalse(self.result("coverage", "--strict", expected=3)["complete"])

    def test_audit_without_any_records_has_explicit_scope_gap(self):
        self.audit(); self.ingest()
        report = self.result("coverage", "--strict", expected=3)
        self.assertFalse(report["complete"])
        self.assertTrue(any(gap.get("runId") == "deleted-run" for gap in report["scopeGaps"]))
        self.assertEqual([], report["intervals"])

    def test_coverage_run_filter_excludes_other_runs(self):
        self.source(); self.source(run="other-run", capture="other-capture"); self.audit(); self.ingest()
        report = self.result("coverage", "--run", "run", "--strict")
        self.assertTrue(report["complete"])
        self.assertEqual({"run"}, {interval["runId"] for interval in report["intervals"]})
        self.assertEqual([], report["scopeGaps"])

    def test_deleted_run_filter_reports_gap_without_contexts(self):
        self.source(); self.audit(); self.ingest()
        report = self.result("coverage", "--run", "deleted-run", "--strict", expected=3)
        self.assertEqual([], report["intervals"])
        self.assertTrue(report["scopeGaps"])

    def test_round_filter_selects_actual_round_without_capture(self):
        self.source(); self.source(round_number=2, capture="round-two"); self.ingest()
        report = self.result("coverage", "--round", "2", "--strict")
        self.assertTrue(report["complete"])
        self.assertEqual({2}, {int(interval["round"]) for interval in report["intervals"]})

    def test_round_filter_cannot_exclude_unlocated_deleted_round(self):
        self.source(round_number=2); self.audit(); self.ingest()
        report = self.result("coverage", "--round", "2", "--strict", expected=3)
        self.assertTrue(report["scopeGaps"])

    def test_capture_without_explicit_run_cannot_exclude_deleted_run(self):
        self.source(); self.audit(); self.ingest()
        report = self.result("coverage", "--capture", "capture", "--strict", expected=3)
        self.assertFalse(report["complete"])
        self.assertTrue(report["scopeGaps"])
        allowed = self.result("coverage", "--capture", "capture", "--run", "run", "--strict")
        self.assertTrue(allowed["complete"])
        self.assertEqual([], allowed["scopeGaps"])

    def test_explicit_finite_capture_interval_remains_complete(self):
        self.source(); self.audit(); self.ingest()
        report = self.result("coverage", "--capture", "capture", "--first", "4", "--last", "6", "--strict")
        self.assertTrue(report["complete"])
        self.assertEqual([], report["scopeGaps"])

    def test_replica_can_restore_finite_interval_but_not_all_run_sources(self):
        self.audit(run="run"); self.source(peer="replica"); self.ingest("host", "replica")
        finite = self.result("coverage", "--capture", "capture", "--run", "run", "--first", "1", "--last", "6", "--strict")
        self.assertTrue(finite["complete"])
        whole = self.result("coverage", "--run", "run", "--strict", expected=3)
        self.assertTrue(whole["scopeGaps"])
        self.assertFalse(whole["complete"])

    def test_invalid_audits_cannot_certify_global_capture(self):
        self.source()
        invalid = ["{bad}\n", '{"runId":', "null\n", "[]\n",
                   json.dumps(dict(reason="GlobalCapacityRetention")) + "\n"]
        invalid.extend(json.dumps(dict(runId=value, reason="GlobalCapacityRetention")) + "\n"
                       for value in (None, True, 7, "", "   ", [], {}))
        invalid.append(json.dumps(dict(runId="deleted-run", reason="UnknownCleanup")) + "\n")
        for index, raw in enumerate(invalid):
            with self.subTest(raw=raw):
                # Use a fresh import so one malformed audit cannot mask the next.
                self.db.close()
                self.db_path = self.root / ("invalid-audit-" + str(index) + ".sqlite")
                self.db = evidence.connect(self.db_path)
                self.audit(raw=raw); self.ingest()
                report = self.result("coverage", "--strict", expected=3)
                self.assertFalse(report["complete"])
                self.assertTrue(report["scopeGaps"])
                finite = self.result("coverage", "--capture", "capture", "--first", "4", "--last", "6", "--strict")
                self.assertTrue(finite["complete"])

    def test_unknown_run_audit_remains_conservative_with_explicit_run(self):
        self.source(); self.audit(raw="null\n"); self.ingest()
        report = self.result("coverage", "--run", "run", "--strict", expected=3)
        self.assertTrue(report["scopeGaps"])

    def test_reimported_corruption_cannot_reuse_the_old_audits_run(self):
        self.source(); self.audit(); self.ingest()
        self.audit(raw="{broken}\n"); self.ingest()
        report = self.result("coverage", "--run", "run", "--strict", expected=3)
        self.assertTrue(any("runId" not in gap for gap in report["scopeGaps"]))
        finite = self.result("coverage", "--capture", "capture", "--first", "4", "--last", "6", "--strict")
        self.assertTrue(finite["complete"])

    def test_legacy_truncation_issue_does_not_prove_the_old_row_was_parsed(self):
        self.source(); path = self.audit(); self.ingest()
        # The old importer stopped before JSON parsing when the newline was absent.
        self.db.execute("INSERT INTO issues VALUES(?,1,'InvalidMetadata',?)",
                        (str(path), "Truncated cleanup audit"))
        report = self.result("coverage", "--run", "run", "--strict", expected=3)
        self.assertTrue(any("runId" not in gap for gap in report["scopeGaps"]))

    def test_parsed_missing_newline_audit_still_identifies_its_run(self):
        self.source()
        self.audit(raw=json.dumps(dict(runId="deleted-run", reason="GlobalCapacityRetention")))
        self.ingest()
        report = self.result("coverage", "--strict", expected=3)
        self.assertTrue(all(gap.get("runId") == "deleted-run" for gap in report["scopeGaps"]))
        unaffected = self.result("coverage", "--run", "run", "--strict")
        self.assertTrue(unaffected["complete"])

    def test_scope_gap_merges_run_audits_and_preserves_all_original_references(self):
        self.source()
        row = json.dumps(dict(runId="deleted-run", reason="GlobalCapacityRetention")) + "\n"
        first = self.audit(raw=row + row)
        second = self.audit(peer="replica")
        self.ingest("host", "replica")
        report = evidence.coverage(self.db)
        gaps = [gap for gap in report["scopeGaps"] if gap.get("runId") == "deleted-run"]
        self.assertEqual(1, len(gaps))
        gap = gaps[0]
        self.assertNotIn("captureId", gap)
        self.assertNotIn("round", gap)
        self.assertNotIn("first", gap)
        self.assertNotIn("last", gap)
        self.assertEqual({(str(first), 1), (str(first), 2), (str(second), 1)},
                         {(ref["path"], ref["line"]) for ref in gap["references"]})
        self.ingest("host", "replica")
        self.assertEqual(report["scopeGaps"], evidence.coverage(self.db)["scopeGaps"])

    def test_corrupt_audit_keeps_original_line_reference(self):
        self.source()
        path = self.audit(raw=json.dumps(dict(runId="deleted-run", reason="GlobalCapacityRetention")) + "\n{bad}\n")
        self.ingest()
        gaps = evidence.coverage(self.db)["scopeGaps"]
        self.assertTrue(any(ref["path"] == str(path) and ref["line"] == 2
                            for gap in gaps for ref in gap["references"]))

    def test_query_and_history_browse_but_strict_checks_cannot_bypass_scope_gaps(self):
        self.source(); self.audit(); self.ingest()
        for command in (("query", "--capture", "capture"), ("player-output", "--player", "9")):
            with self.subTest(command=command):
                normal = self.result(*command)
                self.assertFalse(normal["complete"])
                self.assertTrue(normal["scopeGaps"])
                strict = self.result(*command, "--strict", expected=3)
                self.assertFalse(strict["complete"])
        history = evidence.history(self.db, argparse.Namespace(command="player-output", player="9", limit=50))
        self.assertTrue(all(gap in history["evidenceGaps"] for gap in history["scopeGaps"]))

    def test_query_with_explicit_other_run_or_finite_interval_is_unaffected(self):
        self.source(); self.audit(); self.ingest()
        for scope in (("--run", "run"), ("--capture", "capture", "--first", "4", "--last", "6")):
            with self.subTest(scope=scope):
                report = self.result("query", *scope, "--strict")
                self.assertTrue(report["complete"])
                self.assertEqual([], report["scopeGaps"])

    def test_extract_uses_real_checkpoint_for_explicit_end_only(self):
        self.source(); self.audit(); self.ingest()
        finite = evidence.extract(self.db, "capture", "status-1", last=6)
        self.assertTrue(finite["complete"], finite["gaps"])
        self.assertEqual(1, len(finite["steps"]))
        self.assertEqual([], finite["scopeGaps"])
        whole = evidence.extract(self.db, "capture", "status-1")
        self.assertFalse(whole["complete"])
        self.assertTrue(whole["scopeGaps"])
        process = self.cli("extract", "--capture", "capture", "--engine", "status-1", "--output", str(self.root / "fixture.json"))
        self.assertEqual(3, process.returncode, process.stdout + process.stderr)

    def test_coverage_bounds_without_capture_are_structured_invalid_requests(self):
        self.source(); self.ingest()
        for bounds in (("--first", "4"), ("--last", "6"), ("--first", "4", "--last", "6")):
            for strict in ((), ("--strict",)):
                with self.subTest(bounds=bounds, strict=strict):
                    report = self.result("coverage", *bounds, *strict, expected=3)
                    self.assertFalse(report["complete"])
                    self.assertTrue(report.get("error") or report.get("scopeGaps"))


if __name__ == "__main__":
    unittest.main()
