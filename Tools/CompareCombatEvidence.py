#!/usr/bin/env python3
"""Compare bounded one-second observations from off/local/replicated evidence runs.

Reads a manifest of SQLite captures and/or standalone --network-diagnostics JSONL.
Cumulative logger counters become interval rates, never per-frame percentiles.
"""
import argparse
import json
import math
import sqlite3
from collections import defaultdict
from pathlib import Path
import CombatEvidence as evidence

COUNTERS = ("sinkMs", "sinkCalls", "writerMs", "checkpointMs", "replicationMainMs", "replicationWorkerMs",
            "replicatedBytes", "dropped", "sinkFailures", "rejectedReplicationPackets")
GAUGES = ("pendingBytes", "retainedBytes", "peakRetainedBytes", "peakQueueBytes")
BOUNDS = (8.34, 16.67, 25, 50, 100, 250)
MAX_SAMPLES = 18000


class BoundedList(list):
    def __init__(self, maximum): super().__init__(); self.maximum = maximum; self.omitted = 0
    def append(self, value):
        if len(self) < self.maximum: super().append(value)
        else: self.omitted += 1


def summary(values):
    values = sorted(values)
    if not values: return {"available": False, "samples": 0}
    def percentile(p): return values[max(0, math.ceil(len(values) * p) - 1)]
    return dict(available=True, samples=len(values), minimum=values[0], maximum=values[-1],
                mean=sum(values) / len(values), p50=percentile(.5), p95=percentile(.95), p99=percentile(.99))


def histogram_quantile(histogram, p):
    threshold = math.ceil(sum(histogram) * p)
    if not threshold: return None
    seen = 0
    for index, count in enumerate(histogram):
        seen += count
        if seen >= threshold:
            return {"lowerExclusiveMs": 0 if index == 0 else BOUNDS[index - 1],
                    "upperInclusiveMs": BOUNDS[index] if index < len(BOUNDS) else None}


def source_rows(case, base):
    if case.get("performanceFile"):
        path = base / case["performanceFile"]
        with path.open("rb") as stream:
            line = 0
            while raw := stream.readline(evidence.MAX_BLOB + 1):
                line += 1
                try:
                    if len(raw) > evidence.MAX_BLOB or not raw.endswith(b"\n"):
                        raise ValueError("TruncatedOrOversizedLine")
                    value = json.loads(raw)
                    yield ("performance.header" if value.get("kind") == "header" else "performance.snapshot"), value, value.get("time"), {"path": str(path.resolve()), "line": line}
                except ValueError as error:
                    yield "gap", {"reason": str(error)}, None, {"path": str(path.resolve()), "line": line}
                    if not raw.endswith(b"\n"): break
    if case.get("db"):
        path = base / case["db"]
        # Read-only: comparison must never modify the imported evidence database.
        with sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True) as db:
            clauses, values = [], [case["capture"]]
            for field in ("run", "round"):
                if field in case: clauses.append(field + "=?"); values.append(case[field])
            scoped = " AND ".join(clauses) + " AND " if clauses else ""
            sql = "SELECT stage,body FROM records WHERE capture=? AND (stage='performance.header' OR (" + scoped + "stage IN ('performance.snapshot','observation.snapshot'))) ORDER BY length(seq),seq"
            for stage, raw in db.execute(sql, values):
                record = json.loads(raw)
                pointer = {"db": str(path.resolve()), "captureId": record["captureId"], "recordSequence": record["recordSequence"]}
                try:
                    value = evidence.payload(db, record)
                    if isinstance(value, str): value = json.loads(value)
                    yield stage, value, record.get("monotonicTime"), pointer
                except ValueError as error: yield "gap", {"reason": str(error)}, None, pointer
            for metadata_path, raw in db.execute("SELECT path,body FROM metadata WHERE kind='coverage.json'"):
                state = json.loads(raw)
                if state.get("captureId") != case["capture"]: continue
                if any(field in case and str(case[field]) != str(state.get("runId" if field == "run" else field)) for field in ("run", "round")): continue
                for gap in state.get("gaps", []): yield "gap", gap, None, {"path": metadata_path}
                if state.get("tailUnknown"): yield "gap", {"reason": "SourceTailUnknown"}, None, {"path": metadata_path}


