#!/usr/bin/env python3
"""Stream combat evidence into SQLite; query, inspect coverage, and extract Unity replay fixtures.
No third party Python packages, network service, or game UI interaction required.
"""
import argparse
import base64
import io
import gzip
import hashlib
import html
import json
import math
import re
import sqlite3
import struct
from datetime import datetime, timedelta
from pathlib import Path

SCHEMA = """
CREATE TABLE IF NOT EXISTS records(capture TEXT, seq TEXT, run TEXT, round INTEGER,
 event TEXT, root TEXT, parent TEXT, entity INTEGER, stage TEXT, reason TEXT,
 engine TEXT, utc TEXT, server INTEGER, body TEXT, PRIMARY KEY(capture,seq));
CREATE INDEX IF NOT EXISTS by_event ON records(event);
CREATE INDEX IF NOT EXISTS by_root ON records(root);
CREATE INDEX IF NOT EXISTS by_entity ON records(entity,utc);
CREATE INDEX IF NOT EXISTS by_engine ON records(capture,engine,length(seq),seq);
CREATE TABLE IF NOT EXISTS copies(capture TEXT,seq TEXT,path TEXT,line INTEGER,PRIMARY KEY(capture,seq,path));
CREATE TABLE IF NOT EXISTS metadata(path TEXT PRIMARY KEY, kind TEXT, body TEXT);
CREATE TABLE IF NOT EXISTS issues(path TEXT,line INTEGER,reason TEXT,details TEXT);
CREATE TABLE IF NOT EXISTS blobs(hash TEXT PRIMARY KEY,body TEXT);
CREATE TABLE IF NOT EXISTS event_links(capture TEXT,seq TEXT,event TEXT,PRIMARY KEY(capture,seq,event));
CREATE INDEX IF NOT EXISTS linked_event ON event_links(event);
CREATE TABLE IF NOT EXISTS facets(capture TEXT,seq TEXT,kind TEXT,value TEXT,
 PRIMARY KEY(capture,seq,kind,value));
CREATE INDEX IF NOT EXISTS by_facet ON facets(kind,value,capture,seq);
CREATE TABLE IF NOT EXISTS pending_indexes(capture TEXT,seq TEXT,PRIMARY KEY(capture,seq));
"""
MAX_BLOB = 32 << 20


def expand_records(raw):
    item = json.loads(raw)
    encoding = item.get("encoding")
    if encoding not in ("gzip-jsonl-v1", "advance-columns-v1", "advance-binary-v1"):
        if encoding: raise ValueError("Unsupported evidence encoding")
        if item.get("schemaVersion") not in (1, 2):
            raise ValueError("Unsupported schema")
        return [item]
    if item.get("schemaVersion") != 2 or not 0 < item.get("count", 0) <= 65536:
        raise ValueError("Invalid evidence block header")
    with gzip.GzipFile(fileobj=io.BytesIO(base64.b64decode(item["data"], validate=True))) as stream:
        decoded = stream.read((1 << 20) + 1)
    if len(decoded) > 1 << 20 or hashlib.sha256(decoded).hexdigest() != item["hash"]:
        raise ValueError("Evidence block size or checksum mismatch")
    if encoding in ("advance-columns-v1", "advance-binary-v1"):
        body = decode_binary_advances(decoded) if encoding == "advance-binary-v1" else json.loads(decoded)
        records = expand_advances(item, body)
        validate_block_range(item, records)
        return records
    if not decoded.endswith(b"\n"):
        raise ValueError("Truncated decoded evidence block")
    records = []
    for line in decoded.splitlines():
        record = json.loads(line)
        if not isinstance(record, dict) or record.get("schemaVersion") not in (1, 2):
            raise ValueError("Unsupported inner record schema")
        completion = record.pop("completion", None)
        if completion:
            if record.get("stage") != "replay.call" or record.get("inputRef") or record.get("checkpointRef"):
                raise ValueError("Invalid folded operation")
            record["stage"] = "replay.input"
            end = dict(record, stage="replay.output", outcome="Completed", critical=False,
                       recordSequence=completion["sequence"], monotonicTime=completion["monotonic"],
                       networkTime=completion["network"], frame=completion["frame"],
                       fixedStep=completion["fixedStep"], estimatedBytes=completion["estimatedBytes"])
            for field in ("input", "before", "inputRef", "utc"):
                end.pop(field, None)
            if "utc" in completion: end["utc"] = completion["utc"]
            records.extend((record, end))
        else:
            records.append(record)
    validate_block_range(item, records)
    return records


def validate_block_range(item, records):
    if not records or len(records) != item["count"] or str(records[0]["recordSequence"]) != item["first"] or str(records[-1]["recordSequence"]) != item["last"]:
        raise ValueError("Evidence block range mismatch")
    previous = 0
    for record in records:
        sequence = str(record["recordSequence"])
        if not sequence.isdecimal() or not previous < int(sequence) < 1 << 64:
            raise ValueError("Invalid evidence block sequence order")
        previous = int(sequence)


def unity_single_json(value):
    """Mono Single.ToString('R'): seven significant digits if they round-trip, else nine."""
    if not math.isfinite(value): return "NaN" if math.isnan(value) else "Infinity" if value > 0 else "-Infinity"
    candidate = float(format(value, ".7g"))
    try:
        if struct.pack("<f", candidate) == struct.pack("<f", value): return candidate
    except OverflowError:
        pass
    return float(format(value, ".9g"))


