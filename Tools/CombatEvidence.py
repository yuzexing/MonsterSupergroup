#!/usr/bin/env python3
"""Stream combat evidence into SQLite; query, inspect coverage, and extract Unity replay fixtures.
No third party Python packages, network service, or game UI interaction required.
"""
import argparse
import gzip
import hashlib
import html
import json
import sqlite3
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
"""
MAX_BLOB = 32 << 20


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
                        item = json.loads(raw)
                        if item.get("schemaVersion") != 1:
                            raise ValueError("Unsupported schema")
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
                        count += 1
                        if count % 1000 == 0:
                            db.commit()
                    except (ValueError, KeyError, TypeError) as error:
                        db.execute("INSERT INTO issues VALUES(?,?,'InvalidRecord',?)", (str(path), number, str(error)))
        db.commit()
    return {"importedCopies": count, "originalRecords": db.execute("SELECT count(*) FROM records").fetchone()[0], "coverage": coverage(db)}


def index_batch_events(db, record):
    # Index actual batch membership, not just a sequence number that another sender can reuse.
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


def payload(db, record):
    ref = record.get("checkpointRef") or record.get("inputRef")
    if not ref:
        return record.get("input")
    digest = Path(ref).name.split(".")[0]
    row = db.execute("SELECT body FROM blobs WHERE hash=?", (digest,)).fetchone()
    if row is None:
        raise ValueError("MissingBlob:" + ref)
    return json.loads(row[0])


def coverage(db):
    return {"interpretation": "Missing records never prove non-execution. Copies retain original identity; UTC is not causal order.",
            "sources": [dict(row) | {"body": json.loads(row["body"])} for row in db.execute("SELECT * FROM metadata WHERE kind IN ('coverage.json','recovery.json','retention.json','retention.local.json','retention.jsonl','replication.json','manifest.json')")],
            "issues": [dict(row) for row in db.execute("SELECT * FROM issues LIMIT 1000")],
            "issueCount": db.execute("SELECT count(*) FROM issues").fetchone()[0]}


def query(db, args):
    clauses, values = [], []
    for attr, column in (("entity", "entity"), ("reason", "reason"), ("capture", "capture"), ("run", "run"), ("engine", "engine")):
        value = getattr(args, attr, None)
        if value is not None:
            clauses.append(column + "=?")
            values.append(value)
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
        for _ in range(64):
            marks = ",".join("?" for _ in ids)
            rows = db.execute(f"SELECT event,root,parent FROM records WHERE event IN ({marks}) OR root IN ({marks}) OR parent IN ({marks})", list(ids) * 3)
            more = {v for row in rows for v in row if v and v != "0"}
            if more <= ids: break
            ids |= more
            if len(ids) > 10000: raise ValueError("Event tree exceeds bounded query; narrow by run or entity")
        marks = ",".join("?" for _ in ids)
        clauses.append(f"(event IN ({marks}) OR root IN ({marks}) OR parent IN ({marks}) OR (server<>0 AND (run,round,server) IN (SELECT run,round,server FROM records WHERE event IN ({marks}) AND server<>0)) OR (capture,seq) IN (SELECT capture,seq FROM event_links WHERE event IN ({marks})))")
        values += list(ids) * 5
    sql = "SELECT * FROM records" + (" WHERE " + " AND ".join(clauses) if clauses else "") + " ORDER BY capture,length(seq),seq LIMIT ?"
    limit = min(getattr(args, "limit", 2000), 10000)
    records = []
    for row in db.execute(sql, values + [limit + 1]):
        item = json.loads(row["body"])
        item["references"] = [dict(ref) for ref in db.execute("SELECT path,line FROM copies WHERE capture=? AND seq=?", (row["capture"], row["seq"]))]
        if getattr(args, "resolve_inputs", False):
            try: item["input"] = payload(db, item)
            except ValueError as error: item["evidenceGap"] = str(error)
        item["description"] = describe_record(item)
        records.append(item)
    stages = {r["stage"] for r in records}
    expected = ("owner.hit", "collector.enqueue", "network.submit", "gateway.decision", "ledger.apply", "gateway.canonical_link", "replica.entity")
    return {"records": records[:limit], "truncated": len(records) > limit,
            "missingStages": [s for s in expected if s not in stages] if event else [], "coverage": coverage(db)}


def compare(db, args):
    result = query(db, args)
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
            if row["stage"] == "replay.input":
                if pending: gaps.append("MissingOutput:" + pending["record"])
                try: arguments = payload(db, record)
                except ValueError as error: gaps.append(str(error)); arguments = []
                pending = {"record": capture + ":" + str(seq), "operation": record["operation"], "arguments": arguments, "boundary": record.get("before"), "external": [], "expectedTicks": []}
            elif row["stage"] == "replay.external" and pending is not None:
                pending["external"].append(payload(db, record))
            elif row["stage"] == "status.tick" and pending is not None:
                pending["expectedTicks"].append(payload(db, record))
            elif row["stage"] == "replay.output":
                if pending is None: gaps.append("MissingInput:" + str(seq)); continue
                if pending["operation"] != record["operation"] or record.get("outcome") != "Completed": gaps.append("AbortedOrUnpairedOperation:" + str(seq))
                pending["expected"] = record.get("after"); steps.append(pending); pending = None
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
    return {"version": 1, "source": capture, "engine": engine, "domain": domain,
            "complete": not gaps, "gaps": gaps, "checkpoint": baseline, "steps": steps}


def describe_record(record):
    stages = {"owner.hit": "本地命中", "owner.damage_calculation": "伤害计算", "collector.enqueue": "收集器入队",
              "collector.drain": "收集器组批", "network.submit": "提交战斗批次", "gateway.decision": "服务端判断",
              "ledger.apply": "账本结算", "gateway.canonical_link": "合并到权威状态输出", "network.canonical": "传输权威状态",
              "replica.entity": "客户端应用权威状态", "authority.movement": "移动权限校验", "death.report": "提交本地死亡",
              "death.receipt": "死亡确认", "entity.spawn": "实体出生", "entity.destroy": "实体销毁"}
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
    for name in ("query", "view", "compare", "locate"):
        cmd = subs.add_parser(name)
        for field in ("event", "entity", "reason", "capture", "run", "engine", "after", "before", "build"):
            cmd.add_argument("--" + field)
        cmd.add_argument("--limit", type=int, default=2000)
        cmd.add_argument("--resolve-inputs", action="store_true")
        cmd.add_argument("--output")
        if name == "locate": cmd.add_argument("--monster-type", required=True)
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
        else: result = query(db, args)
        content = viewer(result) if args.command == "view" else json.dumps(result, ensure_ascii=False, indent=2)
        if getattr(args, "output", None): Path(args.output).write_text(content, encoding="utf-8")
        else: print(content)


if __name__ == "__main__":
    main()
