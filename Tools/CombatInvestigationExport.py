"""Export original evidence containers plus an explicit, reimportable investigation scope."""
from datetime import datetime, timezone
from contextlib import closing
import gzip
import hashlib
import html
import json
import math
from pathlib import Path, PurePosixPath
import re
import secrets
import shutil
from urllib.parse import quote

import CombatEvidence as decoder


def encoded(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def pointer(record):
    return (str(record.get("captureId", record.get("capture", ""))),
            str(record.get("recordSequence", record.get("sequence", ""))))


def refs(value):
    """Find only the storage format's declared references, never arbitrary strings."""
    if isinstance(value, dict):
        for key, child in value.items():
            if key in ("inputRef", "beforeRef", "afterRef", "checkpointRef", "$evidenceRef") and isinstance(child, str):
                if re.fullmatch(r"(?:inputs|checkpoints)/[0-9a-f]{64}\.json\.gz", child):
                    yield child
            else:
                yield from refs(child)
    elif isinstance(value, list):
        for child in value:
            yield from refs(child)


def digest(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def evidence_root(path):
    """The source store, never a volume root used to search arbitrary attachments."""
    path = Path(path).resolve()
    for parent in path.parents:
        if parent.name == "CombatDiagnostics":
            return parent
    if len(path.parents) >= 5 and path.parent.parent.name == "sources":
        return path.parents[4]
    if path.name == "network.jsonl" and path.parent.name == "network":
        # Automatic capture status and abandonment override live beside this directory.
        return path.parent.parent
    return path.parent


def inside(path, root):
    return path == root or root in path.parents


def package_files(root):
    """Walk only real package directories; never traverse a symlink or Windows junction."""
    original = root / "original"
    pending, files = [original], set()
    while pending:
        path = pending.pop()
        if path.is_symlink() or getattr(path, "is_junction", lambda: False)() or not inside(path.resolve(), root):
            raise ValueError("问题包包含越界路径或文件链接。")
        if path.is_dir():
            pending.extend(path.iterdir())
        elif path.is_file():
            files.add(path.resolve())
    return files


def original_location(path):
    path = Path(path).resolve()
    anchor = hashlib.sha256(path.anchor.encode()).hexdigest()[:8]
    return Path("original") / anchor / path.relative_to(path.anchor)


def readable(value):
    names = {"input": "输入", "decision": "判断", "change": "实际变化", "before": "之前", "after": "之后",
             "Health": "生命", "health": "生命", "Alive": "存活", "level": "等级", "experience": "经验",
             "weapons": "武器", "equipment": "装备", "perks": "祝福", "reason": "原因", "outcome": "结果",
             "buildRevision": "构筑版本", "perspective": "视角", "dropId": "掉落物", "claimVersion": "领取版本"}
    if isinstance(value, dict):
        return "<table>" + "".join("<tr><th>" + html.escape(names.get(k, k)) + "</th><td>" + readable(v) + "</td></tr>"
                                  for k, v in value.items()) + "</table>"
    if isinstance(value, list):
        return "<ul>" + "".join("<li>" + readable(x) + "</li>" for x in value) + "</ul>"
    return html.escape("未知" if value is None else "是" if value is True else "否" if value is False else str(value))


def report_projection(report):
    """Omit derived display duplication; original values and pointers remain in the package."""
    result = dict(report)
    result.pop("originalCoverage", None)
    result["coverageReference"] = "investigation-package.json#originalCoverage"
    result["states"] = []
    for state in report["states"]:
        state = dict(state, domains=[dict(d) for d in state.get("domains", [])])
        for domain in state["domains"]:
            fields = domain.pop("fields", [])
            names = [{k: f[k] for k in ("path", "name", "contentName") if k in f} for f in fields if f.get("contentName")]
            if names:
                domain["contentNames"] = names
        result["states"].append(state)
    result["tracks"] = dict(report["tracks"], tracks=[])
    for track in report["tracks"]["tracks"]:
        value = dict(track)
        value.pop("gaps", None)
        value.pop("coverageRef", None)
        value["gapsReference"] = "investigation-package.json#originalCoverage"
        result["tracks"]["tracks"].append(value)
    if report.get("detail"):
        detail = dict(report["detail"])
        records = detail.pop("records", [])
        detail["recordPointers"] = [dict(capture=pointer(r)[0], sequence=pointer(r)[1],
            associationStatus=r.get("associationStatus"), associationReason=r.get("associationReason"),
            outsideSelection=r.get("outsideSelection", False)) for r in records]
        result["detail"] = detail
    return result


class IssueExporter:
    def __init__(self, investigation, directory):
        self.investigation = investigation
        self.directory = Path(directory).resolve()
        self.plans = {}

    def _track_context(self, selection, initial_records, explicit_seeds):
        inv, db = self.investigation, self.investigation.db
        bounds = {}
        for key, record in initial_records.items():
            if record.get("associationStatus") in ("candidate", "not-established"):
                continue
            row = db.execute("SELECT elapsed,category,stage FROM investigation_records WHERE capture=? AND seq=?", key).fetchone()
            if row is None or row[0] is None or (explicit_seeds and row[1] in ("replay", "performance", "other") and key not in explicit_seeds):
                continue
            if row[2] in ("source.start", "process.start", "performance.header", "observation.snapshot", "investigation.catalog") and key not in explicit_seeds:
                continue
            current = bounds.setdefault(key[0], [row[0], row[0]])
            current[0], current[1] = min(current[0], row[0]), max(current[1], row[0])
        scopes, required, tracks = [], set(), []
        for capture, (first, last) in sorted(bounds.items()):
            if not explicit_seeds:
                first, last = selection.get("after", first), selection.get("before", last)
            samples, sample_warnings = [], []
            for row in db.execute("""SELECT r.seq,r.elapsed,o.body FROM investigation_records r JOIN records o USING(capture,seq)
                WHERE r.capture=? AND r.run=? AND r.round=? AND r.stage IN ('performance.snapshot','observation.snapshot') AND r.elapsed IS NOT NULL ORDER BY r.sort_time""",
                (capture, selection["run"], selection["round"])):
                try:
                    record = json.loads(row[2])
                    data = decoder.payload(db, record)
                    if isinstance(data, str):
                        data = json.loads(data)
                except (ValueError, TypeError, KeyError) as error:
                    data = None
                    sample_warnings.append("InvalidSampleWindowPayload:" + capture + ":" + row[0] + ":" + str(error))
                window = data.get("windowSeconds") if isinstance(data, dict) else None
                stage = json.loads(row[2])["stage"]
                start = row[1] if stage == "observation.snapshot" else row[1] - window if isinstance(window, (int, float)) and not isinstance(window, bool) and math.isfinite(window) and window >= 0 else None
                samples.append((row[0], row[1], start, stage))
            picked = {seq for seq, end, start, _ in samples if start is not None and start <= last and end >= first}
            # Independent streams have independent adjacent boundaries. A nearby writer
            # snapshot must not replace the performance sample needed to assess frames.
            for stage in ("performance.snapshot", "observation.snapshot"):
                previous = [item for item in samples if item[3] == stage and item[1] <= first]
                following = [item for item in samples if item[3] == stage and item[1] >= last]
                for boundary in ((previous[-1] if previous else None), (following[0] if following else None)):
                    if boundary:
                        picked.add(boundary[0])
            selected_times = [end for seq, end, _, _ in samples if seq in picked]
            query = dict(run=selection["run"], round=selection["round"], captures=[capture],
                         after=min([first] + selected_times), before=max([last] + selected_times))
            scope = dict(id="track-" + capture, selection=query, businessAfter=first, businessBefore=last,
                basis="BusinessClosureAndIntersectingOrAdjacentPerformanceSamples" if explicit_seeds else "SelectedIntervalAndPerformanceBoundarySamples",
                windowMapping="Writer snapshots are point observations; performance windows are emission-anchored estimates; preserve each stream's adjacent boundaries; no exact cross-clock alignment claimed", warnings=sample_warnings)
            scopes.append(scope)
            result = inv.tracks(query)
            for track in result["tracks"]:
                tracks.append(dict(track, scopeId=scope["id"], capture=capture))
                required.update(pointer(sample["evidence"]) for sample in track.get("samples", []))
            required.update((capture, seq) for seq, end, _, _ in samples if query["after"] <= end <= query["before"])
        return scopes, required, {"ranges": scopes, "tracks": tracks,
            "description": "Each capture uses its own business interval plus intersecting or adjacent observation samples; earlier state baselines do not extend this interval."}

    def _plan(self, selection, seeds, description):
        inv, db = self.investigation, self.investigation.db
        selected = set()
        seed_items = []
        if seeds:
            for seed in seeds:
                p = pointer(seed)
                result = inv.detail(selection, *p)
                item = result.get("record", {})
                if not item:
                    raise ValueError("所选事件不存在或不属于该对局：" + ":".join(p))
                selected.add(p)
                seed_items.append(item)
        else:
            cursor = None
            while True:
                page = inv.timeline(selection, cursor=cursor, limit=1000)
                for item in page["items"]:
                    selected.add(pointer(item))
                    seed_items.append(item)
                cursor = page.get("nextCursor")
                if not cursor:
                    break
        if not selected:
            raise ValueError("没有符合条件的记录可以导出。")
        required = {pointer(x): x for x in inv.context_records(selection, [dict(capture=c, sequence=s) for c, s in sorted(selected)])}
        failure_windows = inv.failure_windows(selection, seeds) if any(item.get("category") == "network_rejection" for item in seed_items) else []
        for window in failure_windows:
            # Samples and stream facts are local to this capture. The other machine's
            # numeric clock is never used as an exact corresponding window.
            for row in db.execute("""SELECT o.body FROM investigation_records r JOIN records o USING(capture,seq)
                WHERE r.capture=? AND r.run=? AND r.round=? AND r.elapsed BETWEEN ? AND ?""",
                (window["capture"], selection["run"], selection["round"], window["after"], window["before"])):
                record = json.loads(row[0]); required[pointer(record)] = record
        track_scopes, track_records, scoped_tracks = self._track_context(selection, required, selected if seeds else set())
        states = []
        state_targets = set()
        # Batch members and linked peer records may name actors absent from the seed's
        # top-level source/target. Preserve state at every required evidence point,
        # including intermediate entity generations, rather than just two endpoints.
        pending_records = set(required)
        while pending_records:
            added_points = set()
            for capture, seq in pending_records:
                if required[(capture, seq)].get("associationStatus") in ("candidate", "not-established"):
                    continue
                # A snapshot required for one actor contains incidental peers. Keep the
                # entire original snapshot, but do not turn those peers into new state
                # investigation targets unless the snapshot itself was selected.
                if required[(capture, seq)].get("stage") == "observation.snapshot" and (capture, seq) not in selected:
                    continue
                row = db.execute("SELECT elapsed FROM investigation_records WHERE capture=? AND seq=?", (capture, seq)).fetchone()
                if row is None or row[0] is None:
                    continue
                for member in db.execute("SELECT DISTINCT entity,generation FROM investigation_members WHERE capture=? AND seq=?", (capture, seq)):
                    if member[0] != "0" and (capture, member[0], member[1]) not in state_targets:
                        added_points.add((capture, member[0], float(row[0]), member[1]))
            # The initial event closure supplies all requested evidence times. Later
            # state dependencies may introduce a new actor/birth, but an earlier attack
            # observation is not a new request to reconstruct that same actor's past.
            state_targets.update((c, e, generation) for c, e, _, generation in added_points)
            state_seeds = set()
            for capture, entity, at, generation in sorted(added_points):
                state_selection = dict(selection, generation=generation) if generation != "unknown" else selection
                state = inv.state(state_selection, capture, entity, at)
                states.append(state)
                for p in state.get("contextDependencies", []):
                    if isinstance(p, dict) and all(pointer(p)):
                        state_seeds.add(pointer(p))
                for domain in state.get("domains", []):
                    for p in domain.get("dependencies", []) + [domain.get("evidence") or {}]:
                        if isinstance(p, dict) and all(pointer(p)):
                            state_seeds.add(pointer(p))
            state_seeds -= set(required)
            pending_records = set()
            if state_seeds:
                for record in inv.context_records(selection, [dict(capture=c, sequence=s) for c, s in sorted(state_seeds)]):
                    key = pointer(record)
                    if key not in required:
                        pending_records.add(key)
                        required[key] = record
        for key in track_records:
            row = db.execute("SELECT body FROM records WHERE capture=? AND seq=?", key).fetchone()
            if row:
                required[key] = json.loads(row[0])
        captures = {c for c, _ in required} | {c for c, _ in selected}
        # Preserve each capture's true round origin and startup/build identity. A subset must not reset elapsed time.
        for capture in captures:
            for row in db.execute("SELECT body FROM records WHERE capture=? AND run=? AND round=? ORDER BY length(seq),seq LIMIT 1",
                                  (capture, selection["run"], selection["round"])):
                record = json.loads(row[0]); required[pointer(record)] = record
            for row in db.execute("SELECT body FROM records WHERE capture=? AND stage IN ('process.start','source.start','performance.header','investigation.catalog')", (capture,)):
                record = json.loads(row[0])
                if record.get("runId") in ("boot", selection["run"]):
                    required[pointer(record)] = record
        paths = set()
        warnings = []
        network_paths = {Path(row[0]).resolve() for row in db.execute("SELECT path FROM metadata WHERE kind='network-diagnostics-source'")}
        for capture, seq in required:
            copies = list(db.execute("SELECT path FROM copies WHERE capture=? AND seq=?", (capture, seq)))
            found = False
            for copy in copies:
                p = Path(copy[0])
                if ((p.name.startswith("events-") and p.suffix == ".jsonl") or p.resolve() in network_paths) and p.is_file():
                    paths.add(p.resolve()); found = True
            if not found:
                warnings.append("原文件不可用：" + capture + ":" + seq)
        # Include all metadata associated with the selected capture, preserving history and writer observations.
        source_dirs = {p.parent for p in paths}
        source_roots = {evidence_root(p) for p in paths}
        if any(inside(self.directory, root) for root in source_roots):
            raise ValueError("导出目录必须在原始日志归档之外。")
        for row in db.execute("SELECT path,body FROM metadata"):
            p = Path(row[0])
            if not p.is_file() and re.search(r"\.jsonl:\d+$", row[0]):
                p = Path(row[0].rsplit(":", 1)[0])
            if not p.is_file():
                continue
            if not any(inside(p.resolve(), root) for root in source_roots):
                continue
            body = row[1]
            if any(c in body or c in p.name for c in captures) or any(p.parent == d or p.parent in d.parents for d in source_dirs):
                paths.add(p.resolve())
        # Copy full original containers. Resolve dependencies for incidental records too, or reimport would create fake failures.
        blob_refs = set()
        incidental = set()
        for p in sorted(paths):
            if p.resolve() in network_paths:
                from CombatNetworkEvidence import rows
                try:
                    for number, record, _ in rows(p):
                        key = pointer(record); incidental.add(key)
                        imported = db.execute("SELECT body FROM records WHERE capture=? AND seq=?", key).fetchone()
                        if imported is None or imported[0] != decoder.compact(record):
                            warnings.append("原文件与已导入记录不符：" + ":".join(key))
                except (ValueError, KeyError, TypeError, OSError) as error:
                    warnings.append("原轻量日志解析问题：" + str(p) + ":" + str(error))
            elif p.name.startswith("events-") and p.suffix == ".jsonl":
                with p.open("rb") as stream:
                    for number, line in enumerate(stream, 1):
                        try:
                            if not line.endswith(b"\n"):
                                raise ValueError("truncated line")
                            for record in decoder.expand_records(line):
                                key = pointer(record)
                                incidental.add(key)
                                imported = db.execute("SELECT body FROM records WHERE capture=? AND seq=?", key).fetchone()
                                if imported is None or imported[0] != decoder.compact(record):
                                    warnings.append("原文件与已导入记录不符：" + ":".join(key))
                                for field in ("input", "before", "after"):
                                    try:
                                        decoder.payload(db, record, field)
                                    except (ValueError, TypeError, KeyError) as error:
                                        warnings.append("原记录依赖无效：" + ":".join(key) + ":" + field + ":" + str(error))
                                blob_refs.update(refs(record))
                        except (ValueError, KeyError, TypeError, OSError, EOFError) as error:
                            warnings.append("原容器解析问题：" + str(p) + ":" + str(number) + " " + str(error))
        for key in set(required) - incidental:
            warnings.append("原容器中缺少必需记录：" + ":".join(key))
        lookup_bases = source_dirs
        pending = list(blob_refs)
        checked = set()
        while pending:
            ref = pending.pop()
            if ref in checked:
                continue
            checked.add(ref)
            candidates = sorted({base / ref for base in lookup_bases
                                 if inside((base / ref).resolve(), base) and (base / ref).is_file()})
            if not candidates:
                warnings.append("缺少必需附件：" + ref)
                continue
            for p in candidates:
                paths.add(p.resolve())
                try:
                    with gzip.open(p, "rb") as stream:
                        raw = stream.read(decoder.MAX_BLOB + 1)
                    if len(raw) > decoder.MAX_BLOB or hashlib.sha256(raw).hexdigest() != p.name.split(".")[0]:
                        raise ValueError("附件内容哈希不符或过大")
                    pending.extend(refs(json.loads(raw)))
                except (OSError, EOFError, ValueError) as error:
                    warnings.append(str(p) + "：" + str(error))
        coverage = inv.coverage(selection)
        if not coverage.get("complete"):
            warnings.append("来源归档在所选调查范围存在缺口或完整性未证实。")
        files = [{"source": str(p), "path": original_location(p).as_posix(),
                  "bytes": p.stat().st_size, "mtimeNs": p.stat().st_mtime_ns, "sha256": digest(p)} for p in sorted(paths)]
        return {"selection": selection, "description": description, "selected": sorted(selected),
                "required": sorted(required), "incidental": sorted(incidental - set(required)),
                "files": files, "warnings": list(dict.fromkeys(warnings)), "originalCoverage": coverage,
                "states": states, "tracks": scoped_tracks, "trackScopes": track_scopes, "seedItems": seed_items,
                "failureWindows": failure_windows,
                "complete": coverage.get("complete", False) and not warnings}

    def _manifest(self, plan):
        return {"schemaVersion": 1, "kind": "CombatInvestigationIssue", "createdUtc": datetime.now(timezone.utc).isoformat(timespec="microseconds"),
                "selection": plan["selection"], "description": plan["description"],
                "selected": [dict(capture=c, sequence=s) for c, s in plan["selected"]],
                "required": [dict(capture=c, sequence=s) for c, s in plan["required"]],
                "incidental": [dict(capture=c, sequence=s) for c, s in plan["incidental"]],
                "omissions": "Unselected evidence was intentionally not exported; omissions do not prove capture loss.",
                "trackScopes": plan["trackScopes"], "originalCoverage": plan["originalCoverage"],
                "failureWindows": plan.get("failureWindows", []),
                "warnings": list(plan["warnings"]), "complete": False, "files": [],
                "contexts": [dict(capture=cap["capture"], run=match["run"], round=match["round"], timeOrigin=cap.get("timeOrigin"))
                    for match in self.investigation.matches().get("matches", []) for cap in match.get("captures", [])
                    if any(c == cap["capture"] for c, _ in plan["required"])]}

    def _report_data(self, plan):
        detail = self.investigation.detail(plan["selection"], *plan["selected"][0]) if len(plan["selected"]) == 1 else None
        return report_projection({"selection": plan["selection"], "description": plan["description"], "states": plan["states"],
            "failureWindows": plan.get("failureWindows", []),
            "tracks": plan["tracks"], "detail": detail, "items": plan["seedItems"], "warnings": plan["warnings"],
            "recordTimes": [dict(row) for c, s in plan["required"] for row in self.investigation.db.execute(
                "SELECT capture,seq AS sequence,run,round,elapsed FROM investigation_records WHERE capture=? AND seq=?", (c, s))]})

    def preview(self, selection, seeds=None, description=""):
        plan = self._plan(selection, seeds or [], description)
        plan["report"] = self._report_data(plan)
        estimated_manifest = self._manifest(plan)
        estimated_manifest["complete"] = plan["complete"]
        estimated_manifest["files"] = [dict(path=f["path"], originalPath=f["source"], bytes=f["bytes"], sha256=f["sha256"]) for f in plan["files"]]
        evidence_bytes = sum(f["bytes"] for f in plan["files"])
        derived_bytes = sum(len(v.encode("utf-8")) for v in (encoded(estimated_manifest), encoded(plan["report"]), self._report(estimated_manifest, plan["report"])))
        token = secrets.token_urlsafe(24)
        request = encoded([selection, seeds or [], description])
        self.plans.clear()
        self.plans[token] = (request, plan)
        return {"previewToken": token, "selectedRecords": len(plan["selected"]),
                "contextRecords": len(set(plan["required"]) - set(plan["selected"])),
                "incidentalRecords": len(plan["incidental"]), "files": len(plan["files"]),
                "bytes": evidence_bytes, "evidenceBytes": evidence_bytes, "estimatedDerivedBytes": derived_bytes,
                "estimatedPackageBytes": evidence_bytes + derived_bytes,
                "sizeEstimateBasis": "Original evidence bytes plus serialized manifest, report JSON and HTML; unchanged sources required.",
                "warnings": plan["warnings"], "complete": plan["complete"]}

    def export(self, selection, seeds=None, description="", preview_token=None):
        request = encoded([selection, seeds or [], description])
        if preview_token not in self.plans or self.plans[preview_token][0] != request:
            raise ValueError("选择或描述发生变化，请重新预览导出。")
        _, plan = self.plans.pop(preview_token)
        for file in plan["files"]:
            p = Path(file["source"])
            if not p.is_file() or p.stat().st_size != file["bytes"] or p.stat().st_mtime_ns != file["mtimeNs"] or digest(p) != file["sha256"]:
                raise ValueError("预览后原文件发生变化，请重新核验：" + str(p))
        output = self.directory / (datetime.now().strftime("issue-%Y%m%d-%H%M%S-") + secrets.token_hex(4))
        output.mkdir(parents=True, exist_ok=False)
        manifest = self._manifest(plan)
        try:
            for file in plan["files"]:
                source, target = Path(file["source"]), output / file["path"]
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
                source_hash, target_hash = digest(source), digest(target)
                if source_hash != target_hash or source_hash != file["sha256"]:
                    raise ValueError("复制校验失败：" + str(source))
                manifest["files"].append({"path": file["path"], "originalPath": str(source), "bytes": target.stat().st_size, "sha256": target_hash})
            manifest["complete"] = plan["complete"]
            investigation_report = plan["report"]
            (output / "investigation-report.json").write_text(encoded(investigation_report), encoding="utf-8", newline="\n")
            (output / "report.html").write_text(self._report(manifest, investigation_report), encoding="utf-8", newline="\n")
        except Exception as error:
            manifest["complete"] = False
            manifest["warnings"].append("导出未完成：" + str(error))
            raise
        finally:
            (output / "investigation-package.json").write_text(encoded(manifest), encoding="utf-8", newline="\n")
        return {"directory": str(output), "report": str(output / "report.html"), "complete": manifest["complete"],
                "files": len(manifest["files"]), "warnings": manifest["warnings"]}

    @staticmethod
    def _report(manifest, report):
        file_links = {str(Path(f["originalPath"]).resolve()): f["path"] for f in manifest["files"]}
        record_times = {pointer(item): item for item in report.get("recordTimes", [])}
        domain_names = {"build": "构筑", "progression": "等级与经验", "selection": "选卡", "observation": "最近观测",
                        "attackGate": "最近攻击门控", "attackAttributes": "最近攻击属性"}
        context_reasons = {"EarlierBaselineRequiredForState": "为恢复状态保留的更早基线",
                           "EarlierObservationRequiredForState": "为描述状态保留的更早观测",
                           "EarlierAttackObservation": "为说明最近攻击判断保留的更早观测，不代表当前完整构筑"}

        def evidence_text(item):
            if not item:
                return "<p>证据：未知。</p>"
            cap, seq = pointer(item)
            position = record_times.get((cap, seq), {})
            value = html.escape(cap + " / #" + seq) + " · 本端 " + html.escape(str(position.get("elapsed", "未知"))) + " 秒"
            links = []
            for reference in item.get("references", []):
                relative = file_links.get(str(Path(reference.get("path", "")).resolve()))
                if relative:
                    links.append('<a href="' + quote(relative, safe="/") + '">原文件第 ' + html.escape(str(reference.get("line", "未知"))) + ' 行</a>')
            return "<p><small>证据：" + value + (" · " + "；".join(links) if links else "") + "</small></p>"

        def outside_text(item, default):
            if not item.get("outsideSelection"):
                return ""
            reason = context_reasons.get(item.get("contextReason"), item.get("contextReason") or default)
            return '<p class="warning">范围外上下文：' + html.escape(reason) + '。该记录不属于当前筛选范围。</p>'

        parts = ['<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>对局调查问题包</title>',
                 '<style>body{font:15px/1.65 system-ui;max-width:1100px;margin:32px auto;padding:0 24px;color:#203047}table{border-collapse:collapse;width:100%;font-size:13px}td,th{border:1px solid #ccd4de;padding:8px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{width:24%;background:#eef3f8}h2{margin-top:30px}li{margin:7px 0}.warning{background:#fff1d8;padding:12px}small{color:#5c6a7c}a{color:#176d68}</style>',
                 '<h1>对局调查问题包</h1><p>' + html.escape(manifest["description"] or "未填写问题描述") + '</p>',
                 '<p>这是选定问题及必要上下文，不是整局全量归档。未导出范围与原始采集缺口分别保留。</p>',
                 '<p>本端秒数使用各 capture 对局起点；不同端的数值不证明精确同时或因果延迟。</p>',
                 '<p>选中 ' + str(len(manifest["selected"])) + ' 条；必要记录 ' + str(len(manifest["required"])) +
                 ' 条；原容器额外带入 ' + str(len(manifest["incidental"])) + ' 条。</p>']
        if manifest["warnings"]:
            parts.append('<div class="warning">' + readable(manifest["warnings"]) + '</div>')
        parts.extend(['<h2>调查范围</h2>', readable(manifest["selection"]), '<h2>发生了什么</h2>'])
        for item in report["items"]:
            parts.append('<p><strong>' + html.escape(item.get("summary") or item.get("stage", "记录")) + '</strong><br><small>' +
                         html.escape(str(item.get("capture", ""))) + ' / #' + html.escape(str(item.get("sequence", ""))) +
                         ' · 本端 ' + str(item.get("elapsed", "未知")) + ' 秒</small></p>')
        if report["detail"]:
            if report["detail"].get("networkFacts"):
                parts.extend(['<h2>网络拒绝与后续事实</h2><p>接受不等于送达；后续连接事实不是同一消息的送达证明。本报告不自动推断恢复。</p>', readable(report["detail"]["networkFacts"])])
            for step in report["detail"].get("steps", []):
                parts.append('<h3>' + html.escape(step.get("title", step.get("kind", "过程"))) + '</h3>')
                parts.append(outside_text(step, "为关联事件保留的补充证据"))
                parts.append(evidence_text(step.get("evidence")))
                parts.append(readable(step.get("fields", {})))
                if step.get("status") == "unknown":
                    parts.append('<p>该步骤状态未知：' + html.escape(str(step.get("reason") or "缺少直接记录")) + '</p>')
            if report["detail"].get("unknowns"):
                parts.append(readable(report["detail"]["unknowns"]))
            if report["detail"].get("candidates"):
                parts.append('<h3>候选关联与未成立关系</h3><p>这些原记录仅供复核，没有作为已确认的输入、判断或变化链展开。</p>')
                for candidate in report["detail"]["candidates"]:
                    label = "候选，尚未证实" if candidate.get("status") == "candidate" else "关系未成立"
                    parts.append('<p>' + label + '：' + html.escape(str(candidate.get("reason", "未知"))) + '</p>' + evidence_text(candidate.get("evidence")))
        parts.append('<h2>指定时刻状态及其依据</h2>')
        for state in report["states"]:
            parts.append('<h3>对象 ' + html.escape(str(state.get("entity"))) + ' · ' + html.escape(str(state.get("capture"))) +
                         ' · 本端 ' + str(state.get("at")) + ' 秒</h3>')
            if state.get("contextDependencies"):
                parts.append('<h4>身份与生命周期依据</h4>')
                for dependency in state["contextDependencies"]:
                    parts.append('<p>' + html.escape(str(dependency.get("reason") or "状态上下文")) + '</p>' + evidence_text(dependency))
            for domain in state.get("domains", []):
                parts.append('<p>' + html.escape(domain_names.get(domain.get("domain"), domain.get("domain", "状态"))) + ' · ' + html.escape(domain.get("perspective", "")) +
                             ' · ' + {"reliable": "可靠恢复", "observed": "最近观测", "unknown": "未知"}.get(domain.get("status"), "未知") + '</p>')
                parts.append(outside_text(domain, "恢复状态所需的既有证据"))
                parts.append('<p>观测时刻：本端 ' + html.escape(str(domain.get("observedAt", "未知"))) + ' 秒。</p>')
                parts.append(evidence_text(domain.get("evidence")))
                parts.append('<p>状态基线：</p>' + outside_text(domain.get("baseline") or {}, "恢复状态所需的更早基线") + evidence_text(domain.get("baseline")))
                parts.append(readable(domain.get("state")))
                if domain.get("contentNames"):
                    parts.append(readable(domain["contentNames"]))
                parts.append(readable(domain.get("unknowns", [])))
            for missing in state.get("missingDomains", []):
                parts.append('<p>' + html.escape(domain_names.get(missing.get("domain"), missing.get("domain", "状态"))) +
                             ' · 未知：当前时刻或之前未采集该状态域，不能据此推断为空或未发生。</p>')
                parts.append(readable(missing.get("fields", [])))
            if state.get("unknowns"):
                parts.append(readable(state["unknowns"]))
        parts.append('<h2>四类等待与积压</h2>')
        parts.append('<p>各端轨道独立取样：业务关联时域及采样窗口相交或相邻的观测边界。更早状态基线不会扩大为整局轨道；历史采样以写出时间估计窗口，不声称精确跨时钟对齐。</p>')
        for scope in report["tracks"].get("ranges", []):
            query = scope.get("selection", scope)
            parts.append('<p>端 ' + html.escape(", ".join(query.get("captures", []))) + '：业务时域 ' +
                         html.escape(str(scope.get("businessAfter"))) + '–' + html.escape(str(scope.get("businessBefore"))) +
                         ' 秒；保留观测范围 ' + html.escape(str(query.get("after"))) + '–' + html.escape(str(query.get("before"))) + ' 秒。</p>')
        tracks = report["tracks"].get("tracks", [])
        names = {"business": "业务等待", "steam": "Steam 发送积压", "frames": "本地帧停顿", "writer": "日志写入积压"}
        statuses = {"observed": "已观察到", "not-observed": "覆盖范围内未观察到", "unknown": "证据不足", "invalid": "数据无效", "not-applicable": "不适用"}
        for track in tracks:
            parts.append('<h3>' + names.get(track["id"], track["id"]) + ' · ' + html.escape(str(track.get("capture", ""))) +
                         ' · ' + statuses.get(track.get("status"), "未知") + '</h3>')
            parts.append('<p>' + html.escape(str(track.get("reason", ""))) + '</p>')
            for sample in track.get("samples", []):
                parts.append('<details><summary>观测样本 · 本端 ' + html.escape(str(sample.get("elapsed", "未知"))) + ' 秒</summary>' +
                             evidence_text(sample.get("evidence")) + readable(sample.get("values", {})) + '</details>')
        parts.append('<p>完整历史缺口保留在 <a href="investigation-package.json">问题包清单的 originalCoverage</a>；派生报告引用该同一份依据，不重复复制缺口清单。</p>')
        parts.append('<h2>原始证据文件</h2><p>文件保持原始字节。完整依赖、来源路径和 SHA-256 见问题包清单。</p><ul>')
        for file in manifest["files"]:
            parts.append('<li><a href="' + quote(file["path"], safe="/") + '">' + html.escape(file["path"]) + '</a> · ' + str(file["bytes"]) + ' 字节</li>')
        parts.append('</ul><p>重新打开：通过本地调查启动器选择本问题包目录。</p></html>')
        return "\n".join(parts)


def import_package(root, destination):
    """Validate the portable subset and retain original integrity separately from omitted scope."""
    from CombatInvestigationIndex import build_index
    root, destination = Path(root).resolve(), Path(destination).resolve()
    if destination.exists() or root in destination.parents:
        raise ValueError("请在问题包之外选择不存在的新分析数据库。")
    manifest_path = root / "investigation-package.json"
    if manifest_path.is_symlink() or not inside(manifest_path.resolve(), root) or manifest_path.stat().st_size > decoder.MAX_BLOB:
        raise ValueError("问题包清单路径无效或过大。")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != "CombatInvestigationIssue":
        raise ValueError("不支持的问题包版本。")
    errors = []
    listed = set()
    for field in ("selected", "required"):
        if not isinstance(manifest.get(field), list) or not manifest[field]:
            raise ValueError("问题包缺少记录范围。")
        for item in manifest[field]:
            if not isinstance(item, dict) or not all(pointer(item)) or not pointer(item)[1].isdecimal():
                raise ValueError("问题包记录身份无效。")
    if not {pointer(r) for r in manifest["selected"]} <= {pointer(r) for r in manifest["required"]}:
        raise ValueError("选中记录未包含在必需记录中。")
    if not isinstance(manifest.get("files"), list) or not manifest["files"]:
        raise ValueError("问题包没有证据文件。")
    actual_files = package_files(root)
    for file in manifest.get("files", []):
        relative = file.get("path") if isinstance(file, dict) else None
        parts = PurePosixPath(relative).parts if isinstance(relative, str) else ()
        if not parts or parts[0] != "original" or len(parts) < 2 or any(p in (".", "..") for p in parts) or "\\" in relative or ":" in relative:
            raise ValueError("问题包文件路径越界。")
        p = (root / relative).resolve()
        if root not in p.parents or p in listed:
            raise ValueError("问题包文件路径越界或重复。")
        if (not isinstance(file.get("bytes"), int) or isinstance(file["bytes"], bool) or file["bytes"] < 0 or
                not isinstance(file.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", file["sha256"])):
            raise ValueError("问题包文件校验信息无效。")
        listed.add(p)
        if not p.is_file():
            errors.append("MissingPackageFile:" + file["path"])
        elif p.stat().st_size != file["bytes"] or digest(p) != file["sha256"]:
            errors.append("PackageHashMismatch:" + file["path"])
    if actual_files - listed:
        # The decoder walks its input recursively. Do not let an undeclared file influence
        # identities/coverage, or follow an arbitrary payload merely after recording a warning.
        raise ValueError("问题包含未列入清单的文件：" + ",".join(p.relative_to(root).as_posix() for p in sorted(actual_files - listed)))
    destination.parent.mkdir(parents=True, exist_ok=True)
    with closing(decoder.connect(destination)) as db:
        decoder.import_roots(db, [root / "original"])
        for item in manifest.get("required", []):
            if not db.execute("SELECT 1 FROM records WHERE capture=? AND seq=?", pointer(item)).fetchone():
                errors.append("MissingRequiredRecord:" + ":".join(pointer(item)))
        for row in db.execute("SELECT path,line,reason,details FROM issues"):
            errors.append("DecoderIssue:" + encoded(dict(row)))
        # Presence alone does not prove dependency integrity, including before/after and
        # nested shared references. Intentional omitted sequence ranges are not losses.
        for row in db.execute("SELECT body FROM records"):
            record = json.loads(row[0])
            for field in ("input", "before", "after"):
                try:
                    decoder.payload(db, record, field)
                except (ValueError, TypeError, KeyError) as error:
                    errors.append("InvalidDependency:" + ":".join(pointer(record)) + ":" + field + ":" + str(error))
        errors = list(dict.fromkeys(errors))
        manifest["verificationErrors"] = errors
        manifest["complete"] = manifest.get("complete") is True and manifest.get("originalCoverage", {}).get("complete") is True and not errors
        manifest["originalPackagePath"] = str(root)
        db.execute("INSERT OR REPLACE INTO metadata VALUES(?,?,?)", (str(root / "investigation-package.json"), "investigation-package.json", encoded(manifest)))
        for error in errors:
            db.execute("INSERT INTO issues VALUES(?,0,'InvestigationPackageVerification',?)", (str(root), error))
        db.commit()
    build_index(destination)
    return {"database": str(destination), "verificationErrors": errors, "complete": manifest["complete"]}
