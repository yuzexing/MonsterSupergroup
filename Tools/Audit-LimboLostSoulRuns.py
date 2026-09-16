"""Summarize recorded runs without generating game events or inventing passes."""
import argparse
import csv
import json
import re
import statistics
from collections import Counter
from pathlib import Path


def rows(path):
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def summarize(root):
    result = {}
    births = {}
    builds = {}
    for role in ("host", "client"):
        folder = root / role
        if not folder.exists():
            continue
        audit = rows(folder / (role + "-audit.jsonl"))
        births[role] = {}
        current_round = "before-first-frame"
        for e in audit:
            if e["kind"] == "frame":
                current_round = e["snapshot"]["RunId"]
            elif e["kind"] == "birth":
                births[role][current_round + "/" + str(e["id"])] = {k: e[k] for k in ("source", "hp", "damage", "speed", "xp")}
        frames = [e for e in audit if e["kind"] == "frame"]
        by_round = {}
        for e in frames:
            key = e["snapshot"]["RunId"]
            by_round.setdefault(key, {"first": e, "max": e})
            if e["snapshot"]["Elapsed"] >= by_round[key]["max"]["snapshot"]["Elapsed"]:
                by_round[key]["max"] = e
        events = rows(folder / "lostsoul-observation.jsonl")
        phases = [(e, json.loads(e["payload"])) for e in events if e["kind"] in ("phase", "sample", "active-frame")]
        hits = [(e, json.loads(e["payload"])) for e in events if e["kind"] == "damage-attempt"]
        spawns = []
        spawn_files = {}
        for file in folder.glob("spawns-*.csv"):
            with file.open(encoding="utf-8-sig") as stream:
                entries = list(csv.DictReader(stream))
                spawns.extend(entries)
                spawn_files[file.name] = {
                    "events": dict(Counter(e["event"] for e in entries)),
                    "identities": dict(Counter(e["source"] + ":" + e["variant"] for e in entries if e["event"] == "spawn")),
                    "clips": dict(Counter(e["clip"] for e in entries if e["event"] == "spawn")),
                    "lastEvent": entries[-1] if entries else None,
                }
        raw = (folder / "player.log").read_text(encoding="utf-8-sig", errors="replace")
        disposals = [dict(id=int(a), epoch=int(b), action=int(c), combat=float(d), deadline=float(f)) for a, b, c, d, f in re.findall(r"\[LostSoulDispose\] id=(\d+) epoch=(\d+) action=(\d+) combat=([\d.]+) deadline=([\d.]+)", raw)]
        cleanups = [e for e in rows(folder / "stage2-observation.jsonl") if e["kind"] == "completed-cleanup"]
        traps = rows(folder / "spatial-observation.jsonl")
        manifest = folder / "art-build.json"
        build = json.loads(manifest.read_text(encoding="utf-8-sig")) if manifest.exists() else None
        builds[role] = {e["file"]: e["sha256"] for e in build["files"]} if build else None
        result[role] = {
            "artBuildManifestAvailable": build is not None,
            "rounds": by_round,
            "births": len(births[role]),
            "birthMismatches": [e for e in audit if e["kind"] == "birth" and not e["match"]],
            "spawnEvents": dict(Counter(e["event"] for e in spawns)),
            "spawnFiles": spawn_files,
            "spawnByClip": dict(Counter(e["clip"] for e in spawns if e["event"] == "spawn")),
            "spawnByIdentity": dict(Counter(e["source"] + ":" + e["variant"] for e in spawns if e["event"] == "spawn")),
            "statsBindingFailures": sum(p.get("statsBound") is False for _, p in phases),
            "lostSoulHits": dict(Counter(str(p["amount"]) for _, p in hits)),
            "hitsAfterActiveDeadline": [e for e, p in hits if e["combat"] > p["action"]["ActiveUntil"] + .001],
            "disposals": len(disposals),
            "earlyDisposals": [e for e in disposals if e["combat"] < e["deadline"]],
            "maxDisposalDelay": max((e["combat"] - e["deadline"] for e in disposals), default=0),
            "cleanup": cleanups,
            "trapCleanup": [e for e in traps if e["kind"] == "completed-cleanup"],
            "trapClockRates": dict(Counter(str(e["timeScale"]) for e in traps)),
            "assistance": dict(Counter(e["kind"] for e in events if e["kind"].startswith("test-") or e["kind"] == "selection")),
            "errors": re.findall(r"^.*(?:Exception:|\[LimboAudit\].*mismatch|Error:.*).*$", raw, re.M),
            "screenshots": len(list(folder.glob("*.png"))),
        }
        complete = next((e for e in frames if e["snapshot"]["Phase"] == 5), None)
        if complete:
            round_id = complete["snapshot"]["RunId"]
            start = by_round[round_id]["first"]["realtime"]
            end = complete["realtime"]
            performance = [e for e in rows(folder / "art-observation.jsonl") if e["kind"] == "frame" and start + 2 <= e["real"] <= end]
            durations = sorted(e["frameMs"] for e in performance)
            selection_waits = []
            opened = None
            for e in audit:
                if e["kind"] != "selection" or not start <= e["realtime"] <= end:
                    continue
                if e["selecting"] and opened is None:
                    opened = e["realtime"]
                if not e["selecting"] and opened is not None:
                    selection_waits.append(e["realtime"] - opened)
                    opened = None
            deaths = [e for e in rows(folder / "stage2-observation.jsonl") if e["kind"] == "enemy-death" and start <= e["realtime"] <= end]
            ttk = {}
            for e in deaths:
                identity = births[role].get(round_id + "/" + e["detail"], {}).get("source", "unobserved-birth")
                payload = json.loads(e["payload"])
                if payload["hasFirstHit"]:
                    ttk.setdefault(identity, []).append({"id": e["detail"], "seconds": payload["seconds"]})
            result[role]["firstCompletedRound"] = {
                "run": round_id, "startRealtime": start, "endRealtime": end,
                "confirmedKills": len(deaths), "firstHitToConfirmedDeath": ttk,
                "selectionWaitsSeconds": selection_waits,
                "unclosedSelection": opened,
                "frameSampleCount": len(durations),
                "frameMsMedian": statistics.median(durations) if durations else None,
                "frameMsP95": durations[min(len(durations) - 1, int(len(durations) * .95))] if durations else None,
                "frameMsMax": max(durations, default=0),
                "maxAllocatedBytes": max((e["allocated"] for e in performance), default=0),
                "maxParticles": max((e["particles"] for e in performance), default=0),
            }
    if "client" in births:
        result["comparison"] = {
            "missingClientBirthIds": sorted(births["host"].keys() - births["client"].keys()),
            "extraClientBirthIds": sorted(births["client"].keys() - births["host"].keys()),
            "differentBirths": [i for i in births["host"].keys() & births["client"].keys() if births["host"][i] != births["client"][i]],
            "sameBuild": builds["host"] == builds["client"] if builds.get("host") and builds.get("client") else None,
        }
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = json.dumps(summarize(args.directory), ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(report + "\n", encoding="utf-8")
    else:
        print(report)
