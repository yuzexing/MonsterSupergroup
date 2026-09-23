import tempfile
import unittest
import copy
from pathlib import Path
import CompareCombatEvidence as performance


class ComparisonTests(unittest.TestCase):
    def test_cumulative_costs_become_interval_rates_not_frame_percentiles(self):
        rows = [("observation.snapshot", {"sinkMs": 100, "writerMs": 200, "retainedBytes": 500}, 10, {}),
                ("observation.snapshot", {"sinkMs": 106, "writerMs": 210, "retainedBytes": 600}, 12, {})]
        result = performance.analyze_case({"mode": "local"}, rows)
        self.assertEqual(3, result["metrics"]["sinkMsPerSecond"]["mean"])
        self.assertEqual(5, result["metrics"]["writerMsPerSecond"]["mean"])
        self.assertEqual(600, result["metrics"]["retainedBytes"]["maximum"])
        self.assertIsNone(result["frameP95Bounds"])
        self.assertNotIn("mainThreadP95Ms", result["metrics"])

    def test_reset_counter_is_gap_not_negative_cost(self):
        result = performance.analyze_case({"mode": "local"}, [
            ("observation.snapshot", {"sinkMs": 100}, 10, {}),
            ("observation.snapshot", {"sinkMs": 2}, 11, {})])
        self.assertNotIn("sinkMsPerSecond", result["metrics"])
        self.assertTrue(any(g["reason"] == "CounterReset" for g in result["gaps"]))

    def test_histogram_reports_bounds_and_duration(self):
        result = performance.analyze_case({"mode": "off"}, [
            ("performance.snapshot", {"windowSeconds": 1, "frameCount": 100, "frameHistogram": [90, 9, 1, 0, 0, 0, 0], "frameP95Ms": 12, "allocatedBytes": 1000}, 1, {})])
        self.assertEqual({"lowerExclusiveMs": 8.34, "upperInclusiveMs": 16.67}, result["frameP95Bounds"])
        self.assertEqual(100, result["measuredFrames"])
        self.assertEqual(1, result["measuredSeconds"])
        self.assertEqual(1000, result["metrics"]["allocatedBytesPerSecond"]["mean"])

    def test_incomplete_matrix_never_claims_acceptance(self):
        result = performance.compare({"cases": [], "enemyCounts": [50], "minimumRepeats": 3}, Path("."))
        self.assertFalse(any(r["met"] for r in result["coverageRequirements"]))
        self.assertFalse(result["sameBuildVerified"])
        self.assertEqual("NotProvenWithoutPerFrameMainThreadMeasurements", result["performanceTargetVerdict"])

    def test_truncated_standalone_file_is_visible(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "performance.jsonl"
            path.write_bytes(b'{"kind":"header","buildGuid":"build"}\n{"cut":')
            result = performance.analyze_case({"mode": "off"}, performance.source_rows({"performanceFile": path.name}, path.parent))
            self.assertEqual(["build"], result["buildGuids"])
            self.assertTrue(any(g["reason"] == "TruncatedOrOversizedLine" for g in result["gaps"]))


class BenchmarkGateTests(unittest.TestCase):
    def reports(self):
        result = []
        for mode, endpoints, p95, p99 in (("off", 0, .1, .2), ("local", 1, .8, 2), ("replicated", 2, 1, 3)):
            report = dict(kind="status-dominated-paced-cpu-probe", controllers=50, mode=mode, repeat=1,
                          endpointCount=endpoints, statusControllerBinding="shared-replica-root", failure=None,
                          targetSeconds=5, elapsedSeconds=5, frames=720, targetHz=144, achievedHz=144, deadlineMisses=0,
                          mainFrame=dict(count=720, p95Ms=p95, p99Ms=p99, maxMs=4, meanMs=p95 / 2),
                          diagnosticBudgetPeakBytes=endpoints * (32 << 20), perEndpointQueuePeakBytes=[1024] * endpoints,
                          dropped=[0] * endpoints, criticalDropped=[0] * endpoints, observationDropped=[0] * endpoints,
                          catchupComplete=True, catchupSeconds=1, missing=[], businessMatches=True,
                          producedRecords=8000 if endpoints else 0, eventBytes=100000 if endpoints else 0,
                          writerFailures=[None] * endpoints,
                          coverage=[dict(complete=True, tailUnknown=False, gaps=[], produced="8000", flushed="8000", failure=None, recoveryPending=False, queuedBytes=0) for _ in range(endpoints)],
                          allocationMeasurementAvailable=True, mainThreadAllocatedBytes=100,
                          allocationMeasurementReason=None)
            result.append((mode + "/benchmark.json", report))
        return result

    def compare(self, reports):
        return performance.compare_benchmarks(reports, minimum_seconds=5, minimum_repeats=1, counts=(50,))

    def test_passing_measured_matrix_has_separate_cpu_and_full_duration_verdicts(self):
        report = self.compare(self.reports())
        self.assertTrue(report["cpuStorageGatePassed"])
        self.assertTrue(report["allocationMeasurementGatePassed"])
        self.assertFalse(report["fullDurationMatrix"])
        self.assertFalse(report["steamAcceptanceProven"])

    def test_unavailable_allocation_never_becomes_zero_or_fails_cpu_gate(self):
        reports = self.reports()
        reports[1][1].update(allocationMeasurementAvailable=False, mainThreadAllocatedBytes=0, allocationMeasurementReason="CounterDidNotObserveProbeAllocation")
        result = self.compare(reports)
        self.assertTrue(result["cpuStorageGatePassed"])
        self.assertFalse(result["allocationMeasurementGatePassed"])
        self.assertIsNone(result["cases"][1]["allocations"])

    def test_cpu_threshold_is_a_failing_gate_not_only_a_report_column(self):
        for field, value, reason in (("p95Ms", 1.10001, "P95IncrementExceeds1ms"), ("p99Ms", 3.20001, "P99IncrementExceeds3ms")):
            with self.subTest(field=field):
                reports = self.reports(); reports[1][1]["mainFrame"][field] = value
                result = self.compare(reports)
                self.assertFalse(result["cpuStorageGatePassed"])
                self.assertIn(reason, result["cases"][1]["issues"])

    def test_critical_loss_and_catchup_deadline_cannot_pass(self):
        for change, reason in ((dict(criticalDropped=[1]), "CriticalRecordLossOrUnknown"),
                               (dict(catchupSeconds=45.001), "CatchupDeadlineExceededOrUnknown"),
                               (dict(catchupComplete=False), "IncompleteCatchup")):
            reports = self.reports(); reports[1][1].update(change)
            result = self.compare(reports)
            self.assertFalse(result["cpuStorageGatePassed"])
            self.assertIn(reason, result["cases"][1]["issues"])

    def test_replicated_coverage_copies_do_not_double_count_source_loss(self):
        reports = self.reports()
        replicated = next(report for _, report in reports if report["mode"] == "replicated")
        replicated.update(criticalDropped=[17, 17], observationDropped=[3, 3])
        result = self.compare(reports)
        row = next(row for row in result["cases"] if row["mode"] == "replicated")
        self.assertEqual(row["criticalDropped"], 17)
        self.assertEqual(row["observationDropped"], 3)
        self.assertIn("CriticalRecordLossOrUnknown", row["issues"])

    def test_missing_and_duplicate_combinations_are_rejected(self):
        for reports, reason in ((self.reports()[:-1], "MissingCase"), (self.reports() + [copy.deepcopy(self.reports()[1])], "DuplicateCase")):
            result = self.compare(reports)
            self.assertFalse(result["cpuStorageGatePassed"])
            self.assertIn(reason, [issue["reason"] for issue in result["issues"]])

    def test_slow_work_cannot_masquerade_as_five_seconds_at_144_hz(self):
        reports = self.reports(); reports[1][1].update(elapsedSeconds=40, achievedHz=18)
        result = self.compare(reports)
        self.assertFalse(result["cpuStorageGatePassed"])
        self.assertIn("TargetFrequencyNotSustained", result["cases"][1]["issues"])

    def test_empty_nan_and_wrong_endpoint_reports_are_not_valid_measurements(self):
        for change, reason in ((dict(mainFrame={}), "MissingPerFrameMainThreadMeasurements"),
                               (dict(elapsedSeconds=float("nan")), "MissingOrInsufficientTimingData"),
                               (dict(endpointCount=3), "EndpointCountMissingOrIncorrect")):
            reports = self.reports(); reports[1][1].update(change)
            result = self.compare(reports)
            self.assertFalse(result["cpuStorageGatePassed"])
            self.assertIn(reason, result["cases"][1]["issues"])
        self.assertFalse(self.compare([])["cpuStorageGatePassed"])

    def test_budget_limits_remain_failures_even_with_matching_business_output(self):
        reports = self.reports(); reports[1][1].update(diagnosticBudgetPeakBytes=(128 << 20) + 1, perEndpointQueuePeakBytes=[(32 << 20) + 1])
        result = self.compare(reports)
        self.assertFalse(result["cpuStorageGatePassed"])
        self.assertIn("TotalBudgetExceededOrUnknown", result["cases"][1]["issues"])
        self.assertIn("QueueBudgetExceededOrUnknown", result["cases"][1]["issues"])

    def test_fully_flushed_finite_prefix_can_pass_with_unknown_future_tail(self):
        reports = self.reports()
        reports[2][1]["coverage"][1].update(complete=False, tailUnknown=True)
        result = self.compare(reports)
        self.assertTrue(result["cpuStorageGatePassed"])
        self.assertEqual("8000", result["cases"][2]["coverageInterval"]["last"])
        self.assertFalse(result["cases"][2]["coverageInterval"]["wholeSourceClosed"])

    def test_finite_prefix_rejects_unflushed_or_unreliable_requested_records(self):
        for change in (dict(flushed="7999"), dict(produced="7999"), dict(flushed="8001"),
                       dict(flushed=None), dict(recoveryPending=True), dict(recoveryPending=None),
                       dict(queuedBytes=1), dict(failure="DiskFull"), dict(gaps=[{"first":"1","last":"1"}])):
            with self.subTest(change=change):
                reports = self.reports(); reports[2][1]["coverage"][1].update(change)
                result = self.compare(reports)
                self.assertFalse(result["cpuStorageGatePassed"])
                self.assertIn("IncompleteSourceCoverage", result["cases"][2]["issues"])


if __name__ == "__main__": unittest.main()
