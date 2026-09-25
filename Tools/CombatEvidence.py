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
import sys
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
VIEW_CATEGORIES = {
    "movement": ("movement.submit", "movement.receive", "movement.correction", "authority.movement", "authority.handoff"),
    "attack": ("owner.attack_started", "owner.AttackStarted", "owner.attack_window", "owner.attack_gate",
               "owner.attack_stats", "owner.contact", "owner.hit", "owner.HitResolved",
               "replica.attack_window", "replica.contact"),
    "damage": ("owner.damage_calculation", "owner.damage", "owner.DamageResolved", "owner.dot_damage",
               "owner.PredictedLethalHit", "ledger.apply", "entity.canonical_health", "death.report",
               "death.receipt", "status.tick"),
    "sync": ("collector.enqueue", "collector.drain", "network.submit", "gateway.batch",
             "gateway.decision", "gateway.canonical_link", "network.canonical", "replica.entity",
             "entity.canonical_health", "network.send", "network.disconnected"),
}


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
        root = root.resolve()
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
                        if len(raw) > MAX_BLOB: raise ValueError("Oversized cleanup audit")
                        db.execute("INSERT OR REPLACE INTO metadata VALUES(?,?,?)", (str(path) + ":" + str(number), "retention.jsonl", compact(json.loads(raw))))
                        if not raw.endswith(b"\n"): raise ValueError("Parsed cleanup audit missing newline")
                    except ValueError as error:
                        db.execute("INSERT INTO issues VALUES(?,?,'InvalidMetadata',?)", (str(path), number, str(error)))
                        if not raw.endswith(b"\n"): break
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


def sequence_number(value):
    """Do not round, truncate, or accept negative/corrupt sequence watermarks."""
    if isinstance(value, bool) or not re.fullmatch(r"[0-9]+", str(value)):
        raise ValueError("InvalidSequence:" + str(value))
    return int(value)


def selected_coverage(db, capture, run=None, round=None):
    candidates = []
    for row in db.execute("SELECT path,body FROM metadata WHERE kind='coverage.json'"):
        state = json.loads(row["body"])
        if not isinstance(state, dict) or state.get("captureId") != capture: continue
        if run is not None and state.get("runId") != run: continue
        if round is not None and str(state.get("round", 0)) != str(round): continue
        try:
            if "coverageRevision" in state:
                rank = (1, sequence_number(state["coverageRevision"]))
            else:
                rank = (0, sequence_number(state.get("produced")), sequence_number(state.get("flushed")), sequence_number(state.get("written")))
        except ValueError:
            rank = (-1,)
        candidates.append(dict(path=row["path"], body=state, rank=rank))
    if not candidates: return None, []
    rank = max(c["rank"] for c in candidates)
    newest = [c for c in candidates if c["rank"] == rank]
    chosen = min(newest, key=lambda c: c["path"])
    conflicts = [c["path"] for c in newest if c["body"] != chosen["body"]]
    return {"path": chosen["path"], "body": chosen["body"]}, conflicts


def issue_interval(db, issue, capture):
    """Locate damage by validated neighboring records, never by wall-clock order."""
    path = issue["path"]
    refs = list(db.execute("SELECT seq,line FROM copies WHERE capture=? AND path=? ORDER BY line,length(seq),seq", (capture, path)))
    path_capture = re.search(r"(?:^|/)sources/([^/]+)(?:/|$)", path.replace("\\", "/"))
    if not refs and (path_capture is None or path_capture[1] != capture): return None
    if issue["reason"] == "ConflictingCopy":
        prefix = capture + ":"
        if issue["details"].startswith(prefix):
            try:
                seq = sequence_number(issue["details"][len(prefix):])
                return seq, seq
            except ValueError: pass
    previous = [int(r["seq"]) for r in refs if r["line"] < issue["line"]]
    following = [int(r["seq"]) for r in refs if r["line"] > issue["line"]]
    first = max(previous) + 1 if previous else None
    last = min(following) - 1 if following else None
    if first is not None and last is not None and last < first: last = first
    return first, last


def checkpoint_engines(record, data):
    if not isinstance(data, dict): raise ValueError("InvalidCheckpointPayload")
    engines = data.get("engines") if record.get("stage") == "replay.checkpoint" else [data]
    # A full snapshot may have no root engines during startup or between contexts.
    # It still replaces the prior root set; it cannot establish an engine baseline.
    if not isinstance(engines, list): raise ValueError("InvalidCheckpointEngines")
    if any(not isinstance(item, dict) or not item.get("engine") or not item.get("domain") or item.get("state") is None for item in engines):
        raise ValueError("InvalidCheckpointEngineState")
    return engines


