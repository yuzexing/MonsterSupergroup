import copy
import argparse
import contextlib
import gzip
import hashlib
import io
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

import CombatEvidenceMixedBenchmark as mixed


class MixedFixtureTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.execute("CREATE TABLE blobs(hash TEXT PRIMARY KEY,body TEXT)")
        self.pairs, self.groups = {}, {}

    def tearDown(self):
        self.db.close()

    def reference(self, value):
        text = mixed.compact(value)
        digest = hashlib.sha256(text.encode()).hexdigest()
        self.db.execute("INSERT OR IGNORE INTO blobs VALUES(?,?)", (digest, text))
        return {"$evidenceRef": "inputs/" + digest + ".json.gz"}

    def payload(self, record, sequence=1, field="input"):
        return mixed.prepare_payload(self.db, record, field, self.pairs, self.groups, sequence)

    def test_canonical_receive_apply_share_one_explicit_group(self):
        value = {"ServerSequence": 5, "Entities": [{"EntityId": 7}], "Statuses": []}
        ref = self.reference(value)
        first, markers = self.payload({"stage": "network.canonical", "input": {"incomingRound": 1, "batch": ref}}, 10)
        second, following = self.payload({"stage": "replay.input", "operation": "Apply", "input": [ref]}, 11)
        self.assertEqual(first["batch"], value)
        self.assertEqual(second, [value])
        self.assertEqual(markers[0]["group"], following[0]["group"])
        self.assertEqual(self.groups[markers[0]["group"]]["uses"], 2)
        self.assertFalse(self.pairs)

    def test_writer_knockback_reference_is_expanded_not_marked_shared(self):
        value = {"CurveKeys": [{"time": 0, "value": 1}], "Distance": 2}
        result, markers = self.payload({"stage": "movement.submit", "input": {"Runtime": {"KnockbackSettings": self.reference(value)}}})
        self.assertEqual(result["Runtime"]["KnockbackSettings"], value)
        self.assertFalse(markers)

    def test_unknown_shared_location_fails_instead_of_inlining(self):
        ref = self.reference({"ServerSequence": 1, "Entities": []})
        with self.assertRaisesRegex(ValueError, "UnsupportedSharedShape"):
            self.payload({"stage": "unknown", "input": ref})

    def test_shared_apply_without_receive_fails(self):
        ref = self.reference({"ServerSequence": 1, "Entities": []})
        with self.assertRaisesRegex(ValueError, "UnpairedSharedApply"):
            self.payload({"stage": "replay.input", "operation": "Apply", "input": [ref]})

    def test_overlapping_receive_is_not_content_hash_merged(self):
        ref = self.reference({"ServerSequence": 1, "Entities": []})
        record = {"stage": "network.canonical", "input": {"incomingRound": 1, "batch": ref}}
        self.payload(record)
        with self.assertRaisesRegex(ValueError, "OverlappingSharedReceive"):
            self.payload(record, 2)

    def test_malformed_reference_and_missing_blob_fail(self):
        for ref, error in [({"$evidenceRef": "../secret"}, "Malformed"),
                           ({"$evidenceRef": "inputs/" + "f" * 64 + ".json.gz"}, "MissingBlob")]:
            with self.assertRaisesRegex(ValueError, error):
                self.payload({"stage": "network.canonical", "input": {"batch": ref}})

    def test_mapping_is_deterministic_and_separates_identity_without_losing_precision(self):
        source = {"fixtureId": "client", "cycleSeconds": 75.2, "sequenceSpan": 164713, "frameSpan": 4500, "fixedStepSpan": 3750}
        first = mixed.mapping(source, 600)
        self.assertEqual(first, mixed.mapping(copy.deepcopy(source), 600))
        self.assertEqual(len({x["captureId"] for x in first}), len(first))
        self.assertEqual(first[-1]["endSecondsExclusive"], 600)
        self.assertEqual(first[2]["sequenceOffset"], 329426)
        self.assertEqual(mixed.normal({"id": "18446744073709551615"}), {"id": "18446744073709551615"})

    def test_source_envelope_selects_advance_and_rejects_sequence_holes(self):
        self.db.execute("CREATE TABLE copies(capture TEXT,seq TEXT,path TEXT,line INTEGER)")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "events.jsonl"
            path.write_text(json.dumps({"encoding": "advance-binary-v1"}) + "\n", encoding="utf-8")
            self.db.execute("INSERT INTO copies VALUES('a','7',?,1)", (str(path),))
            kinds, sources = mixed.source_encodings(self.db, "a", 7, 7)
            self.assertEqual(kinds, {7: "advance-binary-v1"})
            self.assertEqual(sources[0]["sha256"], mixed.digest(path))
            with self.assertRaisesRegex(ValueError, "SourceSequenceGap"):
                mixed.source_encodings(self.db, "a", 7, 8)

    def verification_case(self, directory, *, corrupt=False, pace=True, count=1, missing=(), extra=False, bad_watermark=False, rejection_guard="QueueLimit"):
        root = Path(directory)
        original = {"schemaVersion": 2, "captureId": "old", "runId": "oldrun", "round": 1,
                    "recordSequence": "1", "stage": "network.message", "utc": "2026-09-25T03:16:29.0928533Z",
                    "monotonicTime": 10.0, "networkTime": 9.0, "frame": 60, "fixedStep": 50,
                    "input": {"longId": "18446744073709551615", "value": .1234567890123456}}
        fixture = root / "records.jsonl.gz"
        expected = []
        for number in range(1, count + 1):
            record = copy.deepcopy(original)
            record["recordSequence"] = str(number)
            record["monotonicTime"] += (number - 1) * .001
            expected.append(record)
        with gzip.open(fixture, "wt", encoding="utf-8") as stream:
            for record in expected:
                stream.write(mixed.compact({"record": record, "kind": "record", "shared": []}) + "\n")
        decoder = Path(mixed.__file__).with_name("CombatEvidence.py").resolve()
        manifest = {"schemaVersion": 1, "fixtureId": "unit", "fixture": fixture.name,
                    "fixtureSha256": mixed.digest(fixture), "decoder": str(decoder), "decoderSha256": mixed.digest(decoder),
                    "cycleSeconds": 1, "sequenceSpan": count, "frameSpan": 1, "fixedStepSpan": 1, "firstMonotonic": 10.0}
        manifest_path = root / "manifest.json"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        output = root / "output"
        mapping = mixed.mapping(manifest, .5)[0]
        actual = []
        for number, record in enumerate(expected, 1):
            if number in missing:
                continue
            record = copy.deepcopy(record)
            record["captureId"], record["runId"] = mapping["captureId"], mapping["runId"]
            if corrupt and number == count:
                record["input"]["longId"] = "18446744073709551614"
            actual.append(record)
        actual_maximum = max((int(x["recordSequence"]) for x in actual), default=0)
        if extra:
            unexpected = copy.deepcopy(actual[-1])
            unexpected["recordSequence"] = str(count + 1)
            actual.append(unexpected)
        source = output / "capture" / mapping["runId"] / "1" / "sources" / mapping["captureId"]
        source.mkdir(parents=True)
        (source / "events-1.jsonl").write_text("".join(json.dumps(record) + "\n" for record in actual), encoding="utf-8")
        (source / "coverage.json").write_text(json.dumps({"schemaVersion": 2, "captureId": mapping["captureId"], "runId": mapping["runId"], "round": 1,
            "produced": str(count), "written": str(actual_maximum), "flushed": "0" if bad_watermark else str(actual_maximum), "complete": not missing, "tailUnknown": False,
            "dropped": len(missing), "gaps": [{"first": str(min(missing)), "last": str(max(missing)), "reason": "QueueOverload", "count": len(missing)}] if missing else []}), encoding="utf-8")
        (output / "benchmark.json").write_text(json.dumps({"fixtureSha256": manifest["fixtureSha256"], "durationSeconds": .5,
            "paceValid": pace, "joined": True, "observationExported": True, "attempted": count,
            "accepted": count - len(missing), "rejected": len(missing), "dropped": len(missing), "sharedCaptureFailures": 0,
            "pendingFinalBytes": 0, "memoryFinalBytes": 0}), encoding="utf-8")
        (output / "writer-observation.json").write_text(json.dumps({"countsBalanced": True, "unfinishedWorkItems": 0,
            "logicalRecords": {"attempts": count, "accepted": count - len(missing), "rejected": len(missing), "captureFailed": 0},
            "firstRejections": [{"guard": rejection_guard, "entry": "TryWriteAdvance"}] if missing else []}), encoding="utf-8")
        return argparse.Namespace(manifest=str(manifest_path), output=str(output))

    def test_independent_import_verifies_every_field_and_seven_digit_utc(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            args = self.verification_case(directory)
            self.assertEqual(mixed.verify(args), 0)
            report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
            self.assertEqual(report["matched"], 1)
            self.assertTrue(report["logicalPassed"])

    def preserved_clock_case(self, directory, corrupt_field=None):
        args = self.verification_case(directory)
        manifest_path, output = Path(args.manifest), Path(args.output)
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["recordClockMapping"] = "preserve-source"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        first_source = next((output / "capture").glob("*/*/sources/*"))
        first_record = json.loads((first_source / "events-1.jsonl").read_text(encoding="utf-8"))
        first_coverage = json.loads((first_source / "coverage.json").read_text(encoding="utf-8"))
        for cycle in mixed.mapping(manifest, 2.5)[1:]:
            record = copy.deepcopy(first_record)
            record.update(captureId=cycle["captureId"], runId=cycle["runId"], recordSequence=str(1 + cycle["sequenceOffset"]))
            if corrupt_field and cycle["cycle"] == 2:
                record[corrupt_field] = "2026-09-25T03:16:30.0928533Z" if corrupt_field == "utc" else record[corrupt_field] + 1
            source = output / "capture" / record["runId"] / "1" / "sources" / record["captureId"]
            source.mkdir(parents=True)
            (source / "events-1.jsonl").write_text(json.dumps(record) + "\n", encoding="utf-8")
            coverage = dict(first_coverage, captureId=record["captureId"], runId=record["runId"],
                            produced=record["recordSequence"], written=record["recordSequence"], flushed=record["recordSequence"])
            (source / "coverage.json").write_text(json.dumps(coverage), encoding="utf-8")
        benchmark = json.loads((output / "benchmark.json").read_text(encoding="utf-8"))
        benchmark.update(durationSeconds=2.5, attempted=3, accepted=3,
                         recordClockMapping="preserve-source", manifestSha256=mixed.digest(manifest_path))
        (output / "benchmark.json").write_text(json.dumps(benchmark), encoding="utf-8")
        observation = json.loads((output / "writer-observation.json").read_text(encoding="utf-8"))
        observation["logicalRecords"].update(attempts=3, accepted=3)
        (output / "writer-observation.json").write_text(json.dumps(observation), encoding="utf-8")
        return args

    def test_preserved_clocks_have_separate_continuous_dispatch_schedule(self):
        manifest = dict(fixtureId="unit", cycleSeconds=1.25, sequenceSpan=3, frameSpan=4,
                        fixedStepSpan=5, recordClockMapping="preserve-source")
        cycles = mixed.mapping(manifest, 3)
        self.assertEqual([row["startSeconds"] for row in cycles], [0, 1.25, 2.5])
        self.assertEqual([row["sequenceOffset"] for row in cycles], [0, 3, 6])
        self.assertTrue(all(row["timeOffsetSeconds"] == row["frameOffset"] == row["fixedStepOffset"] == 0 for row in cycles))
        manifest["recordClockMapping"] = "unknown"
        with self.assertRaisesRegex(ValueError, "UnsupportedRecordClockMapping"):
            mixed.mapping(manifest, 3)

    def test_preserved_clock_cycles_import_and_compare_all_original_fields(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            args = self.preserved_clock_case(directory)
            self.assertEqual(mixed.verify(args), 0)
            report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
            self.assertEqual(report["matched"], 3)
            self.assertEqual(report["recordClockMapping"], "preserve-source")
            self.assertEqual(report["scheduleMapping"][2]["startSeconds"], 2)

    def test_preserved_clock_policy_does_not_ignore_clock_or_frame_corruption(self):
        for field in ("monotonicTime", "networkTime", "utc", "frame", "fixedStep"):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
                args = self.preserved_clock_case(directory, field)
                self.assertEqual(mixed.verify(args), 1)
                report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
                self.assertEqual(report["differentRecords"], 1)
                self.assertIn(field, report["differences"][0]["fields"])

    def test_preserved_clock_verification_rejects_wrong_manifest_or_policy(self):
        for field, value, error in (("manifestSha256", "wrong", "BenchmarkManifestMismatch"),
                                    ("recordClockMapping", "offset", "BenchmarkClockMappingMismatch")):
            with tempfile.TemporaryDirectory() as directory:
                args = self.preserved_clock_case(directory)
                path = Path(args.output) / "benchmark.json"
                result = json.loads(path.read_text(encoding="utf-8"))
                result[field] = value
                path.write_text(json.dumps(result), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, error):
                    mixed.verify(args)

    def test_successful_close_cannot_hide_logical_corruption_or_bad_pacing(self):
        for corrupt, pace in ((True, True), (False, False)):
            with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
                args = self.verification_case(directory, corrupt=corrupt, pace=pace)
                self.assertEqual(mixed.verify(args), 1)
                report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
                self.assertFalse(report["passed"])

    def test_missing_samples_cannot_hide_later_corruption(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            args = self.verification_case(directory, count=24, missing=tuple(range(1, 23)), corrupt=True)
            self.assertEqual(mixed.verify(args), 1)
            report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
            self.assertEqual(len(report["differences"]), 20)
            self.assertEqual(report["missingRecords"], 22)
            self.assertEqual(report["differentRecords"], 1)
            self.assertEqual(report["unexpectedRecords"], 0)
            self.assertFalse(report["baselineQueueRejectionOnly"])

    def test_only_reconciled_queue_limit_loss_is_classified_as_baseline_overload(self):
        for guard, eligible in (("QueueLimit", True), ("BudgetReservation", False)):
            with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
                args = self.verification_case(directory, count=3, missing=(2,), rejection_guard=guard)
                self.assertEqual(mixed.verify(args), 1)
                report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
                self.assertEqual(report["baselineQueueRejectionOnly"], eligible)
                self.assertTrue(report["metadataWatermarksValid"])

    def test_readable_records_with_stale_flushed_watermark_do_not_pass(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            args = self.verification_case(directory, bad_watermark=True)
            self.assertEqual(mixed.verify(args), 1)
            report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
            self.assertEqual(report["matched"], 1)
            self.assertFalse(report["metadataWatermarksValid"])
            self.assertFalse(report["logicalPassed"])

    def test_unexpected_identity_record_is_counted(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            args = self.verification_case(directory, extra=True)
            self.assertEqual(mixed.verify(args), 1)
            report = json.loads((Path(args.output) / "verification.json").read_text(encoding="utf-8"))
            self.assertEqual(report["unexpectedRecords"], 1)
            self.assertFalse(report["baselineQueueRejectionOnly"])


if __name__ == "__main__":
    unittest.main()
