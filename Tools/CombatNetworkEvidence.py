"""Read-only adapter for bounded lightweight network diagnostics and shared validity rules."""
import hashlib
import json
import math
from pathlib import Path


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def capture_integrity_failures(header, end, status, sequences, captures):
    """Shared schema-3 completion contract; a Complete flag alone is insufficient."""
    failures = []
    if not header: failures.append("MissingHeader")
    if not end or end.get("normalClose") is not True: failures.append("MissingNormalEnd")
    end = end or {}
    if end.get("complete") is False: failures.append("CaptureEndIncomplete")
    if not status or status.get("complete") is not True or status.get("failure"):
        failures.append("WriterIncompleteOrMissingStatus")
    for field in ("captureFailures", "writerFailures"):
        if end.get(field, 0): failures.append(field + ":" + str(end[field]))
    unique = set(sequences)
    if unique and (len(unique) != len(sequences) or max(unique)-min(unique)+1 != len(unique)):
        failures.append("StreamSequenceGapOrDuplicate")
    if int(header.get("schemaVersion", 0)) >= 3 and (not unique or min(unique) != 0):
        failures.append("MissingInitialSequence")
    if len(captures) != 1: failures.append("ConflictingCaptureIdentity")
    return failures


def automatic_session(path, header):
    """Read the automatic capture's parent status without trusting a writer Complete flag."""
    path = Path(path)
    config = header.get("config") or {}
    root = path.parent.parent
    session_path, abandoned_path = root / "session.json", root / "abandoned.json"
    expected = isinstance(config, dict) and config.get("automaticCapture") is True
    discovered = path.parent.name == "network" and (session_path.exists() or abandoned_path.exists())
    if not expected and not discovered:
        return None, []
    info = dict(path=str(session_path), status=None, references=[], abandonmentPath=str(abandoned_path))
    failures = []
    for candidate, field in ((session_path, "status"), (abandoned_path, "abandonment")):
        if not candidate.exists():
            continue
        try:
            info["references"].append(dict(path=str(candidate), sha256=file_hash(candidate)))
            if candidate.stat().st_size > 1 << 20:
                raise ValueError("Oversized metadata")
            value = json.loads(candidate.read_text(encoding="utf-8-sig"))
            if not isinstance(value, dict):
                raise ValueError("Non-object metadata")
            info[field] = value
        except (OSError, ValueError) as error:
            failures.append("InvalidAutomaticSessionStatus:" + candidate.name + ":" + str(error))
    if abandoned_path.exists():
        failures.append("AutomaticSessionAbandoned")
    status = info["status"]
    if status is None:
        failures.append("MissingAutomaticSessionStatus")
        return info, failures
    if status.get("state") != "Saved" or status.get("localSaveCompleted") is not True or status.get("failure"):
        failures.append("AutomaticSessionIncomplete")
    if status.get("actualSource") != "network" or status.get("mode") != "Network":
        failures.append("ConflictingAutomaticSessionMode")
    for field in ("processId", "buildGuid"):
        actual, recorded = header.get(field), status.get(field)
        valid = (lambda value: isinstance(value, int) and not isinstance(value, bool) and value > 0) if field == "processId" else (
            lambda value: isinstance(value, str) and bool(value.strip()))
        if not valid(actual) or not valid(recorded):
            failures.append("MissingAutomaticSessionIdentity:" + field)
        elif str(actual).casefold() != str(recorded).casefold():
            failures.append("ConflictingAutomaticSessionIdentity:" + field)
    for value, field in ((header, "executablePath"), (status, "executable")):
        if value.get(field) is not None and not isinstance(value[field], str):
            failures.append("InvalidAutomaticSessionIdentity:" + field)
    left, right = header.get("executablePath"), status.get("executable")
    if isinstance(left, str) and isinstance(right, str) and left.replace("\\", "/").casefold() != right.replace("\\", "/").casefold():
        failures.append("ConflictingAutomaticSessionIdentity:executable")
    try:
        def build_id(value):
            body = value.get("buildInfo") or {}
            body = json.loads(body) if isinstance(body, str) else body
            return body.get("buildId")
        if build_id(header) != build_id(status):
            failures.append("ConflictingAutomaticSessionIdentity:buildId")
    except (ValueError, AttributeError):
        failures.append("InvalidAutomaticSessionIdentity:buildInfo")
    return info, failures


