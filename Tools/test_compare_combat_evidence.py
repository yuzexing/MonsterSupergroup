import tempfile
import unittest
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


if __name__ == "__main__": unittest.main()
