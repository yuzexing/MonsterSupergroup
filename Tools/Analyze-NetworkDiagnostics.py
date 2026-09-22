"""Summarize opt-in Steam diagnostics without inventing whole-run frame percentiles."""
import argparse
import json
from collections import defaultdict
from pathlib import Path


def summarize(path):
    header = None
    groups = defaultdict(list)
    malformed = 0
    with path.open(encoding="utf-8-sig") as stream:
        for line in stream:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                malformed += 1
                continue
            if row.get("kind") == "header":
                header = row
            elif row.get("kind") == "sample":
                groups[(row.get("role"), row.get("run"), row.get("round"))].append(row)
    status_path = Path(str(path) + ".status.json")
    status = json.loads(status_path.read_text(encoding="utf-8-sig")) if status_path.exists() else None
    runs = []
    for (role, run, round_id), rows in groups.items():
        connections = [connection for row in rows for connection in row.get("connections", [])]
        elapsed = rows[-1]["time"] - rows[0]["time"]
        rates = {}
        for field in ("sentBytes", "receivedBytes"):
            rates[field + "PerSecond"] = [
                max(0, rows[-1][field][i] - rows[0][field][i]) / elapsed
                for i in range(2)
            ] if elapsed > 0 else None
        frames = sum(row["frameCount"] for row in rows)
        runs.append({
            "role": role, "run": run, "round": round_id, "samples": len(rows),
            "sampleIntervalSeconds": elapsed, "maxAlive": max(row["alive"] for row in rows),
            "frameMeanMs": sum(row["frameMeanMs"] * row["frameCount"] for row in rows) / frames if frames else None,
            "worstWindowP95Ms": max(row["frameP95Ms"] for row in rows),
            "worstWindowP99Ms": max(row["frameP99Ms"] for row in rows),
            "maxFrameMs": max(row["frameMaxMs"] for row in rows),
            "frameOverflow": sum(row.get("frameOverflow", 0) for row in rows),
            "maxQueueMs": max((c["queueMilliseconds"] for c in connections), default=None),
            "maxPendingReliableBytes": max((c["pendingReliableBytes"] for c in connections), default=None),
            "maxPendingUnreliableBytes": max((c["pendingUnreliableBytes"] for c in connections), default=None),
            "maxPendingDeaths": max(row["pendingDeaths"] for row in rows),
            "lastPendingDeaths": rows[-1]["pendingDeaths"],
            "maxDeathWaitSeconds": max(row["oldestPendingDeathSeconds"] for row in rows),
            "lastRejections": rows[-1]["rejections"],
            "sendFailuresDelta": rows[-1]["sendFailures"] - rows[0]["sendFailures"],
            "reliableSnapshotPacketsDelta": rows[-1]["reliableSnapshotPackets"] - rows[0]["reliableSnapshotPackets"],
            **rates,
        })
    return {"file": str(path), "header": header, "writerStatus": status, "malformedLines": malformed, "runs": runs}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", nargs="+", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    reports = [summarize(path) for path in args.files]
    result = json.dumps(reports, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(result + "\n", encoding="utf-8")
    print(result)


if __name__ == "__main__":
    main()