def analyze_case(case, rows):
    metrics, hist = defaultdict(list), [0] * 7
    gaps, headers, previous = BoundedList(1000), BoundedList(16), None
    observations = frames = 0
    first = last = None
    durations = 0.0
    alive = BoundedList(MAX_SAMPLES)
    references = []
    settings = set()
    def add(name, value):
        if isinstance(value, (int, float)) and math.isfinite(value) and value >= 0:
            if len(metrics[name]) < MAX_SAMPLES: metrics[name].append(value)
            elif not any(g.get("reason") == "AnalysisSampleLimit" for g in gaps): gaps.append({"reason": "AnalysisSampleLimit"})
    for stage, data, timestamp, pointer in rows:
        if stage == "gap": gaps.append(dict(data, references=[pointer])); continue
        if stage == "performance.header": headers.append(data); continue
        if not isinstance(data, dict): gaps.append({"reason": "InvalidSample", "references": [pointer]}); continue
        if timestamp is not None:
            first = timestamp if first is None else min(first, timestamp)
            last = timestamp if last is None else max(last, timestamp)
        if len(references) < 2: references.append(pointer)
        elif references: references[-1] = pointer
        if stage == "observation.snapshot":
            observations += 1
            for field in GAUGES: add(field, data.get(field))
            if previous is not None and timestamp is not None and previous[0] is not None:
                elapsed = timestamp - previous[0]
                if elapsed <= 0:
                    gaps.append({"reason": "NonIncreasingObservationTime", "references": [pointer]})
                else:
                    for field in COUNTERS:
                        before, after = previous[1].get(field), data.get(field)
                        if not isinstance(before, (int, float)) or not isinstance(after, (int, float)): continue
                        if after < before:
                            gaps.append({"reason": "CounterReset", "field": field, "references": [pointer]}); continue
                        add(field + "PerSecond", (after - before) / elapsed)
                    if elapsed > 2.5: gaps.append({"reason": "ObservationIntervalExceeded", "seconds": elapsed, "references": [pointer]})
            previous = timestamp, data
            for field in ("writerFailure", "replicationFailure"):
                if data.get(field): gaps.append({"reason": field, "details": data[field], "references": [pointer]})
        elif stage == "performance.snapshot":
            seconds = data.get("windowSeconds", 0)
            if not isinstance(seconds, (int, float)) or seconds <= 0:
                gaps.append({"reason": "MissingPerformanceWindowDuration", "references": [pointer]}); continue
            durations += seconds
            frames += data.get("frameCount", 0)
            if data.get("alive") is not None: alive.append(data["alive"])
            for field in ("frameP95Ms", "frameP99Ms", "frameMeanMs", "frameMaxMs", "mainMaxMs", "gpuMs", "mainWorkMs", "rttMs", "managedBytes", "unityAllocatedBytes", "workingSetBytes", "privateBytes"):
                add("window_" + field, data.get(field))
            for field in ("allocatedBytes", "gcMs"):
                value = data.get(field)
                if isinstance(value, (int, float)) and value >= 0: add(field + "PerSecond", value / seconds)
            distribution = data.get("frameHistogram")
            if isinstance(distribution, list) and len(distribution) == len(hist):
                hist = [a + b for a, b in zip(hist, distribution)]
            if data.get("frameOverflow", 0): gaps.append({"reason": "FrameSampleOverflow", "count": data["frameOverflow"], "references": [pointer]})
            if data.get("focused") is False: gaps.append({"reason": "UnfocusedWindow", "references": [pointer]})
            settings.add((data.get("width"), data.get("height"), data.get("fullScreenMode")))
            for connection in data.get("connections", []):
                for field in ("pendingReliableBytes", "pendingUnreliableBytes", "unacknowledgedBytes", "queueMilliseconds", "sendRateBytesPerSecond"):
                    add("connection_" + field, connection.get(field))
    if not frames: gaps.append({"reason": "NoPerformanceFrames"})
    if case.get("mode") != "off" and not observations: gaps.append({"reason": "NoLoggerCostObservations"})
    build_guids = sorted({h["buildGuid"] for h in headers if h.get("buildGuid")})
    return dict(case=case, buildGuids=build_guids, headers=headers, observationCount=observations,
                measuredSeconds=durations, observationSpanSeconds=None if first is None else last - first,
                measuredFrames=frames, alive=summary(alive), displaySettings=[list(v) for v in settings],
                metrics={key: summary(value) for key, value in metrics.items()},
                frameHistogram=hist, frameP95Bounds=histogram_quantile(hist, .95), frameP99Bounds=histogram_quantile(hist, .99),
                gaps=gaps, omittedGapDetails=gaps.omitted, omittedAliveSamples=alive.omitted, references=references,
                limitations=["CumulativeCountersAreDifferencedIntoIntervalRates", "MetricPercentilesDescribeObservationWindows",
                             "WindowPercentilesAreNotGlobalFramePercentiles", "MainThreadPerFrameP95P99NotAvailableFromTheseCounters"])