def decode_binary_advances(raw):
    cursor = 0
    def read(format):
        nonlocal cursor
        size = struct.calcsize("<" + format)
        if cursor + size > len(raw): raise ValueError("Truncated advance binary block")
        values = struct.unpack_from("<" + format, raw, cursor); cursor += size
        return values[0] if len(values) == 1 else values
    def text():
        nonlocal cursor
        length = read("H")
        if length == 0xffff: return None
        if length > 1024 or cursor + length > len(raw): raise ValueError("Invalid advance binary string length")
        value = raw[cursor:cursor+length].decode("utf-8", errors="strict"); cursor += length
        return value
    magic, count, engine_count, boundary_count = read("IHHH")
    if magic != 0x32445641 or not 0 < count <= 128 or not 0 < engine_count <= 128 or not 0 <= boundary_count <= 128:
        raise ValueError("Invalid advance binary header")
    engines = [[text(), text(), text()] for _ in range(engine_count)]
    boundaries = []
    for _ in range(boundary_count):
        flags = read("B")
        if flags & ~63: raise ValueError("Invalid advance boundary flags")
        boundary = {key: bool(flags & bit) for key, bit in (("eventIds", 1), ("supported", 2), ("executeAll", 4), ("offline", 8), ("server", 16))}
        if flags & 32:
            slot, epoch, next_sequence = read("HHI")
            boundary["ids"] = {"slot": slot, "epoch": epoch, "next": next_sequence}
        boundary["localPlayer"], boundary["targetOwner"] = read("II")
        boundaries.append(boundary)
    rows = []
    for _ in range(count):
        row = list(read("QqddiiBfHh"))
        row[0], row[1], row[7] = str(row[0]), str(row[1]), unity_single_json(row[7])
        for i in (2, 3):
            if not math.isfinite(row[i]): row[i] = "NaN" if math.isnan(row[i]) else "Infinity" if row[i] > 0 else "-Infinity"
        rows.append(row)
    if cursor != len(raw): raise ValueError("Trailing bytes in advance binary block")
    return {"engines": engines, "boundaries": boundaries, "rows": rows}


def expand_advances(envelope, body):
    engines, boundaries, rows = body["engines"], body["boundaries"], body["rows"]
    if not isinstance(rows, list) or not 0 < len(rows) <= 128 or len(engines) > 128 or len(boundaries) > 128:
        raise ValueError("Invalid advance block row count")
    records = []
    for row in rows:
        if len(row) != 10: raise ValueError("Invalid advance row")
        sequence, ticks, monotonic, network, frame, fixed, phase, delta, engine, boundary = row
        if phase not in (0, 1, 2) or not isinstance(engine, int) or not 0 <= engine < len(engines):
            raise ValueError("Invalid advance phase or engine")
        if not isinstance(boundary, int) or boundary < -1 or boundary >= len(boundaries):
            raise ValueError("Invalid advance boundary")
        if phase != 0 and boundary != -1: raise ValueError("Completion cannot carry an input boundary")
        if len(engines[engine]) != 3: raise ValueError("Invalid advance engine identity")
        role, engine_id, operation = engines[engine]
        try:
            seconds, fraction = divmod(int(ticks), 10000000)
            date = datetime(1, 1, 1) + timedelta(seconds=seconds)
        except (ValueError, OverflowError) as error:
            raise ValueError("Invalid advance UTC ticks") from error
        utc = f"{date.year:04}-{date.month:02}-{date.day:02}T{date.hour:02}:{date.minute:02}:{date.second:02}.{fraction:07}Z"
        record = dict(schemaVersion=2, captureId=envelope["captureId"], runId=envelope["runId"], round=envelope["round"],
                      recordSequence=str(sequence), role=role, engine=engine_id, operation=operation,
                      stage="replay.input" if phase == 0 else "replay.output", utc=utc, monotonicTime=monotonic,
                      networkTime=network, frame=frame, fixedStep=fixed, estimatedBytes=512, critical=phase != 1,
                      source=0, target=0, connectionEpoch=0, assignmentEpoch=0, batchSequence=0,
                      serverSequence=0, stateVersion=0, applicationRevision=0, tickIndex=0)
        for key in ("role", "engine", "operation"):
            if record[key] is None: del record[key]
        if phase == 0:
            record["input"] = [delta]
            if boundary >= 0: record["before"] = boundaries[boundary]
        else:
            record["outcome"] = "Completed" if phase == 1 else "Aborted"
            if phase == 2: record["reason"] = "ExceptionOrEarlyExit"
        records.append(record)
    return records


def connect(path):
    db = sqlite3.connect(path)
    db.row_factory = sqlite3.Row
    db.execute("PRAGMA journal_mode=WAL")
    db.executescript(SCHEMA)
    return db


