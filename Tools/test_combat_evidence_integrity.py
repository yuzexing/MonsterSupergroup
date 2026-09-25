"""Interval integrity regressions; every file is a synthetic, temporary capture."""
import argparse
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import CombatEvidence as evidence


class IntervalIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.db_path = self.root / "evidence.sqlite"
        self.db = evidence.connect(self.db_path)

    def tearDown(self):
        self.db.close()
        self.temp.cleanup()

    def records(self):
        return [
            dict(stage="replay.engine_checkpoint", engine="damage-1", input=dict(engine="damage-1", domain="damage", state=dict(version=1))),
            dict(stage="owner.damage_calculation", engine="damage-1", operation="Calculate", source=9, input=dict(criticalRoll=.1), after=dict(requestedDamage=20)),
            dict(stage="owner.damage_calculation", engine="damage-1", operation="Calculate", source=9, input=dict(criticalRoll=.9), after=dict(requestedDamage=10)),
        ]

    def source(self, peer="host", records=None, coverage=True, **changes):
        records = self.records() if records is None else records
        folder = self.root / peer / "run" / "1" / "sources" / "capture"
        folder.mkdir(parents=True, exist_ok=True)
        bodies = [dict(schemaVersion=2, captureId="capture", recordSequence=str(i), runId="run", round=1, **r) for i, r in enumerate(records, 1)]
        (folder / "events-00001.jsonl").write_text("".join(json.dumps(r) + "\n" for r in bodies), encoding="utf-8")
        if coverage:
            state = dict(schemaVersion=2, captureId="capture", runId="run", round=1, produced=str(len(records)), written=str(len(records)),
                         flushed=str(len(records)), complete=True, tailUnknown=False, gaps=[], failure=None)
            state.update(changes)
            (folder / "coverage.json").write_text(json.dumps(state), encoding="utf-8")
        return folder

    def ingest(self, *peers):
        evidence.import_roots(self.db, [self.root / peer for peer in (peers or ("host",))])

    def fixture(self, **kwargs):
        return evidence.extract(self.db, "capture", "damage-1", **kwargs)

    def test_semantic_capture_failure_without_engine_invalidates_replay_and_history(self):
        rows = self.records()
        rows.insert(2, dict(stage="owner.damage_calculation", outcome="CaptureFailed", reason="InvalidOperationException", source=9))
        self.source(records=rows); self.ingest()
        fixture = self.fixture()
        self.assertFalse(fixture["complete"], "A failed semantic capture must not disappear between two valid calculations")
        history = evidence.history(self.db, argparse.Namespace(command="player-output", player="9", limit=50))
        self.assertFalse(history["complete"])
        self.assertTrue(any("CaptureFailed" in g["reason"] for g in history["evidenceGaps"]))

    def test_requested_last_beyond_available_records_is_incomplete_even_with_watermark(self):
        self.source(produced="10", written="10", flushed="10"); self.ingest()
        fixture = self.fixture(last=10)
        self.assertFalse(fixture["complete"])
        self.assertTrue(any("MissingRecordRange:4-10" in g for g in fixture["gaps"]))

    def test_visible_records_above_flushed_are_not_reliable(self):
        self.source(flushed="2", complete=False, tailUnknown=True); self.ingest()
        self.assertFalse(self.fixture(last=3)["complete"])

    def test_missing_coverage_never_proves_complete_replay(self):
        self.source(coverage=False); self.ingest()
        self.assertFalse(self.fixture(last=3)["complete"])

    def test_bounded_flushed_prefix_is_reliable_despite_unknown_tail(self):
        self.source(complete=False, tailUnknown=True); self.ingest()
        self.assertTrue(self.fixture(last=3)["complete"])
        self.assertFalse(self.fixture()["complete"])

    def test_old_gap_does_not_explain_new_unknown_failure(self):
        self.source(failure="New disk failure", gaps=[dict(first="1", last="1", reason="OldQueueOverflow")]); self.ingest()
        fixture = self.fixture(first=2, last=3)
        self.assertFalse(fixture["complete"])
        self.assertTrue(any("SourceFailureUnknownRange" in g for g in fixture["gaps"]))

    def test_newest_revision_supersedes_stale_replica_metadata(self):
        self.source("old", coverageRevision="1", failure="Old failure", tailUnknown=True, complete=False)
        self.source("new", coverageRevision="2")
        self.ingest("old", "new")
        fixture = self.fixture(last=3)
        self.assertTrue(fixture["complete"], fixture["gaps"])
        self.assertTrue(evidence.coverage(self.db)["complete"])

    def test_equal_revisions_with_conflicting_metadata_are_not_silently_selected(self):
        self.source("old", coverageRevision="7", failure="Disk failed")
        self.source("new", coverageRevision="7")
        self.ingest("old", "new")
        self.assertFalse(self.fixture(last=3)["complete"])

    def test_legacy_newer_watermark_supersedes_older_metadata(self):
        self.source("old", produced="2", written="2", flushed="2", complete=False, tailUnknown=True)
        self.source("new")
        self.ingest("old", "new")
        self.assertTrue(self.fixture(last=3)["complete"])

    def test_truncated_tail_preserves_explicit_flushed_prefix_only(self):
        folder = self.source()
        with (folder / "events-00001.jsonl").open("ab") as stream: stream.write(b'{"partial":')
        self.ingest()
        self.assertTrue(self.fixture(last=3)["complete"])
        self.assertFalse(self.fixture()["complete"])

    def test_corrupt_line_inside_interval_is_visible_to_all_assessments(self):
        folder = self.source()
        path = folder / "events-00001.jsonl"
        lines = path.read_text().splitlines(); lines[1] = '{"bad":'
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        self.ingest()
        self.assertFalse(self.fixture(last=3)["complete"])
        report = evidence.coverage(self.db)
        self.assertFalse(report["complete"])
        self.assertTrue(any("InvalidRecord" in g["reason"] for a in report["intervals"] for g in a["gaps"]))

    def test_missing_dependency_is_incomplete_in_history_and_replay(self):
        rows = self.records(); rows[1]["input"] = {"$evidenceRef": "inputs/" + "0" * 64 + ".json.gz"}
        self.source(records=rows); self.ingest()
        self.assertFalse(self.fixture(last=3)["complete"])
        history = evidence.history(self.db, argparse.Namespace(command="player-output", player="9", limit=50))
        self.assertFalse(history["complete"])

    def test_interval_restarts_after_known_gap_and_complete_checkpoint(self):
        rows = self.records(); rows.extend(self.records())
        rows[3] = dict(stage="replay.checkpoint", engine="*", input=dict(engines=[rows[3]["input"]]))
        self.source(records=rows, failure="Prior write failure", failureFirstSequence="2", failureLastSequence="3",
                    reliableFromSequence="4", gaps=[dict(first="2", last="3", reason="WriteFailure")]); self.ingest()
        fixture = self.fixture(first=4, last=6)
        self.assertTrue(fixture["complete"], fixture["gaps"])

    def test_explicit_failure_range_does_not_poison_earlier_flushed_interval(self):
        self.source(failure="Later write failure", failureFirstSequence="4", failureLastSequence=None,
                    complete=False, tailUnknown=True); self.ingest()
        self.assertTrue(self.fixture(last=3)["complete"])

    def test_recovery_pending_blocks_post_failure_checkpoint_but_not_earlier_prefix(self):
        rows = self.records(); rows.extend(self.records())
        self.source(records=rows, failure="Write failed", failureFirstSequence="3", failureLastSequence="3",
                    recoveryPending=True, tailUnknown=True, complete=False); self.ingest()
        self.assertTrue(self.fixture(last=2)["complete"])
        self.assertFalse(self.fixture(first=4, last=6)["complete"])

    def test_post_failure_interval_requires_the_confirmed_recovery_checkpoint(self):
        rows = self.records(); rows.extend(self.records()); rows.extend(self.records())
        rows[6] = dict(stage="replay.checkpoint", engine="*", input=dict(engines=[rows[6]["input"]]))
        self.source(records=rows, failure="Write failed", failureFirstSequence="3", failureLastSequence="3",
                    recoveryPending=False, reliableFromSequence="7"); self.ingest()
        self.assertFalse(self.fixture(first=4, last=6)["complete"])
        self.assertTrue(self.fixture(first=7, last=9)["complete"])

    def test_semantic_failure_after_last_engine_record_is_not_hidden_by_default_end(self):
        rows = self.records(); rows.append(dict(stage="owner.damage_calculation", outcome="CaptureFailed", reason="SnapshotFailed"))
        self.source(records=rows); self.ingest()
        self.assertFalse(self.fixture()["complete"])
        self.assertTrue(self.fixture(last=3)["complete"])

    def test_coverage_and_history_browse_normally_but_strict_mode_returns_three(self):
        self.source(coverage=False); self.ingest(); self.db.commit()
        for command in (["coverage", "--capture", "capture"], ["player-output", "--player", "9"], ["query", "--capture", "capture"]):
            with self.subTest(command=command):
                args = [sys.executable, "-B", str(Path(evidence.__file__)), "--db", str(self.db_path), *command]
                normal = subprocess.run(args, text=True, capture_output=True)
                strict = subprocess.run(args + ["--strict"], text=True, capture_output=True)
                self.assertEqual(0, normal.returncode, normal.stderr)
                self.assertEqual(3, strict.returncode, strict.stderr)
                self.assertFalse(json.loads(strict.stdout)["complete"])

    def test_strict_bounded_history_is_reliable_with_unknown_source_tail(self):
        self.source(complete=False, tailUnknown=True); self.ingest(); self.db.commit()
        process = subprocess.run([sys.executable, "-B", str(Path(evidence.__file__)), "--db", str(self.db_path),
                                  "player-output", "--player", "9", "--last", "3", "--strict"], text=True, capture_output=True)
        self.assertEqual(0, process.returncode, process.stderr)
        self.assertTrue(json.loads(process.stdout)["complete"])

    def test_relative_import_retains_the_same_bounded_corruption_location(self):
        folder = self.source()
        with (folder / "events-00001.jsonl").open("ab") as stream: stream.write(b'{"partial":')
        previous = Path.cwd()
        try:
            os.chdir(self.root)
            evidence.import_roots(self.db, [Path("host")])
        finally:
            os.chdir(previous)
        fixture = self.fixture(last=3)
        self.assertTrue(fixture["complete"], fixture["gaps"])

    def test_corrupt_additional_source_is_not_hidden_by_one_clean_source(self):
        self.source()
        folder = self.root / "host" / "run" / "1" / "sources" / "lost-capture"
        folder.mkdir(parents=True)
        (folder / "coverage.json").write_text('{"broken":', encoding="utf-8")
        self.ingest()
        self.assertFalse(evidence.coverage(self.db)["complete"])
        self.assertTrue(self.fixture(last=3)["complete"], "An unrelated damaged source must not poison a bounded local fixture")

    def test_malformed_checkpoint_returns_incomplete_fixture_instead_of_crashing(self):
        rows = self.records(); rows[0]["input"] = None
        self.source(records=rows); self.ingest()
        fixture = self.fixture(last=3)
        self.assertFalse(fixture["complete"])
        self.assertTrue(any("Checkpoint" in reason for reason in fixture["gaps"]))

    def test_empty_full_checkpoint_describes_no_roots_but_cannot_create_a_fixture(self):
        self.source(records=[dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))])
        self.ingest()
        report = evidence.coverage(self.db)
        self.assertTrue(report["complete"], report)
        fixture = self.fixture(last=1)
        self.assertFalse(fixture["complete"])
        self.assertIsNone(fixture["checkpoint"])
        self.assertEqual([], fixture["steps"])
        self.assertIn("MissingCheckpoint", fixture["gaps"])

    def test_empty_full_checkpoint_before_independent_engine_does_not_poison_source(self):
        rows = [dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[])),
                self.records()[0],
                dict(stage="replay.input", engine="damage-1", operation="Calculate", input=[]),
                dict(stage="replay.output", engine="damage-1", operation="Calculate", outcome="Completed", after=None)]
        self.source(records=rows); self.ingest()
        report = evidence.coverage(self.db)
        self.assertTrue(report["complete"], report)
        fixture = self.fixture(first=1, last=4)
        self.assertTrue(fixture["complete"], fixture["gaps"])
        self.assertEqual(1, len(fixture["steps"]))

    def test_empty_full_checkpoint_after_completed_calls_keeps_earlier_steps_reliable(self):
        rows = self.records() + [dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))]
        self.source(records=rows); self.ingest()
        fixture = self.fixture(last=4)
        self.assertTrue(fixture["complete"], fixture["gaps"])
        self.assertEqual(2, len(fixture["steps"]))

    def test_empty_full_checkpoint_revokes_the_prior_independent_state_boundary(self):
        rows = [self.records()[0], dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[])),
                dict(stage="replay.input", engine="damage-1", operation="Clear", input=[]),
                dict(stage="replay.output", engine="damage-1", operation="Clear", outcome="Completed", after=None)]
        self.source(records=rows); self.ingest()
        fixture = self.fixture(last=4)
        self.assertFalse(fixture["complete"])
        self.assertIn("MissingCheckpoint:damage-1", fixture["gaps"])

    def test_empty_full_checkpoint_does_not_hide_an_uncompleted_call(self):
        rows = [self.records()[0], dict(stage="replay.input", engine="damage-1", operation="Calculate", input=[]),
                dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))]
        self.source(records=rows); self.ingest()
        fixture = self.fixture(last=3)
        self.assertFalse(fixture["complete"])
        self.assertTrue(any("MissingOutput" in gap for gap in fixture["gaps"]), fixture["gaps"])

    def test_empty_checkpoint_contract_still_rejects_malformed_engine_arrays(self):
        for data in (None, {}, {"engines": None}, {"engines": {}}, {"engines": "[]"},
                     {"engines": [None]}, {"engines": [{}]},
                     {"engines": [dict(engine="damage-1", domain="damage", state=None)]}):
            with self.subTest(data=data):
                with self.assertRaises(ValueError):
                    evidence.checkpoint_engines(dict(stage="replay.checkpoint"), data)
        with self.assertRaises(ValueError):
            evidence.checkpoint_engines(dict(stage="replay.engine_checkpoint"), dict(engines=[]))

    def test_strict_cli_accepts_empty_source_but_rejects_its_empty_fixture(self):
        self.source(records=[dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))])
        self.ingest(); self.db.commit()
        prefix = [sys.executable, "-B", str(Path(evidence.__file__)), "--db", str(self.db_path)]
        coverage = subprocess.run(prefix + ["coverage", "--capture", "capture", "--strict"], text=True, capture_output=True)
        self.assertEqual(0, coverage.returncode, coverage.stderr + coverage.stdout)
        output = self.root / "empty-fixture.json"
        fixture = subprocess.run(prefix + ["extract", "--capture", "capture", "--engine", "damage-1",
                                          "--last", "1", "--output", str(output)], text=True, capture_output=True)
        self.assertEqual(3, fixture.returncode, fixture.stderr + fixture.stdout)
        self.assertFalse(json.loads(output.read_text())["complete"])

    def test_empty_full_checkpoint_cannot_satisfy_a_declared_recovery_boundary(self):
        rows = self.records() + [dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))] + self.records()
        self.source(records=rows, failure="Prior write failure", failureFirstSequence="3", failureLastSequence="3",
                    recoveryPending=False, reliableFromSequence="4", gaps=[dict(first="3", last="3", reason="WriteFailure")])
        self.ingest()
        original = json.loads(self.db.execute("SELECT body FROM metadata WHERE kind='coverage.json'").fetchone()[0])
        for versioned in (False, True):
            with self.subTest(versioned=versioned):
                state = dict(original)
                if versioned:
                    state.update(integrityHistoryVersion=1, failureEpoch=1, integrityHistory=[
                        dict(first="3", last="3", epoch=1, reliableFromSequence="4")])
                self.db.execute("UPDATE metadata SET body=? WHERE kind='coverage.json'", (json.dumps(state),))
                self.assertTrue(self.fixture(last=2)["complete"])
                for first in (4, 5):
                    fixture = self.fixture(first=first, last=7)
                    self.assertFalse(fixture["complete"], fixture)
                    self.assertTrue(any("RecoveryCheckpoint" in gap for gap in fixture["gaps"]), fixture["gaps"])

    def test_empty_full_checkpoint_keeps_pending_recovery_unreliable(self):
        rows = self.records() + [dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))] + self.records()
        self.source(records=rows, failure="Prior write failure", failureFirstSequence="3", failureLastSequence="3",
                    recoveryPending=True, complete=False, tailUnknown=True, gaps=[dict(first="3", last="3", reason="WriteFailure")])
        self.ingest()
        fixture = self.fixture(first=5, last=7)
        self.assertFalse(fixture["complete"])
        self.assertIn("RecoveryCheckpointPending", fixture["gaps"])

    def test_empty_historical_recovery_does_not_revoke_a_later_real_recovery(self):
        rows = self.records() + [dict(stage="replay.checkpoint", engine="*", input=dict(version=1, engines=[]))] + self.records() * 2
        rows[7] = dict(stage="replay.checkpoint", engine="*", input=dict(engines=[rows[0]["input"]]))
        self.source(records=rows, failure="Prior write failures", failureFirstSequence="3", failureLastSequence="7",
                    recoveryPending=False, reliableFromSequence="8", failureEpoch=2,
                    gaps=[dict(first="3", last="3", reason="WriteFailure"), dict(first="7", last="7", reason="WriteFailure")],
                    integrityHistoryVersion=1, integrityHistory=[
                        dict(first="3", last="3", epoch=1, reliableFromSequence="4"),
                        dict(first="7", last="7", epoch=2, reliableFromSequence="8")])
        self.ingest()
        self.assertTrue(self.fixture(last=2)["complete"])
        self.assertFalse(self.fixture(first=5, last=6)["complete"])
        fixture = self.fixture(first=8, last=10)
        self.assertTrue(fixture["complete"], fixture["gaps"])

    def test_queue_gap_recovery_pending_keeps_the_pre_gap_prefix_reliable(self):
        rows = self.records(); rows.extend(self.records())
        self.source(records=rows, failure=None, failureEpoch=1, recoveryPending=True, complete=False, tailUnknown=True,
                    gaps=[dict(first="3", last="3", reason="QueueOverload")]); self.ingest()
        self.assertTrue(self.fixture(last=2)["complete"])
        self.assertFalse(self.fixture(first=4, last=6)["complete"])

    def test_queue_gap_recovery_boundary_applies_without_a_failure_string(self):
        rows = self.records(); rows.extend(self.records()); rows.extend(self.records())
        rows[6] = dict(stage="replay.checkpoint", engine="*", input=dict(engines=[rows[6]["input"]]))
        self.source(records=rows, failure=None, failureEpoch=1, recoveryPending=False, reliableFromSequence="7",
                    gaps=[dict(first="3", last="3", reason="QueueOverload")]); self.ingest()
        self.assertFalse(self.fixture(first=4, last=6)["complete"])
        self.assertTrue(self.fixture(first=7, last=9)["complete"])

    def test_recovery_sidecar_cannot_hide_a_truncated_tail_after_csharp_repair(self):
        folder = self.source()
        (folder / "recovery.json").write_text(json.dumps(dict(tailUnknown=True, file="events-00001.jsonl",
            retainedBytes=(folder / "events-00001.jsonl").stat().st_size)), encoding="utf-8")
        self.ingest()
        self.assertFalse(self.fixture()["complete"])
        self.assertTrue(self.fixture(last=3)["complete"])

    def recovered_epoch_source(self, pending=True, failure="Later write failure", **changes):
        rows = self.records() * 3
        checkpoint = dict(stage="replay.checkpoint", engine="*", input=dict(engines=[rows[0]["input"]]))
        rows[3] = checkpoint; rows[7] = checkpoint
        state = dict(failure=failure, failureFirstSequence="2", failureLastSequence="7", failureEpoch=2,
                     recoveryPending=pending, complete=False, tailUnknown=pending, reliableFromSequence="4" if pending else "8",
                     gaps=[dict(first="2", last="2", reason="WriteFailure"), dict(first="7", last="7", reason="WriteFailure")],
                     integrityHistoryVersion=1, integrityHistory=[
                         dict(first="2", last="2", epoch=1, reliableFromSequence="4"),
                         dict(first="7", last="7", epoch=2, reliableFromSequence=None if pending else "8")])
        state.update(changes)
        self.source(records=rows, **state); self.ingest()

    def test_later_pending_failure_does_not_revoke_prior_reliable_window(self):
        self.recovered_epoch_source()
        fixture = self.fixture(first=4, last=6)
        self.assertTrue(fixture["complete"], fixture["gaps"])
        self.assertFalse(self.fixture(first=8, last=9)["complete"])
        history = evidence.history(self.db, argparse.Namespace(command="player-output", player="9", first=4, last=6, limit=50))
        self.assertTrue(history["complete"], history["evidenceGaps"])
        self.assertTrue(evidence.coverage(self.db, [evidence.assess_interval(self.db, "capture", 4, 6, "run", 1)])["complete"])

    def test_later_recovered_failure_does_not_revoke_prior_reliable_window(self):
        self.recovered_epoch_source(pending=False)
        self.assertTrue(self.fixture(first=4, last=6)["complete"])
        self.assertTrue(self.fixture(first=8, last=9)["complete"])
        self.assertFalse(self.fixture(first=4, last=9)["complete"])
        self.assertFalse(self.fixture(first=3, last=6)["complete"], "The first episode still requires checkpoint 4")

    def test_queue_only_epochs_preserve_prior_reliable_window(self):
        self.recovered_epoch_source(failure=None)
        self.assertTrue(self.fixture(first=4, last=6)["complete"])
        self.assertFalse(self.fixture(first=8, last=9)["complete"])

    def test_complete_epoch_history_refines_conservative_legacy_gap_hull(self):
        self.recovered_epoch_source(gaps=[dict(first="2", last="7", reason="CoalescedCaptureGap", conservative=True)])
        self.assertTrue(self.fixture(first=4, last=6)["complete"])

    def test_compressed_epoch_history_explicitly_sacrifices_its_middle_window(self):
        self.recovered_epoch_source(pending=False, integrityHistory=[
            dict(first="2", last="7", epoch=2, reliableFromSequence="8", conservative=True)])
        self.assertFalse(self.fixture(first=4, last=6)["complete"])
        self.assertTrue(self.fixture(first=8, last=9)["complete"])

    def test_known_start_unknown_end_preserves_earlier_reliable_window(self):
        self.recovered_epoch_source(integrityHistory=[dict(first="2", last="2", epoch=1, reliableFromSequence="4"),
                                                    dict(first="7", last=None, epoch=2)])
        self.assertTrue(self.fixture(first=4, last=6)["complete"])
        self.assertFalse(self.fixture(first=8, last=9)["complete"])

    def test_unknown_start_pending_epoch_never_claims_earlier_prefix(self):
        self.recovered_epoch_source(integrityHistory=[dict(first="2", last="2", epoch=1, reliableFromSequence="4"),
                                                    dict(first=None, last="7", epoch=2)])
        self.assertFalse(self.fixture(first=1, last=1)["complete"])
        self.assertFalse(self.fixture(first=4, last=6)["complete"])

    def test_unknown_historical_failure_can_recover_only_at_its_confirmed_checkpoint(self):
        self.recovered_epoch_source(integrityHistory=[dict(first=None, last=None, epoch=1, reliableFromSequence="4"),
                                                    dict(first="7", last="7", epoch=2)])
        self.assertFalse(self.fixture(first=1, last=1)["complete"])
        self.assertTrue(self.fixture(first=4, last=6)["complete"])

    def test_incomplete_or_contradictory_epoch_metadata_is_never_trusted(self):
        self.recovered_epoch_source()
        original = json.loads(self.db.execute("SELECT body FROM metadata WHERE kind='coverage.json'").fetchone()[0])
        changes = [dict(integrityHistory=[]), dict(failureEpoch=3), dict(recoveryPending=False),
                   dict(integrityHistoryVersion=2), dict(reliableFromSequence="8"),
                   dict(integrityHistory=[dict(first="2", last="2", epoch=1, reliableFromSequence="10"), dict(first="11", last="11", epoch=2)]),
                   dict(integrityHistory=[dict(first="2", last="2", epoch=2, reliableFromSequence="4"), dict(first="7", last="7", epoch=1)]),
                   dict(integrityHistory=[dict(first="2", last="2", epoch=1, reliableFromSequence="4"), dict(first="3", last="7", epoch=2)]),
                   dict(gaps=[dict(first="5", last="5", reason="NewUnrepresentedFailure")])]
        for change in changes:
            with self.subTest(change=change):
                state = dict(original, **change)
                self.db.execute("UPDATE metadata SET body=? WHERE kind='coverage.json'", (json.dumps(state),))
                result = self.fixture(first=4, last=6)
                self.assertFalse(result["complete"])
                self.assertTrue(any("IntegrityHistory" in gap for gap in result["gaps"]), result["gaps"])

    def test_versioned_empty_history_is_valid_only_for_a_clean_source(self):
        self.source(integrityHistoryVersion=1, integrityHistory=[], failureEpoch=0, recoveryPending=False); self.ingest()
        self.assertTrue(self.fixture(last=3)["complete"])
        state = json.loads(self.db.execute("SELECT body FROM metadata WHERE kind='coverage.json'").fetchone()[0])
        state["failure"] = "Unknown new disk failure"
        self.db.execute("UPDATE metadata SET body=? WHERE kind='coverage.json'", (json.dumps(state),))
        self.assertFalse(self.fixture(last=3)["complete"])

    def test_no_new_record_flush_failure_marker_preserves_the_flushed_prefix(self):
        self.source(failure="Metadata flush failed", failureFirstSequence="4", failureLastSequence="4", failureEpoch=1,
                    recoveryPending=True, complete=False, tailUnknown=True, gaps=[],
                    integrityHistoryVersion=1, integrityHistory=[dict(first="4", last="4", epoch=1, tailMarker=True)])
        self.ingest()
        result = self.fixture(last=3)
        self.assertTrue(result["complete"], result["gaps"])
        self.assertFalse(self.fixture()["complete"], "An unproduced pending tail marker still blocks an open-ended claim")
        state = json.loads(self.db.execute("SELECT body FROM metadata WHERE kind='coverage.json'").fetchone()[0])
        for first, last, flushed in (("5", "5", "3"), ("4", "5", "3"), ("2", "4", "3"), ("4", "4", "2")):
            with self.subTest(first=first, last=last, flushed=flushed):
                changed = dict(state, flushed=flushed, integrityHistory=[dict(first=first, last=last, epoch=1)],
                               gaps=[dict(first=first, last=last, reason="WriteFailure")])
                self.db.execute("UPDATE metadata SET body=? WHERE kind='coverage.json'", (json.dumps(changed),))
                result = self.fixture(last=2)
                self.assertFalse(result["complete"])
                self.assertTrue(any("InvalidIntegrityHistory" in gap for gap in result["gaps"]), result["gaps"])

    def test_tail_marker_can_be_replaced_by_the_next_durable_checkpoint(self):
        rows = self.records()
        rows.append(dict(stage="replay.checkpoint", engine="*", input=dict(engines=[rows[0]["input"]])))
        self.source(records=rows, failure="Metadata flush failed", failureFirstSequence="4", failureLastSequence="4", failureEpoch=1,
                    recoveryPending=False, reliableFromSequence="4", gaps=[],
                    integrityHistoryVersion=1, integrityHistory=[dict(first="4", last="4", epoch=1, reliableFromSequence="4", tailMarker=True)])
        self.ingest()
        self.assertTrue(self.fixture(last=3)["complete"])
        result = self.fixture(first=4, last=4)
        self.assertTrue(result["complete"], result["gaps"])
        state = json.loads(self.db.execute("SELECT body FROM metadata WHERE kind='coverage.json'").fetchone()[0])
        state["integrityHistory"][0]["tailMarker"] = False
        self.db.execute("UPDATE metadata SET body=? WHERE kind='coverage.json'", (json.dumps(state),))
        result = self.fixture(first=4, last=4)
        self.assertFalse(result["complete"])
        self.assertTrue(any("InvalidIntegrityHistory" in gap for gap in result["gaps"]), result["gaps"])

    def test_real_lost_record_cannot_be_hidden_as_an_empty_tail_marker(self):
        self.source(failure="WriteFailure", failureEpoch=1, recoveryPending=False, reliableFromSequence="3",
                    gaps=[dict(first="3", last="3", reason="WriteFailure")], integrityHistoryVersion=1,
                    integrityHistory=[dict(first="3", last="3", epoch=1, reliableFromSequence="3", tailMarker=True)])
        self.ingest()
        self.assertFalse(self.fixture(first=3, last=3)["complete"])

    def test_strict_cli_accepts_the_confirmed_window_between_failure_epochs(self):
        self.recovered_epoch_source(); self.db.commit()
        for command in (["coverage", "--capture", "capture", "--strict"], ["player-output", "--player", "9", "--strict"],
                        ["extract", "--capture", "capture", "--engine", "damage-1", "--output", str(self.root / "epochs-fixture.json")]):
            with self.subTest(command=command):
                result = subprocess.run([sys.executable, "-B", str(Path(evidence.__file__)), "--db", str(self.db_path),
                                         *command, "--first", "4", "--last", "6"], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr + result.stdout)

    def test_cli_incomplete_extract_writes_fixture_and_returns_three(self):
        self.source(coverage=False); self.ingest(); self.db.commit()
        output = self.root / "fixture.json"
        process = subprocess.run([sys.executable, "-B", str(Path(evidence.__file__)), "--db", str(self.db_path), "extract",
                                  "--capture", "capture", "--engine", "damage-1", "--last", "3", "--output", str(output)],
                                 text=True, capture_output=True)
        self.assertEqual(3, process.returncode, process.stderr)
        self.assertFalse(json.loads(output.read_text())["complete"])


if __name__ == "__main__":
    unittest.main()