def valid_metric(connection, field):
    value = connection.get(field)
    if not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(value) or value < 0:
        return None
    if connection.get("readSucceeded") is False or connection.get("available") is False:
        return None
    flag = "queueValid" if field == "queueMilliseconds" else "pendingValid"
    if flag in connection and connection[flag] is not True:
        return None
    if field == "queueMilliseconds" and (value > 3600000 or connection.get("queueValidity") in ("ReadFailed", "Negative", "AboveOneHourUnverified")):
        return None
    # Historical positive values within range are observations; missing flags never validate a zero.
    return value if connection.get(flag) is True or value > 0 else None


def rejected(record):
    data = record.get("input") or {}
    return record.get("stage") == "network.transport" and isinstance(data, dict) and (
        record.get("outcome") == "Rejected" or str(data.get("result")) in ("k_EResultLimitExceeded", "LimitExceeded", "25"))


def network_file(path):
    if path.name.startswith("events-") or path.name == "retention.jsonl":
        return False
    try:
        with path.open("rb") as stream:
            for _ in range(4):
                raw = stream.readline(32 << 20)
                if not raw:
                    break
                try:
                    value = json.loads(raw)
                except ValueError:
                    continue
                if value.get("kind") in ("network_event", "sample"):
                    return True
                if value.get("kind") == "header" and any(k in value for k in ("networkCapabilities", "captureId", "buildGuid")):
                    return True
    except (OSError, ValueError, AttributeError):
        pass
    return False


def rows(path):
    """Yield normalized records with provenance; never rewrite source bytes."""
    header = {}
    capture = None
    fallback = file_hash(path)[:32]
    with path.open("rb") as stream:
        for number, raw in enumerate(stream, 1):
            if len(raw) > 32 << 20 or not raw.endswith(b"\n"):
                raise ValueError("TruncatedOrOversizedLine:" + str(number))
            value = json.loads(raw)
            if not isinstance(value, dict):
                raise ValueError("NonObjectLine:" + str(number))
            kind = value.get("kind")
            if kind == "header":
                header = value
                capture = value.get("captureId") or fallback
            if kind not in ("header", "sample", "network_event", "end"):
                continue
            if kind == "network_event":
                record = dict(value["record"])
                capture = capture or record.get("captureId")
            else:
                # Old samples have no stream sequence. A stable private capture avoids collision with CE IDs.
                legacy = int(header.get("schemaVersion", 0)) < 3
                cap = fallback if legacy else (value.get("captureId") or capture or fallback)
                sequence = number if legacy else value.get("recordSequence", 0 if kind == "header" else None)
                if sequence is None:
                    raise ValueError("MissingStreamSequence:" + str(number))
                record = dict(schemaVersion=2, captureId=cap, recordSequence=str(sequence),
                    runId=value.get("run", "boot"), round=value.get("round", 0), role=value.get("role", "Unknown"),
                    stage={"header": "process.start", "sample": "performance.snapshot", "end": "network.capture_end"}[kind],
                    utc=value.get("utc", value.get("utcStart")), monotonicTime=value.get("time", value.get("monotonicStart")),
                    networkTime=value.get("networkTime"), input=dict(value, lightweightNetwork=True))
            record["lightweightNetwork"] = True
            record["rawLineSha256"] = hashlib.sha256(raw).hexdigest()
            yield number, record, value