def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def import_roots(db, roots):
    count = 0
    for root in map(Path, roots):
        if not root.is_dir():
            raise ValueError(f"Missing evidence directory: {root}")
        for path in root.rglob("*.json.gz"):
            try:
                with gzip.open(path, "rb") as stream:
                    raw = stream.read(MAX_BLOB + 1)
                if len(raw) > MAX_BLOB:
                    raise ValueError("Oversized blob")
                digest = hashlib.sha256(raw).hexdigest()
                if path.name != digest + ".json.gz":
                    raise ValueError("Content hash mismatch")
                db.execute("INSERT OR IGNORE INTO blobs VALUES(?,?)", (digest, compact(json.loads(raw))))
            except (OSError, ValueError, EOFError) as error:
                db.execute("INSERT INTO issues VALUES(?,0,'InvalidBlob',?)", (str(path), str(error)))
        for path in root.rglob("*.json"):
            try:
                if path.stat().st_size > MAX_BLOB:
                    raise ValueError("Oversized metadata")
                body = json.loads(path.read_text(encoding="utf-8-sig"))
                db.execute("INSERT OR REPLACE INTO metadata VALUES(?,?,?)", (str(path), path.name, compact(body)))
            except (OSError, ValueError) as error:
                db.execute("INSERT INTO issues VALUES(?,0,'InvalidMetadata',?)", (str(path), str(error)))
        for path in root.rglob("retention.jsonl"):
            with path.open("rb") as stream:
                number = 0
                while raw := stream.readline(MAX_BLOB + 1):
                    number += 1
                    try:
                        if not raw.endswith(b"\n"): raise ValueError("Truncated cleanup audit")
                        db.execute("INSERT OR REPLACE INTO metadata VALUES(?,?,?)", (str(path) + ":" + str(number), "retention.jsonl", compact(json.loads(raw))))
                    except ValueError as error:
                        db.execute("INSERT INTO issues VALUES(?,?,'InvalidMetadata',?)", (str(path), number, str(error)))
                        break
        for path in root.rglob("events-*.jsonl"):
            with path.open("rb") as stream:
                number = 0
                while True:
                    raw = stream.readline(MAX_BLOB + 1)
                    if not raw:
                        break
                    number += 1
                    if not raw.endswith(b"\n"):
                        db.execute("INSERT INTO issues VALUES(?,?,'TruncatedOrOversizedLine',?)", (str(path), number, "Tail range unknown"))
                        break
                    try:
                        for item in expand_records(raw):
                            capture, seq = item["captureId"], str(item["recordSequence"])
                            if not seq.isdecimal():
                                raise ValueError("Invalid sequence")
                            body = compact(item)
                            previous = db.execute("SELECT body FROM records WHERE capture=? AND seq=?", (capture, seq)).fetchone()
                            if previous and previous[0] != body:
                                db.execute("INSERT INTO issues VALUES(?,?,'ConflictingCopy',?)", (str(path), number, capture + ":" + seq))
                            db.execute("INSERT OR IGNORE INTO records VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (
                                capture, seq, item.get("runId"), item.get("round", 0), item.get("eventId"), item.get("rootEventId"),
                                item.get("parentEventId"), item.get("target", 0), item.get("stage"), item.get("reason"),
                                item.get("engine"), item.get("utc"), item.get("serverSequence", 0), body))
                            db.execute("INSERT OR IGNORE INTO copies VALUES(?,?,?,?)", (capture, seq, str(path.resolve()), number))
                            if not previous or previous[0] == body:
                                index_batch_events(db, item)
                                index_facets(db, item)
                                if item.get("inputRef") or item.get("input") is not None:
                                    try: payload(db, item)
                                    except ValueError:
                                        db.execute("INSERT OR IGNORE INTO pending_indexes VALUES(?,?)", (capture, seq))
                            count += 1
                            if count % 1000 == 0:
                                db.commit()
                    except (ValueError, KeyError, TypeError, OSError, EOFError) as error:
                        db.execute("INSERT INTO issues VALUES(?,?,'InvalidRecord',?)", (str(path), number, str(error)))
        db.commit()
    # A later peer directory may supply a blob absent from the first source directory.
    for row in db.execute("SELECT records.body FROM records JOIN pending_indexes USING(capture,seq)"):
        item = json.loads(row[0])
        try: payload(db, item)
        except ValueError: continue
        index_batch_events(db, item); index_facets(db, item)
        db.execute("DELETE FROM pending_indexes WHERE capture=? AND seq=?", (item["captureId"], str(item["recordSequence"])))
    # Join explicit business batches to message envelopes, then their exact payload identities to transport packets.
    # A Steam connection handle or a timestamp alone cannot establish this association.
    db.execute("""INSERT OR IGNORE INTO event_links
        SELECT m.capture,m.seq,e.event FROM facets m
        JOIN records r ON r.capture=m.capture AND r.seq=m.seq
        JOIN facets b ON b.kind=m.kind AND b.value=m.value
        JOIN event_links e ON e.capture=b.capture AND e.seq=b.seq
        WHERE m.kind IN ('submission','canonical') AND r.stage='network.message'""")
    db.execute("""INSERT OR IGNORE INTO event_links
        SELECT packet.capture,packet.seq,e.event FROM facets packet
        JOIN records r ON r.capture=packet.capture AND r.seq=packet.seq
        JOIN facets message ON message.kind=packet.kind AND message.value=packet.value
        JOIN event_links e ON e.capture=message.capture AND e.seq=message.seq
        WHERE packet.kind='message' AND r.stage='network.transport'""")
    db.commit()
    return {"importedCopies": count, "originalRecords": db.execute("SELECT count(*) FROM records").fetchone()[0], "coverage": coverage(db)}


def index_batch_events(db, record):
    # Index actual batch membership, not just a sequence number that another sender can reuse.
    direct = {str(record[key]) for key in ("eventId", "rootEventId", "parentEventId") if record.get(key) and str(record[key]) != "0"}
    db.executemany("INSERT OR IGNORE INTO event_links VALUES(?,?,?)", ((record["captureId"], str(record["recordSequence"]), event) for event in direct))
    if record.get("stage") not in ("collector.drain", "network.submit", "gateway.decision", "replay.input"):
        return
    try:
        value = payload(db, record)
    except ValueError:
        return  # Missing content remains visible when resolving the record and in replay validation.
    candidates = value if isinstance(value, list) else [value]
    ids = set()
    for candidate in candidates:
        if not isinstance(candidate, dict): continue
        for field in ("Results", "StatusMutations", "EnemyDeathReports", "PlayerHealthReports"):
            for entry in candidate.get(field) or []:
                if not isinstance(entry, dict): continue
                for key in ("EventId", "RootEventId", "ParentEventId", "CauseEventId"):
                    event = entry.get(key)
                    if event and str(event) != "0": ids.add(str(event))
    db.executemany("INSERT OR IGNORE INTO event_links VALUES(?,?,?)", ((record["captureId"], str(record["recordSequence"]), event) for event in ids))


