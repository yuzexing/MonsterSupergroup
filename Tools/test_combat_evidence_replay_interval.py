import argparse
import unittest

import CombatEvidence as evidence
import test_combat_evidence_integrity as integrity_tests


class ReplayIntervalContractTests(unittest.TestCase):
    setUp = integrity_tests.IntervalIntegrityTests.setUp
    tearDown = integrity_tests.IntervalIntegrityTests.tearDown
    source = integrity_tests.IntervalIntegrityTests.source
    ingest = integrity_tests.IntervalIntegrityTests.ingest

    @staticmethod
    def records():
        return [
            dict(stage="replay.engine_checkpoint", engine="status-1", input=dict(engine="status-1", domain="status", state=dict(version=1))),
            dict(stage="replay.input", engine="status-1", operation="Advance", input=[.01]),
            dict(stage="replay.output", engine="status-1", operation="Advance", outcome="Completed"),
        ]

    def checked(self, rows, first=1, **changes):
        self.source(records=rows, **changes); self.ingest()
        return evidence.query(self.db, argparse.Namespace(capture="capture", first=first, last=len(rows), limit=100))

    def test_valid_bounded_call_uses_prior_checkpoint_with_open_tail(self):
        self.assertTrue(self.checked(self.records(), first=2, tailUnknown=True, complete=False)["complete"])

    def test_missing_completion_cannot_pass_strict_query(self):
        self.assertFalse(self.checked(self.records()[:-1])["complete"])

    def test_missing_input_cannot_pass_even_with_contiguous_sequences(self):
        self.assertFalse(self.checked([self.records()[0], self.records()[2]])["complete"])

    def test_missing_argument_payload_cannot_pass(self):
        rows = self.records(); rows[1].pop("input")
        self.assertFalse(self.checked(rows)["complete"])

    def test_aborted_completion_cannot_pass(self):
        rows = self.records(); rows[2]["outcome"] = "Aborted"
        self.assertFalse(self.checked(rows)["complete"])

    def test_mismatched_operation_cannot_pass(self):
        rows = self.records(); rows[2]["operation"] = "Clear"
        self.assertFalse(self.checked(rows)["complete"])

    def test_empty_checkpoint_cannot_certify_a_call(self):
        rows = self.records(); rows[0] = dict(stage="replay.checkpoint", engine="*", input=dict(engines=[]))
        self.assertFalse(self.checked(rows)["complete"])

    def test_call_without_checkpoint_cannot_pass(self):
        self.assertFalse(self.checked(self.records()[1:])["complete"])

    def test_semantic_input_cannot_disappear(self):
        rows = integrity_tests.IntervalIntegrityTests.records(self); rows[1].pop("input")
        self.assertFalse(self.checked(rows)["complete"])

    def test_plain_process_interval_does_not_require_an_engine(self):
        self.assertTrue(self.checked([dict(stage="process.start", outcome="Started")])["complete"])

    def test_fresh_checkpoint_can_start_after_an_unfinished_historical_call(self):
        rows = self.records()[:-1] + self.records()
        self.assertTrue(self.checked(rows, first=3)["complete"])

    def test_later_call_needs_checkpoint_after_an_earlier_incomplete_call(self):
        rows = self.records(); rows[2]["outcome"] = "Aborted"
        rows += self.records()[1:]
        self.assertFalse(self.checked(rows, first=4)["complete"])


if __name__ == "__main__":
    unittest.main()
