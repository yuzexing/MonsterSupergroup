"""Read Phase 6 evidence. This does not launch the game or infer a pass from missing data."""
import argparse
import csv
import json
import re
import statistics
from collections import Counter
from pathlib import Path


def read(path):
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def distribution(values):
    values = sorted(values)
    return dict(samples=len(values), median=statistics.median(values) if values else None,
                p95=values[min(len(values)-1, int(len(values)*.95))] if values else None,
                maximum=max(values) if values else None)


def summarize(root):
    result, birth_sets = {}, {}
    for role in ("host", "client"):
        folder = root / role
        if not folder.is_dir():
            continue
        audit = read(folder / f"{role}-audit.jsonl")
        full = read(folder / "full.jsonl")
        stage = read(folder / "stage2-observation.jsonl")
        spatial = read(folder / "spatial-observation.jsonl")
        births = {(e["run"], e["id"]): e for e in audit if e["kind"] == "birth"}
        birth_sets[role] = births
        runs = {}
        for entry in audit:
            run = entry.get("run")
            if not run:
                continue
            record = runs.setdefault(run, dict(initial=[], endings=[], selections=[], acceptedChoices=0))
            if entry["kind"] == "player-initialized":
                record["initial"].append(json.loads(entry["payload"]))
            if entry["kind"] == "run-ended":
                record["endings"].append(entry)
            if entry["kind"] in ("selection-close", "selection-interrupted"):
                record["selections"].append(entry)
            if entry["kind"] == "selection-accepted":
                record["acceptedChoices"] += 1
        for run, record in runs.items():
            frames = [e for e in audit if e["kind"] == "frame" and e["snapshot"]["RunId"] == run]
            record["lastFrame"] = frames[-1] if frames else None
            record["requests"] = [e for e in full if e.get("run") == run and e["kind"] == "request-observed"]
            record["completions"] = [e for e in full if e.get("run") == run and e["kind"] == "completed"]
            record["waits"] = [e for e in full if e.get("run") == run and e["kind"] == "frame"
                               and json.loads(e["payload"])["snapshot"]["Phase"] == 6]
            if record["requests"] and record["completions"]:
                record["transitionWaitWallSeconds"] = record["completions"][0]["realtime"] - record["requests"][0]["realtime"]
            record["helpers"] = dict(Counter(e["kind"] for e in full if e.get("run") == run and e["kind"].startswith("test-")))
            record["births"] = sum(k[0] == run for k in births)
            record["birthMismatches"] = [e for k, e in births.items() if k[0] == run and not e["match"]]
            deaths = [e for e in stage if e.get("run") == run and e["kind"] == "enemy-death"]
            record["confirmedKills"] = len(deaths)
            record["firstHitToConfirmedDeathSeconds"] = distribution([json.loads(e["payload"])["seconds"] for e in deaths if json.loads(e["payload"])["hasFirstHit"]])
            record["stageCleanup"] = [e for e in stage if e.get("run") == run and e["kind"] in ("cleanup", "completed-cleanup")]
            record["trapCleanup"] = [e for e in spatial if e.get("run") == run and e["kind"] == "completed-cleanup"]
            record["healthChanges"] = [e for e in audit if e["kind"] == "health" and e.get("run") == run]
            record["acceptedDamageChanges"] = sum(e["current"] < e["previous"] for e in record["healthChanges"])
            record["trapPhases"] = {}
            for e in spatial:
                if e.get("run") == run and e["kind"] == "phase":
                    record["trapPhases"].setdefault(str(e["id"]), []).append(e["state"]["Phase"])
            record["actionPhaseSamples"] = sum(e.get("run") == run and e["kind"] == "phase" for e in stage)
            geometry = [json.loads(e["payload"]) for e in stage if e.get("run") == run and e["kind"] == "geometry"]
            record["geometrySamples"] = len(geometry)
            record["statsBindingFailures"] = [g for g in geometry if g.get("currentStatsBound") is False]
            performance = [json.loads(e["payload"]) for e in audit if e["kind"] == "performance" and e.get("run") == run]
            active = [p for p in performance if p["snapshot"]["RunId"] == run and p["snapshot"]["Phase"] in (2, 6) and p["snapshot"]["Elapsed"] >= 2]
            record["performanceActive"] = {k: distribution([p[k] for p in active]) for k in ("meanFrameMs", "p95FrameMs", "maxFrameMs", "logWriteMs", "logFlushMs")}
            record["sampledAlivePeak"] = max((p["sampledAlivePeak"] for p in performance if p["snapshot"]["RunId"] == run), default=0)
            record["spawnFiles"] = []
            for file in folder.glob(f"spawns-{run}-*.csv"):
                with file.open(encoding="utf-8-sig") as stream:
                    rows = list(csv.DictReader(stream))
                attempts = [e for e in rows if e["event"] == "spawn"]
                success = [e for e in attempts if e["result"] == "Spawned"]
                counts = Counter(e["event"] for e in rows)
                end = next((e for e in rows if e["event"] == "completed"), None)
                record["spawnFiles"].append(dict(file=file.name, attempts=len(attempts), successful=len(success),
                    successByClip=dict(Counter(e["clip"] for e in success)), results=dict(Counter(e["result"] for e in attempts)),
                    events=dict(counts), completion=end,
                    conservationDelta=len(success)-len(deaths)-counts["self-destruct"]-counts["retired"]-int(end["totalAlive"]) if end else None))
        raw = (folder / "player.log").read_text(encoding="utf-8-sig", errors="replace")
        status_files = list(folder.glob("*.status.json"))
        statuses = {p.name: json.loads(p.read_text(encoding="utf-8-sig")) for p in status_files}
        # Legacy packages have no writer sidecars. New packages require all files
        # registered by the audit to close, not merely a process-closed text line.
        background = (folder / "performance-detail.jsonl").exists()
        missing_status = [p.name for p in folder.glob("*.jsonl") if not Path(str(p) + ".status.json").exists()] if background else []
        result[role] = dict(runs=runs, processClosed=any(e["kind"] == "process-closed" for e in audit),
                            writerStatuses=statuses, missingWriterStatuses=missing_status,
                            backgroundFilesComplete=(not missing_status and bool(statuses) and all(s.get("complete") for s in statuses.values())) if background else None,
                            screenshots=len(list(folder.glob("*.png"))),
                            transitionServerLog=re.findall(r"^\[LimboTransition\].*$", raw, re.M),
                            runEndLog=re.findall(r"^\[RunEnd\].*$", raw, re.M),
                            errors=[s for s in raw.splitlines() if "Exception:" in s or "Error:" in s or "[LimboAudit]" in s and "mismatch" in s])
    if "host" in birth_sets and "client" in birth_sets:
        host, client = birth_sets["host"], birth_sets["client"]
        same = host.keys() & client.keys()
        fields = ("source", "hp", "damage", "speed", "xp", "birth")
        result["comparison"] = dict(missingClient=[list(k) for k in host.keys()-client.keys()],
            extraClient=[list(k) for k in client.keys()-host.keys()],
            differing=[list(k) for k in same if any(host[k][f] != client[k][f] for f in fields)])
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(summarize(args.directory), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