def compare(manifest, base):
    results = [analyze_case(case, source_rows(case, base)) for case in manifest["cases"]]
    requirements = []
    expected_seconds = manifest.get("minimumSeconds", 180)
    repeats = manifest.get("minimumRepeats", 3)
    for count in manifest.get("enemyCounts", [50, 200, 500]):
        for mode in ("off", "local", "replicated"):
            cases = [r for r in results if r["case"].get("mode") == mode and r["case"].get("enemyCount") == count]
            enough = len({r["case"].get("repeat") for r in cases if r["measuredSeconds"] >= expected_seconds}) >= repeats
            requirements.append(dict(enemyCount=count, mode=mode, minimumSeconds=expected_seconds, minimumRepeats=repeats, met=enough))
    builds = sorted({guid for r in results for guid in r["buildGuids"]})
    differences = []
    for count in manifest.get("enemyCounts", [50, 200, 500]):
        baseline = [r for r in results if r["case"].get("mode") == "off" and r["case"].get("enemyCount") == count]
        for mode in ("local", "replicated"):
            measured = [r for r in results if r["case"].get("mode") == mode and r["case"].get("enemyCount") == count]
            for field in ("window_frameMeanMs", "window_frameP95Ms", "window_frameP99Ms", "allocatedBytesPerSecond", "gcMsPerSecond"):
                before = [r["metrics"][field]["mean"] for r in baseline if field in r["metrics"]]
                after = [r["metrics"][field]["mean"] for r in measured if field in r["metrics"]]
                if before and after:
                    differences.append(dict(enemyCount=count, mode=mode, metric=field,
                                            scope="DifferenceOfCaseMeansOfOneSecondWindows",
                                            baselineMean=sum(before)/len(before), measuredMean=sum(after)/len(after),
                                            delta=sum(after)/len(after)-sum(before)/len(before)))
    return dict(formatVersion=1, cases=results, coverageRequirements=requirements, buildGuids=builds,
                comparisons=differences,
                sameBuildVerified=len(builds) == 1 and all(r["buildGuids"] == builds for r in results),
                performanceTargetVerdict="NotProvenWithoutPerFrameMainThreadMeasurements",
                interpretation="Reports measured one-second windows and cumulative-counter deltas. No Steam, GPU, or per-frame percentile success is inferred from missing evidence.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    path = Path(args.manifest).resolve()
    result = compare(json.loads(path.read_text(encoding="utf-8-sig")), path.parent)
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
