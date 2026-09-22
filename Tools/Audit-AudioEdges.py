"""Summarize opt-in edge diagnostics without treating short RMS samples as loudness."""
import argparse
import collections
import json
import math
import pathlib
import re


def vector(value):
    return tuple(value[k] for k in ("x", "y", "z"))


def audit(path):
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    samples = [json.loads(r["detail"]) for r in rows if r["kind"] == "spatial-sample"]
    grouped = collections.defaultdict(list)
    filters = collections.defaultdict(list)
    dsp_types = collections.Counter()
    for sample in samples:
        grouped[sample["location"]].append(sample)
        for dsp in sample["dsp"]:
            dsp_type = dsp.split(":", 1)[1].split(";", 1)[0]
            dsp_types[dsp_type] += 1
            if any(name in dsp_type for name in ("PASS", "EQ", "FILTER")):
                for name, value in re.findall(r";([^=;]+)=([^;]+)", dsp):
                    try:
                        number = float(value)
                        filters[dsp.split(";", 1)[0] + "/" + name].append(number)
                    except ValueError:
                        pass
    locations = {}
    for location, values in grouped.items():
        locations[location] = {
            "samples": len(values),
            "listener_owner_fixed_depth_max_error": max(math.dist(vector(s["listener"]),
                (s["player"]["x"], s["player"]["y"], s["player"]["z"] - 10)) for s in values),
            "actual_source_distance_range": [min(s["geometricDistance"] for s in values), max(s["geometricDistance"] for s in values)],
            "old_camera_policy_distance_range": [min(s["oldCameraDistance"] for s in values), max(s["oldCameraDistance"] for s in values)],
            "distance_parameter_read_results": dict(collections.Counter(s["distanceResult"] for s in values)),
            "max_observed_peak": max(s["peak"] for s in values),
            "max_observed_rms": max(s["rms"] for s in values),
        }
    return {
        "log": str(path),
        "completion": [r["detail"] for r in rows if r["kind"] in ("smoke-passed", "smoke-failed")],
        "position_checks": [r["detail"] for r in rows if r["kind"] in ("edge-check", "framing-check")],
        "event_guids": sorted(set(s["guid"] for s in samples)),
        "locations": locations,
        "dsp_types": dict(dsp_types),
        "filter_parameter_ranges": {k: [min(v), max(v)] for k, v in filters.items()},
        "note": "Short event-block meters; random clips and stages are not normalized. No listening acceptance or causal loudness claim.",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    args = parser.parse_args()
    result = [audit(path) for path in sorted(args.directory.glob("*/host/weapon-audio.jsonl"))]
    result += [audit(path) for path in sorted(args.directory.glob("*/client/weapon-audio.jsonl"))]
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Summarized {len(result)} process logs: {args.output}")
