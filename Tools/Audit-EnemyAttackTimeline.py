"""Passive audit of recorded local attack windows; never turns missing evidence into a pass."""
import argparse
import collections
import json
from pathlib import Path


def rows(path):
    if not path.exists():
        return
    for index, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        try:
            yield index, json.loads(line)
        except json.JSONDecodeError:
            yield index, {"kind": "incomplete-line"}


def audit(folder):
    counts = collections.Counter()
    damage = collections.Counter()
    active_counts = collections.Counter()
    cycles = collections.defaultdict(set)
    cycle_keys = {}
    violations = []
    centers = {}
    orders = {}
    for line, row in rows(folder / "attack-timeline.jsonl"):
        kind = row.get("kind")
        counts[kind] += 1
        a = row.get("action", {})
        key = (row.get("run"), row.get("enemy"), a.get("ActionId"))
        if kind == "incomplete-line":
            violations.append([line, "incomplete-line"])
        if kind not in ("phase", "handoff", "damage-attempt"):
            continue
        if kind == "damage-attempt":
            damage[str(row.get("damage"))] += 1
            if not (a.get("WarningUntil", 0) <= row.get("contactAt", 0) < a.get("ActiveUntil", 0)):
                violations.append([line, "contact-outside-original-window"])
            if row.get("contactAction") != a.get("ActionId"):
                violations.append([line, "contact-action-mismatch"])
            continue
        if not row.get("statsBound"):
            violations.append([line, "wrong-stats-binding"])
        phase = a.get("Phase", 0)
        strike = a.get("StrikeIndex", 0)
        order = {0: 100, 3: 6, 4: 101}.get(phase, strike * 2 + (phase == 2))
        if key[-1] and order < orders.get(key, -1):
            violations.append([line, "phase-rewind", key, order, orders[key]])
        orders[key] = order
        if row.get("damageEnabled"):
            if phase != 2 or not (a["WarningUntil"] <= row["combat"] < a["ActiveUntil"]):
                violations.append([line, "enabled-outside-active"])
            if not a.get("Sequence") or a.get("PoseStrikeIndex") == strike and a.get("LockedStrikeMask", 0) & (1 << strike):
                active_counts[f'{row.get("source")}/v{row.get("variant")}/strike{strike}'] += 1
            else:
                violations.append([line, "active-without-pose"])
        if phase in (1, 2, 3):
            f = a.get("Facing", {})
            quadrant = ("R" if f.get("x", 0) >= 0 else "L") + ("U" if f.get("y", 0) >= 0 else "D")
            cycle = key + (strike,)
            cycles[cycle].add(phase)
            cycle_keys.setdefault(cycle, f'{row.get("source")}/v{row.get("variant")}/{quadrant}/strike{strike}')
        if row.get("source") == "LostSoul" and row.get("instance") and phase in (2, 3):
            center = row["localExplosionCenter"]
            if key in centers and center != centers[key]:
                violations.append([line, "local-center-changed", key])
            centers[key] = center
    full_cycles = collections.Counter(cycle_keys[k] for k, phases in cycles.items() if {1, 2, 3} <= phases)
    active_cycles = collections.Counter(cycle_keys[k] for k, phases in cycles.items() if {1, 2} <= phases)
    health = collections.Counter()
    performance = []
    for path in folder.glob("*-audit.jsonl"):
        for _, row in rows(path):
            if row.get("kind") == "health" and row.get("current", 0) < row.get("previous", 0):
                health[str(row["previous"] - row["current"])] += 1
            if row.get("kind") == "performance":
                performance.append(json.loads(row["payload"]).get("meanFrameMs", 0))
    cleanup = [json.loads(r["payload"]) for _, r in rows(folder / "stage2-observation.jsonl") if r.get("kind") == "completed-cleanup"]
    return {"folder": str(folder), "counts": dict(counts), "activeEntries": dict(active_counts),
            "completeWAR": dict(full_cycles), "completeWA": dict(active_cycles), "damageAttempts": dict(damage), "effectiveHealthDrops": dict(health),
            "localCenters": len(centers), "violations": violations, "cleanup": cleanup,
            "meanFrameMs": sum(performance) / len(performance) if performance else None,
            "note": "Counts are observations, not proof of every planned matrix case. Ghoul strike 0/1 deliberately have no Recovery."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folders", nargs="+")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    result = [audit(Path(f)) for f in args.folders]
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    for item in result:
        print(item["folder"], "violations=", len(item["violations"]), "health=", item["effectiveHealthDrops"], "cleanup=", item["cleanup"])