def payload(db, record, field="input"):
    ref = (record.get("checkpointRef") if field == "input" else None) or record.get(field + "Ref")
    budget = [MAX_BLOB]
    def charge(amount):
        budget[0] -= amount
        if budget[0] < 0: raise ValueError("ExpandedPayloadLimitExceeded")
    def read(reference, stack, depth, strict):
        if strict and (not isinstance(reference, str) or not re.fullmatch(r"inputs/[0-9a-f]{64}\.json\.gz", reference)):
            raise ValueError("InvalidSharedReference:" + str(reference))
        digest = Path(reference).name.split(".")[0]
        if digest in stack: raise ValueError("SharedReferenceCycle:" + reference)
        row = db.execute("SELECT body FROM blobs WHERE hash=?", (digest,)).fetchone()
        if row is None: raise ValueError("MissingBlob:" + reference)
        charge(len(row[0]))
        return resolve(json.loads(row[0]), stack + (digest,), depth + 1)
    def resolve(value, stack, depth):
        if depth > 32: raise ValueError("SharedReferenceDepthExceeded")
        charge(32)
        if isinstance(value, dict):
            if set(value) == {"$evidenceRef"}: return read(value["$evidenceRef"], stack, depth, True)
            return {key: resolve(member, stack, depth + 1) for key, member in value.items()}
        if isinstance(value, list): return [resolve(member, stack, depth + 1) for member in value]
        if isinstance(value, str): charge(len(value) * 2)
        return value
    return read(ref, (), 0, False) if ref else resolve(record.get(field), (), 0)


def index_facets(db, record):
    """Index stable identities, never infer a connection ID from an authority epoch."""
    facets = set()
    def add(kind, value, allow_zero=False):
        if isinstance(value, dict): value = value.get("Value")
        if value is not None and value != "" and (allow_zero or str(value) != "0"):
            facets.add((kind, str(value)))
    add("player", record.get("source"))
    add("status", record.get("statusInstanceId"))
    add("connection", record.get("connectionId"), True)
    context = [record.get("runId"), record.get("round", 0)]
    if record.get("batchSequence") and record.get("source"):
        add("submission", compact(context + [record["source"], record["batchSequence"]]))
    if record.get("serverSequence"):
        add("canonical", compact(context + [record["serverSequence"]]))
    # Checkpoints describe every entity and are not each entity's execution history.
    if record.get("stage") not in ("replay.checkpoint", "replay.engine_checkpoint", "observation.snapshot"):
        try:
            root = payload(db, record)
            if record.get("stage") == "performance.snapshot" and isinstance(root, str): root = json.loads(root)
            values = [root]
        except ValueError:
            values = []
        while values:
            value = values.pop()
            if isinstance(value, list):
                values.extend(value)
            elif isinstance(value, dict):
                for key, member in value.items():
                    normalized = key.replace("_", "").lower()
                    if normalized in ("sourceentityid", "sourceplayerid", "playerid"):
                        add("player", member)
                    elif normalized == "statusinstanceid" or (normalized == "instanceid" and record.get("stage", "").startswith(("status.", "owner.dot"))): add("status", member)
                    elif normalized in ("connectionid", "peerid"): add("connection", member, True)
                    elif normalized == "steamconnection": add("steam_connection", member, True)
                    elif isinstance(member, (dict, list)): values.append(member)
                if value.get("payloadHash") and value.get("correlation") == "PayloadIdentity":
                    add("message", compact(context + [value.get(k) for k in ("messageId", "kind", "entity", "component", "function", "payloadHash")]))
    db.executemany("INSERT OR IGNORE INTO facets VALUES(?,?,?,?)",
                   ((record["captureId"], str(record["recordSequence"]), kind, value) for kind, value in facets))


def coverage(db):
    return {"interpretation": "Missing records never prove non-execution. Copies retain original identity; UTC is not causal order.",
            "sources": [dict(row) | {"body": json.loads(row["body"])} for row in db.execute("SELECT * FROM metadata WHERE kind IN ('coverage.json','recovery.json','retention.json','retention.local.json','retention.jsonl','replication.json','manifest.json')")],
            "issues": [dict(row) for row in db.execute("SELECT * FROM issues LIMIT 1000")],
            "issueCount": db.execute("SELECT count(*) FROM issues").fetchone()[0]}


