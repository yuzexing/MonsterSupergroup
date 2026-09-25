import json
import hashlib
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path

import CombatEvidence as decoder
import CombatInvestigationIndex as investigation


class InvestigationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.path = self.root / "analysis.sqlite"

    def tearDown(self):
        self.temp.cleanup()

    def build(self, rows, complete=True, gaps=None):
        with closing(decoder.connect(self.path)) as db, db:
            for number, changes in enumerate(rows, 1):
                record = dict(schemaVersion=2, captureId="c", recordSequence=str(number), runId="r", round=1,
                    monotonicTime=float(number), utc="2026-09-25T00:00:00.0000000Z", role="Owner", stage="owner.hit",
                    source=2, target=3, outcome="Applied", **{})
                record.update(changes)
                c, seq = record["captureId"], str(record["recordSequence"])
                db.execute("INSERT INTO records VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (c, seq, record["runId"], record["round"],
                    record.get("eventId"), record.get("rootEventId"), record.get("parentEventId"), record.get("target", 0),
                    record["stage"], record.get("reason"), record.get("engine"), record["utc"], record.get("serverSequence", 0), json.dumps(record)))
                db.execute("INSERT INTO copies VALUES(?,?,?,?)", (c, seq, str(self.root / (c + ".jsonl")), number))
                decoder.index_batch_events(db, record); decoder.index_facets(db, record)
        investigation.build_index(self.path)
        # Supply an independently assessed immutable-import coverage fixture. The decoder's
        # own retention/coverage semantics have their separate real-file test suites.
        with closing(sqlite3.connect(self.path)) as db, db:
            contexts = db.execute("SELECT capture,run,round FROM investigation_context").fetchall()
            coverage = {"complete": complete, "intervals": [dict(captureId=c, runId=r, round=n, complete=complete, gaps=gaps or []) for c, r, n in contexts], "scopeGaps": []}
            db.execute("UPDATE investigation_meta SET value=? WHERE key='coverage'", (json.dumps(coverage),))
        return investigation.Investigation(self.path)

    def test_full_pagination_over_ten_thousand_and_uint64_identity(self):
        rows = [{"monotonicTime": float(i // 3), "recordSequence": str(18446744073709538000 + i)} for i in range(12007)]
        with self.build(rows) as inv:
            selection = {"run": "r", "round": 1, "category": "business"}
            found, cursor = [], None
            while True:
                page = inv.timeline(selection, cursor, 997)
                self.assertEqual(12007, page["total"])
                found += [r["sequence"] for r in page["items"]]
                cursor = page["nextCursor"]
                if cursor is None:
                    break
            self.assertEqual([r["recordSequence"] for r in rows], found)
            self.assertEqual(len(found), len(set(found)))

    def test_cursor_cannot_be_reused_for_another_filter(self):
        with self.build([{}, {}, {}]) as inv:
            cursor = inv.timeline({"run": "r", "round": 1}, limit=1)["nextCursor"]
            with self.assertRaises(ValueError):
                inv.timeline({"run": "r", "round": 1, "after": 1}, cursor)

    def test_source_target_batch_and_snapshot_membership(self):
        rows = [{"source": 7, "target": 8},
                {"stage": "network.submit", "source": 0, "target": 0, "input": {"Results": [{"SourceEntityId": 7, "TargetEntityId": 8}]}},
                {"stage": "observation.snapshot", "source": 0, "target": 0, "input": {"actors": [{"entity": 7, "kind": "player", "health": 40}]}}]
        with self.build(rows) as inv:
            s = {"run": "r", "round": 1, "entity": "7"}
            self.assertEqual(3, inv.timeline(s)["total"])
            self.assertEqual(2, inv.timeline(dict(s, direction="source"))["total"])
            self.assertEqual(0, inv.timeline(dict(s, direction="target"))["total"])

    def test_generation_reuse_and_perspectives_do_not_merge(self):
        def state(birth, perspective, health):
            return {"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "perspective": perspective,
                "stateRevision": "1", "baseline": True, "continuous": True, "identity": {"birth": birth}, "state": {"health": health}}}
        with self.build([state("life1", "Server", 10), state("life1", "Replica", 5), state("life2", "Server", 90)]) as inv:
            s = {"run": "r", "round": 1, "generation": "life1"}
            found = inv.state(s, "c", "3", 2)
            self.assertEqual({"Server", "Replica"}, {d["perspective"] for d in found["domains"]})
            self.assertEqual({5, 10}, {d["state"]["health"] for d in found["domains"]})
            self.assertEqual(90, inv.state({"run": "r", "round": 1}, "c", "3", 2)["domains"][0]["state"]["health"])

    def test_future_state_never_used_and_gap_does_not_claim_reliable(self):
        rows = [{"target": 0}, {"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "perspective": "Server",
            "identity": {"birth": "b"}, "continuous": True, "baseline": True, "state": {"health": 99}}}]
        with self.build(rows, complete=False, gaps=[{"reason": "QueueLimit", "first": "2", "last": "2"}]) as inv:
            s = {"run": "r", "round": 1}
            self.assertEqual("unknown", inv.state(s, "c", "3", .5)["status"])
            state = inv.state(s, "c", "3", 2)
            self.assertNotEqual("reliable", state["status"])
            self.assertIn("EvidenceGapMayHideLaterMutation", state["domains"][0]["unknowns"])

    def test_match_scope_prevents_reused_event_and_drop_linkage(self):
        rows = [{"eventId": "42", "stage": "investigation.pickup.request", "input": {"dropId": "99"}},
                {"eventId": "43", "stage": "investigation.pickup.receipt_commit", "input": {"dropId": "99"}},
                {"runId": "other", "eventId": "42", "stage": "investigation.pickup.receipt_commit", "input": {"dropId": "99"}}]
        with self.build(rows) as inv:
            detail = inv.detail({"run": "r", "round": 1, "before": 0}, "c", "1")
            self.assertEqual(["1", "2"], [r["recordSequence"] for r in detail["records"]])
            self.assertTrue(detail["records"][1]["outsideSelection"])

    def test_four_tracks_remain_independent_with_unavailable_steam_metrics(self):
        rows = [{"stage": "performance.snapshot", "input": {"frameMaxMs": 180, "workingSetBytes": 123,
            "evidenceQueuedBytes": 400, "pendingDeaths": 1, "connections": [{"available": False, "pendingReliableBytes": 0}]}},
            {"stage": "death.report", "eventId": "7"}]
        with self.build(rows) as inv:
            tracks = {t["id"]: t for t in inv.tracks({"run": "r", "round": 1})["tracks"]}
            self.assertEqual({"business", "steam", "frames", "writer"}, set(tracks))
            self.assertEqual("unknown", tracks["steam"]["status"])
            for name in ("business", "frames", "writer"):
                self.assertEqual("observed", tracks[name]["status"])

    def test_valid_normal_samples_do_not_claim_observed_problem(self):
        data = {"frameMaxMs": 17, "evidenceQueuedBytes": 0, "pendingDeaths": 0, "oldestPendingDeathSeconds": 0,
                "connections": [{"queueValid": True, "pendingValid": True, "queueMilliseconds": 0,
                    "pendingReliableBytes": 0, "pendingUnreliableBytes": 0, "unacknowledgedBytes": 0}]}
        with self.build([{"stage": "performance.snapshot", "input": data}]) as inv:
            tracks = inv.tracks({"run": "r", "round": 1})["tracks"]
            self.assertTrue(all(t["status"] == "not-observed" and t["observationStatus"] == "observed" for t in tracks))

    def test_no_remote_host_is_not_applicable_but_missing_samples_unknown(self):
        data = {"role": "host", "connections": [], "playerCount": 1, "frameMaxMs": 18, "evidenceQueuedBytes": 0}
        with self.build([{"stage": "performance.snapshot", "input": data}]) as inv:
            tracks = {t["id"]: t for t in inv.tracks({"run": "r", "round": 1})["tracks"]}
            self.assertEqual("not-applicable", tracks["steam"]["status"])
            self.assertEqual("unknown", tracks["business"]["status"])
            self.assertEqual("host", inv.matches()["matches"][0]["captures"][0]["actualRole"])
            self.assertEqual(0, inv.timeline({"run": "r", "round": 1, "category": "business"})["total"])

    def test_steam_valid_pending_and_invalid_queue_coexist(self):
        data = {"connections": [{"queueValid": False, "queueValidity": "AboveOneHourUnverified", "queueMilliseconds": 9223372036854776,
                    "pendingValid": True, "pendingReliableBytes": 64, "pendingUnreliableBytes": 0, "unacknowledgedBytes": 0}]}
        with self.build([{"stage": "performance.snapshot", "input": data}]) as inv:
            steam = next(t for t in inv.tracks({"run": "r", "round": 1})["tracks"] if t["id"] == "steam")
            self.assertEqual("observed", steam["issueStatus"])
            self.assertEqual(1, steam["metrics"]["invalidSamples"])
            connection = steam["samples"][0]["values"]["connections"][0]
            self.assertEqual(64, connection["pendingReliableBytes"])
            self.assertIsNone(connection["queueMilliseconds"])

    def test_old_pickup_report_list_arguments_and_receipt_are_searchable(self):
        report = {"PickupDropId": "18446744073709551615", "PickupClaimVersion": 1, "PickupRestoredHealth": 20,
                  "EntityId": 3, "PlayerId": 3, "EventId": "42"}
        rows = [{"stage": "replay.input", "source": 0, "target": 0, "input": [3, {"PlayerHealthReports": [report]}, 7.25]},
                {"stage": "replay.external", "reason": "PickupReceiptFact", "eventId": "42", "input": {"report": report, "accepted": True}},
                {"stage": "gateway.decision", "eventId": "42", "input": report, "before": {"Health": 30}, "after": {"Health": 50}},
                {"stage": "gateway.canonical_link", "eventId": "42", "serverSequence": 8},
                {"captureId": "peer", "stage": "replica.entity", "serverSequence": 8},
                {"stage": "observation.snapshot", "source": 0, "target": 0, "input": {"actors": [{"entity": 3, "health": 50}]}}]
        with self.build(rows) as inv:
            s = {"run": "r", "round": 1, "category": "pickup"}
            self.assertEqual(5, inv.timeline(s)["total"])
            self.assertIn("1", [r["sequence"] for r in inv.timeline(dict(s, entity="3", direction="target"))["items"]])
            detail = inv.detail(s, "c", "2")
            self.assertTrue(any(step["stage"] == "replay.external" for step in detail["steps"]))
            self.assertTrue(any(step["stage"] == "replay.input" for step in detail["steps"]))
            self.assertNotIn("observation.snapshot", [r["stage"] for r in inv.timeline(dict(s, category="business"))["items"]])

    def test_prepare_does_not_change_source_and_refuses_existing_destination(self):
        with self.build([{}]):
            pass
        # prepare must not accept already indexed sources as a silent overwrite/rebuild.
        before = self.path.read_bytes()
        destination = self.root / "must-not-overwrite.sqlite"
        destination.write_bytes(b"original")
        with self.assertRaises(FileExistsError):
            investigation.prepare(self.path, destination)
        self.assertEqual(before, self.path.read_bytes())
        self.assertEqual(b"original", destination.read_bytes())

    def test_prepare_copies_unindexed_source_without_creating_source_sidecars(self):
        source = self.root / "source.sqlite"
        with closing(decoder.connect(source)) as db, db:
            raw = dict(captureId="c", recordSequence="1", runId="r", round=1, stage="owner.hit", monotonicTime=1)
            db.execute("INSERT INTO records VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)", ("c", "1", "r", 1, None, None, None, 0, "owner.hit", None, None, None, 0, json.dumps(raw)))
        before = {p.name: p.read_bytes() for p in self.root.iterdir()}
        result = investigation.prepare(source, self.path)
        self.assertEqual(1, result["records"])
        after = {p.name: p.read_bytes() for p in self.root.iterdir() if p.name.startswith("source.sqlite")}
        self.assertEqual(before, after)
        with investigation.Investigation(self.path) as inv:
            self.assertEqual(1, inv.timeline({"run": "r", "round": 1})["total"])

    def test_readonly_connection_refuses_mutation(self):
        with self.build([{}]) as inv:
            with self.assertRaises(sqlite3.OperationalError):
                inv.db.execute("DELETE FROM records")

    def test_unknown_match_capture_and_invalid_time_are_rejected(self):
        with self.build([{}]) as inv:
            for s in ({"run": "boot", "round": 1}, {"run": "r", "round": 1, "captures": ["other"]},
                      {"run": "r", "round": 1, "after": float('nan')}, {"run": "r", "round": 1, "after": 3, "before": 2}):
                with self.assertRaises(ValueError):
                    inv.timeline(s)

    def test_state_dependencies_include_baseline_and_every_revision(self):
        rows = [{"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "perspective": "Owner",
            "identity": {"birth": "b"}, "baseline": i == 1, "continuous": True, "stateRevision": str(i),
            "state": {"definition": {"weaponId": 7, "damage": i + .125}}}} for i in range(1, 4)]
        with self.build(rows) as inv:
            state = inv.state({"run": "r", "round": 1, "after": 1.5}, "c", "3", 2)
            domain = state["domains"][0]
            self.assertEqual("reliable", domain["status"])
            self.assertEqual(["1", "2", "3"], [r["sequence"] for r in domain["dependencies"]])
            self.assertEqual("1", domain["baseline"]["sequence"])
            self.assertTrue(domain["baseline"]["outsideSelection"])
            self.assertFalse(domain["outsideSelection"])
            self.assertEqual("EarlierBaselineRequiredForState", domain["baseline"]["contextReason"])
            self.assertEqual(3.125, domain["state"]["definition"]["damage"])

    def test_new_birth_without_state_does_not_reuse_previous_instance_state(self):
        baseline = {"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "identity": {"birth": "old"},
            "stateRevision": "1", "baseline": True, "continuous": True, "state": {"health": 100}}}
        begin = {"stage": "investigation.build.begin", "input": {"entity": 3, "operationId": "1", "identity": {"birth": "new"}}}
        with self.build([baseline, begin]) as inv:
            result = inv.state({"run": "r", "round": 1}, "c", "3", 1)
            self.assertEqual("unknown", result["status"])
            self.assertEqual([], result["domains"])
            old = inv.state({"run": "r", "round": 1, "generation": "old"}, "c", "3", 1)
            self.assertIn("LocalEntityInstanceReused", old["domains"][0]["unknowns"])

    def test_old_mixed_clock_age_cannot_make_zero_pending_look_positive(self):
        rows = [{"stage": "performance.snapshot", "input": {"pendingDeaths": 0, "oldestPendingDeathSeconds": 1234,
                 "lastDeathConfirmationSeconds": 456, "maximumDeathConfirmationSeconds": 789}}]
        with self.build(rows) as inv:
            business = inv.tracks({"run": "r", "round": 1})["tracks"][0]
            self.assertEqual("not-observed", business["issueStatus"])
            sample = business["samples"][0]["values"]
            self.assertIsNone(sample["oldestPendingDeathSeconds"])
            self.assertEqual(1234, sample["raw"]["oldestPendingDeathSeconds"])
            self.assertEqual("MixedClockAgeUnavailable", sample["invalidReasons"]["oldestPendingDeathSeconds"])

    def test_explicit_unscaled_age_capability_allows_duration_but_not_cross_capture_clock(self):
        rows = [{"stage": "performance.snapshot", "input": {"pendingDeaths": 1, "oldestPendingDeathSeconds": .25,
                 "pendingDeathAgeClock": "Unity.UnscaledTime", "pendingDeathAgeClockVersion": 1}}]
        with self.build(rows) as inv:
            business = inv.tracks({"run": "r", "round": 1})["tracks"][0]
            self.assertEqual("observed", business["issueStatus"])
            self.assertEqual(.25, business["samples"][0]["values"]["oldestPendingDeathSeconds"])
            self.assertFalse(business["clock"]["crossCaptureExact"])

    def test_timeline_summary_is_readable_without_event_id_noise(self):
        with self.build([{"stage": "gateway.decision", "outcome": "Accepted", "eventId": "18446744073709551615",
                         "before": {"Health": 50}, "after": {"Health": 70}}]) as inv:
            summary = inv.timeline({"run": "r", "round": 1})["items"][0]["summary"]
            self.assertIn("服务端", summary); self.assertIn("接受", summary); self.assertIn("50 → 70", summary)
            self.assertNotIn("gateway", summary); self.assertNotIn("18446744073709551615", summary)

    def test_local_entity_filter_retains_peer_event_context_without_merging_birth(self):
        rows = [{"captureId": "client", "eventId": "42", "entityGeneration": "client-birth", "stage": "owner.hit"},
                {"captureId": "host", "eventId": "42", "entityGeneration": "host-birth", "stage": "ledger.apply"}]
        with self.build(rows) as inv:
            s = {"run": "r", "round": 1, "captures": ["client", "host"], "entityCapture": "client",
                 "entity": "3", "generation": "client-birth"}
            self.assertEqual(["client"], [r["capture"] for r in inv.timeline(s)["items"]])
            detail = inv.detail(s, "client", "1")
            peer = next(r for r in detail["records"] if r["captureId"] == "host")
            self.assertTrue(peer["outsideSelection"])
            self.assertEqual("host-birth", peer["entityGeneration"])
            self.assertEqual({"client", "host"}, {r["captureId"] for r in detail["records"]})
            with self.assertRaises(ValueError):
                inv.timeline(dict(s, entityCapture="absent"))

    def test_revision_gap_or_open_build_operation_makes_state_unknown(self):
        baseline = {"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "perspective": "Owner",
            "identity": {"birth": "b"}, "baseline": True, "continuous": True, "stateRevision": "1", "state": {"health": 10}}}
        begin = {"stage": "investigation.build.begin", "input": {"entity": 3, "operationId": "8", "perspective": "Owner", "identity": {"birth": "b"}}}
        final = {"stage": "investigation.state", "input": dict(baseline["input"], baseline=False, stateRevision="2", operationId="8", state={"health": 50})}
        with self.build([baseline, begin, final]) as inv:
            s = {"run": "r", "round": 1}
            during = inv.state(s, "c", "3", 1.5)
            self.assertEqual("unknown", during["status"])
            self.assertIn("OperationInProgressOrCompletionMissing", during["domains"][0]["unknowns"])
            self.assertEqual("reliable", inv.state(s, "c", "3", 2)["status"])
            self.assertEqual(["1", "2", "3"], [r["recordSequence"] for r in inv.detail(s, "c", "3")["records"]])

    def test_after_capture_tail_is_not_a_reliable_current_state(self):
        rows = [{"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "perspective": "Owner",
            "identity": {"birth": "b"}, "baseline": True, "continuous": True, "stateRevision": "1", "state": {"health": 10}}}]
        with self.build(rows) as inv:
            result = inv.state({"run": "r", "round": 1}, "c", "3", 60)
            self.assertEqual("unknown", result["status"])
            self.assertIn("RequestedTimeBeyondCapturedTail", result["domains"][0]["unknowns"])

    def test_invalid_steam_values_are_not_zero_measurements(self):
        connection = {"readSucceeded": False, "queueValidity": "ReadFailed", "queueValid": False, "pendingValid": False,
                      "queueMilliseconds": -1, "pendingReliableBytes": 0}
        with self.build([{"stage": "performance.snapshot", "input": {"connections": [connection]}}]) as inv:
            steam = next(t for t in inv.tracks({"run": "r", "round": 1})["tracks"] if t["id"] == "steam")
            self.assertEqual("invalid", steam["status"])
            sample = steam["samples"][0]["values"]["connections"][0]
            self.assertIsNone(sample["queueMilliseconds"])
            self.assertIsNone(sample["pendingReliableBytes"])
            self.assertEqual(0, sample["raw"]["pendingReliableBytes"])

    def test_catalog_is_build_bound_and_duplicate_ids_are_unknown(self):
        catalog = {"buildId": "build", "entries": [dict(kind="weapon", contentId=7, identityValid=True, names={"zh-CN": "剑"}, assetGuid="one")]}
        raw = json.dumps(catalog, ensure_ascii=False)
        header = {"stage": "source.start", "input": {"buildInfo": json.dumps({"buildId": "build"}), "investigationCatalog": raw,
            "buildManifest": json.dumps({"investigationCatalog": {"sha256": hashlib.sha256(raw.encode()).hexdigest()}})}}
        state = {"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "state": {"weaponId": 7}}}
        with self.build([header, state]) as inv:
            field = inv.state({"run": "r", "round": 1}, "c", "3", 1)["domains"][0]["fields"][0]
            self.assertEqual("剑", field["contentName"]["name"])
            self.assertEqual("observed", inv.matches()["matches"][0]["captures"][0]["contentCatalog"]["status"])
        catalog["entries"].append(dict(catalog["entries"][0], assetGuid="two"))
        self.assertEqual("unknown", investigation._name(catalog, "weapon", 7)["status"])
        self.assertEqual("unknown", investigation._catalog(header["input"], "other")["status"])

    def test_package_scope_preserves_original_coverage_only_within_declared_selection(self):
        with self.build([{}, {}, {}]) as inv:
            original = inv.coverage({"run": "r", "round": 1})
        manifest = {"schemaVersion": 1, "selection": {"run": "r", "round": 1, "captures": ["c"], "after": 1, "before": 2},
                    "originalCoverage": original, "required": [{"capture": "c", "sequence": "2"}], "complete": True,
                    "omissions": "unselected evidence intentionally not exported", "verificationErrors": []}
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("INSERT INTO metadata VALUES(?,?,?)", ("package", "investigation-package.json", json.dumps(manifest)))
        with investigation.Investigation(self.path) as inv:
            supported = inv.coverage(manifest["selection"])
            self.assertTrue(supported["complete"])
            self.assertEqual("within-package", supported["packageScope"]["status"])
            self.assertFalse(inv.coverage({"run": "r", "round": 1})["complete"])
        manifest["required"].append({"capture": "c", "sequence": "999"})
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("UPDATE metadata SET body=? WHERE path='package'", (json.dumps(manifest),))
        with investigation.Investigation(self.path) as inv:
            self.assertFalse(inv.coverage(manifest["selection"])["complete"])

    def test_pickup_change_uses_actual_reward_fields_and_keeps_disappearance_unknown(self):
        rows = [{"stage": "investigation.pickup.xp_grant", "input": {"dropId": "18446744073709551615", "data": {
            "before": {"experience": 10}, "after": {"experience": 17}, "actualAwarded": 7}}},
            {"stage": "investigation.pickup.disappearance", "input": {"dropId": "18446744073709551615", "data": {"benefitConfirmed": False}}}]
        with self.build(rows) as inv:
            s = {"run": "r", "round": 1}
            detail = inv.detail(s, "c", "1")
            self.assertEqual(17, detail["steps"][2]["fields"]["after"]["experience"])
            self.assertIn("DisappearanceOrResolutionDoesNotProveBenefit", inv.detail(s, "c", "2")["unknowns"])

    def test_missing_build_progression_selection_are_explicit_not_inferred_from_attack_stats(self):
        rows = [{"stage": "owner.attack_stats", "input": {"weaponId": 9}, "after": {"Damage": 15}},
                {"stage": "observation.snapshot", "input": {"actors": [{"entity": 3, "health": 55}]}}]
        with self.build(rows) as inv:
            state = inv.state({"run": "r", "round": 1}, "c", "3", 1)
            missing = {domain["domain"]: domain for domain in state["missingDomains"]}
            self.assertEqual({"build", "progression", "selection", "attackGate", "attackAttributes", "observation"}, set(missing))
            self.assertTrue(all(f["value"] is None and f["status"] == "unknown" for d in missing.values() for f in d["fields"]))
            self.assertEqual(55, state["domains"][0]["state"]["health"])

    def test_detail_explains_complete_chain_per_capture_without_per_hit_canonical_attribution(self):
        rows = [{"stage": "owner.hit", "eventId": "11", "input": {"Value": 7}, "before": {"health": 20}},
                {"stage": "ledger.apply", "role": "Server", "eventId": "11", "before": {"Health": 20}, "after": {"Health": 13}},
                {"stage": "gateway.canonical_link", "role": "Server", "eventId": "11", "serverSequence": 99, "reason": "Coalesced"},
                {"stage": "gateway.canonical_link", "role": "Server", "eventId": "12", "serverSequence": 99, "reason": "Coalesced"},
                {"captureId": "peer", "stage": "replica.entity", "role": "Replica", "serverSequence": 99,
                 "before": {"Health": 20}, "after": {"Health": 8}}]
        with self.build(rows) as inv:
            detail = inv.detail({"run": "r", "round": 1}, "c", "1")
            self.assertEqual(5, len(detail["records"]))
            self.assertEqual(15, len(detail["steps"]))
            changes = [s for s in detail["steps"] if s["kind"] == "change"]
            self.assertEqual(13, next(s for s in changes if s["stage"] == "ledger.apply")["fields"]["after"]["Health"])
            replica = next(s for s in changes if s["stage"] == "replica.entity")
            self.assertEqual("peer", replica["capture"])
            self.assertEqual("aggregate", replica["attribution"])
            self.assertIn("CanonicalCoalescingPreventsPerHitAttribution", detail["unknowns"])
            self.assertEqual(["c"] * 12 + ["peer"] * 3, [s["capture"] for s in detail["steps"]])

    def test_inline_shared_dependencies_keep_raw_reference_and_resolved_fields(self):
        digest = "a" * 64; reference = "inputs/" + digest + ".json.gz"
        with self.build([{"input": {"attack": {"$evidenceRef": reference}}}]):
            pass
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("INSERT INTO blobs VALUES(?,?)", (digest, json.dumps({"damage": 7.125, "weaponId": "18446744073709551615"})))
        with investigation.Investigation(self.path) as inv:
            detail = inv.detail({"run": "r", "round": 1}, "c", "1")
            record = detail["records"][0]
            self.assertEqual(reference, record["raw"]["input"]["attack"]["$evidenceRef"])
            self.assertEqual(7.125, record["input"]["attack"]["damage"])
            self.assertEqual("18446744073709551615", record["input"]["attack"]["weaponId"])
            self.assertEqual([{"hash": digest, "reference": reference, "present": True}], detail["dependencies"])


    def test_attack_observation_is_local_historical_and_never_a_current_loadout(self):
        rows = [{"stage": "network.identity", "target": 2, "connectionEpoch": 1},
                {"stage": "owner.attack_stats", "source": 2, "target": 0, "input": {"weaponId": 9}, "after": {"Damage": 15}},
                {"stage": "owner.attack_gate", "source": 2, "target": 0, "outcome": "Rejected", "reason": "Cooldown", "input": {"weaponId": 9}},
                {"stage": "owner.attack_stats", "source": 2, "target": 0, "input": {"weaponId": 10}, "after": {"Damage": 30}},
                {"stage": "network.identity", "target": 2, "connectionEpoch": 2}]
        with self.build(rows) as inv:
            s = {"run": "r", "round": 1, "after": 2}
            state = inv.state(s, "c", "2", 2)
            domains = {d["domain"]: d for d in state["domains"]}
            self.assertEqual(9, domains["attackAttributes"]["state"]["input"]["weaponId"])
            self.assertEqual("observed", domains["attackAttributes"]["status"])
            self.assertTrue(domains["attackAttributes"]["outsideSelection"])
            self.assertIn("AttackObservationDoesNotDescribeCurrentLoadout", domains["attackAttributes"]["unknowns"])
            self.assertEqual("Rejected", domains["attackGate"]["state"]["outcome"])
            missing = {d["domain"] for d in state["missingDomains"]}
            self.assertIn("build", missing); self.assertIn("observation", missing)
            self.assertFalse(inv.state(s, "c", "2", 4)["domains"])

    def test_empty_observation_log_queue_does_not_prove_combat_writer_empty(self):
        with self.build([{"stage": "performance.snapshot", "input": {"logQueuedBytes": 0, "logFailures": 0}}]) as inv:
            writer = next(t for t in inv.tracks({"run": "r", "round": 1})["tracks"] if t["id"] == "writer")
            self.assertEqual("unknown", writer["issueStatus"])
            self.assertEqual("unknown", writer["observationStatus"])
            self.assertEqual(0, writer["samples"][0]["values"]["logQueuedBytes"])


    def test_state_context_dependencies_preserve_birth_reuse_and_destroy(self):
        rows = [{"stage": "network.identity", "target": 3, "connectionEpoch": 1},
                {"stage": "observation.snapshot", "input": {"actors": [{"entity": 3, "health": 20}]}},
                {"stage": "entity.destroy", "target": 3, "entityGeneration": "avatar:3:epoch:1"},
                {"stage": "network.identity", "target": 3, "connectionEpoch": 2}]
        with self.build(rows) as inv:
            state = inv.state({"run": "r", "round": 1, "generation": "avatar:3:epoch:1"}, "c", "3", 3)
            refs = {p["sequence"]: p["reason"] for p in state["contextDependencies"]}
            self.assertEqual({"1": "SelectedEntityBirth", "4": "LatestEntityBirthAtRequestedTime", "3": "EntityLifecycleEnded"}, refs)
            self.assertIn("LocalEntityInstanceReused", state["domains"][0]["unknowns"])
            self.assertIn("LocalEntityLifecycleEnded", state["domains"][0]["unknowns"])

    def test_package_preserves_complete_host_state_despite_client_history_gap(self):
        state = {"stage": "investigation.state", "input": {"entity": 3, "domain": "build", "stateRevision": "1",
            "identity": {"birth": "b"}, "baseline": True, "continuous": True, "state": {"health": 20}}}
        with self.build([dict(state, captureId="host"), dict(state, captureId="client")]) as inv:
            original = inv.coverage({"run": "r", "round": 1})
        client = next(i for i in original["intervals"] if i["captureId"] == "client")
        client.update(complete=False, gaps=[{"first": "1", "last": "12811", "reason": "HistoricalMissingRecords"}])
        original["complete"] = False
        manifest = {"selection": {"run": "r", "round": 1, "captures": ["client", "host"]}, "originalCoverage": original,
                    "complete": False, "required": [{"capture": "host", "sequence": "1"}, {"capture": "client", "sequence": "2"}]}
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("INSERT INTO metadata VALUES(?,?,?)", ("package", "investigation-package.json", json.dumps(manifest)))
        with investigation.Investigation(self.path) as inv:
            self.assertEqual("reliable", inv.state(manifest["selection"], "host", "3", 0)["domains"][0]["status"])
            self.assertEqual("unknown", inv.state(manifest["selection"], "client", "3", 0)["domains"][0]["status"])
            coverage = inv.coverage(manifest["selection"])
            self.assertFalse(coverage["complete"])
            self.assertEqual(client, next(i for i in coverage["intervals"] if i["captureId"] == "client"))

    def test_legacy_batch_and_hash_links_are_candidates_without_recursive_causality(self):
        rows = [{"eventId": "42", "stage": "owner.hit"},
                {"stage": "network.message", "batchSequence": 34, "input": {"business": "EnemyMovement", "payloadHash": "shared"}},
                {"stage": "network.transport", "input": {"payloadHash": "shared", "recipient": 10}},
                {"eventId": "99", "stage": "ledger.apply"},
                {"stage": "network.transport", "input": {"payloadHash": "shared", "recipient": 20}},
                {"stage": "network.transport", "input": {"payloadHash": "shared", "recipient": 10, "retry": True}}]
        with self.build(rows):
            pass
        with closing(sqlite3.connect(self.path)) as db, db:
            db.executemany("INSERT OR IGNORE INTO event_links VALUES(?,?,?)", [("c", n, "42") for n in ("2", "3", "5", "6")] + [("c", "3", "99")])
        with investigation.Investigation(self.path) as inv:
            detail = inv.detail({"run": "r", "round": 1}, "c", "1")
            records = {str(r["recordSequence"]): r for r in detail["records"]}
            self.assertNotIn("4", records)
            self.assertEqual("not-established", records["2"]["associationStatus"])
            self.assertEqual("ConflictingTransportBusinessType", records["2"]["associationReason"])
            self.assertTrue(all(records[n]["associationStatus"] == "candidate" for n in ("3", "5", "6")))
            self.assertEqual(4, len(detail["candidates"]))
            self.assertTrue(all(s["stage"] == "owner.hit" for s in detail["steps"]))

    def test_declared_track_scope_does_not_expand_business_package_coverage(self):
        with self.build([{"stage": "performance.snapshot", "input": {"pendingDeaths": 0, "frameMaxMs": 18, "evidenceQueuedBytes": 0}}, {}, {}]) as inv:
            original = inv.coverage({"run": "r", "round": 1})
        scope = {"run": "r", "round": 1, "captures": ["c"], "after": 0, "before": 1}
        manifest = {"selection": dict(scope, after=2, before=2), "originalCoverage": original, "complete": True,
                    "required": [{"capture": "c", "sequence": "1"}], "trackScopes": [{"id": "track-c", "selection": scope}]}
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("INSERT INTO metadata VALUES(?,?,?)", ("package", "investigation-package.json", json.dumps(manifest)))
        with investigation.Investigation(self.path) as inv:
            self.assertFalse(inv.coverage(scope)["complete"])
            tracks = inv.tracks(dict(scope, packageTrackScope="track-c"))
            self.assertTrue(tracks["coverage"]["complete"])
            self.assertEqual("not-observed", tracks["tracks"][0]["issueStatus"])
            self.assertTrue(all(t["coverageRef"] == "#/coverage" and "gaps" not in t for t in tracks["tracks"]))
            with self.assertRaises(ValueError):
                inv.tracks(dict(scope, before=2, packageTrackScope="track-c"))


    def test_legacy_snapshot_writer_queue_and_cumulative_dropped_are_independent_facts(self):
        rows = [{"stage": "observation.snapshot", "input": {"actors": [], "pendingBytes": 25689220, "peakQueueBytes": 30013832,
                    "retainedBytes": 69024700, "dropped": 12811, "writerMs": 72436.5969}},
                {"stage": "observation.snapshot", "input": {"actors": [], "pendingBytes": 14042984, "peakQueueBytes": 30013832,
                    "retainedBytes": 57405044, "dropped": 12811, "writerMs": 75008.3247}}]
        with self.build(rows, complete=False, gaps=[{"reason": "HistoricalMissingRecords", "first": "1", "last": "100"}]) as inv:
            writer = next(t for t in inv.tracks({"run": "r", "round": 1})["tracks"] if t["id"] == "writer")
            self.assertEqual("observed", writer["issueStatus"])
            self.assertEqual([0, 1], [s["elapsed"] for s in writer["samples"]])
            self.assertEqual([25689220, 14042984], [s["values"]["pendingBytes"] for s in writer["samples"]])
            self.assertTrue(all(s["clock"]["observationKind"] == "point" for s in writer["samples"]))
            lost = next(f for f in writer["metrics"]["recordedFacts"] if f["name"] == "dropped")
            self.assertEqual(12811, lost["value"])
            self.assertEqual("CumulativeAtObservationNotSelectedIntervalDelta", lost["semantics"])


if __name__ == "__main__":
    unittest.main()
