#!/usr/bin/env python3
"""Compare bounded one-second observations from off/local/replicated evidence runs.

Reads a manifest of SQLite captures and/or standalone --network-diagnostics JSONL.
Cumulative logger counters become interval rates, never per-frame percentiles.
"""
import argparse
import json
import math
import sqlite3
import sys
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


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def nonnegative_total(value):
    values = value if isinstance(value, list) else [value]
    return sum(values) if values and all(finite(v) and v >= 0 for v in values) else None


def sequence(value):
    if isinstance(value, str) and value.isascii() and value.isdecimal(): value = int(value)
    return value if isinstance(value, int) and not isinstance(value, bool) and 0 <= value < 1 << 64 else None


def compare_benchmarks(reports, minimum_seconds=180, minimum_repeats=3, counts=(50, 200, 500),
                       target_hz=144, minimum_hz_ratio=.99, catchup_limit=45):
    """Strict CPU/storage gate. Reports contain measured per-frame distributions.

    Endpoint count is 0/1/2 for off/local/replicated. Three-endpoint relay tests
    are a separate correctness suite, never mixed into this performance matrix.
    Unknown allocation measurements remain unknown and have their own gate.
    """
    results, issues, grouped = [], [], defaultdict(list)
    for path, report in reports:
        if not isinstance(report, dict):
            issues.append({"reason": "InvalidReport", "path": str(path)}); continue
        row = dict(path=str(path), controllers=report.get("controllers"), mode=report.get("mode"), repeat=report.get("repeat"), issues=[])
        results.append(row)
        def fail(reason): row["issues"].append(reason)
        key = (row["controllers"], row["mode"], row["repeat"])
        if not isinstance(key[0], int) or key[0] not in counts or key[1] not in ("off", "local", "replicated") or not isinstance(key[2], int) or not 1 <= key[2] <= minimum_repeats:
            fail("UnexpectedMatrixCase"); continue
        grouped[key].append((row, report))
        expected_endpoints = {"off": 0, "local": 1, "replicated": 2}[key[1]]
        if report.get("endpointCount") != expected_endpoints: fail("EndpointCountMissingOrIncorrect")
        if report.get("statusControllerBinding") != "shared-replica-root": fail("UnexpectedStatusBinding")
        if report.get("kind") != "status-dominated-paced-cpu-probe": fail("UnsupportedBenchmarkKind")
        if report.get("failure"): fail("BenchmarkException")
        if report.get("businessMatches") is not True: fail("BusinessOutputMismatchOrUnmeasured")
        duration, elapsed, frames = report.get("targetSeconds"), report.get("elapsedSeconds"), report.get("frames")
        valid_timing = finite(duration) and duration >= minimum_seconds and finite(elapsed) and elapsed > 0 and isinstance(frames, int) and frames > 0
        if not valid_timing: fail("MissingOrInsufficientTimingData")
        elif report.get("targetHz") != target_hz or frames < math.ceil(minimum_seconds * target_hz): fail("InsufficientFrameWork")
        else:
            achieved = frames / elapsed
            row.update(targetSeconds=duration, elapsedSeconds=elapsed, frames=frames, achievedHz=achieved,
                       deadlineMisses=report.get("deadlineMisses"))
            # A paced loop timestamps completion of its last frame at (frames-1)/Hz.
            if elapsed < (frames - 1) / target_hz - .02: fail("TimingShorterThanPacedWork")
            if achieved < target_hz * minimum_hz_ratio: fail("TargetFrequencyNotSustained")
            measured_hz = report.get("achievedHz")
            if not finite(measured_hz) or abs(measured_hz - achieved) > max(.01, achieved * .001): fail("InconsistentAchievedFrequency")
        distribution = report.get("mainFrame", {})
        if not isinstance(distribution, dict): distribution = {}
        if distribution.get("count") != frames or not all(finite(distribution.get(field)) and distribution[field] >= 0 for field in ("p95Ms", "p99Ms", "maxMs", "meanMs")):
            fail("MissingPerFrameMainThreadMeasurements")
        elif not distribution["p95Ms"] <= distribution["p99Ms"] <= distribution["maxMs"]:
            fail("InvalidFrameDistribution")
        else: row.update(p95Ms=distribution["p95Ms"], p99Ms=distribution["p99Ms"])
        budget, queue = report.get("diagnosticBudgetPeakBytes"), report.get("perEndpointQueuePeakBytes")
        if not finite(budget) or not 0 <= budget <= 128 << 20: fail("TotalBudgetExceededOrUnknown")
        if not isinstance(queue, list) or len(queue) != expected_endpoints or not all(finite(v) and 0 <= v <= 32 << 20 for v in queue): fail("QueueBudgetExceededOrUnknown")
        row["budgetPeakBytes"] = budget
        dropped = report.get("dropped")
        if not isinstance(dropped, list) or len(dropped) != expected_endpoints or not all(finite(v) and v == 0 for v in dropped): fail("DroppedRecordsOrUnknown")
        critical = nonnegative_total(report.get("criticalDropped")) if expected_endpoints else 0
        observations = nonnegative_total(report.get("observationDropped")) if expected_endpoints else 0
        # These watermarks all describe the primary capture, including its remote
        # copies. A replicated gap must not be counted as a second lost input.
        if expected_endpoints and critical is not None:
            critical = max(report["criticalDropped"], default=0)
        if expected_endpoints and observations is not None:
            observations = max(report["observationDropped"], default=0)
        if expected_endpoints and critical != 0: fail("CriticalRecordLossOrUnknown")
        row.update(dropped=dropped, criticalDropped=critical, observationDropped=observations)
        if report.get("catchupComplete") is not True or report.get("missing") != []: fail("IncompleteCatchup")
        catchup = report.get("catchupSeconds")
        if not finite(catchup) or not 0 <= catchup <= catchup_limit: fail("CatchupDeadlineExceededOrUnknown")
        if expected_endpoints:
            requested = sequence(report.get("producedRecords"))
            if requested is None or requested <= 0 or not finite(report.get("eventBytes")) or report["eventBytes"] <= 0:
                fail("NoCapturedWork")
            coverage = report.get("coverage")
            # The remote may still describe an open source. Prove the requested
            # finite interval, without claiming its unknown future tail is closed.
            def complete_prefix(state):
                if not isinstance(state, dict) or requested is None or requested <= 0: return False
                produced, flushed = sequence(state.get("produced")), sequence(state.get("flushed"))
                return (produced is not None and flushed is not None and produced >= flushed >= requested and
                        state.get("gaps") == [] and not state.get("failure") and
                        state.get("recoveryPending") is False and state.get("queuedBytes") == 0)
            if not isinstance(coverage, list) or len(coverage) != expected_endpoints or not all(complete_prefix(c) for c in coverage):
                fail("IncompleteSourceCoverage")
            row["coverageInterval"] = {"scope": "FiniteProducedPrefix", "first": "1", "last": str(requested) if requested is not None else None,
                                       "wholeSourceClosed": isinstance(coverage, list) and bool(coverage) and all(isinstance(c, dict) and c.get("complete") is True and c.get("tailUnknown") is False for c in coverage)}
            failures = report.get("writerFailures")
            if not isinstance(failures, list) or len(failures) != expected_endpoints or any(failures): fail("WriterFailureOrUnknown")
        allocation_available = report.get("allocationMeasurementAvailable") is True
        allocation_bytes = report.get("mainThreadAllocatedBytes") if allocation_available else None
        allocation_available = allocation_available and finite(allocation_bytes) and allocation_bytes >= 0
        row.update(allocationMeasurementAvailable=allocation_available, allocations=allocation_bytes if allocation_available else None,
                   allocationMeasurementReason=None if allocation_available else report.get("allocationMeasurementReason", "MissingValidAllocationMeasurement"),
                   catchupSeconds=catchup, eventBytes=report.get("eventBytes"))
        row["performanceBreakdown"] = {field: report.get(field) for field in ("businessAndCapture", "captureOnly", "checkpoint", "evidenceStages",
                                            "writerMilliseconds", "replicationMainMilliseconds", "replicationWorkerMilliseconds", "replicationSentBytes")}
    requirements = []
    for count in counts:
        for repeat in range(1, minimum_repeats + 1):
            baseline = grouped.get((count, "off", repeat), [])
            for mode in ("off", "local", "replicated"):
                matching = grouped.get((count, mode, repeat), [])
                requirements.append({"controllers": count, "mode": mode, "repeat": repeat, "presentExactlyOnce": len(matching) == 1})
                if len(matching) != 1:
                    issues.append({"reason": "MissingCase" if not matching else "DuplicateCase", "controllers": count, "mode": mode, "repeat": repeat})
                for row, report in matching:
                    if len(baseline) != 1 or "p95Ms" not in baseline[0][0] or "p95Ms" not in row:
                        row["issues"].append("MissingValidMatchingBaseline"); continue
                    before = baseline[0][0]
                    row["p95IncrementMs"] = row["p95Ms"] - before["p95Ms"]
                    row["p99IncrementMs"] = row["p99Ms"] - before["p99Ms"]
                    if row["p95IncrementMs"] > 1 + 1e-9: row["issues"].append("P95IncrementExceeds1ms")
                    if row["p99IncrementMs"] > 3 + 1e-9: row["issues"].append("P99IncrementExceeds3ms")
    passed = bool(results) and not issues and all(not r["issues"] for r in results)
    allocation_complete = bool(results) and all(r.get("allocationMeasurementAvailable") is True for r in results)
    return {"formatVersion": 2, "scope": "TwoEndpointEditModeCpuStorageBenchmark", "cpuStorageGatePassed": passed,
            "allocationMeasurementGatePassed": allocation_complete,
            "fullDurationMatrix": minimum_seconds >= 180 and minimum_repeats >= 3 and set(counts) == {50, 200, 500},
            "steamAcceptanceProven": False, "requirements": requirements, "cases": results, "issues": issues,
            "thresholds": {"p95IncrementMs": 1, "p99IncrementMs": 3, "cacheBytes": 128 << 20, "queueBytes": 32 << 20,
                           "catchupSeconds": catchup_limit, "minimumSeconds": minimum_seconds, "repeats": minimum_repeats,
                           "targetHz": target_hz, "minimumAchievedHz": target_hz * minimum_hz_ratio},
            "interpretation": "Passing this gate proves only the recorded CPU/storage matrix. Unknown allocations remain unknown; real Steam, physics, scene frames and GPU require separate evidence."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument("--manifest")
    inputs.add_argument("--benchmark-root")
    parser.add_argument("--output", required=True)
    parser.add_argument("--minimum-seconds", type=float, default=180)
    parser.add_argument("--minimum-repeats", type=int, default=3)
    parser.add_argument("--catchup-limit", type=float, default=45)
    parser.add_argument("--require-complete-measurements", action="store_true")
    args = parser.parse_args()
    if args.benchmark_root:
        reports = []
        for path in sorted(Path(args.benchmark_root).rglob("benchmark.json")):
            try: report = json.loads(path.read_text(encoding="utf-8-sig"))
            except (OSError, ValueError): report = None
            reports.append((path, report))
        result = compare_benchmarks(reports, args.minimum_seconds, args.minimum_repeats, catchup_limit=args.catchup_limit)
    else:
        path = Path(args.manifest).resolve()
        result = compare(json.loads(path.read_text(encoding="utf-8-sig")), path.parent)
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.benchmark_root:
        print(json.dumps({key: result[key] for key in ("cpuStorageGatePassed", "allocationMeasurementGatePassed", "fullDurationMatrix")}))
        if not result["cpuStorageGatePassed"]: return 2
        if args.require_complete_measurements and not result["allocationMeasurementGatePassed"]: return 3
    return 0


if __name__ == "__main__":
    sys.exit(main())
