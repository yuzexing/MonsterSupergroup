#!/usr/bin/env python3
"""Freeze real mixed Store inputs and independently verify a paced benchmark output.

This is a storage benchmark, not a recreation of gameplay or its capture CPU cost.
Original Advance binary rows select TryWriteAdvance; other decoded rows select
TryWrite. Known runtime shared batches are reconstructed as typed leases. Writer
created KnockbackSettings references are expanded before they are deduplicated again.
Unknown shared reference shapes fail preparation instead of silently changing cost.
"""
import argparse
import collections
import copy
import gzip
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import sqlite3
import sys

SCHEMA = 1
REFERENCE = re.compile(r"inputs/[0-9a-f]{64}\.json\.gz\Z")
LIMITATIONS = [
    "Store-only comparison: no gameplay, Steam, rendering, original capture CPU, or weak-device acceptance.",
    "Disk records do not retain original DTO allocations or shared-handle lifetimes. Non-shared payloads use detached JSON; recorded queue charges are retained.",
    "Canonical receive/Apply shared pairs are reconstructed with real typed SharedEvidencePayload handles. Content identity is preserved; original handle identity is not recoverable.",
    "Advance call identity, phase, boundary and cadence are preserved, but original writer-dependent Advance block packing is intentionally not forced.",
    "Each repeat cycle has distinct run/capture record identities. Payload IDs are reused; investigation joins must respect cycle boundaries. This is not one uninterrupted game simulation.",
    "The scheduler reports lateness; a slow fixture reader must not be interpreted as improved Store throughput.",
]


def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True, allow_nan=False)