def query(db, args):
    clauses, values = [], []
    for attr, column in (("entity", "entity"), ("reason", "reason"), ("capture", "capture"), ("run", "run"), ("round", "round"), ("engine", "engine")):
        value = getattr(args, attr, None)
        if value is not None:
            clauses.append(column + "=?")
            values.append(value)
    for attr, kind in (("player", "player"), ("status", "status"), ("connection", "connection"), ("steam_connection", "steam_connection")):
        value = getattr(args, attr, None)
        if value is not None:
            clauses.append("(capture,seq) IN (SELECT capture,seq FROM facets WHERE kind=? AND value=?)")
            values.extend((kind, str(value)))
    if getattr(args, "after", None):
        clauses.append("utc>=?"); values.append(args.after)
    if getattr(args, "before", None):
        clauses.append("utc<=?"); values.append(args.before)
    if getattr(args, "build", None):
        captures = set()
        for row in db.execute("SELECT capture,body FROM records WHERE stage IN ('source.start','process.start')"):
            try:
                data = payload(db, json.loads(row["body"])) or {}
                manifest = json.loads(data.get("buildManifest") or "{}")
                if args.build in (data.get("buildGuid"), manifest.get("sourceConfigurationHash")): captures.add(row["capture"])
            except (ValueError, TypeError): pass
        clauses.append("capture IN (" + ",".join("?" for _ in captures) + ")" if captures else "0")
        values += list(captures)
    event = getattr(args, "event", None)
    if event:
        # Find the related event tree, then canonical outputs linked to those events.
        ids = {event}
        context_clauses, context_values = [], []
        for field in ("run", "round"):
            if getattr(args, field, None) is not None:
                context_clauses.append(field + "=?"); context_values.append(getattr(args, field))
        context_sql = " AND " + " AND ".join(context_clauses) if context_clauses else ""
        for _ in range(64):
            marks = ",".join("?" for _ in ids)
            rows = db.execute(f"SELECT event,root,parent FROM records WHERE (event IN ({marks}) OR root IN ({marks}) OR parent IN ({marks})){context_sql}", list(ids) * 3 + context_values)
            more = {v for row in rows for v in row if v and v != "0"}
            if more <= ids: break
            ids |= more
            if len(ids) > 10000: raise ValueError("Event tree exceeds bounded query; narrow by run or entity")
        marks = ",".join("?" for _ in ids)
        clauses.append(f"(event IN ({marks}) OR root IN ({marks}) OR parent IN ({marks}) OR (server<>0 AND (run,round,server) IN (SELECT run,round,server FROM records WHERE event IN ({marks}) AND server<>0)) OR (capture,seq) IN (SELECT capture,seq FROM event_links WHERE event IN ({marks})))")
        values += list(ids) * 5
    sql = "SELECT * FROM records" + (" WHERE " + " AND ".join(clauses) if clauses else "") + " ORDER BY capture,length(seq),seq LIMIT ?"
    limit = max(1, min(getattr(args, "limit", 2000), 10000))
    records = []
    for row in db.execute(sql, values + [limit + 1]):
        item = json.loads(row["body"])
        item["references"] = [dict(ref) for ref in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=?", (row["capture"], row["seq"]))]
        if getattr(args, "resolve_inputs", False):
            for field in ("input", "before", "after"):
                try: item[field] = payload(db, item, field)
                except ValueError as error:
                    item.setdefault("evidenceGaps", []).append(str(error))
                    item.setdefault("missingPayloads", []).append({"field": field, "reason": str(error)})
                    item["evidenceGap"] = str(error)
        item["description"] = describe_record(item)
        records.append(item)
    stages = {r["stage"] for r in records}
    expected = ("owner.hit", "collector.enqueue", "network.submit", "gateway.decision", "ledger.apply", "gateway.canonical_link", "replica.entity")
    return {"records": records[:limit], "truncated": len(records) > limit,
            "missingPayloads": [dict(evidence_pointer(r), **failure) for r in records[:limit] for failure in r.get("missingPayloads", [])],
            "missingStages": [s for s in expected if s not in stages] if event else [], "coverage": coverage(db)}


def evidence_pointer(record):
    return {"captureId": record["captureId"], "recordSequence": str(record["recordSequence"]),
            "eventId": record.get("eventId"), "references": record.get("references", [])}


def history(db, args):
    """A causal evidence report, not a claim to replay Unity collision callbacks."""
    options = argparse.Namespace(**vars(args)); options.resolve_inputs = True
    result = query(db, options)
    result["kind"] = args.command
    result["ordering"] = "Original source and record sequence; cross-source UTC is not an execution order."
    result["limitations"] = ["MissingRecordsNeverProveNonExecution"]
    result["evidenceGaps"] = [dict(evidence_pointer(r), reason=reason) for r in result["records"]
                              for reason in r.get("evidenceGaps", [])]
    for metadata in result["coverage"]["sources"]:
        if metadata["kind"] != "coverage.json": continue
        state = metadata["body"]
        if any(getattr(args, arg, None) is not None and str(getattr(args, arg)) != str(state.get(field))
               for arg, field in (("capture", "captureId"), ("run", "runId"), ("round", "round"))): continue
        for gap in state.get("gaps", []):
            result["evidenceGaps"].append(dict(gap, captureId=state.get("captureId"), runId=state.get("runId"),
                                             round=state.get("round"), references=[{"path": metadata["path"]}]))
        if state.get("tailUnknown"):
            result["evidenceGaps"].append({"reason": "SourceTailUnknown", "captureId": state.get("captureId"),
                                          "flushed": state.get("flushed"), "references": [{"path": metadata["path"]}]})
        if state.get("failure") and not state.get("gaps"):
            result["evidenceGaps"].append({"reason": "SourceFailureUnknownRange:" + state["failure"],
                                          "references": [{"path": metadata["path"]}]})
    if result["truncated"]: result["evidenceGaps"].append({"reason": "QueryLimitReached"})
    result["flow"] = [dict(evidence_pointer(r), stage=r.get("stage"), source=r.get("source"), target=r.get("target"),
                           input=r.get("input"), before=r.get("before"),
                           decision={"outcome": r.get("outcome"), "reason": r.get("reason")}, after=r.get("after"))
                      for r in result["records"]]
    failures = [r for r in result["records"] if r.get("outcome") in ("Failed", "Rejected", "Aborted")]
    result["firstObservedFailure"] = failures[0] if failures else None
    result["firstDivergence"] = None
    result["divergenceStatus"] = "RequiresMatchingReplayOrCanonicalStateEvidence"
    if args.command == "player-output":
        layers = {"owner.damage_calculation": "calculation", "owner.damage": "prediction", "owner.hit": "prediction",
                  "owner.dot_damage": "prediction", "ledger.apply": "canonical", "stats.damage": "statistics", "statistics.damage": "statistics"}
        result["damagePerspectives"] = {}
        for record in result["records"]:
            layer = layers.get(record.get("stage"))
            if layer:
                result["damagePerspectives"].setdefault(layer, []).append(dict(evidence_pointer(record), input=record.get("input"),
                                                                            before=record.get("before"), after=record.get("after")))
        result["limitations"] += ["DamagePerspectivesHaveDifferentSemantics", "PhysicsBoundaryNotReplayable"]
        result["hitBoundary"] = "An attack with no contact evidence stops at the recorded attack/window boundary; this does not prove that no collision occurred."
    elif args.command == "dot":
        result["ticks"] = [dict(evidence_pointer(r), source=r.get("source"), target=r.get("target"),
                                statusInstanceId=r.get("statusInstanceId"), applicationRevision=r.get("applicationRevision"),
                                tickIndex=r.get("tickIndex"), outcome=r.get("outcome"), reason=r.get("reason"))
                           for r in result["records"] if r.get("stage") in ("status.tick", "owner.dot_damage")]
        result["limitations"].append("RepeatedTickEvidenceCanDescribeDifferentStagesOrRoles")
    elif args.command == "connection":
        result["limitations"] += ["ConnectionIdIsLocalToCapture", "SteamHandleIsNotMirrorConnectionId", "SuccessfulSendDoesNotProveRemoteApplication"]
    report_path = getattr(args, "replay_report", None)
    if report_path:
        path = Path(report_path)
        if path.stat().st_size > MAX_BLOB: raise ValueError("Oversized replay report")
        report = json.loads(path.read_text(encoding="utf-8-sig"))
        result["replayReport"] = report
        result["replayReportReference"] = str(path.resolve())
        if report.get("reliable") and report.get("firstDivergence", -1) >= 0:
            matched = next((r for r in result["records"] if r["captureId"] + ":" + str(r["recordSequence"]) == report.get("record")), None)
            result["firstDivergence"] = dict(report, references=matched["references"] if matched else [])
            result["divergenceStatus"] = "BusinessReplayReportedDifference"
            if matched is None: result["evidenceGaps"].append({"reason": "ReplayRecordOutsideQuery", "record": report.get("record")})
        elif report.get("reliable") and report.get("passed"):
            result["divergenceStatus"] = "ReplayPassedForSuppliedFixtureOnly"
        else:
            result["divergenceStatus"] = "ReplayUnreliableOrUnsupported"
            result["evidenceGaps"].append({"reason": report.get("reason", "ReplayUnreliableOrUnsupported")})
    return result


def compare(db, args):
    options = argparse.Namespace(**vars(args)); options.resolve_inputs = True
    result = query(db, options)
    canonical, replicas, differences, unmatched = {}, [], [], []
    for record in result["records"]:
        state = record.get("after")
        if not isinstance(state, dict) or "StateVersion" not in state: continue
        key = (record.get("runId"), record.get("round"), record.get("target"), state["StateVersion"])
        if record["stage"] == "gateway.canonical_link": canonical.setdefault(key, record)
        if record["stage"] == "replica.entity" and record.get("outcome") == "Applied": replicas.append((key, record))
    for key, record in replicas:
        expected = canonical.get(key)
        if expected is None:
            unmatched.append({"state": key, "record": record["references"], "reason": "MissingServerStateEvidence"})
        elif expected["after"] != record["after"]:
            differences.append({"state": key, "expected": expected, "actual": record})
    return {"comparison": "Canonical states matched by run, round, entity and state version; predicted/display states are separate evidence.",
            "firstDivergence": differences[0] if differences else None, "differences": differences,
            "unmatched": unmatched, "truncated": result["truncated"], "coverage": result["coverage"]}


def locate(db, args):
    result = query(db, args); found = {}
    for record in result["records"]:
        if record.get("stage") != "observation.snapshot": continue
        try: actors = (payload(db, record) or {}).get("actors", [])
        except ValueError: continue
        for actor in actors:
            if args.monster_type.casefold() not in actor.get("name", "").casefold(): continue
            key = (record["captureId"], record["runId"], record["round"], actor["entity"])
            if key not in found:
                found[key] = dict(capture=key[0], run=key[1], round=key[2], entity=str(key[3]), name=actor["name"], first=record.get("utc"))
            found[key].update(last=record.get("utc"), lastSnapshot=actor, references=record["references"])
    return {"entities": list(found.values()), "truncated": result["truncated"], "coverage": result["coverage"]}


def extract(db, capture, engine, first=None, last=None):
    if last is None:
        latest = db.execute("SELECT seq FROM records WHERE capture=? AND engine=? ORDER BY length(seq) DESC,seq DESC LIMIT 1", (capture, engine)).fetchone()
        if latest is not None: last = int(latest[0])
    rows = db.execute("SELECT seq,stage,engine,body FROM records WHERE capture=? ORDER BY length(seq),seq", (capture,))
    baseline = None; steps = []; pending = None; gaps = []; domain = None; last_checkpoint = None; previous_sequence = None; contexts = set()
    for row in rows:
        seq = int(row["seq"])
        if last is not None and seq > last: break
        record = json.loads(row["body"])
        if baseline is not None and previous_sequence is not None and seq != previous_sequence + 1:
            gaps.append(f"MissingRecordRange:{previous_sequence + 1}-{seq - 1}")
        previous_sequence = seq
        if row["stage"] in ("replay.checkpoint", "replay.engine_checkpoint"):
            try: data = payload(db, record)
            except ValueError as error: gaps.append(str(error)); continue
            checkpoints = data.get("engines", []) if row["engine"] == "*" else [data]
            match = next((c for c in checkpoints if c["engine"] == engine), None)
            if match:
                if baseline is None or (first is not None and seq <= first):
                    baseline = match["state"]; domain = match["domain"]; steps = []; pending = None; gaps = []; last_checkpoint = seq
                    contexts = {(record.get("runId"), record.get("round", 0))}
                elif steps:
                    steps[-1]["expectedState"] = match["state"]
        elif baseline is not None:
            if row["stage"] == "evidence.gap": gaps.append("RecordedGap:" + str(seq))
            if row["engine"] != engine: continue
            contexts.add((record.get("runId"), record.get("round", 0)))
            if row["stage"] in ("owner.damage_calculation", "owner.attack_stats", "stats.damage"):
                if pending: gaps.append("MissingOutput:" + pending["record"])
                pending = None
                try:
                    steps.append({"record": capture + ":" + str(seq), "operation": record["operation"],
                                  "arguments": [payload(db, record)], "expected": payload(db, record, "after"),
                                  "boundary": payload(db, record, "before"), "external": [], "expectedTicks": []})
                except (ValueError, KeyError) as error: gaps.append("IncompleteSemanticInput:" + str(error))
            elif row["stage"] == "replay.input":
                if pending: gaps.append("MissingOutput:" + pending["record"])
                try: arguments = payload(db, record)
                except ValueError as error: gaps.append(str(error)); arguments = []
                try: boundary = payload(db, record, "before")
                except ValueError as error: gaps.append(str(error)); boundary = None
                pending = {"record": capture + ":" + str(seq), "operation": record["operation"], "arguments": arguments, "boundary": boundary, "external": [], "expectedTicks": []}
            elif row["stage"] == "replay.external" and pending is not None:
                try: pending["external"].append(payload(db, record))
                except ValueError as error: gaps.append(str(error))
            elif row["stage"] == "status.tick" and record.get("outcome") == "Executed" and pending is not None:
                try:
                    tick = payload(db, record)
                    pending["expectedTicks"].append(tick.get("tick", tick) if isinstance(tick, dict) else tick)
                except ValueError as error: gaps.append(str(error))
            elif row["stage"] == "replay.output":
                if pending is None: gaps.append("MissingInput:" + str(seq)); continue
                if pending["operation"] != record["operation"] or record.get("outcome") != "Completed": gaps.append("AbortedOrUnpairedOperation:" + str(seq))
                try: pending["expected"] = payload(db, record, "after")
                except ValueError as error: gaps.append(str(error)); pending["expected"] = None
                steps.append(pending); pending = None
    if baseline is None: gaps.append("MissingCheckpoint")
    if pending: gaps.append("MissingOutput:" + pending["record"])
    # Any explicit capture failure touching this replay interval invalidates completeness.
    for row in db.execute("SELECT body FROM metadata WHERE kind='coverage.json'"):
        state = json.loads(row[0])
        if state.get("captureId") != capture: continue
        if (state.get("runId"), state.get("round", 0)) not in contexts: continue
        if state.get("failure") and not state.get("gaps"): gaps.append("SourceFailureUnknownRange:" + state["failure"])
        for gap in state.get("gaps", []):
            if int(gap.get("last", 0)) >= (last_checkpoint or 0) and (last is None or int(gap.get("first", 0)) <= last):
                gaps.append("CaptureGap:" + compact(gap))
    for row in db.execute("SELECT path,reason,details FROM issues WHERE reason IN ('ConflictingCopy','InvalidRecord')"):
        if capture in row["path"] or capture in row["details"]: gaps.append(row["reason"] + ":" + row["path"])
    return {"version": 2, "source": capture, "engine": engine, "domain": domain,
            "complete": not gaps, "gaps": gaps, "checkpoint": baseline, "steps": steps}


def describe_record(record):
    stages = {"owner.hit": "本地命中", "owner.damage_calculation": "伤害计算", "collector.enqueue": "收集器入队",
              "collector.drain": "收集器组批", "network.submit": "提交战斗批次", "gateway.decision": "服务端判断",
              "ledger.apply": "账本结算", "gateway.canonical_link": "合并到权威状态输出", "network.canonical": "传输权威状态",
              "replica.entity": "客户端应用权威状态", "authority.movement": "移动权限校验", "death.report": "提交本地死亡",
              "death.receipt": "死亡确认", "entity.spawn": "实体出生", "entity.destroy": "实体销毁",
              "owner.attack_stats": "攻击属性构建", "owner.dot_damage": "持续伤害结算", "status.tick": "持续状态 Tick",
              "stats.damage": "输出统计入账", "statistics.damage": "输出统计入账", "network.send": "网络发送",
              "network.metrics": "网络观测", "network.disconnected": "连接断开"}
    outcomes = {"Accepted": "接受", "Rejected": "拒绝", "Ignored": "忽略", "Deferred": "延后", "Applied": "已应用",
                "Applying": "开始应用", "Sent": "已发送", "Received": "已接收", "Produced": "已输出", "Confirmed": "已确认"}
    reasons = {"DuplicateEvent": "事件重复，未再次结算", "WrongOwner": "发送者已无模拟权限", "WrongEpoch": "权限版本不匹配",
               "OlderStateVersion": "旧状态版本", "StaleVersion": "旧状态版本", "Coalesced": "多个变更合并为最终状态",
               "Direct": "直接输出该次状态", "WrongRound": "对局轮次不匹配", "QueueOverload": "记录队列已满"}
    stage = record.get("stage", "")
    result = stages.get(stage, stage) + "：" + outcomes.get(record.get("outcome"), record.get("outcome") or "已记录")
    if record.get("source"): result += f"；发起者 {record['source']}"
    if record.get("target"): result += f"；目标 {record['target']}"
    if record.get("eventId"): result += f"；事件 {record['eventId']}"
    reason = record.get("reason")
    if reason and reason != "None": result += "；" + reasons.get(reason, reason)
    before, after = record.get("before"), record.get("after")
    if isinstance(before, dict) and isinstance(after, dict):
        for key, label in (("Health", "生命"), ("StateVersion", "状态版本")):
            if key in before and key in after: result += f"；{label} {before[key]} → {after[key]}"
    return result


def viewer(result):
    rows = []
    for r in result["records"]:
        who = r.get("captureId", "")[:8] + " / " + r.get("role", "")
        text = r.get("description") or describe_record(r)
        details = html.escape(json.dumps(r, ensure_ascii=False, indent=2))
        rows.append(f"<tr><td>{html.escape(who)}</td><td>{html.escape(str(r.get('recordSequence', '')))}</td><td>{html.escape(str(r.get('eventId', '')))}</td><td>{html.escape(str(r.get('target', '')))}</td><td>{html.escape(text)}<details><summary>输入、前后状态及原始文件</summary><pre>{details}</pre></details></td></tr>")
    evidence = html.escape(json.dumps({k:v for k,v in result.items() if k != 'records'}, ensure_ascii=False, indent=2))
    return """<!doctype html><meta charset="utf-8"><title>战斗证据时间线</title>
<style>body{font:15px system-ui;margin:32px;background:#111827;color:#e5e7eb}input{padding:12px;width:60%}table{border-collapse:collapse;width:100%;margin-top:20px}td,th{padding:10px;text-align:left;border-bottom:1px solid #374151}pre{white-space:pre-wrap;overflow-wrap:anywhere}summary{cursor:pointer;color:#93c5fd}</style>
<h1>战斗证据时间线</h1><p>按原始来源与执行序号排列。跨端联系以事件和收发编号为准；缺少记录不能证明没有执行。</p>
<input id="filter" placeholder="筛选实体、事件、原因、来源或状态"><details><summary>覆盖范围与证据缺口</summary><pre>""" + evidence + "</pre></details><table><thead><tr><th>来源 / 角色</th><th>序号</th><th>事件</th><th>实体</th><th>处理结果</th></tr></thead><tbody>" + "".join(rows) + "</tbody></table><script>document.querySelector('#filter').oninput=e=>{const q=e.target.value.toLowerCase();document.querySelectorAll('tbody tr').forEach(r=>r.hidden=!r.textContent.toLowerCase().includes(q));};</script>"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", default="combat-evidence.sqlite")
    subs = parser.add_subparsers(dest="command", required=True)
    imp = subs.add_parser("import"); imp.add_argument("roots", nargs="+")
    subs.add_parser("coverage")
    perf = subs.add_parser("performance")
    perf.add_argument("--capture", required=True); perf.add_argument("--output", required=True)
    for name in ("query", "view", "compare", "locate", "player-output", "dot", "connection"):
        cmd = subs.add_parser(name)
        for field in ("event", "entity", "reason", "capture", "run", "round", "engine", "after", "before", "build"):
            cmd.add_argument("--" + field)
        cmd.add_argument("--limit", type=int, default=2000)
        cmd.add_argument("--resolve-inputs", action="store_true")
        cmd.add_argument("--output")
        cmd.add_argument("--html", action="store_true", help="Render the same evidence as an offline HTML timeline")
        if name in ("player-output", "dot", "connection"):
            cmd.add_argument("--replay-report", help="Attach a Unity business-replay result and resolve its first differing record")
        if name == "locate": cmd.add_argument("--monster-type", required=True)
        if name == "player-output": cmd.add_argument("--player", required=True, help="Source player's network entity ID")
        if name == "dot": cmd.add_argument("--status", required=True, help="Exact StatusInstanceId; keep 64-bit IDs as strings")
        if name == "connection":
            selector = cmd.add_mutually_exclusive_group(required=True)
            selector.add_argument("--connection", help="Mirror connection ID local to --capture")
            selector.add_argument("--steam-connection", help="Steam socket handle, distinct from a Mirror connection ID")
    cmd = subs.add_parser("extract")
    cmd.add_argument("--capture", required=True); cmd.add_argument("--engine", required=True)
    cmd.add_argument("--first", type=int); cmd.add_argument("--last", type=int); cmd.add_argument("--output", required=True)
    args = parser.parse_args()
    with connect(args.db) as db:
        if args.command == "performance":
            with Path(args.output).open("w", encoding="utf-8") as stream:
                for row in db.execute("SELECT body FROM records WHERE capture=? AND stage IN ('performance.header','performance.snapshot') ORDER BY length(seq),seq", (args.capture,)):
                    value = payload(db, json.loads(row[0]))
                    stream.write(value if isinstance(value, str) else compact(value)); stream.write("\n")
            return
        if args.command == "import": result = import_roots(db, args.roots)
        elif args.command == "coverage": result = coverage(db)
        elif args.command == "extract": result = extract(db, args.capture, args.engine, args.first, args.last)
        elif args.command == "compare": result = compare(db, args)
        elif args.command == "locate": result = locate(db, args)
        elif args.command in ("player-output", "dot", "connection"): result = history(db, args)
        else: result = query(db, args)
        content = viewer(result) if args.command == "view" or getattr(args, "html", False) else json.dumps(result, ensure_ascii=False, indent=2)
        if getattr(args, "output", None): Path(args.output).write_text(content, encoding="utf-8")
        else: print(content)


if __name__ == "__main__":
    main()
