import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

import CombatEvidence as decoder
from CombatInvestigation import prepare_archive
from CombatInvestigationIndex import Investigation
from CombatInvestigationExport import IssueExporter, import_package
from CombatNetworkEvidence import file_hash, valid_metric


def event(seq, stage, data, outcome=None, time=None):
    return {"kind": "network_event", "record": {"schemaVersion": 2, "captureId": "capture-a", "recordSequence": str(seq),
        "runId": "run-a", "round": 1, "role": "Host", "utc": "2026-09-25T00:00:00Z", "monotonicTime": time if time is not None else seq,
        "stage": stage, "input": data, "outcome": outcome}}


def fixture(path, broken=False, missing_end=False):
    header = {"kind": "header", "schemaVersion": 3, "recordSequence": "0", "captureId": "capture-a",
              "buildInfo": '{"buildId":"test"}', "monotonicStart": 0, "networkCapabilities": {"version": 1},
              "config": {"evidenceMode": "off", "lightweightNetworkEnabled": True}}
    rows = [header]
    for instance, attempt, seq, target in (("connection-1", "1", 1, 42), ("connection-2", "1", 4, 43), ("connection-3", "1", 7, 44)):
        rows.append(event(seq, "network.connection", {"connectionInstance": instance, "connectionId": 8, "steamConnection": 99,
                                                      "remoteIdentity": "peer" if instance != "connection-2" else "other"}))
        fields = {"connectionInstance": instance, "direction": "Send", "attempt": attempt, "channel": 0 if target != 43 else 1,
                  "result": "k_EResultLimitExceeded", "messageNumber": None, "bytes": 50, "injected": True}
        rows.append(event(seq+1, "network.transport", fields, "Rejected", 12+seq))
        rows.append(event(seq+2, "network.batch", dict(fields, complete=not broken, members=[{"business": "damage", "payloadHash": "same",
            "routeEntity": 500, "parseFailure": "truncated" if broken else None,
            "entities": [] if broken else [{"entity": target, "source": 5, "eventId": str(900+target), "role": "DamageTarget"}]}]), time=12+seq))
    rows += [event(10, "network.transport", {"connectionInstance": "connection-1", "direction": "Send", "attempt": "2",
                "channel": 0, "result": "k_EResultOK", "messageNumber": "9007199254740993", "bytes": 50}, "Accepted", 25),
             event(11, "network.batch", {"connectionInstance": "connection-1", "direction": "Send", "attempt": "2", "channel": 0,
                "complete": True, "members": [{"payloadHash": "same", "entities": [{"entity": 77, "eventId": "977"}]}]}, time=25),
             {"kind": "sample", "captureId": "capture-a", "recordSequence": "12", "run": "run-a", "round": 1, "role": "Host", "time": 45,
              "frameMaxMs": 20, "frameCount": 60, "frameMeanMs": 16, "frameP95Ms": 20, "windowSeconds": 1, "playerCount": 2,
              "logQueuedBytes": 200000, "connections": [{"readSucceeded": False, "queueValid": False, "queueMilliseconds": 9.22e15,
               "pendingValid": False, "pendingReliableBytes": 999, "pendingUnreliableBytes": 0, "unacknowledgedBytes": 0}]}]
    if not missing_end:
        rows.append({"kind": "end", "captureId": "capture-a", "recordSequence": "13", "run": "run-a", "round": 1,
                     "normalClose": True, "time": 46, "captureFailures": 0, "writerFailures": 0, "parseFailures": 3 if broken else 0})
    path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
    Path(str(path)+".status.json").write_text('{"complete":true,"failure":""}', encoding="utf-8")


class LightweightNetworkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "source"; self.source.mkdir()
        self.raw = self.source / "network-diagnostics.jsonl"
        self.db = self.root / "analysis" / "investigation.sqlite"
        self.selection = dict(run="run-a", round=1, captures=["capture-a"], category="network_rejection")

    def tearDown(self):
        self.temp.cleanup()

    def automatic_fixture(self):
        network = self.source / "network"; network.mkdir()
        self.raw = network / "network.jsonl"
        fixture(self.raw)
        rows = [json.loads(line) for line in self.raw.read_text().splitlines()]
        rows[0].update(processId=123, buildGuid="build-guid", executablePath="C:/Game/Game.exe")
        rows[0]["config"].update(automaticCapture=True, appliedCaptureMode="network")
        self.raw.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        status = dict(state="Saved", localSaveCompleted=True, failure=None, mode="Network", actualSource="network",
                      processId=123, buildGuid="build-guid", executable="C:/Game/Game.exe", buildInfo='{"buildId":"test"}')
        (self.source / "session.json").write_text(json.dumps(status), encoding="utf-8")
        return status

    def test_automatic_saved_session_is_referenced_and_survives_offline_export(self):
        self.automatic_fixture()
        prepare_archive([self.source], self.db)
        with Investigation(self.db) as inv:
            coverage = inv.coverage(self.selection)
            self.assertTrue(coverage["complete"], coverage)
            references = [r for interval in coverage["intervals"] for r in interval["references"]]
            self.assertTrue(any(Path(r["path"]).name == "session.json" and r["sha256"] for r in references))
            exporter = IssueExporter(inv, self.root / "exports")
            seeds = [{"capture": "capture-a", "sequence": "2"}]
            preview = exporter.preview(self.selection, seeds, "Automatic capture")
            result = exporter.export(self.selection, seeds, "Automatic capture", preview["previewToken"])
        portable = Path(result["directory"])
        self.assertTrue(any(portable.rglob("session.json")))
        imported = import_package(portable, self.root / "reimport.sqlite")
        self.assertTrue(imported["complete"], imported)
        with Investigation(self.root / "reimport.sqlite") as inv:
            self.assertTrue(inv.coverage(self.selection)["complete"])

    def test_automatic_missing_unfinished_or_conflicting_session_is_incomplete(self):
        status = self.automatic_fixture()
        status_path = self.source / "session.json"
        for label, replacement, expected in (
            ("missing", None, "MissingAutomaticSessionStatus"),
            ("unfinished", dict(status, state="Finalizing"), "AutomaticSessionIncomplete"),
            ("process", dict(status, processId=124), "ConflictingAutomaticSessionIdentity:processId"),
            ("build", dict(status, buildGuid="other"), "ConflictingAutomaticSessionIdentity:buildGuid"),
        ):
            with self.subTest(label=label):
                if replacement is None:
                    status_path.unlink(missing_ok=True)
                else:
                    status_path.write_text(json.dumps(replacement), encoding="utf-8")
                target = self.root / label / "analysis.sqlite"
                prepare_archive([self.source], target)
                with Investigation(target) as inv:
                    coverage = inv.coverage(self.selection)
                    self.assertFalse(coverage["complete"])
                    reasons = {g["reason"] for interval in coverage["intervals"] for g in interval["gaps"]}
                    self.assertIn(expected, reasons)

    def test_automatic_abandonment_overrides_complete_end_writer_and_session(self):
        self.automatic_fixture()
        (self.source / "abandoned.json").write_text('{"complete":false,"reason":"UserAborted"}', encoding="utf-8")
        prepare_archive([self.source], self.db)
        with Investigation(self.db) as inv:
            coverage = inv.coverage(self.selection)
            self.assertFalse(coverage["complete"])
            self.assertIn("AutomaticSessionAbandoned", {g["reason"] for i in coverage["intervals"] for g in i["gaps"]})
            references = [r for i in coverage["intervals"] for r in i["references"]]
            self.assertTrue(any(Path(r["path"]).name == "abandoned.json" for r in references))
        spec = importlib.util.spec_from_file_location("legacy", Path(__file__).with_name("Analyze-NetworkDiagnostics.py"))
        legacy = importlib.util.module_from_spec(spec); spec.loader.exec_module(legacy)
        self.assertFalse(legacy.read(self.raw)[3]["complete"])
        self.assertIn("AutomaticSessionAbandoned", legacy.read(self.raw)[3]["captureIntegrityFailures"])

    def test_targeted_attempts_multirecipient_and_reconnect_are_isolated(self):
        fixture(self.raw)
        rows = [json.loads(line) for line in self.raw.read_text().splitlines()]
        # Same content/business resent successfully on the same connection is a
        # second attempt, not delivery proof for the first rejected attempt.
        retry = next(r["record"] for r in rows if r.get("record", {}).get("recordSequence") == "11")
        retry["input"]["members"][0]["entities"] = [{"entity": 42, "eventId": "942"}]
        self.raw.write_text("".join(json.dumps(row)+"\n" for row in rows), encoding="utf-8")
        before = file_hash(self.raw)
        prepare_archive([self.source], self.db)
        with Investigation(self.db) as inv:
            page = inv.timeline(self.selection)
            self.assertEqual(3, page["total"])
            for seq, instance, entity in (("2", "connection-1", "42"), ("5", "connection-2", "43"), ("8", "connection-3", "44")):
                detail = inv.detail(self.selection, "capture-a", seq)
                sends = [r for r in detail["records"] if r["stage"] == "network.transport"]
                self.assertEqual(["2", "10"] if seq == "2" else [seq], [str(r["recordSequence"]) for r in sends])
                self.assertEqual(len(sends), len({r["input"]["attempt"] for r in sends}))
                self.assertEqual(instance, detail["networkFacts"]["connectionInstance"])
                self.assertEqual("not-inferred", detail["networkFacts"]["recovery"])
                binding_steps = [step for step in detail["steps"] if step["stage"] == "network.connection"]
                self.assertTrue(all(step["fields"]["connectionInstance"] == instance for step in binding_steps))
                for origin in [r for r in detail["records"] if r["stage"] == "network.connection" and r["input"]["connectionInstance"] != instance]:
                    self.assertEqual("context", origin["associationStatus"])
                    self.assertEqual("CaptureBuildOrTimeOrigin", origin["associationReason"])
                    self.assertTrue(origin["outsideSelection"])
                self.assertEqual(1, inv.timeline(dict(self.selection, connectionInstance=instance))["total"])
                self.assertEqual(1, inv.timeline(dict(self.selection, entity=entity))["total"])
                self.assertIn(entity, [e["entity"] for e in inv.entities(self.selection)["items"]])
            self.assertEqual(1, inv.timeline(dict(self.selection, channel="1"))["total"])
            self.assertEqual(3, inv.timeline(dict(self.selection, returnCode="k_EResultLimitExceeded"))["total"])
        self.assertEqual(before, file_hash(self.raw))

    def test_rejection_survives_parse_and_metric_failure_no_false_recovery(self):
        fixture(self.raw, broken=True)
        prepare_archive([self.source], self.db)
        with Investigation(self.db) as inv:
            self.assertEqual(3, inv.timeline(self.selection)["total"])
            steam = next(t for t in inv.tracks(self.selection)["tracks"] if t["id"] == "steam")
            self.assertEqual("observed", steam["status"])
            self.assertEqual(3, len([s for s in steam["samples"] if s.get("sendRejected")]))
            sample = next(s for s in steam["samples"] if s["sequence"] == "12")
            self.assertIsNone(sample["values"]["connections"][0]["queueMilliseconds"])
            self.assertFalse({"42", "43", "44"} & {e["entity"] for e in inv.entities(self.selection)["items"]})
            self.assertTrue(inv.coverage(self.selection)["complete"])

    def test_light_export_roundtrip_and_missing_shutdown_remain_truthful(self):
        fixture(self.raw)
        prepare_archive([self.source], self.db)
        with Investigation(self.db) as inv:
            exporter = IssueExporter(inv, self.root / "exports")
            seeds = [{"capture": "capture-a", "sequence": "2"}]
            preview = exporter.preview(self.selection, seeds, "Synthetic rejection; not real Steam")
            result = exporter.export(self.selection, seeds, "Synthetic rejection; not real Steam", preview["previewToken"])
            self.assertTrue(result["complete"], result)
            expected = inv.detail(self.selection, "capture-a", "2")
        portable = Path(result["directory"])
        self.assertTrue(any(p.name == self.raw.name for p in portable.rglob("*.jsonl")))
        imported = import_package(portable, self.root / "reimport.sqlite")
        self.assertTrue(imported["complete"], imported)
        with Investigation(self.root / "reimport.sqlite") as inv:
            actual = inv.detail(self.selection, "capture-a", "2")
            self.assertEqual(expected["networkFacts"]["connectionInstance"], actual["networkFacts"]["connectionInstance"])
            self.assertEqual(expected["record"]["summary"], actual["record"]["summary"])
            self.assertTrue(inv.coverage(self.selection)["complete"])
        fixture(self.raw, missing_end=True)
        prepare_archive([self.source], self.root / "missing" / "analysis.sqlite")
        with Investigation(self.root / "missing" / "analysis.sqlite") as inv:
            self.assertFalse(inv.coverage(self.selection)["complete"])
        fixture(self.raw)
        self.raw.write_text("\n".join(self.raw.read_text().splitlines()[1:]) + "\n", encoding="utf-8")
        prepare_archive([self.source], self.root / "missing-header" / "analysis.sqlite")
        with Investigation(self.root / "missing-header" / "analysis.sqlite") as inv:
            self.assertFalse(inv.coverage(self.selection)["complete"])

    def test_writer_only_does_not_prove_steam_and_legacy_validity_agrees(self):
        fixture(self.raw)
        rows = [json.loads(line) for line in self.raw.read_text().splitlines()]
        for row in rows:
            if row.get("record", {}).get("stage") == "network.transport":
                row["record"]["input"]["result"] = "k_EResultOK"; row["record"]["outcome"] = "Accepted"
        self.raw.write_text("".join(json.dumps(row)+"\n" for row in rows), encoding="utf-8")
        prepare_archive([self.source], self.db)
        with Investigation(self.db) as inv:
            self.assertEqual(0, inv.timeline(self.selection)["total"])
            tracks = {t["id"]: t for t in inv.tracks(self.selection)["tracks"]}
            self.assertNotEqual("observed", tracks["steam"]["status"])
            self.assertEqual("observed", tracks["writer"]["status"])
        spec = importlib.util.spec_from_file_location("legacy", Path(__file__).with_name("Analyze-NetworkDiagnostics.py"))
        legacy = importlib.util.module_from_spec(spec); spec.loader.exec_module(legacy)
        sample = next(r for r in rows if r.get("kind") == "sample")
        self.assertNotIn("network-queue", legacy.point(sample)["signals"])
        self.assertIsNone(legacy.point(sample)["queueMs"])
        self.assertIsNone(valid_metric({"queueMilliseconds": 9.22e15}, "queueMilliseconds"))
        self.assertTrue(legacy.read(self.raw)[3]["complete"])
        status_path = Path(str(self.raw)+".status.json")
        status_path.write_text('{"complete":true,"failure":"failed final flush"}', encoding="utf-8")
        self.assertFalse(legacy.read(self.raw)[3]["complete"])
        status_path.write_text('{"complete":true,"failure":""}', encoding="utf-8")
        rows[-1]["complete"] = False
        self.raw.write_text("".join(json.dumps(row)+"\n" for row in rows), encoding="utf-8")
        self.assertFalse(legacy.read(self.raw)[3]["complete"])
        rows[-1]["complete"] = True
        self.raw.write_text("".join(json.dumps(row)+"\n" for row in rows if row.get("record", {}).get("recordSequence") != "5"), encoding="utf-8")
        self.assertFalse(legacy.read(self.raw)[3]["complete"])


if __name__ == "__main__":
    unittest.main()
