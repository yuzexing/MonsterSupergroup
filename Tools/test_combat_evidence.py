import argparse
import hashlib
import gzip
import json
import tempfile
import unittest
from pathlib import Path
import CombatEvidence as evidence


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.db = evidence.connect(self.root / "evidence.sqlite")

    def tearDown(self):
        self.db.close()
        self.temp.cleanup()

    def source(self, peer, records):
        path = self.root / peer / "run" / "1" / "sources" / "capture"
        path.mkdir(parents=True, exist_ok=True)
        with (path / "events-00001.jsonl").open("w", encoding="utf-8") as f:
            for i, record in enumerate(records, 1):
                f.write(json.dumps(dict(schemaVersion=1, captureId="capture", recordSequence=str(i), runId="run", round=1, **record)) + "\n")
        return path

    def test_duplicate_copies_are_not_duplicate_executions(self):
        records = [{"eventId": "18446744073709551615", "stage": "gateway.decision", "outcome": "Accepted", "target": 7}]
        self.source("host", records); self.source("client", records)
        evidence.import_roots(self.db, [self.root / "host", self.root / "client"])
        result = evidence.query(self.db, argparse.Namespace(event="18446744073709551615", limit=50))
        self.assertEqual(1, len(result["records"]))
        self.assertEqual(2, len(result["records"][0]["references"]))
        self.assertIn("owner.hit", result["missingStages"])

    def test_truncated_tail_and_missing_blob_are_visible(self):
        path = self.source("host", [{"stage": "replay.input", "engine": "gateway-1", "operation": "ProcessBatch", "inputRef": "inputs/missing.json.gz"}])
        with (path / "events-00001.jsonl").open("ab") as f: f.write(b'{"partial":')
        evidence.import_roots(self.db, [self.root / "host"])
        result = evidence.query(self.db, argparse.Namespace(resolve_inputs=True, limit=50))
        self.assertEqual("TruncatedOrOversizedLine", result["coverage"]["issues"][0]["reason"])
        self.assertTrue(result["records"][0]["evidenceGap"].startswith("MissingBlob"))

    def test_extract_requires_paired_input_and_output(self):
        self.source("host", [
            {"stage": "replay.engine_checkpoint", "engine": "gateway-1", "input": {"engine": "gateway-1", "domain": "gateway", "state": {"version": 1}}},
            {"stage": "replay.input", "engine": "gateway-1", "operation": "StopCombat", "input": []},
            {"stage": "replay.output", "engine": "gateway-1", "operation": "StopCombat", "outcome": "Completed"},
            {"stage": "replay.input", "engine": "gateway-1", "operation": "ResetForNextRun", "input": []}])
        evidence.import_roots(self.db, [self.root / "host"])
        good = evidence.extract(self.db, "capture", "gateway-1", last=3)
        bad = evidence.extract(self.db, "capture", "gateway-1")
        self.assertTrue(good["complete"])
        self.assertFalse(bad["complete"])
        self.assertEqual(1, len(good["steps"]))

    def test_hash_is_validated_and_html_is_escaped(self):
        path = self.source("host", [{"stage": "<script>evil()</script>", "role": "Server"}])
        folder = path / "inputs"; folder.mkdir()
        (folder / "bad.json.gz").write_bytes(gzip.compress(b'{}'))
        evidence.import_roots(self.db, [self.root / "host"])
        result = evidence.query(self.db, argparse.Namespace(limit=10))
        self.assertEqual("InvalidBlob", result["coverage"]["issues"][0]["reason"])
        self.assertNotIn("<script>evil()", evidence.viewer(result))

    def test_removed_runs_remain_visible_in_coverage(self):
        (self.root / "retention.jsonl").write_text(json.dumps({"runId": "old-run", "reason": "GlobalCapacityRetention"}) + "\n", encoding="utf-8")
        evidence.import_roots(self.db, [self.root])
        audit = [row for row in evidence.coverage(self.db)["sources"] if row["kind"] == "retention.jsonl"]
        self.assertEqual("old-run", audit[0]["body"]["runId"])

    def test_event_query_includes_actual_batch_membership(self):
        self.source("host", [
            {"stage": "collector.drain", "batchSequence": 1, "input": {"Results": [{"EventId": "42"}]}},
            {"stage": "network.submit", "batchSequence": 1, "input": {"Results": [{"EventId": "42"}]}},
            {"stage": "network.submit", "batchSequence": 1, "input": {"Results": [{"EventId": "43"}]}},
            {"stage": "gateway.decision", "eventId": "42", "outcome": "Rejected", "reason": "DuplicateEvent"}])
        evidence.import_roots(self.db, [self.root / "host"])
        result = evidence.query(self.db, argparse.Namespace(event="42", limit=20))
        self.assertEqual(["1", "2", "4"], [r["recordSequence"] for r in result["records"]])
        self.assertNotIn("network.submit", result["missingStages"])


if __name__ == "__main__":
    unittest.main()
