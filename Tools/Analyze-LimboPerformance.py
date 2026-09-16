"""Compare observed runs; sampled alive counts are not instantaneous stall counts."""
import argparse
import collections
import json
from pathlib import Path


def read(path):
    result = []
    if path.exists():
        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            try:
                result.append(json.loads(line))
            except ValueError as exc:
                raise ValueError(f"Incomplete JSON at {path}:{number}") from exc
    return result


def summarize(folder, run=None):
    audit_path = next(folder.glob("*-audit.jsonl"))
    audit = read(audit_path)
    detail = read(folder / "performance-detail.jsonl")
    performance = [dict(json.loads(e["payload"]), realtime=e["realtime"]) for e in audit if e["kind"] == "performance"]
    if run:
        detail = [e for e in detail if e.get("run") == run]
        performance = [e for e in performance if e["snapshot"]["RunId"] == run]
    raw = (folder / "player.log").read_text(encoding="utf-8-sig", errors="replace")
    intervals = [(0, 135), (135, 180), (180, 201), (240, 300), (333, 379), (599, 721)]
    report = {}
    for start, end in intervals:
        windows = [p for p in performance if start <= p["snapshot"]["Elapsed"] < end and p["snapshot"]["Phase"] in (2, 6)]
        in_range = [p for p in detail if p.get("run") and start <= p["elapsed"] < end]
        observed = [p for p in in_range if p.get("phase", 2) in (2, 6) and p.get("timeScale", 1) > 0]
        long = [p for p in observed if p["kind"] == "long-frame"]
        report[f"{start}-{end}"] = {
            "oneSecondWindows": len(windows),
            "maxFrameMs": max((p["maxFrameMs"] for p in windows), default=None),
            "maxWindowP95FrameMs": max((p["p95FrameMs"] for p in windows), default=None),
            "maxWindowMeanFrameMs": max((p["meanFrameMs"] for p in windows), default=None),
            "sampledAlivePeak": max((p["snapshot"]["Alive"] for p in windows), default=None),
            "longFrames100ms": len(long) if detail else None,
            "longFrames250ms": sum(p["frameMs"] >= 250 for p in long) if detail else None,
            "longFrameRecords": long,
            "pausedLongFrames": [p for p in in_range if p["kind"] == "long-frame" and p not in observed],
            "gpuMaxMs": max((p["gpuMs"] for p in observed), default=None),
            "renderMaxMs": max((p["renderMs"] for p in observed), default=None),
            "allocationMaxBytes": max((p["allocatedBytes"] for p in observed), default=None),
            "queuedBytesMax": max((p["queuedBytes"] for p in observed), default=None),
            "frameHistogram": [sum(p.get("frameHistogram", [0] * 7)[i] for p in observed) for i in range(7)],
            "frameHistogramUpperBoundsMs": [8.34, 16.67, 25, 50, 100, 250, None],
        }
    return {"directory": str(folder.resolve()), "run": run or "all-process-rounds", "intervals": report,
            "settings": [e for e in audit if e["kind"] == "display-settings"],
            "endings": [e for e in audit if e["kind"] == "run-ended" and (not run or e.get("run") == run)],
            "helpers": dict(collections.Counter(e["kind"] for e in read(folder / "full.jsonl") if e["kind"].startswith("test-") and (not run or e.get("run") == run))),
            "logBytes": (folder / "player.log").stat().st_size,
            "orphanEventWarnings": raw.count("OptionalWarning.UselessEvent -"),
            "invalidSpeedWarnings": raw.count("Animator.speed doesn't affect Animancer"),
            "staticBodyWarnings": sum("linearVelocity" in line and "static body" in line for line in raw.splitlines()),
            "writerStatuses": {p.name: json.loads(p.read_text(encoding="utf-8-sig")) for p in folder.glob("*.status.json")},
            "note": "Frame distribution fields summarize 1-second windows, not the pooled per-frame distribution. Legacy logs cannot count individual long frames or attribute CPU/GPU. ProfilerRecorder/FrameTiming values are latest available samples and may lag the frame event; timingTimestamp identifies the GPU/CPU timing sample. Worker write times in fixed builds do not represent main-thread stalls. Different loadouts/inputs are not identical gameplay."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folders", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--run", help="Keep one RunId when the process was restarted through the UI.")
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps([summarize(p, args.run) for p in args.folders], ensure_ascii=False, indent=2), encoding="utf-8")