def digest(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def load_decoder(path):
    spec = importlib.util.spec_from_file_location("frozen_combat_evidence", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def blob(db, reference):
    if not re.fullmatch(r"(?:inputs|checkpoints)/[0-9a-f]{64}\.json\.gz", reference):
        raise ValueError("UnsupportedReference:" + str(reference))
    key = Path(reference).name.split(".")[0]
    row = db.execute("SELECT body FROM blobs WHERE hash=?", (key,)).fetchone()
    if row is None:
        raise ValueError("MissingBlob:" + reference)
    # The importer hashes original bytes. Its stored JSON can be canonicalized,
    # so provenance additionally freezes the read-only database and source files.
    return json.loads(row[0])


def prepare_payload(db, record, field, shared_pairs, groups, sequence):
    reference = (record.get("checkpointRef") if field == "input" else None) or record.get(field + "Ref")
    value = blob(db, reference) if reference else record.get(field)
    shared = []

    def walk(item, path, stack=()):
        if isinstance(item, dict):
            if "$evidenceRef" in item:
                if set(item) != {"$evidenceRef"} or not REFERENCE.fullmatch(str(item["$evidenceRef"])):
                    raise ValueError("MalformedSharedReference")
                ref = item["$evidenceRef"]
                if ref in stack or len(stack) > 32:
                    raise ValueError("SharedReferenceCycleOrDepth")
                resolved = blob(db, ref)
                if path and path[-1] == "KnockbackSettings":
                    if not isinstance(resolved, dict) or not resolved.get("CurveKeys"):
                        raise ValueError("UnsupportedKnockbackReference")
                    return walk(resolved, path, stack + (ref,))
                is_receive = field == "input" and record.get("stage") == "network.canonical" and path == ["batch"]
                is_apply = field == "input" and record.get("stage") == "replay.input" and record.get("operation") == "Apply" and path == [0]
                if not (is_receive or is_apply):
                    raise ValueError("UnsupportedSharedShape:" + compact([record.get("stage"), record.get("operation"), field, path]))
                if not isinstance(resolved, dict) or "ServerSequence" not in resolved or "Entities" not in resolved:
                    raise ValueError("UnsupportedSharedBatchType")
                if is_receive:
                    if ref in shared_pairs:
                        raise ValueError("OverlappingSharedReceive:" + ref)
                    group = "canonical-" + str(sequence)
                    shared_pairs[ref] = group
                    groups[group] = {"kind": "canonical", "firstSequence": str(sequence), "uses": 1, "sourceReference": ref}
                else:
                    if ref not in shared_pairs:
                        raise ValueError("UnpairedSharedApply:" + ref)
                    group = shared_pairs.pop(ref)
                    groups[group]["uses"] += 1
                    groups[group]["lastSequence"] = str(sequence)
                shared.append({"field": field, "path": path, "group": group, "kind": "canonical"})
                return walk(resolved, path, stack + (ref,))
            return {key: walk(member, path + [key], stack) for key, member in item.items()}
        if isinstance(item, list):
            return [walk(member, path + [index], stack) for index, member in enumerate(item)]
        return item

    return walk(value, []), shared


def source_encodings(db, capture, first, last):
    """Use verified physical envelopes, never guess Advance from operation names."""
    wanted = collections.defaultdict(dict)
    for sequence, path, line in db.execute(
            "SELECT seq,path,line FROM copies WHERE capture=? AND CAST(seq AS INTEGER) BETWEEN ? AND ? ORDER BY path,line", (capture, first, last)):
        wanted[path].setdefault(line, []).append(int(sequence))
    encodings, sources = {}, []
    for name, lines in wanted.items():
        path = Path(name)
        sources.append({"path": str(path.resolve()), "sha256": digest(path), "bytes": path.stat().st_size})
        with path.open(encoding="utf-8-sig") as stream:
            for index, line in enumerate(stream, 1):
                if index not in lines:
                    continue
                header = json.loads(line)
                encoding = header.get("encoding", "json")
                if encoding not in ("json", "gzip-jsonl-v1", "advance-binary-v1", "advance-columns-v1"):
                    raise ValueError("UnsupportedEncoding:" + encoding)
                for sequence in lines[index]:
                    previous = encodings.setdefault(sequence, encoding)
                    if previous != encoding:
                        raise ValueError("EncodingCopiesConflict")
    if set(encodings) != set(range(first, last + 1)):
        raise ValueError("SourceSequenceGap")
    return encodings, sources


def synthetic_identity(fixture_id, cycle):
    prefix = hashlib.sha256((fixture_id + ":" + str(cycle)).encode()).hexdigest()
    return "mixed-" + prefix[:24], prefix[24:56]


def record_clock_mapping(manifest):
    mode = manifest.get("recordClockMapping", "offset")
    if mode not in ("offset", "preserve-source"):
        raise ValueError("UnsupportedRecordClockMapping")
    return mode


def mapping(manifest, duration):
    if not 0 < duration <= 3600:
        raise ValueError("DurationOutsideBounds")
    period = manifest["cycleSeconds"]
    preserve = record_clock_mapping(manifest) == "preserve-source"
    return [{"cycle": cycle, "startSeconds": cycle * period,
             "runId": synthetic_identity(manifest["fixtureId"], cycle)[0],
             "captureId": synthetic_identity(manifest["fixtureId"], cycle)[1],
             "sequenceOffset": cycle * manifest["sequenceSpan"],
             "timeOffsetSeconds": 0 if preserve else cycle * period,
             "frameOffset": 0 if preserve else cycle * manifest["frameSpan"],
             "fixedStepOffset": 0 if preserve else cycle * manifest["fixedStepSpan"],
             "endSecondsExclusive": min(duration, (cycle + 1) * period)}
            for cycle in range(math.ceil(duration / period))]


def freeze(args):
    db_path, decoder_path, output = Path(args.database).resolve(), Path(args.decoder).resolve(), Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    db_hash, decoder_hash = digest(db_path), digest(decoder_path)
    decoder = load_decoder(decoder_path)
    db = sqlite3.connect(db_path.as_uri() + "?mode=ro", uri=True)
    db.execute("PRAGMA query_only=ON")
    if db.execute("SELECT COUNT(*) FROM issues").fetchone()[0]:
        raise ValueError("InputDatabaseHasIssues")
    encodings, sources = source_encodings(db, args.capture, args.first, args.last)
    pairs, groups, stages, kinds = {}, {}, collections.Counter(), collections.Counter()
    first_record = last_record = None
    previous = args.first - 1
    previous_monotonic = -math.inf
    body_hash = hashlib.sha256()
    fixture = output / "records.jsonl.gz"
    # Fixed gzip metadata makes preparation deterministic.
    with fixture.open("wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as zipped:
        for sequence, body in db.execute("SELECT CAST(seq AS INTEGER),body FROM records WHERE capture=? AND CAST(seq AS INTEGER) BETWEEN ? AND ? ORDER BY CAST(seq AS INTEGER)", (args.capture, args.first, args.last)):
            if sequence != previous + 1:
                raise ValueError("InputSequenceGap")
            previous = sequence
            record = json.loads(body)
            if not math.isfinite(record["monotonicTime"]) or record["monotonicTime"] < previous_monotonic:
                raise ValueError("NonMonotonicSourceSequence")
            previous_monotonic = record["monotonicTime"]
            original = copy.deepcopy(record)
            shared = []
            for field in ("input", "before", "after"):
                value, found = prepare_payload(db, record, field, pairs, groups, sequence)
                shared.extend(found)
                if value is not None:
                    record[field] = value
                else:
                    record.pop(field, None)
                record.pop(field + "Ref", None)
                if field == "input":
                    record.pop("checkpointRef", None)
                if value != decoder.payload(db, original, field):
                    raise ValueError("IndependentPayloadResolutionMismatch")
            kind = "advance" if encodings[sequence].startswith("advance-") else "record"
            if kind == "advance":
                if record.get("stage") not in ("replay.input", "replay.output") or shared:
                    raise ValueError("InvalidAdvanceLogicalShape")
            wrapper = {"record": record, "kind": kind, "shared": shared,
                       "sourceBodySha256": hashlib.sha256(body.encode()).hexdigest()}
            line = (compact(wrapper) + "\n").encode()
            body_hash.update(line)
            zipped.write(line)
            stages[record["stage"]] += 1
            kinds[kind] += 1
            first_record = first_record or record
            last_record = record
    if previous != args.last or pairs:
        raise ValueError("IncompleteSelectionOrUnpairedSharedInput")
    times = db.execute("SELECT MIN(json_extract(body,'$.monotonicTime')),MAX(json_extract(body,'$.monotonicTime')) FROM records WHERE capture=? AND CAST(seq AS INTEGER) BETWEEN ? AND ?", (args.capture, args.first, args.last)).fetchone()
    # One source frame between repeat cycles avoids an artificial simultaneous seam.
    period = times[1] - times[0] + 1 / 60
    manifest = {"schemaVersion": SCHEMA, "fixtureId": args.name, "fixture": fixture.name,
                "fixtureSha256": digest(fixture), "uncompressedSha256": body_hash.hexdigest(),
                "database": str(db_path), "databaseSha256": db_hash, "decoder": str(decoder_path), "decoderSha256": decoder_hash,
                "sourceCapture": args.capture, "sourceRun": first_record["runId"], "sourceRound": first_record["round"],
                "firstSequence": str(args.first), "lastSequence": str(args.last), "sequenceSpan": args.last - args.first + 1,
                "firstMonotonic": times[0], "lastMonotonic": times[1], "cycleSeconds": period,
                "frameSpan": last_record["frame"] - first_record["frame"] + 1,
                "fixedStepSpan": last_record["fixedStep"] - first_record["fixedStep"] + 1,
                "counts": dict(kinds), "stages": dict(stages), "sharedGroups": groups,
                "sources": sources, "limitations": LIMITATIONS}
    manifest["mapping600Seconds"] = mapping(manifest, 600)
    db.close()
    if digest(db_path) != db_hash or digest(decoder_path) != decoder_hash or any(digest(s["path"]) != s["sha256"] for s in sources):
        raise ValueError("FrozenInputChanged")
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(compact({"manifest": str(output / "manifest.json"), "counts": manifest["counts"], "sharedGroups": len(groups), "sourceSeconds": period}))


def check_artifacts(manifest, base):
    if manifest.get("schemaVersion") != SCHEMA:
        raise ValueError("UnsupportedFixtureSchema")
    if digest(base / manifest["fixture"]) != manifest["fixtureSha256"]:
        raise ValueError("FixtureHashMismatch")
    if digest(manifest["decoder"]) != manifest["decoderSha256"]:
        raise ValueError("DecoderHashMismatch")


def normal(value):
    """JSON null omission and floating rendering are not logical field changes."""
    if isinstance(value, dict):
        return {key: normal(member) for key, member in value.items() if member is not None}
    if isinstance(value, list):
        return [normal(member) for member in value]
    return value


def verify(args):
    manifest_path, output = Path(args.manifest).resolve(), Path(args.output).resolve()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    check_artifacts(manifest, manifest_path.parent)
    result = json.loads((output / "benchmark.json").read_text(encoding="utf-8"))
    clock_mode = record_clock_mapping(manifest)
    if result.get("recordClockMapping", "offset") != clock_mode:
        raise ValueError("BenchmarkClockMappingMismatch")
    manifest_hash = digest(manifest_path)
    if clock_mode == "preserve-source" and result.get("manifestSha256") != manifest_hash:
        raise ValueError("BenchmarkManifestMismatch")
    if result["fixtureSha256"] != manifest["fixtureSha256"]:
        raise ValueError("BenchmarkFixtureMismatch")
    decoder = load_decoder(manifest["decoder"])
    db_path = output / "verification.sqlite"
    if db_path.exists():
        raise ValueError("VerificationOutputExists")
    # Use the frozen decoder's normal schema and WAL initialization. This is a
    # disposable verification database; all import and comparison checks remain.
    db = decoder.connect(db_path)
    decoder.import_roots(db, [output / "capture"])
    db.commit()
    issues = db.execute("SELECT COUNT(*) FROM issues").fetchone()[0]
    differences, expected_count, matched = [], 0, 0
    missing_count = different_count = 0
    expected_contexts = {}
    for cycle in mapping(manifest, result["durationSeconds"]):
        with gzip.open(manifest_path.parent / manifest["fixture"], "rt", encoding="utf-8") as stream:
            for line in stream:
                expected = json.loads(line)["record"]
                due = expected["monotonicTime"] - manifest["firstMonotonic"] + cycle["startSeconds"]
                if due >= result["durationSeconds"]:
                    continue
                sequence = str(int(expected["recordSequence"]) + cycle["sequenceOffset"])
                expected_count += 1
                context = (cycle["captureId"], cycle["runId"], expected["round"])
                expected_contexts[context] = int(sequence)
                row = db.execute("SELECT body FROM records WHERE capture=? AND seq=?", (cycle["captureId"], sequence)).fetchone()
                if row is None:
                    missing_count += 1
                    if len(differences) < 20:
                        differences.append({"cycle": cycle["cycle"], "seq": sequence, "reason": "MissingRecord"})
                    continue
                actual = json.loads(row[0])
                # Recompute the declared identity/clock mapping independently.
                expected["captureId"], expected["runId"] = cycle["captureId"], cycle["runId"]
                expected["recordSequence"] = sequence
                if clock_mode == "offset":
                    expected["monotonicTime"] += cycle["timeOffsetSeconds"]
                    expected["networkTime"] += cycle["timeOffsetSeconds"]
                    expected["frame"] += cycle["frameOffset"]
                    expected["fixedStep"] += cycle["fixedStepOffset"]
                    # UTC uses original ticks + rounded offset ticks; avoid microsecond truncation.
                    match = re.fullmatch(r"(.+)\.(\d{7})Z", expected["utc"])
                    if not match:
                        raise ValueError("UnsupportedUtcPrecision")
                    from datetime import datetime, timedelta
                    seconds = datetime.fromisoformat(match[1])
                    ticks = int(match[2]) + round(cycle["timeOffsetSeconds"] * 10_000_000)
                    seconds += timedelta(seconds=ticks // 10_000_000)
                    expected["utc"] = seconds.isoformat(timespec="seconds") + "." + str(ticks % 10_000_000).zfill(7) + "Z"
                # preserve-source does not even add zero: signed zero and the
                # source clocks remain unchanged within each independent cycle.
                for field in ("input", "before", "after"):
                    value = decoder.payload(db, actual, field)
                    actual.pop(field + "Ref", None)
                    if field == "input":
                        actual.pop("checkpointRef", None)
                    actual[field] = value
                if normal(actual) == normal(expected):
                    matched += 1
                else:
                    different_count += 1
                    if len(differences) < 20:
                        differences.append({"cycle": cycle["cycle"], "seq": sequence, "reason": "LogicalDifference", "fields": [key for key in set(actual) | set(expected) if normal(actual.get(key)) != normal(expected.get(key))]})
    actual_count = db.execute("SELECT COUNT(*) FROM records").fetchone()[0]
    coverage = [json.loads(body) for (body,) in db.execute("SELECT body FROM metadata WHERE kind='coverage.json'")]
    actual_maxima = {(capture, run, round_number): maximum for capture, run, round_number, maximum in db.execute(
        "SELECT capture,run,round,MAX(CAST(seq AS INTEGER)) FROM records GROUP BY capture,run,round")}
    db.close()
    unexpected_count = actual_count - matched - different_count
    coverage_by_context = {(x.get("captureId"), x.get("runId"), x.get("round")): x for x in coverage}
    context_set_valid = bool(expected_contexts) and set(coverage_by_context) == set(expected_contexts) and len(coverage_by_context) == len(coverage)
    watermark_errors = []
    for context, maximum in expected_contexts.items():
        state = coverage_by_context.get(context)
        if state is None:
            watermark_errors.append({"capture": context[0], "reason": "MissingCoverage"})
            continue
        try:
            produced, written, flushed = (int(state[name]) for name in ("produced", "written", "flushed"))
            readable = actual_maxima.get(context, 0)
            if produced != maximum or written != readable or flushed != readable or state.get("queuedBytes", 0) != 0 or state.get("tailUnknown"):
                watermark_errors.append({"capture": context[0], "reason": "WatermarkMismatch", "expectedProduced": maximum,
                                         "actualMaximumSequence": readable, "produced": produced, "written": written, "flushed": flushed})
        except (KeyError, TypeError, ValueError):
            watermark_errors.append({"capture": context[0], "reason": "InvalidWatermark"})
    watermarks_valid = context_set_valid and not watermark_errors
    observation = json.loads((output / "writer-observation.json").read_text(encoding="utf-8-sig"))
    logical = observation.get("logicalRecords", {})
    counters_valid = (logical.get("attempts") == expected_count == result.get("attempted") and
                      logical.get("accepted") == actual_count == result.get("accepted") and
                      logical.get("rejected") == missing_count == result.get("rejected") == result.get("dropped") and
                      logical.get("captureFailed") == 0 and result.get("sharedCaptureFailures") == 0 and
                      observation.get("countsBalanced") is True and observation.get("unfinishedWorkItems") == 0)
    healthy = bool(result.get("joined") and result.get("observationExported") and not result.get("failure") and not result.get("lastFailure") and
                   result.get("pendingFinalBytes") == 0 and result.get("memoryFinalBytes") == 0 and counters_valid)
    logical_passed = not issues and matched == expected_count == actual_count and not differences and watermarks_valid and all(
        x.get("complete") and not x.get("dropped") and not x.get("failure") and not x.get("gaps") for x in coverage)
    rejection_slots = observation.get("firstRejections", [])
    baseline_rejection_only = bool(not issues and healthy and watermarks_valid and missing_count > 0 and different_count == 0 and unexpected_count == 0 and
        matched == actual_count and sum(x.get("dropped", 0) for x in coverage) == missing_count and
        all(not x.get("failure") and all(gap.get("reason") in ("QueueOverload", "CoalescedCaptureGap") for gap in x.get("gaps", [])) for x in coverage) and
        rejection_slots and all(slot.get("guard") == "QueueLimit" and slot.get("entry") in ("TryWrite", "TryWriteAdvance") for slot in rejection_slots))
    passed = logical_passed and result.get("paceValid") is True and healthy
    report = {"passed": bool(passed), "logicalPassed": bool(logical_passed), "paceValid": result.get("paceValid", False),
              "recordClockMapping": clock_mode, "manifestSha256": manifest_hash,
              "scheduleMapping": mapping(manifest, result["durationSeconds"]),
              "expected": expected_count, "actual": actual_count, "matched": matched,
              "missingRecords": missing_count, "differentRecords": different_count, "unexpectedRecords": unexpected_count,
              "metadataWatermarksValid": watermarks_valid, "watermarkErrors": watermark_errors,
              "countersValid": counters_valid, "benchmarkHealthy": healthy, "baselineQueueRejectionOnly": baseline_rejection_only,
              "issues": issues, "differences": differences, "coverage": coverage,
              "benchmark": result, "limitations": LIMITATIONS}
    (output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(compact({key: report[key] for key in ("passed", "expected", "actual", "matched", "issues", "differences")}))
    return 0 if passed else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prepare = commands.add_parser("freeze")
    for name in ("database", "decoder", "output", "capture", "name"):
        prepare.add_argument("--" + name, required=True)
    prepare.add_argument("--first", required=True, type=int)
    prepare.add_argument("--last", required=True, type=int)
    inspect = commands.add_parser("verify")
    inspect.add_argument("--manifest", required=True)
    inspect.add_argument("--output", required=True)
    args = parser.parse_args()
    return freeze(args) if args.command == "freeze" else verify(args)


if __name__ == "__main__":
    sys.dont_write_bytecode = True
    raise SystemExit(main())