def assess_replay_calls(db, where, values, first, last, add):
    """Check replay dependencies and call pairing even when sequence numbers are continuous."""
    checkpoints, pending, tainted = {}, {}, {}
    semantic = ("owner.damage_calculation", "owner.attack_stats", "stats.damage")
    stages = ("replay.checkpoint", "replay.engine_checkpoint", "replay.input", "replay.output") + semantic
    marks = ",".join("?" for _ in stages)
    rows = db.execute("SELECT seq,body FROM records WHERE " + where + " AND stage IN (" + marks + ") ORDER BY length(seq),seq", values + list(stages))

    def report(reason, record, start=None):
        seq = int(record["recordSequence"])
        if seq < first and (start is None or start < first): return
        references = [dict(r) for r in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=?", (record["captureId"], str(seq)))]
        add(reason, start if start is not None else seq, seq, references)

    for row in rows:
        seq = int(row["seq"])
        if seq > last: break
        record = json.loads(row["body"])
        stage, engine = record.get("stage"), record.get("engine")
        if stage in ("replay.checkpoint", "replay.engine_checkpoint"):
            try: states = checkpoint_engines(record, payload(db, record))
            except (ValueError, TypeError, AttributeError) as error:
                if stage == "replay.checkpoint": checkpoints.clear()
                else: checkpoints.pop(engine, None)
                report(str(error), record); continue
            if stage == "replay.checkpoint": checkpoints.clear()
            for state in states:
                key = state["engine"]
                if key in pending:
                    prior = pending.pop(key)
                    prior_seq = int(prior["record"]["recordSequence"])
                    if prior_seq >= first or seq > first:
                        report("MissingOutput:" + str(prior_seq), record, prior_seq)
                checkpoints[key] = seq
                tainted.pop(key, None)
            continue
        if stage == "replay.input" or stage in semantic:
            errors = []
            if not engine or not record.get("operation"): errors.append("MissingReplayIdentity")
            if engine not in checkpoints: errors.append("MissingCheckpoint:" + str(engine))
            if engine in tainted: errors.append("PriorIncompleteReplay:" + str(tainted[engine]))
            try:
                arguments = payload(db, record)
                if (stage == "replay.input" and not isinstance(arguments, list)) or (stage in semantic and arguments is None):
                    errors.append("MissingReplayInput" if stage == "replay.input" else "IncompleteSemanticInput")
            except (ValueError, TypeError, AttributeError) as error: errors.append(str(error))
            for error in errors: report(error, record)
            if errors: tainted[engine] = seq
            if stage == "replay.input":
                if engine in pending:
                    report("MissingOutput:" + pending[engine]["record"]["recordSequence"], record, int(pending[engine]["record"]["recordSequence"]))
                    tainted[engine] = seq
                pending[engine] = dict(record=record, errors=errors)
        elif stage == "replay.output":
            prior = pending.pop(engine, None)
            if prior is None:
                report("MissingInput:" + str(seq), record); tainted[engine] = seq
            else:
                for error in prior["errors"]: report(error, record)
                if record.get("operation") != prior["record"].get("operation") or record.get("outcome") != "Completed":
                    report("AbortedOrUnpairedOperation:" + str(seq), record); tainted[engine] = seq
    for prior in pending.values():
        record = prior["record"]
        start = int(record["recordSequence"])
        add("MissingOutput:" + record["recordSequence"], start, last,
            [dict(r) for r in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=?", (record["captureId"], str(start)))])


def integrity_history(state):
    """Validate the writer's complete, bounded history before it supersedes legacy hulls."""
    if state.get("integrityHistoryVersion") != 1 or isinstance(state.get("integrityHistoryVersion"), bool):
        raise ValueError("UnsupportedVersion")
    entries = state.get("integrityHistory")
    if not isinstance(entries, list) or len(entries) > 128: raise ValueError("InvalidEpisodeList")
    epoch = sequence_number(state.get("failureEpoch"))
    flushed, produced = sequence_number(state.get("flushed")), sequence_number(state.get("produced"))
    result, previous_epoch, previous_recovery = [], 0, None
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict): raise ValueError("InvalidEpisode")
        start = sequence_number(entry["first"]) if entry.get("first") is not None else None
        end = sequence_number(entry["last"]) if entry.get("last") is not None else None
        recovered = sequence_number(entry["reliableFromSequence"]) if entry.get("reliableFromSequence") is not None else None
        current_epoch = sequence_number(entry.get("epoch"))
        tail_marker = entry.get("tailMarker", False)
        if not isinstance(tail_marker, bool): raise ValueError("InvalidTailMarker")
        if tail_marker and end is None: raise ValueError("UnboundedTailMarker")
        maximum = produced + 1 if tail_marker and flushed == produced else produced
        if current_epoch <= previous_epoch: raise ValueError("UnorderedEpochs")
        if start is not None and (start < 1 or start > maximum): raise ValueError("InvalidEpisodeStart")
        if end is not None and (end < 1 or end > maximum or (start is not None and end < start)):
            raise ValueError("InvalidEpisodeEnd")
        if index and (previous_recovery is None or (start is not None and start < previous_recovery)):
            raise ValueError("OverlappingOrUnrecoveredEpisodes")
        if recovered is not None and (recovered < 1 or recovered > flushed or
                                      (start is not None and (recovered < start or (recovered == start and not tail_marker))) or
                                      (end is not None and (recovered < end or (recovered == end and not tail_marker)))):
            raise ValueError("UnconfirmedRecoveryCheckpoint")
        if entry.get("conservative", False) not in (True, False): raise ValueError("InvalidConservativeFlag")
        result.append(dict(first=start, last=end, epoch=current_epoch, recovered=recovered,
                           conservative=entry.get("conservative", False), tailMarker=tail_marker))
        previous_epoch, previous_recovery = current_epoch, recovered
    if previous_epoch != epoch: raise ValueError("MissingLatestFailureEpoch")
    if state.get("recoveryPending", False) != bool(result and result[-1]["recovered"] is None):
        raise ValueError("PendingStateMismatch")
    known_recoveries = [entry["recovered"] for entry in result if entry["recovered"] is not None]
    latest_recovery = sequence_number(state["reliableFromSequence"]) if state.get("reliableFromSequence") is not None else None
    if latest_recovery != (known_recoveries[-1] if known_recoveries else None): raise ValueError("LatestRecoveryMismatch")
    if not result and (state.get("failure") or state.get("gaps")): raise ValueError("UnrepresentedFailure")

    def contains(entry, start, end):
        last_record = entry["last"] - 1 if entry["tailMarker"] else entry["last"]
        return (entry["first"] is None or entry["first"] <= start) and (last_record is None or last_record >= end)

    for gap in state.get("gaps", []):
        if not isinstance(gap, dict): raise ValueError("InvalidGap")
        start, end = sequence_number(gap.get("first")), sequence_number(gap.get("last"))
        if end < start: raise ValueError("ReversedGap")
        if gap.get("conservative"):
            # Legacy bounded gap storage may merge across reliable windows; the versioned
            # epoch history preserves their recovery proofs. Check its endpoints only.
            represented = all(any(contains(entry, point, point) for entry in result) for point in (start, end))
        else:
            represented = any(contains(entry, start, end) for entry in result)
        if not represented: raise ValueError("UnrepresentedCaptureGap")
    return result


def retention_scope_gaps(db, capture=None, first=None, last=None, run=None):
    """Root cleanup audits describe lost source inventory, not record sequence gaps."""
    if capture is not None and first is not None and last is not None: return []
    issues = [dict(row) for row in db.execute("SELECT path,line,details FROM issues WHERE reason='InvalidMetadata'")
              if Path(row["path"]).name == "retention.jsonl"]
    invalid_locations = {(row["path"], row["line"]) for row in issues}
    unknown_locations = {(row["path"], row["line"]) for row in issues if row["details"] != "Parsed cleanup audit missing newline"}
    seen = set(); groups = {}

    def add(reason, run_id, path, line):
        if run is not None and run_id is not None and run_id != run: return
        key = (reason, run_id)
        if key not in groups:
            groups[key] = dict(reason=reason, references=[])
            if run_id is not None: groups[key]["runId"] = run_id
        reference = dict(path=path, line=line)
        if reference not in groups[key]["references"]: groups[key]["references"].append(reference)

    for row in db.execute("SELECT path,body FROM metadata WHERE kind='retention.jsonl' ORDER BY path"):
        path, separator, number = row["path"].rpartition(":")
        if not separator or not number.isdecimal(): path, number = row["path"], "0"
        location = (path, int(number)); seen.add(location)
        state = json.loads(row["body"])
        run_id = state.get("runId") if isinstance(state, dict) else None
        if not isinstance(run_id, str) or not run_id.strip(): run_id = None
        # A failed re-import may leave an older parsed row at this location. Only
        # the fully parsed, newline-missing case can still identify the damaged run.
        if location in unknown_locations: run_id = None
        valid = run_id is not None and state.get("reason") == "GlobalCapacityRetention" and location not in invalid_locations
        add("RunPruned" if valid else "InvalidRetentionAudit", run_id, *location)
    for row in issues:
        if (row["path"], row["line"]) not in seen:
            add("InvalidRetentionAudit", None, row["path"], row["line"])
    result = sorted(groups.values(), key=lambda gap: (gap.get("runId", ""), gap["reason"]))
    for gap in result: gap["references"].sort(key=lambda ref: (ref["path"], ref["line"]))
    return result


def assess_interval(db, capture, first=None, last=None, run=None, round=None):
    """One conservative completeness decision for extraction, histories and coverage.

    Explicit bounds describe a finite source sequence interval. An omitted end asks
    for the known source tail and cannot prove completeness while that tail is open.
    It does not claim that uninstrumented gameplay or Unity physics is replayable.
    """
    open_ended = last is None
    implicit_first = first is None
    scope_gaps = retention_scope_gaps(db, capture, first, last, run)
    source, conflicts = selected_coverage(db, capture, run, round)
    state = source["body"] if source else {}
    clauses, values = ["capture=?"], [capture]
    if run is not None: clauses.append("run=?"); values.append(run)
    if round is not None: clauses.append("round=?"); values.append(round)
    where = " AND ".join(clauses)
    earliest = db.execute("SELECT seq FROM records WHERE " + where + " ORDER BY length(seq),seq LIMIT 1", values).fetchone()
    latest = db.execute("SELECT seq FROM records WHERE " + where + " ORDER BY length(seq) DESC,seq DESC LIMIT 1", values).fetchone()
    gaps = []

    def add(reason, start=None, end=None, references=None):
        gap = dict(reason=reason, captureId=capture, runId=run, round=round)
        if start is not None: gap["first"] = str(start)
        if end is not None: gap["last"] = str(end)
        if references: gap["references"] = references
        gaps.append(gap)

    def intersects(start, end):
        return (end is None or end >= first) and (start is None or start <= last)

    def reject_empty_recovery(recovered, end=None):
        if recovered is None or not intersects(recovered, end): return
        row = db.execute("SELECT body FROM records WHERE " + where + " AND seq=?", values + [str(recovered)]).fetchone()
        if row is None: return  # Retain the existing rules for historical/pruned recovery records.
        record = json.loads(row["body"])
        if record.get("stage") != "replay.checkpoint": return
        try: engines = checkpoint_engines(record, payload(db, record))
        except (ValueError, TypeError, AttributeError): return
        if not engines:
            references = [dict(r) for r in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=?", (capture, str(recovered)))]
            add("InvalidRecoveryCheckpoint:EmptyEngineSet", recovered, end, references)

    try:
        first = sequence_number(first) if first is not None else (int(earliest[0]) if earliest else 1)
        observed_last = int(latest[0]) if latest else 0
        last = sequence_number(last) if last is not None else max(observed_last, sequence_number(state.get("produced", observed_last)))
    except ValueError as error:
        add(str(error)); first, last = 1, 0
    if first < 1 or last < first: add("EmptyOrInvalidInterval", first, last)
    for row in db.execute("SELECT path,body FROM metadata WHERE kind IN ('retention.json','retention.local.json')"):
        directory = Path(row["path"]).parent
        if directory.name != capture or directory.parent.name != "sources": continue
        if run is not None and directory.parent.parent.parent.name != str(run): continue
        if round is not None and directory.parent.parent.name != str(round): continue
        references = [{"path": row["path"]}]
        try:
            retained = json.loads(row["body"])
            if not isinstance(retained, dict): raise ValueError("InvalidRetentionObject")
            boundary = sequence_number(retained.get("beforeSequence"))
            if boundary < 1: raise ValueError("InvalidRetentionBoundary")
            filename = retained.get("firstRetainedFile")
            if filename is not None and (not isinstance(filename, str) or
                    not re.fullmatch(r"events-[0-9]+\.jsonl", filename) or int(filename[7:-6]) != boundary):
                raise ValueError("ConflictingRetentionBoundary")
        except ValueError as error:
            add("InvalidRetention:" + str(error), references=references)
            continue
        # Sequence numbers span rounds. A surviving checkpoint does not establish
        # the original source start; callers must explicitly select a retained range.
        # Explicit ranges still use merged copies below, which may restore pruned data.
        if implicit_first and boundary > 1:
            add("SourcePrefixPruned", end=boundary - 1, references=references)
    reference = [{"path": source["path"]}] if source else []
    if source is None: add("MissingCoverage")
    else:
        if conflicts: add("ConflictingCoverageRevision", references=reference + [{"path": p} for p in conflicts])
        if state.get("schemaVersion", 1) not in (1, 2): add("UnsupportedCoverageVersion", references=reference)
        try:
            flushed = sequence_number(state.get("flushed"))
            produced = sequence_number(state.get("produced"))
            written = sequence_number(state.get("written"))
            if not flushed <= written <= produced: add("InvalidCoverageWatermarks", references=reference)
            if last > flushed: add("BeyondFlushedWatermark:" + str(flushed), max(first, flushed + 1), last, reference)
            if last > produced: add("BeyondProducedWatermark:" + str(produced), max(first, produced + 1), last, reference)
        except ValueError as error: add("InvalidCoverage:" + str(error), references=reference)
        if open_ended and state.get("tailUnknown", True): add("SourceTailUnknown", references=reference)
        if open_ended and state.get("recoveryPending"): add("RecoveryCheckpointPending", references=reference)
        declared_gaps = state.get("gaps", [])
        recovery_start = None
        if not isinstance(declared_gaps, list):
            add("InvalidCoverageGapList", references=reference); declared_gaps = []
        episodes = None
        if state.get("integrityHistoryVersion") not in (None, 0):
            try: episodes = integrity_history(state)
            except (ValueError, TypeError) as error: add("InvalidIntegrityHistory:" + str(error), references=reference)
        for gap in declared_gaps:
            try:
                start, end = sequence_number(gap.get("first")), sequence_number(gap.get("last"))
                if end < start: raise ValueError("ReversedGapRange")
                recovery_start = min(start, recovery_start) if recovery_start is not None else start
                if episodes is None and intersects(start, end): add("CaptureGap:" + str(gap.get("reason", "Unknown")), start, end, reference)
            except (ValueError, AttributeError) as error: add("InvalidCoverageGap:" + str(error), references=reference)
        if episodes is not None:
            for index, episode in enumerate(episodes):
                end = episode["recovered"] - 1 if episode["recovered"] is not None else None
                if intersects(episode["first"], end):
                    reason = "ConservativeIntegrityHistory" if episode["conservative"] else "IntegrityHistoryFailure"
                    add(reason + ":epoch=" + str(episode["epoch"]), episode["first"], end, reference)
                next_recovery = next((later["recovered"] for later in episodes[index + 1:] if later["recovered"] is not None), None)
                reject_empty_recovery(episode["recovered"], next_recovery - 1 if next_recovery is not None else None)
        elif state.get("failure"):
            try:
                start = sequence_number(state["failureFirstSequence"])
                end = sequence_number(state["failureLastSequence"]) if state.get("failureLastSequence") is not None else None
                if end is not None and end < start: raise ValueError("ReversedFailureRange")
                recovery_start = min(start, recovery_start) if recovery_start is not None else start
                if intersects(start, end): add("SourceFailure:" + str(state["failure"]), start, end, reference)
            except (KeyError, ValueError):
                # Unrelated old gaps cannot explain a later failure of unknown extent.
                add("SourceFailureUnknownRange:" + str(state["failure"]), references=reference)
        if episodes is None and state.get("recoveryPending") and (recovery_start is None or last >= recovery_start):
            add("RecoveryCheckpointPending" if recovery_start is not None else "RecoveryCheckpointPendingUnknownRange", recovery_start, None, reference)
        if episodes is None and state.get("reliableFromSequence") is not None:
            try:
                recovered = sequence_number(state["reliableFromSequence"])
                if recovery_start is not None and recovered > recovery_start and intersects(recovery_start, recovered - 1):
                    add("BeforeRecoveryCheckpoint", recovery_start, recovered - 1, reference)
                reject_empty_recovery(recovered)
            except ValueError: add("InvalidRecoveryCheckpointSequence", references=reference)

    expected = first
    for row in db.execute("SELECT seq,body FROM records WHERE " + where + " ORDER BY length(seq),seq", values):
        seq = int(row["seq"])
        if seq < first: continue
        if seq > last: break
        if seq > expected: add(f"MissingRecordRange:{expected}-{seq - 1}", expected, seq - 1)
        expected = seq + 1
        record = json.loads(row["body"])
        def references():
            return [dict(r) for r in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=?", (capture, str(seq)))]
        if record.get("stage") == "evidence.gap" or record.get("outcome") == "CaptureFailed":
            add(("CaptureFailed:" if record.get("outcome") == "CaptureFailed" else "RecordedGap:") + str(record.get("reason", seq)), seq, seq, references())
        for field in ("input", "before", "after"):
            try:
                data = payload(db, record, field)
                if field == "input" and record.get("stage") in ("replay.engine_checkpoint", "replay.checkpoint"):
                    checkpoint_engines(record, data)
            except (ValueError, TypeError, AttributeError) as error: add(str(error), seq, seq, references())
    if expected <= last: add(f"MissingRecordRange:{expected}-{last}", expected, last)
    assess_replay_calls(db, where, values, first, last, add)
    for row in db.execute("SELECT path,line,reason,details FROM issues WHERE reason IN ('ConflictingCopy','InvalidRecord','TruncatedOrOversizedLine','InvalidMetadata')"):
        extent = issue_interval(db, row, capture)
        if extent is None: continue
        start, end = extent
        if intersects(start, end) or (open_ended and end is None):
            add(row["reason"] + ":" + row["details"], start, end, [{"path": row["path"], "line": row["line"]}])
    for row in db.execute("SELECT path,body FROM metadata WHERE kind='recovery.json'"):
        recovered = json.loads(row["body"])
        if not isinstance(recovered, dict) or not recovered.get("tailUnknown"): continue
        filename = recovered.get("file", "")
        if not isinstance(filename, str) or not re.fullmatch(r"events-[0-9]+\.jsonl", filename): continue
        event_path = str(Path(row["path"]).parent / filename)
        extent = issue_interval(db, dict(path=event_path, line=2**63 - 1, reason="RecoveredTruncatedTail", details=""), capture)
        if extent is None: continue
        start, end = extent
        if intersects(start, end) or open_ended:
            add("SourceRecoveryTailUnknown", start, end, [{"path": row["path"]}])
    return dict(captureId=capture, runId=run, round=round, first=str(first), last=str(last),
                scope="SourceTail" if open_ended else "FiniteInterval", complete=not gaps and not scope_gaps,
                reliable=not gaps and not scope_gaps, gaps=gaps, scopeGaps=scope_gaps, selectedCoverage=source)


def coverage(db, intervals=None, request=None):
    sources = [dict(row) | {"body": json.loads(row["body"])} for row in db.execute("SELECT * FROM metadata WHERE kind IN ('coverage.json','recovery.json','retention.json','retention.local.json','retention.jsonl','replication.json','manifest.json')")]
    capture, run, round, first, last = (getattr(request, key, None) for key in ("capture", "run", "round", "first", "last"))
    if request is None and intervals is not None:
        scope_gaps = []
        for interval in intervals:
            for gap in interval.get("scopeGaps", []):
                if gap not in scope_gaps: scope_gaps.append(gap)
    else:
        scope_gaps = retention_scope_gaps(db, capture, first, last, run)
    invalid_selection = (getattr(request, "command", None) == "coverage" and capture is None and
                         (first is not None or last is not None))
    if invalid_selection:
        scope_gaps.append(dict(reason="InvalidCoverageSelection", details="Sequence bounds require --capture", references=[]))
        intervals = []
    elif intervals is None:
        contexts = {(row["capture"], row["run"], row["round"]) for row in db.execute("SELECT DISTINCT capture,run,round FROM records")}
        contexts.update((r["body"].get("captureId"), r["body"].get("runId"), r["body"].get("round", 0))
                        for r in sources if r["kind"] == "coverage.json" and isinstance(r["body"], dict) and r["body"].get("captureId"))
        known_captures = {context[0] for context in contexts}
        for row in db.execute("SELECT path FROM issues WHERE reason IN ('InvalidMetadata','InvalidRecord','TruncatedOrOversizedLine')"):
            match = re.search(r"(?:^|/)sources/([^/]+)(?:/|$)", row["path"].replace("\\", "/"))
            if match and match[1] not in known_captures:
                directory = Path(row["path"]).parent
                contexts.add((match[1], directory.parent.parent.parent.name, directory.parent.parent.name))
                known_captures.add(match[1])
        contexts = [context for context in contexts if
                    (capture is None or context[0] == capture) and (run is None or context[1] == run) and
                    (round is None or str(context[2]) == str(round))]
        intervals = [assess_interval(db, c, first, last, r, n) for c, r, n in sorted(contexts, key=str)]
    return {"interpretation": "Missing records never prove non-execution. Copies retain original identity; UTC is not causal order.",
            "complete": bool(intervals) and all(a["complete"] for a in intervals) and not scope_gaps,
            "intervals": intervals, "sources": sources, "scopeGaps": scope_gaps, "invalidSelection": invalid_selection,
            "issues": [dict(row) for row in db.execute("SELECT * FROM issues LIMIT 1000")],
            "issueCount": db.execute("SELECT count(*) FROM issues").fetchone()[0]}


def query_intervals(db, args, records):
    contexts = {}
    for record in records:
        key = (record["captureId"], record.get("runId"), record.get("round", 0))
        contexts[key] = min(contexts.get(key, int(record["recordSequence"])), int(record["recordSequence"]))
    if not contexts and getattr(args, "capture", None):
        for row in db.execute("SELECT DISTINCT run,round FROM records WHERE capture=?", (args.capture,)):
            if getattr(args, "run", None) is not None and args.run != row["run"]: continue
            if getattr(args, "round", None) is not None and str(args.round) != str(row["round"]): continue
            contexts[(args.capture, row["run"], row["round"])] = None
        if not contexts: contexts[(args.capture, getattr(args, "run", None), getattr(args, "round", None))] = None
    return [assess_interval(db, capture, first=getattr(args, "first", None) if getattr(args, "first", None) is not None else earliest,
                            last=getattr(args, "last", None), run=run, round=round)
            for (capture, run, round), earliest in sorted(contexts.items(), key=str)]


def query(db, args):
    clauses, values = [], []
    for attr, column in (("entity", "entity"), ("reason", "reason"), ("capture", "capture"), ("run", "run"), ("round", "round"), ("engine", "engine")):
        value = getattr(args, attr, None)
        if value is not None:
            clauses.append(column + "=?")
            values.append(value)
    category = getattr(args, "category", None)
    if category:
        stages = VIEW_CATEGORIES[category]
        clauses.append("stage IN (" + ",".join("?" for _ in stages) + ")")
        values.extend(stages)
    for attr, kind in (("player", "player"), ("status", "status"), ("connection", "connection"), ("steam_connection", "steam_connection")):
        value = getattr(args, attr, None)
        if value is not None:
            clauses.append("(capture,seq) IN (SELECT capture,seq FROM facets WHERE kind=? AND value=?)")
            values.extend((kind, str(value)))
    if getattr(args, "after", None):
        clauses.append("utc>=?"); values.append(args.after)
    if getattr(args, "before", None):
        clauses.append("utc<=?"); values.append(args.before)
    for bound, operator in (("first", ">="), ("last", "<=")):
        value = getattr(args, bound, None)
        if value is not None:
            value = str(value)
            # Sequence IDs can exceed SQLite's signed 64-bit INTEGER range.
            clauses.append(f"(length(seq),seq) {operator} (?,?)")
            values += [len(value), value]
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
    assessed = coverage(db, query_intervals(db, args, records[:limit]), request=args)
    return {"records": records[:limit], "truncated": len(records) > limit,
            "complete": assessed["complete"] and len(records) <= limit, "scopeGaps": assessed["scopeGaps"],
            "missingPayloads": [dict(evidence_pointer(r), **failure) for r in records[:limit] for failure in r.get("missingPayloads", [])],
            "missingStages": [s for s in expected if s not in stages] if event else [], "coverage": assessed,
            "selection": {key: getattr(args, key, None) for key in ("category", "capture", "run", "round", "entity", "event", "first", "last")}}


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
    result["evidenceGaps"].extend(gap for interval in result["coverage"]["intervals"] for gap in interval["gaps"])
    result["evidenceGaps"].extend(result["scopeGaps"])
    if result["truncated"]: result["evidenceGaps"].append({"reason": "QueryLimitReached"})
    result["flow"] = [dict(evidence_pointer(r), stage=r.get("stage"), source=r.get("source"), target=r.get("target"),
                           input=r.get("input"), before=r.get("before"),
                           decision={"outcome": r.get("outcome"), "reason": r.get("reason")}, after=r.get("after"))
                      for r in result["records"]]
    failures = [r for r in result["records"] if r.get("outcome") in ("Failed", "Rejected", "Aborted", "CaptureFailed")]
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
            "unmatched": unmatched, "truncated": result["truncated"], "coverage": result["coverage"], "scopeGaps": result["scopeGaps"]}


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
    return {"entities": list(found.values()), "truncated": result["truncated"], "coverage": result["coverage"], "scopeGaps": result["scopeGaps"]}


def extract(db, capture, engine, first=None, last=None):
    rows = db.execute("SELECT seq,stage,engine,body FROM records WHERE capture=? ORDER BY length(seq),seq", (capture,))
    baseline = None; steps = []; pending = None; gaps = []; domain = None; last_checkpoint = None; contexts = set(); last_seen = 0
    for row in rows:
        seq = int(row["seq"])
        if last is not None and seq > last: break
        last_seen = seq
        record = json.loads(row["body"])
        if row["stage"] in ("replay.checkpoint", "replay.engine_checkpoint"):
            try: checkpoints = checkpoint_engines(record, payload(db, record))
            except ValueError as error: gaps.append(str(error)); continue
            match = next((c for c in checkpoints if c["engine"] == engine), None)
            if match:
                if baseline is None or (first is not None and seq <= first):
                    baseline = match["state"]; domain = match["domain"]; steps = []; pending = None; gaps = []; last_checkpoint = seq
                    contexts = {(record.get("runId"), record.get("round", 0))}
                elif steps:
                    steps[-1]["expectedState"] = match["state"]
        elif baseline is not None:
            contexts.add((record.get("runId"), record.get("round", 0)))
            if row["engine"] != engine: continue
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
    if first is not None and (first < 1 or first > last_seen): gaps.append("RequestedFirstOutsideAvailableEvidence:" + str(first))
    if len(contexts) > 1: gaps.append("ReplayContextChanged")
    if not contexts: contexts.add((None, None))
    assessment_first = min(first, last_checkpoint) if first is not None and last_checkpoint is not None else last_checkpoint
    assessments = [assess_interval(db, capture, assessment_first, last, run, round) for run, round in sorted(contexts, key=str)]
    scope_gaps = retention_scope_gaps(db, capture, assessment_first, last)
    gaps.extend(g["reason"] for assessed in assessments for g in assessed["gaps"])
    gaps.extend(g["reason"] for g in scope_gaps)
    return {"version": 2, "source": capture, "engine": engine, "domain": domain,
            "complete": not gaps, "gaps": list(dict.fromkeys(gaps)), "checkpoint": baseline, "steps": steps,
            "integrity": assessments, "scopeGaps": scope_gaps}


def describe_record(record):
    stages = {"owner.hit": "本地命中", "owner.damage_calculation": "伤害计算", "collector.enqueue": "收集器入队",
              "collector.drain": "收集器组批", "network.submit": "提交战斗批次", "gateway.decision": "服务端判断",
              "ledger.apply": "账本结算", "gateway.canonical_link": "合并到权威状态输出", "network.canonical": "传输权威状态",
              "replica.entity": "客户端应用权威状态", "authority.movement": "移动权限校验", "death.report": "提交本地死亡",
              "death.receipt": "死亡确认", "entity.spawn": "实体出生", "entity.destroy": "实体销毁",
              "owner.attack_stats": "攻击属性构建", "owner.dot_damage": "持续伤害结算", "status.tick": "持续状态 Tick",
              "stats.damage": "输出统计入账", "statistics.damage": "输出统计入账", "network.send": "网络发送",
              "network.metrics": "网络观测", "network.disconnected": "连接断开",
              "movement.submit": "提交移动快照", "movement.receive": "接收移动快照",
              "movement.correction": "位置校正", "authority.handoff": "移动权限交接",
              "owner.attack_started": "开始攻击", "owner.AttackStarted": "攻击开始标记",
              "owner.attack_gate": "攻击许可", "owner.attack_window": "攻击窗口",
              "owner.contact": "攻击接触", "owner.HitResolved": "命中处理结果",
              "owner.DamageResolved": "本地伤害处理结果", "owner.PredictedLethalHit": "本地预测致命命中",
              "entity.canonical_health": "权威生命更新", "replica.attack_window": "远端攻击窗口",
              "replica.contact": "远端攻击接触"}
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
        for key, label in (("Health", "生命"), ("health", "本地生命"), ("localHealth", "本地生命"),
                           ("StateVersion", "状态版本")):
            if key in before and key in after: result += f"；{label} {before[key]} → {after[key]}"
    return result


def viewer(result):
    def brief(r):
        stage, before, after, data = r.get("stage"), r.get("before") or {}, r.get("after") or {}, r.get("input") or {}
        if not isinstance(before, dict): before = {}
        if not isinstance(after, dict): after = {}
        if not isinstance(data, dict): data = {}
        if stage == "owner.attack_started": return f"开始攻击；武器 {data.get('weaponId', '未知')}；{r.get('outcome', '已记录')}"
        if stage == "owner.damage_calculation": return f"计算伤害 {after.get('requestedDamage', '未知')}；尚非最终扣血"
        if stage == "owner.hit":
            if "health" in before and "health" in after:
                return f"本地命中 {r.get('outcome', '已记录')}；生命 {before['health']} → {after['health']}"
            return f"本地命中处理中；请求伤害 {data.get('Value', '未知')}"
        if stage == "movement.submit": return f"提交 {len(data.get('Snapshots', []))} 个移动快照；批次 {r.get('batchSequence', '未知')}；{r.get('outcome', '已记录')}"
        if stage == "movement.receive":
            position = after.get("position") or data.get("Position")
            if isinstance(position, dict): return f"移动快照 {r.get('outcome', '已记录')}；位置 ({position.get('x', '?')}, {position.get('y', '?')})；{r.get('reason') or '无附加原因'}"
        if stage == "movement.correction": return f"位置校正；距离 {after.get('distance', '未知')}"
        if stage == "network.canonical": return f"收到权威更新；服务端序号 {r.get('serverSequence', '未知')}；尚需看 Replica 是否应用"
        if stage == "replica.entity":
            return f"权威实体状态 {r.get('outcome', '已记录')}；生命 {before.get('Health', '?')} → {after.get('Health', '?')}；存活 {after.get('Alive', '?')}；版本 {after.get('StateVersion', '?')}"
        if stage == "entity.canonical_health":
            return f"更新本地权威生命；服务端 {data.get('canonicalHealth', '?')}；本地 {after.get('localHealth', '?')}"
        for key in ("Health", "health", "localHealth"):
            if key in before and key in after: return f"生命 {before[key]} → {after[key]}；{describe_record(r)}"
        return describe_record(r)

    selection = result.get("selection", {})
    category_names = {"movement": "移动", "attack": "攻击", "damage": "受伤与生命", "sync": "同步"}
    category = category_names.get(selection.get("category"), "全部")
    intervals = result.get("coverage", {}).get("intervals", [])
    source_cards = []
    for interval in intervals:
        state = (interval.get("selectedCoverage") or {}).get("body") or {}
        gaps = interval.get("gaps", [])
        status = "该范围完整" if interval.get("complete") else "该范围有缺口"
        reasons = "、".join(dict.fromkeys(str(g.get("reason", "未知")) for g in gaps[:3]))
        source_cards.append("<div class='card'><strong>" + html.escape(interval.get("captureId", "")[:8]) +
            " · " + status + "</strong><span>已写 " + html.escape(str(state.get("written", "未知"))) +
            " / 已产生 " + html.escape(str(state.get("produced", "未知"))) +
            "；丢弃 " + html.escape(str(state.get("dropped", "未知"))) +
            "；缺口 " + str(len(gaps)) + " 处</span>" +
            ("<small>例如：" + html.escape(reasons) + "</small>" if reasons else "") + "</div>")
    rows = []
    for r in result["records"]:
        details = {key: r.get(key) for key in ("captureId", "recordSequence", "runId", "round", "stage", "role",
                   "eventId", "source", "target", "outcome", "reason", "before", "after", "references") if key in r}
        for field in ("input", "before", "after"):
            if field in r:
                value = json.dumps(r[field], ensure_ascii=False)
                details[field] = r[field] if len(value) <= 1200 else "内容较长；完整内容请按事件 ID 使用 query 查询，或查看原始文件"
        moment = str(r.get("utc") or "")
        moment = moment[11:23] + " UTC" if len(moment) >= 23 else moment
        actor = f"{r.get('source') or '—'} → {r.get('target') or '—'}"
        reference = html.escape(json.dumps(details, ensure_ascii=False, indent=2))
        rows.append("<tr><td>" + html.escape(moment) + "<small>#" + html.escape(str(r.get("recordSequence", ""))) +
                    "</small></td><td>" + html.escape(str(r.get("stage", ""))) + "<small>" +
                    html.escape(str(r.get("role", ""))) + "</small></td><td>" + html.escape(actor) +
                    "</td><td>" + html.escape(brief(r)) + "<small>事件 " +
                    html.escape(str(r.get("eventId") or "—")) + "</small><details><summary>证据详情与原始文件</summary><pre>" +
                    reference + "</pre></details></td></tr>")
    limit_note = "已达到本次查询上限；页面搜索只查当前这些记录。请用 --first/--last 或 --entity/--event 缩小范围。" if result.get("truncated") else "页面搜索仅筛选本次查出的记录。"
    scope_note = "证据有缺口，缺少的记录不能解释为事件没有发生。" if not result.get("coverage", {}).get("complete") else "所选证据范围完整。"
    return """<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>战斗证据查看</title>
<style>body{font:15px/1.6 system-ui;margin:0 auto;padding:28px;max-width:1440px;background:#101827;color:#e5e7eb}h1{margin:0}p{margin:8px 0 20px}.muted,small{color:#aab7c9}small{display:block;font-size:12px}.cards{display:flex;gap:12px;flex-wrap:wrap}.card{background:#1e293b;border:1px solid #334155;border-radius:8px;padding:12px;min-width:240px}.card span{display:block}input{box-sizing:border-box;width:100%;padding:12px;margin:18px 0;background:#1e293b;color:#fff;border:1px solid #64748b;border-radius:6px}table{border-collapse:collapse;width:100%}td,th{padding:10px;text-align:left;vertical-align:top;border-bottom:1px solid #374151}th{position:sticky;top:0;background:#172338}td:nth-child(4){width:48%}pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:320px;overflow:auto;background:#0b1220;padding:12px}summary{cursor:pointer;color:#93c5fd}tr[hidden]{display:none}</style>
<h1>战斗证据 · """ + html.escape(category) + """</h1><p>先看“发生了什么”，展开单条记录再看证据。按每个来源的序号排列；两端 UTC 不能直接当作因果顺序。</p>
<div class="cards">""" + "".join(source_cards) + """</div><p><strong>""" + html.escape(scope_note) + "</strong> 本页 " + str(len(rows)) + " 条。" + html.escape(limit_note) + """</p>
<input id="filter" aria-label="筛选当前页面记录" placeholder="筛选当前页面：实体编号、事件 ID、阶段、原因"><table><thead><tr><th>时间 / 序号</th><th>阶段 / 角色</th><th>发起 → 目标</th><th>发生了什么</th></tr></thead><tbody>""" + "".join(rows) + """</tbody></table><script>document.querySelector('#filter').oninput=e=>{const q=e.target.value.toLowerCase();document.querySelectorAll('tbody tr').forEach(r=>r.hidden=!r.textContent.toLowerCase().includes(q));};</script></html>"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", default="combat-evidence.sqlite")
    subs = parser.add_subparsers(dest="command", required=True)
    imp = subs.add_parser("import"); imp.add_argument("roots", nargs="+")
    cov = subs.add_parser("coverage")
    cov.add_argument("--strict", action="store_true", help="Exit 3 after reporting incomplete evidence")
    cov.add_argument("--capture"); cov.add_argument("--run"); cov.add_argument("--round")
    cov.add_argument("--first", type=int); cov.add_argument("--last", type=int)
    cov.add_argument("--output")
    perf = subs.add_parser("performance")
    perf.add_argument("--capture", required=True); perf.add_argument("--output", required=True)
    serve = subs.add_parser("serve", help="Open the local match investigation timeline")
    source = serve.add_mutually_exclusive_group()
    source.add_argument("--source-db", help="Read an existing database and derive a NEW analysis database")
    source.add_argument("--roots", nargs="+", help="Read raw export directories or an investigation issue package")
    serve.add_argument("--output", help="New analysis/output directory; never an original evidence directory")
    serve.add_argument("--port", type=int, default=0)
    serve.add_argument("--open", action="store_true")
    serve.add_argument("--prepare-only", action="store_true")
    for name in ("query", "view", "compare", "locate", "player-output", "dot", "connection"):
        cmd = subs.add_parser(name)
        for field in ("event", "entity", "reason", "capture", "run", "round", "engine", "after", "before", "build"):
            cmd.add_argument("--" + field)
        cmd.add_argument("--limit", type=int, default=2000)
        cmd.add_argument("--category", choices=tuple(VIEW_CATEGORIES), help="Show movement, attack, damage or sync business records")
        cmd.add_argument("--first", type=int); cmd.add_argument("--last", type=int)
        cmd.add_argument("--strict", action="store_true", help="Exit 3 after reporting incomplete evidence")
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
    if args.command == "serve":
        from CombatInvestigation import main as investigate
        options = ["--port", str(args.port)]
        if args.source_db: options += ["--source-db", args.source_db]
        elif args.roots: options += ["--roots", *args.roots]
        else: options += ["--db", args.db]
        if args.output: options += ["--output", args.output]
        if args.open: options.append("--open")
        if args.prepare_only: options.append("--prepare-only")
        return investigate(options)
    with connect(args.db) as db:
        if args.command == "performance":
            with Path(args.output).open("w", encoding="utf-8") as stream:
                for row in db.execute("SELECT body FROM records WHERE capture=? AND stage IN ('performance.header','performance.snapshot') ORDER BY length(seq),seq", (args.capture,)):
                    value = payload(db, json.loads(row[0]))
                    stream.write(value if isinstance(value, str) else compact(value)); stream.write("\n")
            return
        if args.command == "import": result = import_roots(db, args.roots)
        elif args.command == "coverage":
            result = coverage(db, request=args)
        elif args.command == "extract": result = extract(db, args.capture, args.engine, args.first, args.last)
        elif args.command == "compare": result = compare(db, args)
        elif args.command == "locate": result = locate(db, args)
        elif args.command in ("player-output", "dot", "connection"): result = history(db, args)
        else: result = query(db, args)
        content = viewer(result) if args.command == "view" or getattr(args, "html", False) else json.dumps(result, ensure_ascii=False, indent=2)
        if getattr(args, "output", None): Path(args.output).write_text(content, encoding="utf-8")
        else: print(content)
        if result.get("invalidSelection"): return 3
        if args.command == "extract" or getattr(args, "strict", False):
            complete = result.get("complete", result.get("coverage", {}).get("complete", False))
            return 0 if complete and not result.get("truncated", False) else 3
    return 0


if __name__ == "__main__":
    sys.exit(main())