def import_file(db, path):
    import CombatEvidence as decoder
    path = path.resolve()
    header, end, sequences, captures, contexts, failures = {}, {}, [], set(), set(), []
    count = 0
    try:
        for number, record, raw in rows(path):
            if raw.get("kind") == "header": header = raw
            if raw.get("kind") == "end": end = raw
            cap, seq = str(record["captureId"]), str(record["recordSequence"])
            if not seq.isdecimal(): raise ValueError("InvalidSequence:" + str(number))
            captures.add(cap); sequences.append(int(seq))
            contexts.add((cap, record.get("runId") or "boot", int(record.get("round", 0))))
            body = decoder.compact(record)
            old = db.execute("SELECT body FROM records WHERE capture=? AND seq=?", (cap, seq)).fetchone()
            if old and old[0] != body:
                failures.append("ConflictingCopy:" + cap + ":" + seq)
                db.execute("INSERT INTO issues VALUES(?,?,'ConflictingCopy',?)", (str(path), number, cap + ":" + seq))
            db.execute("INSERT OR IGNORE INTO records VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (cap, seq,
                record.get("runId") or "boot", record.get("round", 0), record.get("eventId"), record.get("rootEventId"),
                record.get("parentEventId"), record.get("target", 0), record.get("stage"), record.get("reason"),
                record.get("engine"), record.get("utc"), record.get("serverSequence", 0), body))
            db.execute("INSERT OR IGNORE INTO copies VALUES(?,?,?,?)", (cap, seq, str(path), number))
            decoder.index_batch_events(db, record); decoder.index_facets(db, record)
            count += 1
    except (ValueError, TypeError, KeyError, OSError) as error:
        failures.append(str(error))
        db.execute("INSERT INTO issues VALUES(?,0,'InvalidNetworkRecord',?)", (str(path), str(error)))
    status_path = Path(str(path) + ".status.json")
    try:
        status = json.loads(status_path.read_text(encoding="utf-8-sig")) if status_path.exists() else None
    except (OSError, ValueError):
        status = None; failures.append("InvalidWriterStatus")
    failures.extend(capture_integrity_failures(header, end, status, sequences, captures))
    session, session_failures = automatic_session(path, header)
    failures.extend(session_failures)
    if session:
        for reference in session["references"]:
            source = Path(reference["path"])
            value = session.get("status" if source.name == "session.json" else "abandonment")
            db.execute("INSERT OR REPLACE INTO metadata VALUES(?,?,?)", (str(source), source.name,
                decoder.compact(value if value is not None else reference)))
    info = dict(path=str(path), sha256=file_hash(path), header=header, end=end, status=status,
                statusPath=str(status_path), captures=sorted(captures), contexts=[list(c) for c in sorted(contexts)],
                failures=failures, complete=bool(count) and not failures, records=count,
                semanticUnknowns={"parseFailures": end.get("parseFailures", None)}, automaticSession=session)
    db.execute("INSERT OR REPLACE INTO metadata VALUES(?,?,?)", (str(path), "network-diagnostics-source", decoder.compact(info)))
    return count


def apply_coverage(db, result):
    sources = [json.loads(r[0]) for r in db.execute("SELECT body FROM metadata WHERE kind='network-diagnostics-source'")]
    captures = {c for source in sources for c in source["captures"]}
    if not captures: return result
    selected = {(i.get("captureId"), i.get("runId"), i.get("round")) for i in result["intervals"]}
    result["intervals"] = [i for i in result["intervals"] if i.get("captureId") not in captures]
    grouped = {}
    for source in sources:
        for capture, run, round_id in source["contexts"]:
            if (capture, run, round_id) in selected:
                grouped.setdefault((capture, run, round_id), []).append(source)
    for (capture, run, round_id), originals in grouped.items():
        faults = sorted({f for source in originals for f in source["failures"]})
        hashes = {source["sha256"] for source in originals}
        if len(hashes) > 1: faults.append("ConflictingCaptureFiles")
        if db.execute("SELECT 1 FROM issues WHERE reason='ConflictingCopy' AND details LIKE ?", (capture + ":%",)).fetchone():
            faults.append("ConflictingCopy")
        result["intervals"].append(dict(captureId=capture, runId=run, round=round_id, complete=not faults,
            status="complete" if not faults else "incomplete", sourceType="lightweight-network", gaps=[{"reason": f} for f in faults],
            references=[reference for s in originals for reference in
                        ([{"path": s["path"], "sha256": s["sha256"]}] + (s.get("automaticSession") or {}).get("references", []))],
            collectionContract="Network indexes and sampled metrics only; unrecorded gameplay remains unknown"))
    result["complete"] = bool(result["intervals"]) and all(i["complete"] for i in result["intervals"]) and not result["scopeGaps"]
    return result
