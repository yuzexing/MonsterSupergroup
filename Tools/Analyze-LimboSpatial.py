"""Summarize rendered spatial fixtures; assertions do not certify visual appearance."""
import collections
import csv
import json
import sys
from pathlib import Path

root = Path(sys.argv[1])
result = {"directory": str(root.resolve()), "roles": {}}
for role in ("host", "client"):
    path = root / role / "spatial-observation.jsonl"
    if not path.exists():
        continue
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    runs = collections.defaultdict(list)
    for row in rows:
        runs[row["run"]].append(row)
    report = {}
    for run, records in runs.items():
        traps = collections.defaultdict(list)
        for row in records:
            if row["id"]:
                traps[row["id"]].append(row)
        report[run] = {
            "cleanup": [r for r in records if r["kind"] == "completed-cleanup"],
            "maximumSlots": max((r["slots"] for r in records), default=0),
            "missingStateRows": sum("state" not in r for r in records),
            "traps": {},
        }
        for ident, samples in traps.items():
            states = [r for r in samples if "state" in r and r["kind"] != "removed"]
            phase_rows = [r for r in samples if r["kind"] == "phase"]
            report[run]["traps"][ident] = {
                "phases": [{k: r[k] for k in ("elapsed", "combat", "realtime", "state") if k in r} for r in phase_rows],
                "removed": [{k: r[k] for k in ("elapsed", "combat", "realtime", "slots")} for r in samples if r["kind"] == "removed"],
                "centers": list({(r["state"]["Center"]["x"], r["state"]["Center"]["y"]) for r in states}),
                "radiusRange": [min((r["state"]["Radius"] for r in states), default=None), max((r["state"]["Radius"] for r in states), default=None)],
                "colliderSamples": sum(r["state"]["Collision"] for r in states),
                "edgePointCounts": sorted({len(r.get("worldEdgePoints", [])) for r in states}),
                "localContactSamples": sum(r.get("localPlayerTouchesBarrier", False) for r in states),
                "contactElapsedRange": [min((r["elapsed"] for r in states if r.get("localPlayerTouchesBarrier")), default=None),
                                        max((r["elapsed"] for r in states if r.get("localPlayerTouchesBarrier")), default=None)],
                "timeScales": sorted({r["timeScale"] for r in states if "timeScale" in r}),
            }
    result["roles"][role] = report

result["spawnTraces"] = []
for path in sorted(root.rglob("spawns-*.csv")):
    with path.open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    births = [r for r in rows if r["event"] == "spawn" and r["result"] == "Spawned"]
    result["spawnTraces"].append({
        "file": str(path.relative_to(root)),
        "successfulByClip": dict(collections.Counter(r["clip"] for r in births)),
        "birthTimesByClip": {clip: sorted({float(r["elapsed"]) for r in births if r["clip"] == clip}) for clip in {r["clip"] for r in births}},
        "countedAliveAtBirth": sorted({int(r["countedAlive"]) for r in births}),
        "events": dict(collections.Counter(r["event"] for r in rows)),
    })
(root / "spatial-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(root / "spatial-summary.json")
