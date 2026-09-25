import argparse
import base64
import hashlib
import gzip
import json
import math
import struct
import tempfile
import unittest
from pathlib import Path
import CombatEvidence as evidence


class EvidenceTests(unittest.TestCase):
    def test_compact_block_expands_exact_time_steps_and_validates_hash(self):
        calls = []
        for index, delta in enumerate((0.007, 0.0069444445, 0.25)):
            calls.append(dict(schemaVersion=2, captureId="capture", runId="run", round=1,
                              recordSequence=str(index * 2 + 1), stage="replay.call", engine="replica-1",
                              operation="controller.7.Advance", input=[delta],
                              completion=dict(sequence=str(index * 2 + 2), utc="2026-09-22T00:00:00Z",
                                              monotonic=index / 144, network=index / 144, frame=index,
                                              fixedStep=0, estimatedBytes=512)))
        raw = ("\n".join(json.dumps(v) for v in calls) + "\n").encode()
        block = dict(schemaVersion=2, encoding="gzip-jsonl-v1", first="1", last="6", count=6,
                     hash=hashlib.sha256(raw).hexdigest(), data=base64.b64encode(gzip.compress(raw)).decode())
        records = evidence.expand_records(json.dumps(block))
        self.assertEqual([r["input"][0] for r in records[::2]], [0.007, 0.0069444445, 0.25])
        self.assertEqual([r["recordSequence"] for r in records], list(map(str, range(1, 7))))
        self.assertTrue(all(r["stage"] == "replay.output" and r["outcome"] == "Completed" for r in records[1::2]))
        block["hash"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "checksum"):
            evidence.expand_records(json.dumps(block))

    def test_v2_direct_record_and_v1_record_remain_readable(self):
        for version in (1, 2):
            self.assertEqual(evidence.expand_records(json.dumps({"schemaVersion": version, "recordSequence": "9"}))[0]["recordSequence"], "9")

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
        (path / "coverage.json").write_text(json.dumps(dict(schemaVersion=1, captureId="capture", runId="run", round=1,
            produced=str(len(records)), written=str(len(records)), flushed=str(len(records)), complete=True, tailUnknown=False,
            gaps=[], failure=None)), encoding="utf-8")
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

    def test_business_view_filters_before_limit_and_keeps_coverage_brief(self):
        self.source("host", [
            {"stage": "replay.input", "engine": "gateway-1"},
            {"stage": "movement.receive", "source": 2, "target": 4, "after": {"position": {"x": 1, "y": 2}}},
            {"stage": "owner.attack_started", "source": 2, "eventId": "42", "outcome": "Created", "input": {"weaponId": 7}},
            {"stage": "owner.damage_calculation", "source": 2, "target": 4, "eventId": "43", "after": {"requestedDamage": 19}}])
        evidence.import_roots(self.db, [self.root / "host"])
        attack = evidence.query(self.db, argparse.Namespace(category="attack", capture="capture", run="run", round=1, limit=1))
        self.assertEqual(["owner.attack_started"], [r["stage"] for r in attack["records"]])
        self.assertFalse(attack["truncated"])
        attack["coverage"]["intervals"][0]["gaps"] = [{"reason": "QueueOverload", "first": i} for i in range(2000)]
        page = evidence.viewer(attack)
        self.assertIn("战斗证据 · 攻击", page)
        self.assertIn("开始攻击；武器 7", page)
        self.assertIn("缺口 2000 处", page)
        self.assertLess(len(page), 20000)
        damage = evidence.query(self.db, argparse.Namespace(category="damage", capture="capture", run="run", round=1, limit=1))
        self.assertEqual(["owner.damage_calculation"], [r["stage"] for r in damage["records"]])

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

    def test_player_output_keeps_damage_perspectives_and_physics_boundary(self):
        self.source("host", [
            {"stage": "owner.attack", "source": 9, "eventId": "40", "outcome": "Produced"},
            {"stage": "owner.damage_calculation", "source": 9, "target": 7, "eventId": "42", "rootEventId": "40", "input": {"requestedDamage": 20}},
            {"stage": "owner.damage", "source": 9, "target": 7, "eventId": "42", "outcome": "Applied", "after": {"appliedDamage": 12}},
            {"stage": "ledger.apply", "source": 9, "target": 7, "eventId": "42", "after": {"Health": 0}},
            {"stage": "statistics.damage", "source": 9, "eventId": "42", "input": {"damage": 20}},
            {"stage": "owner.attack", "source": 10, "eventId": "50", "outcome": "Produced"}])
        evidence.import_roots(self.db, [self.root / "host"])
        result = evidence.history(self.db, argparse.Namespace(command="player-output", player="9", limit=50))
        self.assertEqual(5, len(result["records"]))
        self.assertEqual({"calculation", "prediction", "canonical", "statistics"}, set(result["damagePerspectives"]))
        self.assertTrue(all(r["references"] for r in result["records"]))
        self.assertIn("PhysicsBoundaryNotReplayable", result["limitations"])

    def test_dot_history_keeps_source_revision_tick_and_missing_content(self):
        self.source("host", [
            {"stage": "status.tick", "source": 9, "target": 7, "statusInstanceId": "18446744073709551615", "applicationRevision": 1, "tickIndex": 3, "eventId": "42"},
            {"stage": "status.tick", "source": 10, "target": 7, "statusInstanceId": "8", "tickIndex": 3},
            {"stage": "status.tick", "source": 9, "target": 7, "inputRef": "inputs/missing.json.gz", "statusInstanceId": "18446744073709551615", "applicationRevision": 2, "tickIndex": 1}])
        evidence.import_roots(self.db, [self.root / "host"])
        result = evidence.history(self.db, argparse.Namespace(command="dot", status="18446744073709551615", limit=50))
        self.assertEqual(["1", "3"], [r["recordSequence"] for r in result["records"]])
        self.assertEqual([1, 2], [r["applicationRevision"] for r in result["records"]])
        self.assertTrue(any(g["reason"].startswith("MissingBlob") for g in result["evidenceGaps"]))

    def test_connection_history_includes_epoch_and_nested_telemetry(self):
        self.source("host", [
            {"stage": "network.send", "connectionEpoch": 4, "input": {"connectionId": 2, "batchSequence": 8}, "outcome": "Failed", "reason": "QueueFull"},
            {"stage": "network.metrics", "input": {"connections": [{"connectionId": 2, "pendingBytes": 1200}, {"connectionId": 3, "pendingBytes": 0}]}},
            {"stage": "network.disconnected", "input": {"connectionId": 3}}])
        evidence.import_roots(self.db, [self.root / "host"])
        result = evidence.history(self.db, argparse.Namespace(command="connection", connection="2", limit=50))
        self.assertEqual(["1", "2"], [r["recordSequence"] for r in result["records"]])
        self.assertEqual("QueueFull", result["firstObservedFailure"]["reason"])
        self.assertIn("ConnectionIdIsLocalToCapture", result["limitations"])

    def test_unsupported_block_inner_version_does_not_import_partial_records(self):
        records = [dict(schemaVersion=2, captureId="capture", recordSequence="1"), dict(schemaVersion=99, captureId="capture", recordSequence="2")]
        raw = ("\n".join(json.dumps(r) for r in records) + "\n").encode()
        block = dict(schemaVersion=2, encoding="gzip-jsonl-v1", first="1", last="2", count=2,
                     hash=hashlib.sha256(raw).hexdigest(), data=base64.b64encode(gzip.compress(raw)).decode())
        with self.assertRaisesRegex(ValueError, "schema"):
            evidence.expand_records(json.dumps(block))

    def test_semantic_damage_and_statistics_records_are_replay_inputs(self):
        self.source("host", [
            {"stage": "replay.engine_checkpoint", "engine": "damage-1", "input": {"engine": "damage-1", "domain": "damage", "state": {"version": 1}}},
            {"stage": "owner.damage_calculation", "engine": "damage-1", "operation": "Calculate", "input": {"criticalRoll": 0.1}, "after": {"requestedDamage": 20}},
            {"stage": "owner.damage_calculation", "engine": "damage-1", "operation": "Calculate", "input": {"criticalRoll": 0.9}, "after": {"requestedDamage": 10}}])
        evidence.import_roots(self.db, [self.root / "host"])
        fixture = evidence.extract(self.db, "capture", "damage-1")
        self.assertTrue(fixture["complete"])
        self.assertEqual([0.1, 0.9], [s["arguments"][0]["criticalRoll"] for s in fixture["steps"]])
        self.assertEqual([20, 10], [s["expected"]["requestedDamage"] for s in fixture["steps"]])

    def test_batch_blob_available_only_on_later_peer_is_indexed(self):
        raw = json.dumps({"Results": [{"EventId": "42", "SourceEntityId": 9}]}).encode()
        digest = hashlib.sha256(raw).hexdigest()
        self.source("host", [{"stage": "network.submit", "inputRef": "inputs/" + digest + ".json.gz"}])
        folder = self.root / "client" / "inputs"; folder.mkdir(parents=True)
        (folder / (digest + ".json.gz")).write_bytes(gzip.compress(raw))
        evidence.import_roots(self.db, [self.root / "host", self.root / "client"])
        self.assertEqual(1, len(evidence.query(self.db, argparse.Namespace(event="42", limit=10))["records"]))
        self.assertEqual(1, len(evidence.history(self.db, argparse.Namespace(command="player-output", player="9", limit=10))["records"]))

    def test_transport_failure_correlates_by_batch_and_payload_not_batch_number_alone(self):
        member = dict(messageId=2, kind="Command", entity=9, component=0, function=123, payloadHash="abc", correlation="PayloadIdentity")
        self.source("host", [
            {"stage": "network.submit", "source": 9, "batchSequence": 1, "input": {"Results": [{"EventId": "42"}]}},
            {"stage": "network.message", "source": 9, "batchSequence": 1, "input": {"member": member}},
            {"stage": "network.transport", "outcome": "Failed", "reason": "k_EResultLimitExceeded", "input": {"steamConnection": "2", "members": [member]}},
            {"stage": "network.message", "source": 10, "batchSequence": 1, "input": {"member": dict(member, entity=10, payloadHash="def")}}])
        evidence.import_roots(self.db, [self.root / "host"])
        records = evidence.query(self.db, argparse.Namespace(event="42", limit=20))["records"]
        self.assertEqual(["1", "2", "3"], [r["recordSequence"] for r in records])
        self.assertEqual(1, len(evidence.history(self.db, argparse.Namespace(command="connection", steam_connection="2", limit=20))["records"]))
        self.assertEqual(0, len(evidence.history(self.db, argparse.Namespace(command="connection", connection="2", limit=20))["records"]))

    def test_legacy_dot_and_json_string_performance_are_queryable(self):
        self.source("host", [
            {"stage": "status.tick", "input": {"InstanceId": {"Value": "18446744073709551615"}, "TickIndex": 3}},
            {"stage": "performance.snapshot", "input": json.dumps({"connections": [{"connectionId": 2, "pendingReliableBytes": 8}]})}])
        evidence.import_roots(self.db, [self.root / "host"])
        self.assertEqual(1, len(evidence.history(self.db, argparse.Namespace(command="dot", status="18446744073709551615", limit=10))["records"]))
        self.assertEqual(1, len(evidence.history(self.db, argparse.Namespace(command="connection", connection="2", limit=10))["records"]))

    def test_columnar_advances_preserve_boundaries_abort_and_utc_precision(self):
        body = dict(engines=[["replica", "replica-1", "controller.7.Advance"]], boundaries=[{"currentTime": 0.5}],
                    rows=[["1", "637134336001234567", 1.0, 2.0, 10, 4, 0, 0.0069444445, 0, 0],
                          ["2", "637134336001234569", 1.1, 2.1, 10, 4, 2, 0, 0, -1]])
        raw = json.dumps(body).encode()
        envelope = dict(schemaVersion=2, encoding="advance-columns-v1", captureId="capture", runId="run", round=1,
                        first="1", last="2", count=2, hash=hashlib.sha256(raw).hexdigest(),
                        data=base64.b64encode(gzip.compress(raw)).decode())
        records = evidence.expand_records(json.dumps(envelope))
        self.assertEqual("2020-01-01T00:00:00.1234567Z", records[0]["utc"])
        self.assertEqual([0.0069444445], records[0]["input"])
        self.assertEqual({"currentTime": 0.5}, records[0]["before"])
        self.assertEqual("ExceptionOrEarlyExit", records[1]["reason"])
        self.assertTrue(records[1]["critical"])
        body["rows"][1][8] = -1
        raw = json.dumps(body).encode(); envelope.update(hash=hashlib.sha256(raw).hexdigest(), data=base64.b64encode(gzip.compress(raw)).decode())
        with self.assertRaisesRegex(ValueError, "engine"):
            evidence.expand_records(json.dumps(envelope))

    def test_status_tick_wrapper_does_not_change_replay_tick_assertion(self):
        self.source("host", [
            {"stage": "replay.engine_checkpoint", "engine": "status-1", "input": {"engine": "status-1", "domain": "status", "state": {}}},
            {"stage": "replay.input", "engine": "status-1", "operation": "Advance", "input": [0.1]},
            {"stage": "status.tick", "engine": "status-1", "outcome": "Ignored", "reason": "ExecutionPolicyRejected", "input": {"tick": {"TickIndex": 2}, "immediate": False}},
            {"stage": "status.tick", "engine": "status-1", "outcome": "Executed", "input": {"tick": {"TickIndex": 3}, "immediate": False}},
            {"stage": "replay.output", "engine": "status-1", "operation": "Advance", "outcome": "Completed"}])
        evidence.import_roots(self.db, [self.root / "host"])
        self.assertEqual([{"TickIndex": 3}], evidence.extract(self.db, "capture", "status-1")["steps"][0]["expectedTicks"])

    def test_replay_divergence_resolves_exact_source_record(self):
        self.source("host", [{"stage": "stats.damage", "source": 9, "eventId": "42", "input": {"value": 20}}])
        evidence.import_roots(self.db, [self.root / "host"])
        report = self.root / "replay-result.json"
        report.write_text(json.dumps(dict(reliable=True, passed=False, firstDivergence=0, record="capture:1", differencePath="$.totalDamage", expected=21, actual=20)), encoding="utf-8")
        result = evidence.history(self.db, argparse.Namespace(command="player-output", player="9", replay_report=str(report), limit=10))
        self.assertEqual("$.totalDamage", result["firstDivergence"]["differencePath"])
        self.assertEqual(1, result["firstDivergence"]["references"][0]["line"])

    def test_nested_shared_input_is_resolved_and_missing_dependency_blocks_replay(self):
        curve = {"Strength": 2, "CurveKeys": [{"time": 0, "value": 1}]}
        raw = json.dumps(curve).encode(); digest = hashlib.sha256(raw).hexdigest()
        reference = {"$evidenceRef": "inputs/" + digest + ".json.gz"}
        path = self.source("host", [
            {"stage": "replay.engine_checkpoint", "engine": "authority-1", "input": {"engine": "authority-1", "domain": "authority", "state": {}}},
            {"stage": "replay.input", "engine": "authority-1", "operation": "ApplyMovement", "input": [{"KnockbackSettings": reference}]},
            {"stage": "replay.output", "engine": "authority-1", "operation": "ApplyMovement", "outcome": "Completed"}])
        evidence.import_roots(self.db, [self.root / "host"])
        self.assertFalse(evidence.extract(self.db, "capture", "authority-1")["complete"])
        (path / "inputs").mkdir(); (path / "inputs" / (digest + ".json.gz")).write_bytes(gzip.compress(raw))
        evidence.import_roots(self.db, [self.root / "host"])
        fixture = evidence.extract(self.db, "capture", "authority-1")
        self.assertTrue(fixture["complete"])
        self.assertEqual(curve, fixture["steps"][0]["arguments"][0]["KnockbackSettings"])

    def test_shared_reference_shape_and_path_are_strict(self):
        with self.assertRaisesRegex(ValueError, "InvalidSharedReference"):
            evidence.payload(self.db, {"input": {"$evidenceRef": "../secret.json.gz"}})
        value = {"$evidenceRef": "ordinary-business-data", "other": 1}
        self.assertEqual(value, evidence.payload(self.db, {"input": value}))

    def test_shared_reference_cycles_are_rejected(self):
        digest = "a" * 64
        self.db.execute("INSERT INTO blobs VALUES(?,?)", (digest, json.dumps({"$evidenceRef": "inputs/" + digest + ".json.gz"})))
        with self.assertRaisesRegex(ValueError, "SharedReferenceCycle"):
            evidence.payload(self.db, {"input": {"$evidenceRef": "inputs/" + digest + ".json.gz"}})

    def binary_advances(self):
        raw = bytearray(struct.pack("<IHHH", 0x32445641, 3, 1, 1))
        for value in ("replica", "replica-1", "controller.7.Advance"):
            encoded = value.encode(); raw.extend(struct.pack("<H", len(encoded))); raw.extend(encoded)
        raw.extend(struct.pack("<BHHIII", 1 | 2 | 16 | 32, 2, 4, 99, 9, 10))
        rows = [(1, 637134336001234567, 1.0, 2.0, 10, 4, 0, 1/144, 0, 0),
                (2, 637134336001234568, 1.1, 2.1, 10, 4, 1, 0, 0, -1),
                (3, 637134336001234569, 1.2, 2.2, 10, 4, 2, 0, 0, -1)]
        for row in rows: raw.extend(struct.pack("<QqddiiBfHh", *row))
        return bytes(raw)

    def binary_envelope(self, raw):
        return json.dumps(dict(schemaVersion=2, encoding="advance-binary-v1", captureId="capture", runId="run", round=1,
                               first="1", last="3", count=3, hash=hashlib.sha256(raw).hexdigest(),
                               data=base64.b64encode(gzip.compress(raw)).decode()))

    def test_binary_advances_preserve_exact_ticks_boundary_and_phases(self):
        records = evidence.expand_records(self.binary_envelope(self.binary_advances()))
        self.assertEqual(["replay.input", "replay.output", "replay.output"], [r["stage"] for r in records])
        self.assertEqual("2020-01-01T00:00:00.1234567Z", records[0]["utc"])
        self.assertEqual(struct.pack("<f", 1/144), struct.pack("<f", records[0]["input"][0]))
        self.assertEqual({"slot": 2, "epoch": 4, "next": 99}, records[0]["before"]["ids"])
        self.assertEqual(9, records[0]["before"]["localPlayer"])
        self.assertTrue(records[0]["before"]["server"])
        self.assertEqual("Completed", records[1]["outcome"])
        self.assertEqual("ExceptionOrEarlyExit", records[2]["reason"])

    def test_binary_advances_reject_tail_truncation_flags_counts_and_indices(self):
        good = self.binary_advances()
        variants = [good + b'x', good[:-1], struct.pack("<IHHH", 0x32445641, 129, 1, 0),
                    b'NOPE' + good[4:], good[:10] + struct.pack('<H', 1025) + good[12:]]
        bad_index = bytearray(good); bad_index[-4:-2] = struct.pack('<H', 128); variants.append(bytes(bad_index))
        bad_phase = bytearray(good); bad_phase[-9] = 3; variants.append(bytes(bad_phase))
        # Header plus engine dictionary ends immediately before the boundary flags.
        flag_offset = 10 + sum(2 + len(v.encode()) for v in ("replica", "replica-1", "controller.7.Advance"))
        bad_flags = bytearray(good); bad_flags[flag_offset] = 128; variants.append(bytes(bad_flags))
        for raw in variants:
            with self.subTest(length=len(raw)), self.assertRaises(ValueError):
                evidence.expand_records(self.binary_envelope(raw))

    def test_binary_float32_format_roundtrips_bits_without_double_noise(self):
        for value in (0.1, 1/144, 0.25, -0.0, 1.2345678, 1.234567, 1.401298464e-45, 3.402823466e38):
            bits = struct.pack('<f', value); single = struct.unpack('<f', bits)[0]
            converted = evidence.unity_single_json(single)
            self.assertEqual(bits, struct.pack('<f', converted))
        self.assertEqual(0.1, evidence.unity_single_json(struct.unpack('<f', struct.pack('<f', 0.1))[0]))
        self.assertEqual("NaN", evidence.unity_single_json(math.nan))
        self.assertEqual("Infinity", evidence.unity_single_json(math.inf))

    def test_binary_codec_matches_actual_unity_csharp_golden(self):
        folder = Path(__file__).parent / "fixtures" / "combat-evidence-v2"
        records = evidence.expand_records((folder / "advance-binary.jsonl").read_text(encoding="utf-8-sig"))
        expected = json.loads((folder / "advance-expanded.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(expected, records)


if __name__ == "__main__":
    unittest.main()
