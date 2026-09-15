"""Summarize observed stage-two runs. A summary is evidence, not an automatic visual pass."""
import collections
import json
import sys
from pathlib import Path

root = Path(sys.argv[1])
result = {}
for role in ("host", "client"):
    folder = root / role
    file = folder / "stage2-observation.jsonl"
    if not file.exists():
        continue
    rows = [json.loads(line) for line in file.read_text(encoding="utf-8-sig").splitlines()]
    for row in rows:
        row["p"] = json.loads(row.get("payload") or "{}")
    audit = [json.loads(line) for line in (folder / (role + "-audit.jsonl")).read_text(encoding="utf-8-sig").splitlines()]
    # Read edge sequences. A handoff/reposition may emit a remaining Warning with the
    # same action id; never overwrite an earlier phase and invent a full-cycle duration.
    actions = {}
    cycles = []
    for row in rows:
        if row["kind"] != "phase" or not row["detail"].endswith("/simulator"):
            continue
        action = row["p"]
        key = (row["detail"].split("/")[0], action["ActionId"])
        phase = action["Phase"]
        if phase == 1:
            actions[key] = {1: row}
        elif phase in (2, 3):
            phases = actions.get(key)
            if phases is not None and phase-1 in phases and phase not in phases:
                phases[phase] = row
        elif phase == 0:
            phases = actions.pop(key, {})
            if all(p in phases for p in (1, 2, 3)):
                w, a, r = (phases[p] for p in (1, 2, 3))
                cycles.append(dict(enemy=key[0], action=key[1], elapsed=w["elapsed"],
                    warning=a["combat"]-w["combat"], active=r["combat"]-a["combat"],
                    recovery=row["combat"]-r["combat"],
                    warningEntryDelay=w["combat"]-w["p"]["WarningStartedAt"],
                    planned=w["p"]))
    losses = [dict(elapsed=r["elapsed"], realtime=r["realtime"], loss=r["previous"]-r["current"])
              for r in audit if r["kind"] == "health" and r["current"] < r["previous"]]
    instances = collections.defaultdict(set)
    for row in rows:
        if row["kind"] == "geometry":
            instances[row["p"]["instance"]].add(row["p"]["enemy"])
    result[role] = dict(counts=dict(collections.Counter(r["kind"] for r in rows)),
        configurations=[r["p"] for r in rows if r["kind"] == "configuration"],
        births=[r for r in audit if r["kind"] == "birth"], losses=losses, cycles=cycles,
        quadrantCycles=[sum(2+q*10 <= c["elapsed"] < 12+q*10 for c in cycles) for q in range(4)],
        facingQuadrants=dict(collections.Counter(str((c["planned"]["Facing"]["x"]>0,c["planned"]["Facing"]["y"]>0)) for c in cycles if c["elapsed"]<42)),
        boundaries=[dict(detail=r["detail"], elapsed=r["elapsed"], value=r["p"]) for r in rows if r["kind"].startswith("fixture-boundary")],
        unboundGeometry=sum(not r["p"].get("currentStatsBound", True) for r in rows if r["kind"]=="geometry"),
        fixtures=[{k:r[k] for k in ("kind", "detail", "elapsed", "combat", "realtime")} for r in rows if r["kind"].startswith("fixture-")],
        attackInstances={str(k):sorted(v) for k,v in instances.items()},
        lastFrame=next((r for r in reversed(audit) if r["kind"] == "frame"), None),
        maxAlive=max((r["snapshot"].get("Alive", 0) for r in audit if r["kind"] == "frame"), default=0))
if "host" in result and "client" in result:
    norm = lambda values: sorted([{k:v for k,v in row.items() if k != "role"} for row in values], key=lambda r:r["id"])
    result["both"] = dict(sameBirths=norm(result["host"]["births"]) == norm(result["client"]["births"]))
(root / "stage2-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
for role in ("host", "client"):
    if role in result:
        r=result[role]
        print(role, json.dumps({k:r[k] for k in ("counts", "configurations", "quadrantCycles", "lastFrame")}, ensure_ascii=False))
        print("health losses", collections.Counter(hit["loss"] for hit in r["losses"]))
if "both" in result:
    print(result["both"])
